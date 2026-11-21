# Dataverse.Fluent

A fluent, strongly typed C# client for the Microsoft Dataverse Web API, for .NET 10 and later.

The client has no metadata dependency: it never downloads, caches or parses the environment's entity model. Your
POCOs and the calls you write are the model, so a client is ready as soon as it is constructed. See
[Design Philosophy](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/DESIGN_PHILOSOPHY.md).

## Features

- Queries with C# lambdas translated to OData (`$filter`, `$select`, `$orderby`, `$expand`), paging and streaming
- Create, retrieve, update, upsert and delete, by ID or alternate key, with ETag concurrency control
- Batches with transactional changesets, and deep insert of related records
- Bulk create, update and upsert (`CreateMultiple`, `UpdateMultiple`, `UpsertMultiple`) in chunks, in parallel
- FetchXML, through a fluent builder or as raw XML, with automatic paging
- Aggregations (`$apply`), delta queries (change tracking), actions and functions, notes and file attachments
- Lookups, including polymorphic customer, owner and regarding lookups, and related records mapped on the POCO, so a retrieved record can be written back unchanged
- Request options: bypass custom business logic and flows, impersonate a user, detect duplicates
- Write-safe retries, throttling (HTTP 429) handling, token caching, dependency injection and multiple environments
- `DataverseException` with the Dataverse error code and activity ID

## Installation

```bash
dotnet add package Dataverse.Fluent
```

The client authenticates with any Azure `TokenCredential` or with a delegate that returns a bearer token. To use
`DefaultAzureCredential` or managed identity, also add `Azure.Identity`.

## Quick start

### Create a client

```csharp
using Azure.Identity;
using Dataverse.Fluent;

var client = new DataverseClient(
    "https://yourorg.api.crm.dynamics.com/api/data/v9.2",
    new DefaultAzureCredential());
```

Tokens are requested for `https://yourorg.api.crm.dynamics.com/.default` and cached until shortly before they expire.
Any other token source works through a delegate:

```csharp
var client = new DataverseClient(
    "https://yourorg.api.crm.dynamics.com/api/data/v9.2",
    tokenProvider: () => GetTokenAsync()); // e.g. MSAL AcquireTokenForClient
```

A client created without an `HttpClient` sends requests through this pipeline:

- **Write-safe retries.** GET requests are retried on transient failures. POST, PATCH, PUT, DELETE and `$batch`
  requests are retried only on HTTP 429, honouring `Retry-After`, or when the connection could not be established.
  A write that times out or fails with a 5xx is never sent twice.
- **A circuit breaker** that throttling (429) does not trip.
- **Timeouts sized for Dataverse:** 150 seconds per attempt, above Dataverse's 2-minute execution limit, and
  10 minutes in total.
- **Token caching with refresh on 401.** A rejected token is refreshed and the request sent once more.

If you pass your own `HttpClient`, its pipeline is used as is and retries are up to you.

### Map a table

```csharp
using System.Text.Json.Serialization;
using Dataverse.Fluent;

[DataverseEntity("account", "accounts")]          // table logical name and entity set
public class Account
{
    [DataverseReadOnly(DataverseOperation.Create)] // assigned by Dataverse
    public Guid AccountId { get; set; }

    public string? Name { get; set; }              // unmapped names are lowercased: "name"

    [JsonPropertyName("accountnumber")]
    public string? AccountNumber { get; set; }

    public decimal? Revenue { get; set; }

    [DataverseLookup("primarycontactid", "contacts")]
    public Guid? PrimaryContactId { get; set; }    // read as _primarycontactid_value, written as a bind

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    public DateTime? CreatedOn { get; set; }
}
```

Model only the columns you use. Properties that are `null` are left out of create and update payloads, and
properties marked `[DataverseReadOnly]` are left out of the operations named.

