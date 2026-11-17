# SetFrom and Read-Only Properties

`SetFrom(entity)` copies a POCO's properties into a create, update or upsert payload. Models usually contain columns
that must not be written, such as the primary key, `createdon` or `statecode`, so there are several ways to leave
properties out.

## What SetFrom writes

| Property | Written as |
|----------|-----------|
| A column with a value | `"column": value` |
| `null` | Left out. Use `Set("column", null)` to clear a column. |
| `[DataverseReadOnly]` for the operation | Left out (see below) |
| `[DataverseLookup]` | `"navigation@odata.bind": "/entityset(id)"`, or left out when `null` or `Guid.Empty` |
| A navigation property (a mapped entity, or a collection of one) | Left out, so a record retrieved with `$expand` can be written back |
| `[JsonIgnore]` | Left out |

A lookup mapped only by its read name, `[JsonPropertyName("_x_value")]`, cannot be written. Map it with
`[DataverseLookup]`, or mark it `[DataverseReadOnly(DataverseOperation.CreateAndUpdate)]` and set it with `SetLookup`.

## Marking read-only properties

```csharp
[DataverseEntity("account", "accounts")]
public class Account
{
    [DataverseReadOnly(DataverseOperation.Create)]          // assigned by Dataverse
    public Guid AccountId { get; set; }

    public string? Name { get; set; }

    public decimal? Revenue { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]          // Dataverse sets the initial state
    public int? StateCode { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)] // system-managed
    public DateTime? CreatedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    public DateTime? ModifiedOn { get; set; }
}

var created = await client.Entity<Account>()
    .SetFrom(new Account { Name = "Contoso Ltd", Revenue = 2_000_000 })
    .CreateAsync();
```

`DataverseOperation` is a flags enum: `Create`, `Update`, and `CreateAndUpdate` (also available as `All`).

### Which properties each operation leaves out

| Operation | Leaves out properties marked |
|-----------|------------------------------|
| `CreateAsync`, `UpsertAsync(PreventUpdate)` | `Create` |
| `UpdateAsync`, `UpsertAsync()`, `UpsertAsync(PreventCreate)`, `UpsertAsync(IfUnmodifiedSince)` | `Create` or `Update` |

Any operation that can update a record also leaves out properties marked only for `Create`, so a default value such as
`statecode = 0` is never written over an existing record. A value set explicitly with `Set()` is always sent, even for
a read-only property: that is how to change `statecode`.

Batch operations (`AddCreate`, `AddUpdate`, `AddUpsert`) and deep inserts apply the same rules.

## Excluding properties for one call

By expression:

```csharp
await client.Entity<Account>(id)
    .SetFromExcept(account, a => a.Revenue, a => a.Name)
    .UpdateAsync();
```

By logical name, for exclusions decided at run time:

```csharp
await client.Entity<Account>(id)
    .SetFrom(account, "revenue")
    .UpdateAsync();
```

Both also apply the `[DataverseReadOnly]` rules.

## Overriding a copied value

`Set` after `SetFrom` replaces the copied value:

```csharp
await client.Entity<Account>(id)
    .SetFrom(account)
    .Set(a => a.Name, "Override")
    .UpdateAsync();
```

## Turning the read-only rules off

`SetFrom(entity, excludeReadOnly: false)` copies every non-null property, including those marked `[DataverseReadOnly]`.
Navigation properties and `[JsonIgnore]` members are still left out.

```csharp
await client.Entity<Account>(id)
    .SetFrom(account, excludeReadOnly: false)
    .UpdateAsync();
```
