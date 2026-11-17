# Best Practices

Guidance for using the client in production. Most of what a Dataverse client needs (retries, throttling, token
caching, timeouts) is built in; this page covers what is left to you.

## Client lifetime

Create one client per environment and reuse it. In an application with dependency injection, register it with
`AddDataverseClient` (or `AddKeyedDataverseClient` for more environments) and inject `IDataverseClient`. The
registration uses `IHttpClientFactory`, so connections are pooled and rotated for you.

Without dependency injection, construct the client once (for example as a singleton or a static) and dispose it when
the application stops. The client is safe to use from several threads. Builders are not: create a new builder for
every request (`client.Query<T>()`, `client.Entity<T>()`, ...), and do not keep one in a field.

## Authentication

- Prefer a `TokenCredential` (`ManagedIdentityCredential` in Azure, `DefaultAzureCredential` across environments)
  over secrets. No secret in configuration is the safest kind.
- Keep any secret that you do need in a secret store such as Azure Key Vault, or in user secrets during development.
- Give the application user only the security roles it needs, rather than System Administrator.
- Do not add your own token cache. The client caches tokens until shortly before they expire and refreshes a token
  that Dataverse rejects.

## Retries and throttling

The default pipeline retries for you:

- GET requests on transient failures (5xx, timeouts, connection errors).
- Any request on HTTP 429, after the `Retry-After` delay that Dataverse's service-protection limits return.
- Writes only when Dataverse is known not to have processed them (429, or the connection never opened). A write that
  timed out or failed with a 5xx may already have been applied, so it is not sent again.

Tune it rather than wrapping calls in your own retry policy:

```csharp
services.AddDataverseClient(baseUrl, credential, configureResilience: o =>
{
    o.MaxRetryAttempts = 5;
    o.TotalRequestTimeout = TimeSpan.FromMinutes(20); // room for long Retry-After waits
});
```

If you add an application-level retry for writes, make the write idempotent first, for example an upsert by
alternate key instead of a create. Otherwise a retry after a timeout can create a duplicate.

Service-protection limits apply per user, so heavy parallel work from one application user will be throttled.
Prefer batches and fewer, larger requests to many parallel small ones.

## Queries

- **Select only the columns you need.** Without `Select`, Dataverse returns every column of the table.
  `SelectAll()` selects the columns your POCO maps, which is usually what you want for a full record.
- **Filter on the server.** `Where` runs in Dataverse; filtering a full `GetAllAsync()` result in memory transfers
  every row first.
- **Stream large results.** `StreamAsync()` holds one page at a time; `GetAllAsync()` holds every row.
- **Use `Top` to limit and `PageSize` to page.** `Top` caps the number of rows; it is not a page size.
- **Prefer lambdas to raw OData strings.** A lambda is checked by the compiler and fails before it is sent when it
  cannot be translated. Use raw strings for the OData that lambdas cannot express.
- **Count with an aggregate** when the count can exceed 5,000; `CountAsync()` is capped there by Dataverse.

## Writes

- **Use upsert by alternate key** for imports and synchronisation. It is idempotent, so it is safe to retry.
- **Use ETags** when two writers can change the same record: retrieve with `RetrieveWithMetadataAsync()`, then
  `WithETag(result.ETag)` on the update. A conflict returns HTTP 412 instead of overwriting.
- **Use `UpdateAsync` to update and `UpsertAsync` to create-or-update.** `UpdateAsync` never creates a record.
- **Batch related writes.** A changeset applies its operations atomically, and a batch saves round trips. Keep
  batches to a few hundred operations; Dataverse limits a batch to 1,000.

## Models

- Map each table once with `[DataverseEntity]`, and mark system columns `[DataverseReadOnly]` so `SetFrom` never sends
  them:

  ```csharp
  [DataverseEntity("account", "accounts")]
  public class Account
  {
      [DataverseReadOnly(DataverseOperation.Create)]
      public Guid AccountId { get; set; }

      public string? Name { get; set; }

      [DataverseLookup("primarycontactid", "contacts")]
      public Guid? PrimaryContactId { get; set; }

      [DataverseReadOnly(DataverseOperation.Create)]  // set state explicitly with Set(), not with SetFrom
      public int? StateCode { get; set; }

      [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
      public DateTime? ModifiedOn { get; set; }
  }
  ```

- Use nullable types for every column you do not always set, so an unset value is left out of payloads instead of
  writing a default (`0`, `0001-01-01`) over real data.
- Choice (option set) columns are integers in the Web API. Map them as `int?`, or as an enum with integer values; do
  not use `JsonStringEnumConverter`.
- Map lookups with `[DataverseLookup]`. A lookup that points at more than one table (customer, owner, regarding) is
  set with `SetLookup` instead.
- Purpose-built POCOs that model a few columns for one use case are normal and encouraged.

## Errors and diagnostics

- Catch `DataverseException` and branch on `StatusCode` or `ErrorCode`, not on message text.
- Log `ActivityId`. It identifies the request in Dataverse's server logs, and Microsoft support asks for it.
- The client logs each request through `ILogger` and emits OpenTelemetry traces and metrics under the name in
  `DataverseTelemetry.ActivitySourceName` / `MeterName`. Request URLs are logged without their query string, because
  `$filter` values can be personal data; set `DataverseClientOptions.IncludeQueryInTelemetry` for local debugging.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(DataverseTelemetry.ActivitySourceName))
    .WithMetrics(m => m.AddMeter(DataverseTelemetry.MeterName));
```

## Cancellation

Every asynchronous method takes a `CancellationToken`. Pass the request's token (for example from an ASP.NET Core
action) so abandoned requests stop.

## Testing

- **Unit tests:** fake `IDataverseTransport`, which has one method, and wrap it in `new DataverseClient(transport)`.
  Your code then runs the real builders, and the fake can assert on the exact URL, headers and payload of each
  request. A mock of `IDataverseClient` cannot do this, because the builders are concrete classes. See
  [Testing your code](../README.md#testing-your-code).
- **Integration tests:** run them against a dedicated environment, never production, with an application user that
  has only the privileges the tests need. Delete the records each test creates.
