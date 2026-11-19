# Delta Queries (Change Tracking)

Delta queries return only the records that were created, updated or deleted since a previous query. They use an opaque
delta token to resume from the last change point, which makes them a good fit for synchronisation and change
notification.

Compared with polling on `modifiedon`, delta queries only transfer changed records, report deletions explicitly, and
rely on server-side tracking instead of client timestamp logic.

## Prerequisites

- Change tracking is enabled on the table (Power Apps maker portal: table properties > *Advanced options* >
  *Track changes*; classic: **Customize the System** > entity > **Data Services** > **Change tracking**).
- A valid access token with read permission on the table.

Some tables do not support change tracking.

## Basic usage

```csharp
// Initial query: returns the current state and a delta token
var results = await client.Delta<Account>()
    .Select("name", "revenue", "modifiedon")
    .ExecuteAsync();

var token = results.DeltaToken; // persist this

// Later: only what changed since the token
var changes = await client.Delta<Account>()
    .Since(token)
    .ExecuteAsync();

foreach (var account in changes.Value)          // created or updated
    Console.WriteLine($"Changed: {account.Name}");

foreach (var deleted in changes.Deleted)        // deleted: id and reason only
    Console.WriteLine($"Deleted: {deleted.GetEntityId()} ({deleted.Removed?.Reason})");

token = changes.DeltaToken;                     // use for the next poll
```

Each token marks a point in time. Querying with it returns everything that changed after that point, plus a new token:

```
Initial query            -> token T1
  ...changes C1, C2, C3
Query Since(T1)          -> C1, C2, C3 + token T2
  ...changes C4, C5
Query Since(T2)          -> C4, C5 + token T3
```

## Builder methods

| Method | Purpose |
|--------|---------|
| `Delta<T>()` | Create a delta query builder |
| `Since(token)` | Resume from a delta token |
| `Select(...)` | Choose columns |
| `IncludeFormattedValues()` | Return formatted values for option sets and lookups |
| `WithHeaders(...)` | Add custom request headers |
| `ExecuteAsync()` | Execute and return one `DeltaResults<T>` |
| `StreamAsync(pageSize)` | Stream pages, following `@odata.nextLink` |

A builder instance can be executed once; create a new builder for every query.

### `DeltaResults<T>`

| Property | Type | Description |
|----------|------|-------------|
| `Value` | `List<T>` | Created or updated entities |
| `Deleted` | `List<EntityReference>` | References to deleted entities |
| `DeltaToken` | `string?` | Token for the next query |
| `NextLink` | `string?` | Paging link (used internally by `StreamAsync`) |

`DeltaToken` is set only on the last page. `ExecuteAsync()` returns the first page only, so on a table with more rows
than one page (5,000 by default) an initial `ExecuteAsync()` returns a `NextLink` and no token. Use `StreamAsync()` for
the initial sync, and keep the token from the last page.

`EntityReference.GetEntityId()` extracts the `Guid`; `Removed.Reason` gives the deletion reason.

## Polling loop

```csharp
string? token = await tokenStore.GetAsync("accounts");

while (!ct.IsCancellationRequested)
{
    try
    {
        var builder = client.Delta<Account>().Select("accountid", "name", "revenue");
        if (!string.IsNullOrEmpty(token))
            builder = builder.Since(token);

        var results = await builder.ExecuteAsync(ct);

        foreach (var account in results.Value)
            await ProcessChangeAsync(account);
        foreach (var deleted in results.Deleted)
            await ProcessDeletionAsync(deleted.GetEntityId()!.Value);

        token = results.DeltaToken;
        await Task.Delay(TimeSpan.FromMinutes(5), ct);
    }
    catch (DataverseException ex)
    {
        // transient failures are already retried by the client's resilience pipeline
        logger.LogError(ex, "Delta query failed. ActivityId: {ActivityId}", ex.ActivityId);
        await Task.Delay(TimeSpan.FromSeconds(30), ct);
    }
}
```

## Streaming large change sets

```csharp
await foreach (var page in client.Delta<Account>()
    .Since(token)
    .StreamAsync(pageSize: 2000, ct: ct))
{
    await externalSystem.UpsertAsync(page.Value);
    await externalSystem.DeleteAsync(page.Deleted.Select(d => d.GetEntityId()!.Value));

    token = page.DeltaToken;
    await tokenStore.SaveAsync("accounts", token);
}
```

`pageSize` is sent as `Prefer: odata.maxpagesize` on every page (Dataverse caps it at 5000).

## Error handling

An expired token returns HTTP 410 (Gone). Fall back to an initial query:

```csharp
try
{
    return await client.Delta<Account>().Since(token).ExecuteAsync();
}
catch (DataverseException ex) when (ex.StatusCode == HttpStatusCode.Gone)
{
    return await client.Delta<Account>().ExecuteAsync(); // start again from the current state
}
```

| Symptom | Likely cause |
|---------|--------------|
| HTTP 410 Gone | Token expired. Restart with an initial query. |
| HTTP 400 Bad Request | Change tracking not enabled, an unsupported query option, or an invalid token. |
| `DeltaToken` is `null` | More pages follow (`NextLink` is set): use `StreamAsync()`. Otherwise, change tracking is not enabled on the table. |
| Deletions missing | Change tracking not enabled. |

For general error handling see [ERROR_HANDLING.md](ERROR_HANDLING.md).

## Limitations

- **Only `$select`.** Dataverse rejects `$filter`, `$orderby`, `$top` and `$count` on a change-tracking query ("isn't
  supported when Change Tracking is enabled"). `Where`, `OrderBy`, `Top` and `Count` are obsolete and throw
  `NotSupportedException`. Filter and order the returned changes in code, and use `StreamAsync(pageSize)` for page size.
- **The columns are fixed by the initial query.** The token carries its `$select`; a `Select()` with `Since()` is ignored.
- **Tokens expire**, typically after about 90 days.
- **Page size** is at most 5000 records.
- **Deletions** carry only the id and the reason, not the deleted record's data.

## Best practices

1. Persist delta tokens (database or configuration) so they survive restarts.
2. Handle token expiry by falling back to an initial query.
3. Poll every 5-15 minutes; delta queries count toward API limits.
4. Use `Select()` to retrieve only the columns you need.
5. Use `StreamAsync()` for large change sets.
6. Treat deletions as id-only.

## Tests

- Unit tests: `Dataverse.Fluent.Tests/Builders/DeltaBuilderTests.cs`.
- Integration tests: `DeltaQueryTests.cs` in the integration test project. They run on the Sales competitor table, which has
  change tracking enabled by default. See the
  [integration test README](../../Dataverse.Fluent.IntegrationTests/README.md).