Map a date-only column (a Date Only column with the Date Only behaviour, `Edm.Date` in the Web API) as `DateOnly?`.
Dataverse rejects a date and time for such a column with HTTP 400, so a `DateTime` property cannot be written to it
unless it has a `[JsonConverter]` that writes the date alone. `Set`, `SetFrom`, batches and bulk operations all use a
property's own converter.

### Query

```csharp
var accounts = await client.Query<Account>()
    .Select(a => new { a.Name, a.Revenue })
    .Where(a => a.Revenue > 1_000_000 && a.Name!.StartsWith("Con"))
    .OrderBy(a => a.Revenue, descending: true)
    .Top(10)
    .GetListAsync();
```

`GetListAsync()` returns one page: at most 5,000 rows, even when more match. To read every matching row use
`GetAllAsync()` or `StreamAsync()`; see [Paging](#paging).

### Create, update and delete

```csharp
var created = await client.Entity<Account>()
    .SetFrom(new Account { Name = "Contoso", Revenue = 5_000_000 })
    .CreateAsync();

Guid id = created.Id!.Value; // from the OData-EntityId response header

await client.Entity<Account>(id)
    .Set(a => a.Revenue, 6_000_000m)
    .UpdateAsync();            // sends If-Match: *, so it never creates a record

await client.Entity<Account>(id).DeleteAsync();
```

## Dependency injection and multiple environments

```csharp
// The default client, resolved as IDataverseClient
services.AddDataverseClient("https://yourorg.api.crm.dynamics.com/api/data/v9.2", new DefaultAzureCredential());

// More environments, bound from configuration and resolved by key
var credential = new DefaultAzureCredential();
services.AddKeyedDataverseClient("source", configuration.GetSection("Dataverse:Source"), o => o.Credential = credential);
services.AddKeyedDataverseClient("target", configuration.GetSection("Dataverse:Target"), o => o.Credential = credential);

public class Migrator(
    [FromKeyedServices("source")] IDataverseClient source,
    [FromKeyedServices("target")] IDataverseClient target) { }

// Or choose the environment at runtime
var client = serviceProvider.GetRequiredService<IDataverseClientFactory>().CreateClient("target");
```

```json
{
  "Dataverse": {
    "Source": { "BaseUrl": "https://source.crm.dynamics.com/api/data/v9.2" },
    "Target": {
      "BaseUrl": "https://target.crm.dynamics.com/api/data/v9.2",
      "Resilience": { "MaxRetryAttempts": 5, "AttemptTimeout": "00:03:00", "TotalRequestTimeout": "00:15:00" }
    }
  }
}
```

Options are validated: an HTTPS URL, exactly one of `Credential` and `TokenProvider`, and consistent timeouts. Errors
name the client and are raised on first use, or at startup under a host.

## Queries

### Filters

Lambda filters are translated to OData. Values that do not depend on the record, such as `DateTime.UtcNow.AddDays(-7)`
or a captured variable, are evaluated on the client first. An expression that has no OData translation throws
`NotSupportedException` instead of sending a different query.

```csharp
var since = DateTime.UtcNow.AddDays(-7);
var states = new[] { 0, 1 };

var accounts = await client.Query<Account>()
    .Where(a => a.CreatedOn > since)
    .Where(a => a.Name!.Contains("Corp") || a.AccountNumber == null) // repeated Where calls are combined with "and"
    .GetListAsync();

var raw = await client.Query<Account>().Where("revenue gt 1000000").GetListAsync(); // raw OData
```

| C# | OData |
|----|-------|
| `==`, `!=`, `>`, `>=`, `<`, `<=` | `eq`, `ne`, `gt`, `ge`, `lt`, `le` |
| `&&`, `\|\|`, `!` | `and`, `or`, `not`, with grouping preserved |
| `Contains`, `StartsWith`, `EndsWith` on a string column | `contains`, `startswith`, `endswith`, matching the text literally |
| `string.IsNullOrEmpty(a.Column)` | `column eq null or column eq ''` |
| `list.Contains(a.Column)` | `Microsoft.Dynamics.CRM.In(...)` |
| `a.Nullable.HasValue` | `column ne null` |
| `a.Flag`, `!a.Flag` (a boolean column as the condition) | `flag eq true`, `not (flag eq true)` |
| `a.Related.Column` | `related/column` |
| `DataverseFunctions.Under(a.ParentAccountId, id)`, `EqualUserId`, `LastXDays`, ... | `Microsoft.Dynamics.CRM.*` query functions |

Dataverse treats `%`, `_` and `[` as wildcards in string functions; the library escapes them, so
`Name.Contains("50%")` matches the text `50%` only. Use a raw `Where("...")` filter for wildcard patterns.

Dataverse does not support arithmetic or the other OData string functions (`tolower`, `toupper`, `trim`, `length`,
`substring`, `indexof`, `concat`) in `$filter`, so a lambda that needs them throws `NotSupportedException`. String
comparisons are already case-insensitive, so `ToLower()` is not needed.

A long query, such as an `In` filter with hundreds of IDs, can pass the URL length Dataverse accepts for a direct
request (about 29,700 characters). The library then sends it, and its later pages, inside `$batch`, which accepts about
64,000. Beyond that it throws `InvalidOperationException`; split the IDs into several queries.

### Selecting, ordering and expanding

```csharp
var accounts = await client.Query<Account>()
    .Select(a => new { a.Name, a.PrimaryContactId })
    .OrderBy(a => new { a.Revenue, a.Name }, descending: true)
    .Expand<Contact>("primarycontactid", e => e.Select(c => c.FullName))
    .GetListAsync();
```

`Select` and `OrderBy` take only properties of the entity. A computed value or a property of a related record throws
`ArgumentException`; select related columns inside `Expand`.

### Paging

Dataverse pages with the `Prefer: odata.maxpagesize` header and `@odata.nextLink`. `Top()` caps the total number of
rows; it is not a page size. Dataverse does not support `$skip`.

```csharp
// Page by page, for example behind a UI
var query = client.Query<Account>().OrderBy(a => a.Name).PageSize(50).Count();
var page = await query.GetPagedAsync();
Console.WriteLine($"{page.Items.Count} of {page.TotalCount}");
while (page.HasMorePages)
    page = await query.GetNextPageAsync(page);

// Every row, one page at a time; Top() caps the total
await foreach (var account in client.Query<Account>().StreamAsync(pageSize: 2000))
    Console.WriteLine(account.Name);

// Everything as one list
var all = await client.Query<Account>().GetAllAsync();
```

`GetListAsync()` and `GetListWithCountAsync()` do not page: they return the first page only. When they are called
with neither `Top()` nor `PageSize()` and Dataverse has more rows than the page holds, the client logs a warning,
because the list is incomplete without the call having asked for a limit. The warning needs a logger: one is supplied
by dependency injection, or pass an `ILogger<DataverseClient>` to the `DataverseClient` constructor.

### Counts

`GetListWithCountAsync()` returns a page together with `@odata.count`. `CountAsync()` returns just the number of
matching records. Dataverse caps both counts at 5,000; count larger sets with an aggregate:

```csharp
var (items, total) = await client.Query<Account>().Where(a => a.Revenue > 0).GetListWithCountAsync();

var rows = await client.Aggregate<Account>()
    .Where(a => a.Revenue > 0)
    .Count("total")
    .GetListAsync<CountResult>(); // class CountResult { [JsonPropertyName("total")] public int Total { get; set; } }
```

### Formatted values

`IncludeFormattedValues()` asks Dataverse for display values such as option-set labels. Read them from a single
record with `RetrieveWithMetadataAsync()`, or map them on the POCO:

```csharp
[JsonPropertyName("industrycode@OData.Community.Display.V1.FormattedValue")]
public string? IndustryName { get; set; }
```

## Single records

`client.Entity<T>()` targets one record. The first read option (`Select`, `Expand`, `IncludeFormattedValues`)
continues on a read builder and the first write option (`Set`, `SetFrom`, `WithETag`, `ReturnRepresentation`, ...) on
a write builder, so each chain offers only the options that apply to it.

```csharp
// Retrieve
var account = await client.Entity<Account>(id).Select(a => new { a.Name, a.Revenue }).RetrieveAsync();
var byKey = await client.Entity<Account>().UsingAlternateKey(a => a.AccountNumber, "ACC-001").RetrieveAsync();

// Retrieve with the ETag and formatted values
var result = await client.Entity<Account>(id).IncludeFormattedValues().RetrieveWithMetadataAsync();

// Create and return the record
var created = await client.Entity<Account>().Set(a => a.Name, "Fabrikam").ReturnRepresentation().CreateAsync();
Account fabrikam = created.Entity!;

// Update only if the record has not changed since it was read (412 otherwise)
await client.Entity<Account>(id).WithETag(result.ETag!).Set(a => a.Name, "Renamed").UpdateAsync();

// Clear a column
await client.Entity<Account>(id).Set("description", null).UpdateAsync();
```

### Upsert

```csharp
var upserted = await client.Entity<Account>()
    .UsingAlternateKey(a => a.AccountNumber, "ACC-001")
    .Set(a => a.Name, "Contoso")
    .UpsertAsync();

Guid? recordId = upserted.Id;
```

`UpsertAsync(UpsertConflictResolution.PreventUpdate)` only creates, `PreventCreate` only updates, and
`IfUnmodifiedSince` updates only when the ETag still matches. See
[Upsert Conflict Resolution](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/UPSERT_CONFLICT_RESOLUTION.md).

### Lookups

A `[DataverseLookup]` property is read as `_{column}_value` and written as `{navigation}@odata.bind`, so `SetFrom` and
`Set` handle it:

```csharp
await client.Entity<Account>(id).Set(a => a.PrimaryContactId, contactId).UpdateAsync();
await client.Entity<Account>(id).Set(a => a.PrimaryContactId, null).UpdateAsync(); // clears it
```

A polymorphic lookup, such as a customer, owner or regarding lookup, is a `DataverseReference` (table and ID), with one
target per table it can point at. Reads fill in the table, and writes send the bind for that table:

```csharp
[DataverseLookup("parentcustomerid")]
[DataverseLookupTarget("account", "accounts", "parentcustomerid_account")]
[DataverseLookupTarget("contact", "contacts", "parentcustomerid_contact")]
public DataverseReference? ParentCustomerId { get; set; }

await client.Entity<Contact>(id).Set(c => c.ParentCustomerId, DataverseReference.To<Account>(accountId)).UpdateAsync();
```

`SetFrom` never writes navigation properties (related records loaded with `$expand`), so a retrieved record can be
written back as is. See [Lookup References](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/LOOKUP_REFERENCES.md).

## Batches

Write operations are grouped into changesets, which succeed or fail together. GET operations go to the top level of
the batch.

```csharp
var result = await client.Batch()
    .AddCreate(new Account { Name = "Parent" }, out var parent)
    .AddCreate(new Contact { LastName = "Child" },
        new Dictionary<string, BatchReference> { ["parentcustomerid_account"] = parent }) // binds to the new parent
    .NewChangeset()                                                                       // an independent transaction
    .AddUpdate(existingId, new Account { Revenue = 3_000_000 })
    .AddRetrieve<Account>(otherId, select: ["name", "revenue"])
    .ExecuteAsync();

foreach (var op in result.Results)
    Console.WriteLine($"{op.ContentId}: {(int)op.StatusCode} {(op.WasRolledBack ? "(rolled back)" : "")}");

result.ThrowIfAnyFailed();
```

When one operation in a changeset fails, the others report HTTP 424 with `WasRolledBack = true`.

Any write builder can produce its request without sending it, so a batch supports alternate keys, ETags, lookups and
actions as well:

```csharp
await client.Batch()
    .Add(client.Entity<Account>().UsingAlternateKey(a => a.AccountNumber, "ACC-001").Set(a => a.Revenue, 5m).ToUpsertRequest())
    .Add(client.Entity<Contact>(oldContactId).ToDeleteRequest())
    .ExecuteAsync();
```

## Bulk operations

For many records of one table, `Bulk<T>()` uses `CreateMultiple`, `UpdateMultiple` and `UpsertMultiple`, which are
faster than a batch. Records are sent in chunks, optionally in parallel, and the result reports each chunk.

```csharp
var result = await client.Bulk<Account>()
    .ChunkSize(500)
    .MaxDegreeOfParallelism(4)
    .CreateAsync(accounts);
result.ThrowIfAnyFailed();

await client.Bulk<Account>().UpdateAsync(changes, a => a.AccountId);
await client.Bulk<Account>().UpsertByAlternateKeyAsync(accounts, a => a.AccountNumber);
```

On a standard table each chunk is one transaction: when a record fails, its chunk is rolled back. See
[Bulk Operations](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/BULK_OPERATIONS.md).

## Request options

`WithOptions` returns a client that sends Dataverse's optional headers with every request, including each operation
of a batch. It covers bypassing custom plug-ins, workflows and Power Automate flows, impersonating another user,
duplicate detection and solution association. The original client is not changed.

```csharp
var loader = client.WithOptions(o =>
{
    o.BypassBusinessLogic = BusinessLogicBypass.CustomSync | BusinessLogicBypass.CustomAsync;
    o.BypassPowerAutomateFlows = true;
});

var asUser = client.WithOptions(o => o.CallerObjectId = userObjectId); // impersonation
```

See [Request Options](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/REQUEST_OPTIONS.md).

## Deep insert and associations

```csharp
var created = await client.DeepInsert(new Account { Name = "Contoso" })
    .WithRelated(new[] { new Contact { LastName = "One" }, new Contact { LastName = "Two" } }, "contact_customer_accounts")
    .ExecuteAsync();

await client.Associate()
    .From<Account>(accountId)
    .Via("contact_customer_accounts")
    .To<Contact>(contactId)
    .AssociateAsync();
```

## FetchXML

```csharp
var accounts = await client.FetchBuilder<Account>()
    .AddAttributes(a => new { a.Name, a.Revenue, a.PrimaryContactId })
    .AddFilter(a => a.Revenue > 1_000_000 && a.PrimaryContactId != null)
    .AddOrder(a => a.Revenue, descending: true)
    .ExecuteAsync();                       // follows paging cookies until every page is read

var raw = await client.Fetch<Account>("""
    <fetch>
      <entity name="account">
        <attribute name="name" />
        <filter><condition attribute="statecode" operator="eq" value="0" /></filter>
      </entity>
    </fetch>
    """).ExecuteAsync();
```

`BuildFetchXml()` returns the XML without sending it, and `StreamAsync()` yields rows page by page. See
[FetchXML Examples](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/FETCHXML_EXAMPLES.md).

## Delta queries

Change tracking must be enabled on the table.

```csharp
var initial = await client.Delta<Account>().Select("name", "revenue").ExecuteAsync();
var token = initial.DeltaToken; // persist this

var changes = await client.Delta<Account>().Since(token!).ExecuteAsync();
foreach (var account in changes.Value) Console.WriteLine($"Changed: {account.Name}");
foreach (var deleted in changes.Deleted) Console.WriteLine($"Deleted: {deleted.GetEntityId()}");
```

An expired token returns HTTP 410; restart with an initial query. See
[Delta Queries](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/DELTA_QUERIES.md).

## Actions, functions and notes

```csharp
var me = await client.Function<WhoAmIResponse>("WhoAmI").ExecuteAsync();

await client.Action<WinOpportunityRequest>("WinOpportunity").WithRequest(request).ExecuteAsync();

var note = await client.Annotation()
    .Regarding<Account>(accountId)
    .WithSubject("Signed contract")
    .WithFile("contract.pdf", pdfBytes)
    .CreateAsync();
```

## Testing your code

The `Dataverse.Fluent.Testing` package has a fake transport. Your code runs the real builders, the fake answers from
routes the test sets up, and the test asserts on the exact requests that were sent, including each operation of a
batch.

```csharp
var fake = new FakeDataverseTransport();
fake.OnGet("accounts").RespondWithEntities([new Account { Name = "Contoso" }]);
fake.OnPatch("accounts(*)").RespondWithError(HttpStatusCode.PreconditionFailed);

await new MyService(fake.CreateClient()).RunAsync();

Assert.Equal("revenue gt 0", fake.Requests[0].Query["$filter"]);
```

See the [Dataverse.Fluent.Testing README](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent.Testing/README.md).

Without the package, fake the one-method `IDataverseTransport` interface yourself and wrap it in a `DataverseClient`.
`DataverseResponse.FromEntity` and `FromEntities` build responses with records in the shape Dataverse returns them.

```csharp
sealed class FakeTransport : IDataverseTransport
{
    public string BaseUrl => "https://test.crm.dynamics.com/api/data/v9.2";
    public List<DataverseRequest> Requests { get; } = [];

    public Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        return Task.FromResult(DataverseResponse.FromJson(new JsonObject { ["value"] = new JsonArray() }));
    }
}

var transport = new FakeTransport();
await new DataverseClient(transport).Query<Account>().Where(a => a.Revenue > 0).GetListAsync();
Assert.Equal("accounts?$filter=revenue%20gt%200", transport.Requests.Single().Url);
```

The same interface lets you decorate the HTTP transport, for example to audit requests:
`new DataverseClient(new AuditingTransport(new DataverseHttpTransport(options)))`. Requests that no builder covers can
be sent through `client.Transport`.

## Documentation

| Guide | Covers |
|-------|--------|
| [Getting Started](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/GETTING_STARTED.md) | App registration, authentication, a first program |
| [API Reference](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/API_REFERENCE.md) | Entry points and builders |
| [Best Practices](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/BEST_PRACTICES.md) | Production guidance |
| [Design Philosophy](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/DESIGN_PHILOSOPHY.md) | Why there is no metadata dependency |
| [Custom Type Mapping](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/CUSTOM_TYPE_MAPPING.md) | Using generated types |
| [Schema Validation](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/SCHEMA_VALIDATION.md) | Checking models against an environment, and fixing them |
| [Error Handling](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/ERROR_HANDLING.md) | `DataverseException` and what is retried |
| [Actions and Functions](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/ACTIONS_AND_FUNCTIONS.md) | Bound and unbound actions and functions |
| [Annotations](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/ANNOTATIONS.md) | Notes and file attachments |
| [Deep Insert](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/DEEP_INSERT_GUIDE.md) | Creating related records in one request |
| [Delta Queries](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/DELTA_QUERIES.md) | Change tracking |
| [Bulk Operations](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/BULK_OPERATIONS.md) | `CreateMultiple`, `UpdateMultiple`, `UpsertMultiple` |
| [Request Options](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/REQUEST_OPTIONS.md) | Bypassing business logic, impersonation, duplicate detection |
| [FetchXML Examples](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/FETCHXML_EXAMPLES.md) | Fluent and raw FetchXML |
| [Lookup References](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/LOOKUP_REFERENCES.md) | Mapping and setting lookups |
| [SetFrom](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/SETFROM.md) | Writing POCOs and leaving out read-only columns |
| [Upsert Conflict Resolution](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/UPSERT_CONFLICT_RESOLUTION.md) | Upsert modes and ETags |

For Dynamics 365 Sales, the `Dataverse.Fluent.Sales` package adds mapped Sales tables and typed wrappers for actions
such as `QualifyLead` and `WinOpportunity`. `Dataverse.Fluent.Schema` checks your models against an environment's
metadata in a test, and the `Dataverse.Fluent.Tool` package's `dataverse-fluent` command does the same from the command
line and fixes the mechanical mistakes.

Licensed under the [MIT License](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/LICENSE).
