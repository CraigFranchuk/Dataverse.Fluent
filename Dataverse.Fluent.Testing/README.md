# Dataverse.Fluent.Testing

A fake transport for unit testing code that uses [Dataverse.Fluent](https://www.nuget.org/packages/Dataverse.Fluent).
Your code runs the real fluent builders, the fake answers their requests from routes your test sets up, and the test
asserts on the exact requests that were sent. No Dataverse environment and no mocking library are needed.

```bash
dotnet add package Dataverse.Fluent.Testing
```

## A first test

```csharp
using Dataverse.Fluent.Testing;

[Fact]
public async Task Onboarding_CreatesAContactForTheAccount()
{
    var accountId = Guid.NewGuid();
    var fake = new FakeDataverseTransport();
    fake.OnGet("accounts").RespondWithEntities([new Account { AccountId = accountId, Name = "Contoso" }]);
    fake.OnPost("contacts").RespondWithCreated();

    var service = new OnboardingService(fake.CreateClient());   // takes IDataverseClient
    await service.OnboardAsync("Contoso", "Smith");

    var query = fake.Requests[0];
    Assert.Equal("name eq 'Contoso'", query.Query["$filter"]);

    var create = fake.Requests[1];
    Assert.Equal("Smith", (string?)create.Payload!["lastname"]);
    Assert.Equal($"/accounts({accountId})", (string?)create.Payload["parentcustomerid_account@odata.bind"]);
}
```

To inject the fake through dependency injection, register the client it creates:
`services.AddSingleton<IDataverseClient>(fake.CreateClient())`. For Dynamics 365 Sales,
`new SalesClient(fake.CreateClient())` works the same way.

## Routes

A route matches a method and a path. The path is relative to the base URL, decoded, and without the query string:
`accounts`, `accounts(00000000-0000-0000-0000-000000000001)`, `accounts/Microsoft.Dynamics.CRM.CreateMultiple`, `WhoAmI`.
`*` matches any text, and case is ignored.

```csharp
fake.OnGet("accounts")            // queries
fake.OnGet("accounts(*)")         // retrieves by ID or alternate key
fake.OnPost("accounts")           // creates
fake.OnPatch("accounts(*)")       // updates and upserts
fake.OnDelete("accounts(*)")      // deletes
fake.OnPost("WinOpportunity")     // unbound actions
fake.On(HttpMethod.Get, "...")    // any method
fake.On(r => r.Path.EndsWith("/$ref"))   // any condition
```

`When` narrows a route, for example by query option:

```csharp
fake.OnGet("accounts")
    .When(r => r.GetQueryOption("$filter")?.Contains("statecode eq 0") == true)
    .RespondWithEntities(activeAccounts);
```

When several routes match a request, the one added last answers it. A test can therefore override a default route
set up in a shared fixture. A request that no route matches throws `InvalidOperationException`, and the message names
the request and lists the routes. A test cannot pass on a request it did not expect.

## Responses

| Method | Answers |
|--------|---------|
| `RespondWithEntities(records, nextLink?, totalCount?)` | Queries, FetchXML, delta queries: `{ "value": [...] }` |
| `RespondWithEntity(record, statusCode?, etag?)` | Retrieves, function and action results, creates that return the record |
| `RespondWithCreated(id?)` | Creates and upserts: 204 with the `OData-EntityId` header (a new ID per request when omitted) |
| `RespondWithBulkCreated()` | `CreateMultiple`: one new ID per record in the request |
| `RespondWithNoContent()` | Updates, deletes, associations |
| `RespondWithJson(json, statusCode?)` | Any JSON body |
| `RespondWithError(statusCode, errorCode?, message?)` | A Dataverse error, thrown as `DataverseException` |
| `Throw(exception)` | A failure that never reached Dataverse, such as `HttpRequestException` |
| `Respond(request => response)` | Anything else, built from the request |

Records are written in the shape Dataverse returns them, so the builders read them back into your types. That
includes mapped column names, lookups as `_x_value`, and a `DataverseReference` with its table.

Each `Respond…` call queues one response. Requests get them in order, and the last one repeats. That is how paging
and retry tests work:

```csharp
fake.OnGet("accounts")
    .RespondWithEntities(firstPage, nextLink: $"{fake.BaseUrl}/accounts?$skiptoken=2")
    .RespondWithEntities(secondPage);

var all = await client.Query<Account>().GetAllAsync();
```

A route with no response fails the request that matches it.

## Errors

```csharp
fake.OnPatch("accounts(*)").RespondWithError(HttpStatusCode.PreconditionFailed, "0x80060882", "The version has changed");

var ex = await Assert.ThrowsAsync<DataverseException>(() => service.RenameAsync(id, "Fabrikam"));
Assert.Equal(HttpStatusCode.PreconditionFailed, ex.StatusCode);
```

The exception has the status code, `ErrorCode`, message and an `ActivityId`, the same as one from the HTTP transport.

## Assertions

`fake.Requests` lists every request in the order it was sent, and `route.Requests` lists the requests one route
answered. Each `FakeRequest` has:

| Member | Holds |
|--------|-------|
| `Method`, `Url` | The method, and the URL as the builder sent it |
| `Path` | The decoded path relative to the base URL, without the query |
| `Query`, `GetQueryOption(name)` | The decoded query options: `Query["$filter"]`, `Query["$select"]`, ... |
| `Headers`, `GetHeader(name)` | Headers such as `Prefer`, `If-Match`, `MSCRM.BypassBusinessLogicExecution` |
| `Payload` | The JSON body of a write, as a copy |
| `Body` | The body as text |

## Batches

A `$batch` request is split into its operations. Each operation is answered by the routes like a request of its own,
and the fake returns the multipart response Dataverse would send:

- A failed operation fails its changeset. The other operations in it are reported as rolled back, and the operations
  after it in that changeset are not run.
- Without `ContinueOnError()`, the batch stops at the first failure. With it, later changesets still run.

`fake.Requests` holds the batch as one request, with its operations in `BatchOperations`. `fake.Operations` holds
every operation, whether it was sent on its own or inside a batch, so a test can assert on what was written without
caring how it was sent:

```csharp
var creates = fake.Operations.Where(r => r.Method == HttpMethod.Post && r.Path == "contacts");
Assert.Equal(3, creates.Count());
```

Each batch operation has a `ContentId`, and its `Batch` property points to the batch it was part of. To answer the
batch request yourself, for example to make the whole batch fail, add a route for `POST $batch`.

The fake is thread-safe, so it also works with parallel bulk operations
(`Bulk<T>().MaxDegreeOfParallelism(4)`).

## What the fake does not do

The fake does not store data, so a create is not visible to a later query. It also does not evaluate filters: every
route returns exactly the records you give it, whatever the request's `$filter`. To check that your code builds the
right filter, assert on `Query["$filter"]`. To check how Dataverse itself behaves, run integration tests against a test
environment.

Licensed under the [MIT License](https://github.com/CraigFranchuk/Dataverse.Fluent/blob/main/LICENSE).
