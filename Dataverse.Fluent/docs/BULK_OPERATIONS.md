# Bulk Operations

`Bulk<T>()` writes many records of one table with the Dataverse bulk operation messages: `CreateMultiple`,
`UpdateMultiple` and `UpsertMultiple`. Microsoft recommends them for high-volume writes. They are much faster than one
request per record, and faster than a `$batch`, because Dataverse processes each request's records together.

```csharp
var result = await client.Bulk<Account>()
    .ChunkSize(500)
    .MaxDegreeOfParallelism(4)
    .CreateAsync(accounts);

result.ThrowIfAnyFailed();
Guid firstId = result.Ids[0]; // in the order passed in
```

## Operations

| Method | Message | Identifies each record by |
|--------|---------|---------------------------|
| `CreateAsync(records)` | `CreateMultiple` | (new records; the result holds their IDs) |
| `UpdateAsync(records, a => a.AccountId)` | `UpdateMultiple` | Primary key |
| `UpsertAsync(records, a => a.AccountId)` | `UpsertMultiple` | Primary key: updated when it exists, otherwise created with that ID |
| `UpsertByAlternateKeyAsync(records, a => a.AccountNumber)` | `UpsertMultiple` | Alternate key, sent as `@odata.id` |

A composite alternate key is written as an anonymous type: `UpsertByAlternateKeyAsync(records, a => new { a.Code, a.Region })`.
The Web API does not support alternate keys with `UpdateMultiple`.

Records are serialized as by [`SetFrom`](SETFROM.md):

- `null` properties are left out, so an update sends only the columns you set.
- Lookups mapped with `[DataverseLookup]` are written as `@odata.bind`.
- Properties marked `[DataverseReadOnly]` for the operation are removed. Updates and upserts also leave out
  create-only columns such as `statecode`.

Each record is sent with `@odata.type`, which uses the table's logical name from `[DataverseEntity]` or a name
resolver. Pass the names explicitly for a type that has neither: `client.Bulk<T>("new_records", "new_record")`.

The keys are checked before anything is sent. A record without a key value, or two records with the same key, throw
`ArgumentException`. This matters because Dataverse silently applies only the first duplicate in `UpdateMultiple` and
rejects the whole request in `UpsertMultiple`. Alternate keys are compared case-insensitively, as Dataverse compares
them.

## Chunks and parallelism

The records are sent in chunks of `ChunkSize` (default 100). Microsoft suggests these sizes:

- **Standard tables:** start at 100 to 1,000 records per request when the records are small and no plug-ins run.
- **Elastic tables:** send 100 records per request.

Larger chunks are more efficient, but a request that exceeds Dataverse's size or time limits fails as a whole. Lower
the chunk size if that happens. A single attempt times out after 150 seconds by default; see
`DataverseResilienceOptions.AttemptTimeout`.

`MaxDegreeOfParallelism` (default 1) sends several chunks at once. That raises throughput, and the resilience pipeline
waits out service-protection responses (HTTP 429). Dataverse allows up to 52 concurrent requests per user, and limits
the total execution time per user, so a handful of parallel requests is usually the right range.

## Failures

On a standard table, each chunk is one transaction. If any record in a chunk fails, Dataverse writes none of that
chunk. Earlier chunks stay written. On an elastic table, a failed chunk may be partly written.

A failed chunk does not throw. The result reports each chunk:

```csharp
var result = await client.Bulk<Account>().ChunkSize(200).CreateAsync(accounts);

foreach (var chunk in result.Chunks)
    Console.WriteLine($"{chunk.StartIndex}..{chunk.StartIndex + chunk.Count - 1}: {chunk.Status} {chunk.Error?.Message}");

result.ThrowIfAnyFailed(); // BulkOperationException listing the records not written
```

| `Status` | Meaning |
|----------|---------|
| `Succeeded` | Every record in the chunk was written. `Ids` holds the created IDs for a create. |
| `Failed` | The request failed, and `Error` holds the exception (usually a `DataverseException`). When the error is a timeout or a network failure, the chunk's outcome is unknown. |
| `NotSent` | An earlier chunk failed, so this one was not sent. |

By default, no new chunk is sent after a failure; chunks already in flight still complete. `ContinueOnError()` sends
every chunk regardless.

If you need per-record error handling on a standard table, retry the failed chunk's records with a
[batch](../README.md#batches) and continue-on-error. Microsoft suggests this fallback only when most records are
expected to succeed.

## With request options

[Request options](REQUEST_OPTIONS.md) apply to every chunk, which is the usual setup for a data load:

```csharp
var result = await client
    .WithOptions(o =>
    {
        o.BypassBusinessLogic = BusinessLogicBypass.CustomSync | BusinessLogicBypass.CustomAsync;
        o.BypassPowerAutomateFlows = true;
    })
    .Bulk<Account>()
    .ChunkSize(1000)
    .MaxDegreeOfParallelism(4)
    .UpsertByAlternateKeyAsync(accounts, a => a.AccountNumber);
```

## Limitations

- Not every standard table supports these messages; Dataverse rejects the request for one that does not. To check a
  table, query `sdkmessagefilters` as described in Microsoft's
  [bulk operations article](https://learn.microsoft.com/power-apps/developer/data-platform/bulk-operations#availability-with-standard-tables).
- `DeleteMultiple` exists only for elastic tables and is not covered. To delete in bulk from a standard table, use a
  batch or the `BulkDelete` action.
- Dataverse does not report which records `UpsertMultiple` created.
- Every record counts toward the Power Platform request entitlement, as a single create or update would.
