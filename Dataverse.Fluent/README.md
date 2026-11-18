# Dataverse.Fluent

A fluent, strongly typed C# client for the Microsoft Dataverse Web API, for .NET 10 and later.

The client has no metadata dependency: it never downloads, caches or parses the environment's entity model. Your
POCOs and the calls you write are the model, so a client is ready as soon as it is constructed. See
[Design Philosophy](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/DESIGN_PHILOSOPHY.md).

## Features

- Queries with C# lambdas translated to OData (`$filter`, `$select`, `$orderby`, `$expand`), paging and streaming
- Create, retrieve, update, upsert and delete, by ID or alternate key, with ETag concurrency control
- Batches with transactional changesets, and deep insert of related records
- FetchXML, through a fluent builder or as raw XML, with automatic paging
- Aggregations (`$apply`), delta queries (change tracking), actions and functions, notes and file attachments
- Lookups and related records mapped on the POCO, so a retrieved record can be written back unchanged
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

### Query

```csharp
var accounts = await client.Query<Account>()
    .Select(a => new { a.Name, a.Revenue })
    .Where(a => a.Revenue > 1_000_000 && a.Name!.StartsWith("Con"))
    .OrderBy(a => a.Revenue, descending: true)
    .Top(10)
    .GetListAsync();
```

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
| `+`, `-`, `*`, `/`, `%` | `add`, `sub`, `mul`, `div`, `mod` |
| `Contains`, `StartsWith`, `EndsWith` on a string column | `contains`, `startswith`, `endswith` |
| `ToLower`, `ToUpper`, `Trim`, `Length`, `Substring`, `IndexOf`, `string.Concat`, `string.IsNullOrEmpty` | the OData string functions |
| `list.Contains(a.Column)` | `column in (...)` |
| `a.Nullable.HasValue` | `column ne null` |
| `a.Related.Column` | `related/column` |
| `DataverseFunctions.Under(a.ParentAccountId, id)`, `EqualUserId`, `LastXDays`, ... | `Microsoft.Dynamics.CRM.*` query functions |

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

### Counts

`GetListWithCountAsync()` returns a page together with `@odata.count`. `CountAsync()` sends `$count`, which Dataverse
caps at 5,000; count larger sets with an aggregate:

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
[Upsert Conflict Resolution](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/UPSERT_CONFLICT_RESOLUTION.md).

### Lookups

A `[DataverseLookup]` property is read as `_{column}_value` and written as `{navigation}@odata.bind`, so `SetFrom` and
`Set` handle it:

```csharp
await client.Entity<Account>(id).Set(a => a.PrimaryContactId, contactId).UpdateAsync();
await client.Entity<Account>(id).Set(a => a.PrimaryContactId, null).UpdateAsync(); // clears it
```

A polymorphic lookup such as a contact's parent customer names its target:

```csharp
await client.Entity<Contact>()
    .Set(c => c.LastName, "Doe")
    .SetLookup("parentcustomerid_account", "accounts", accountId)
    .CreateAsync();
```

`SetFrom` never writes navigation properties (related records loaded with `$expand`), so a retrieved record can be
written back as is. See [Lookup References](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/LOOKUP_REFERENCES.md).

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

See [Request Options](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/REQUEST_OPTIONS.md).

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
[FetchXML Examples](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/FETCHXML_EXAMPLES.md).

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
[Delta Queries](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/DELTA_QUERIES.md).

## Actions, functions and notes

```csharp
var me = await client.Function<WhoAmIResponse>("WhoAmI").ExecuteAsync();

await client.Action<WinOpportunityRequest, object>("WinOpportunity").WithRequest(request).ExecuteAsync();

var note = await client.Annotation()
    .Regarding<Account>(accountId)
    .WithSubject("Signed contract")
    .WithFile("contract.pdf", pdfBytes)
    .CreateAsync();
```

## Testing your code

Fake the one-method `IDataverseTransport` interface and wrap it in a `DataverseClient`. Your code then runs the real
builders, and the fake sees the exact requests they produce.

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
| [Getting Started](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/GETTING_STARTED.md) | App registration, authentication, a first program |
| [API Reference](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/API_REFERENCE.md) | Entry points and builders |
| [Best Practices](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/BEST_PRACTICES.md) | Production guidance |
| [Design Philosophy](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/DESIGN_PHILOSOPHY.md) | Why there is no metadata dependency |
| [Custom Type Mapping](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/CUSTOM_TYPE_MAPPING.md) | Using generated types |
| [Error Handling](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/ERROR_HANDLING.md) | `DataverseException` and what is retried |
| [Actions and Functions](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/ACTIONS_AND_FUNCTIONS.md) | Bound and unbound actions and functions |
| [Annotations](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/ANNOTATIONS.md) | Notes and file attachments |
| [Deep Insert](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/DEEP_INSERT_GUIDE.md) | Creating related records in one request |
| [Delta Queries](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/DELTA_QUERIES.md) | Change tracking |
| [Request Options](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/REQUEST_OPTIONS.md) | Bypassing business logic, impersonation, duplicate detection |
| [FetchXML Examples](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/FETCHXML_EXAMPLES.md) | Fluent and raw FetchXML |
| [Lookup References](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/LOOKUP_REFERENCES.md) | Mapping and setting lookups |
| [SetFrom](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/SETFROM.md) | Writing POCOs and leaving out read-only columns |
| [Upsert Conflict Resolution](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/Dataverse.Fluent/docs/UPSERT_CONFLICT_RESOLUTION.md) | Upsert modes and ETags |

For Dynamics 365 Sales, the `Dataverse.Fluent.Sales` package adds mapped Sales tables and typed wrappers for actions
such as `QualifyLead` and `WinOpportunity`.

Licensed under the [MIT License](https://github.com/cfranchuk/Dataverse.Fluent/blob/main/LICENSE).
