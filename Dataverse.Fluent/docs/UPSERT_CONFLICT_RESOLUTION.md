# Upsert Conflict Resolution

An upsert creates a record when none exists with the given ID or alternate key, and updates it otherwise. In the
Dataverse Web API it is a single `PATCH`; conditional headers decide what happens when the record does or does not
exist. `UpsertAsync` takes an `UpsertConflictResolution` that sets those headers.

| Strategy | Header sent | Result |
|----------|-------------|--------|
| `Upsert` (default) | none | Creates or updates |
| `PreventUpdate` | `If-None-Match: *` | Creates; 412 Precondition Failed if the record exists |
| `PreventCreate` | `If-Match: *` | Updates; 404 Not Found if the record does not exist |
| `IfUnmodifiedSince` | `If-Match: <etag>` | Updates only if the record still has that ETag; 412 otherwise |

`UpsertAsync` returns a `DataverseUpsertResult<T>`:

| Property | Value |
|----------|-------|
| `Id` | The record's ID, from the `OData-EntityId` header |
| `Entity` | The record, when `ReturnRepresentation()` was requested |
| `Created` | `true` if the upsert created the record, `false` if it updated one, `null` if Dataverse did not say |

Dataverse distinguishes a create from an update only when a representation is returned (201 or 200); without one,
both return 204 and `Created` is `null`. It also omits `OData-EntityId` when a representation is returned, so read
the key from `Entity` in that case.

## Upsert

```csharp
// By alternate key. Dataverse identifies the record by its key in OData-EntityId, so result.Id is null;
// ask for a representation to learn the ID without another request.
var result = await client.Entity<Account>()
    .UsingAlternateKey(a => a.AccountNumber, "ACC-001")
    .Set(a => a.Name, "Contoso Ltd")
    .ReturnRepresentation(a => a.AccountId)
    .UpsertAsync();

Guid accountId = result.Entity!.AccountId;

// By ID, from a POCO
await client.Entity<Account>(accountId)
    .SetFrom(account)
    .UpsertAsync();
```

An upsert by alternate key is idempotent, which makes it the right choice for imports and synchronisation that may be
retried.

## Create only

```csharp
try
{
    await client.Entity<Account>()
        .UsingAlternateKey(a => a.AccountNumber, "ACC-001")
        .Set(a => a.Name, "New Account")
        .UpsertAsync(UpsertConflictResolution.PreventUpdate);
}
catch (DataverseException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
{
    logger.LogInformation("ACC-001 already exists; left unchanged.");
}
```

Use it when a record must not be overwritten, for example when importing records keyed by an external ID.

## Update only

```csharp
try
{
    await client.Entity<Account>(accountId)
        .Set(a => a.Revenue, 2_000_000m)
        .UpsertAsync(UpsertConflictResolution.PreventCreate);
}
catch (DataverseException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    logger.LogWarning("Account {AccountId} does not exist.", accountId);
}
```

This is what `UpdateAsync` does as well: it always sends `If-Match` and never creates a record.

## Optimistic concurrency

Read the record with its ETag, then make the write conditional on it:

```csharp
var current = await client.Entity<Account>(accountId).RetrieveWithMetadataAsync();

try
{
    await client.Entity<Account>(accountId)
        .WithETag(current.ETag!)
        .Set(a => a.Revenue, current.Entity!.Revenue + 500_000m)
        .UpsertAsync(UpsertConflictResolution.IfUnmodifiedSince);
}
catch (DataverseException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
{
    // Someone else changed the record: read it again, reapply the change, and retry.
}
```

`UpdateAsync` sends the ETag from `WithETag` in the same way. `IfUnmodifiedSince` without `WithETag` throws
`InvalidOperationException` before anything is sent.

A retry loop for conflicts reads the record again on every attempt:

```csharp
for (var attempt = 1; ; attempt++)
{
    var current = await client.Entity<Account>(accountId).RetrieveWithMetadataAsync();
    var revenue = Recalculate(current.Entity!);

    try
    {
        await client.Entity<Account>(accountId)
            .WithETag(current.ETag!)
            .Set(a => a.Revenue, revenue)
            .UpdateAsync();
        break;
    }
    catch (DataverseException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed && attempt < 3)
    {
        // changed since it was read; try again with the new version
    }
}
```

## Read-only columns

`SetFrom` leaves out properties marked `[DataverseReadOnly]` according to what the operation can do. `PreventUpdate`
can only create, so it leaves out properties marked for `Create`. Every other strategy can update an existing record,
so it leaves out properties marked for `Create` or `Update`; a create-only default such as `statecode = 0` is never
written over an existing record. See [SetFrom](SETFROM.md).

## In a batch

`ToUpsertRequest(resolution)` builds the same request without sending it, for `BatchBuilder.Add`:

```csharp
var batch = client.Batch();
foreach (var row in rows)
{
    batch.Add(client.Entity<Account>()
        .UsingAlternateKey(a => a.AccountNumber, row.Number)
        .Set(a => a.Name, row.Name)
        .ToUpsertRequest());
}
var result = await batch.ExecuteAsync();
```

## ETags

Dataverse ETags are weak ETags such as `W/"12345678"`. Read them from `RetrieveWithMetadataAsync().ETag`, or map
`@odata.etag` on a POCO with `[JsonPropertyName("@odata.etag")]`. Do not build one yourself: Dataverse rejects an ETag
it did not issue.
