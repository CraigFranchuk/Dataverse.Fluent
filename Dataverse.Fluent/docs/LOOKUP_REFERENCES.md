# Setting Lookup Fields

How to map and set lookup columns (references to other records) by ID, by alternate key, or by composite key. Each
method writes the `@odata.bind` entry for you.

## Mapping Lookups on the Model

Put `[DataverseLookup]` on a `Guid?` property to make a lookup part of the model. The client then:

- **Reads, selects and filters** it as `_{column}_value`:
  `Where(c => c.ParentAccountId == id)` becomes `_parentaccountid_value eq '...'`.
- **Writes** it as a bind: `SetFrom(contact)` and `Set(c => c.ParentAccountId, id)` send
  `"parentaccountid@odata.bind": "/accounts(...)"`. An unset lookup (`null` or `Guid.Empty`) is not written by
  `SetFrom`; `Set(c => c.ParentAccountId, null)` clears it.
- **Uses the logical name in FetchXML** (`<attribute name="parentaccountid" />`).

```csharp
[DataverseEntity("contact", "contacts")]
public class Contact
{
    [DataverseLookup("parentaccountid", "accounts")]
    public Guid? ParentAccountId { get; set; }

    // A custom lookup's navigation property is usually its schema name:
    [DataverseLookup("new_regionid", "new_regions", NavigationProperty = "new_RegionId")]
    public Guid? RegionId { get; set; }

    // A related record from $expand is read, but never written or selected.
    [JsonPropertyName("parentaccountid")]
    public Account? ParentAccount { get; set; }
}
```

## Polymorphic Lookups

A **polymorphic** lookup (customer, owner, regarding) can point at more than one table, and has one navigation
property per target. Map it as a `DataverseReference`: `[DataverseLookup("column")]`, plus one
`[DataverseLookupTarget(logicalName, entitySet, navigationProperty)]` per table it can point at. The library downloads
no metadata, so the targets are declared on the model:

```csharp
[DataverseEntity("contact", "contacts")]
public class Contact
{
    [DataverseLookup("parentcustomerid")]
    [DataverseLookupTarget("account", "accounts", "parentcustomerid_account")]
    [DataverseLookupTarget("contact", "contacts", "parentcustomerid_contact")]
    public DataverseReference? ParentCustomerId { get; set; }

    // Both owner targets share one navigation property.
    [DataverseReadOnly(DataverseOperation.Create)]
    [DataverseLookup("ownerid")]
    [DataverseLookupTarget("systemuser", "systemusers", "ownerid")]
    [DataverseLookupTarget("team", "teams", "ownerid")]
    public DataverseReference? OwnerId { get; set; }
}
```

A `DataverseReference` holds the table's logical name and the record's ID:

```csharp
contact.ParentCustomerId = DataverseReference.To<Account>(accountId);     // logical name from [DataverseEntity]
contact.ParentCustomerId = new DataverseReference("contact", otherContactId);

if (contact.ParentCustomerId?.LogicalName == "account") { /* ... */ }
```

- **Writes:** `SetFrom`, `Set`, batches and bulk operations send the bind of the matching target, e.g.
  `"parentcustomerid_account@odata.bind": "/accounts(...)"`. A reference to a table that is not a target throws
  `InvalidOperationException`. An unset reference is not written by `SetFrom`, and `Set(c => c.ParentCustomerId, null)`
  clears the lookup.
- **Reads:** the ID comes from `_parentcustomerid_value`, and the table from its
  `Microsoft.Dynamics.CRM.lookuplogicalname` annotation. Queries, retrieves, FetchXML, delta queries, batch retrieves
  and writes that return the record request that annotation automatically when the type, or a related record it
  expands, has a polymorphic lookup. A retrieved record can therefore be written back unchanged.
- **Filters** use the ID: `Where(c => c.ParentCustomerId!.Id == accountId)` becomes
  `_parentcustomerid_value eq '...'`, and `c.ParentCustomerId == null` tests for no value. OData cannot filter on
  the table of a lookup, so a filter on `LogicalName` throws `NotSupportedException`.

The navigation property names are in the Dataverse Web API reference for each table, or in `$metadata`. For a
regarding lookup they include the activity table, e.g. `regardingobjectid_account_task`.

`SetLookup`, below, still works for a polymorphic lookup set without a model property.

## By GUID (ID)

When you already have the target entity's ID:

```csharp
await client.Entity<Contact>()
    .Set(c => c.FirstName, "John")
    .Set(c => c.LastName, "Doe")
    .SetLookup("parentcustomerid_account", "accounts", parentAccountId)
    .CreateAsync();
```

## By Alternate Key (String-Based)

When you know the alternate key value but not the GUID:

```csharp
await client.Entity<Contact>()
    .Set(c => c.FirstName, "Jane")
    .Set(c => c.LastName, "Smith")
    .SetLookupByAlternateKey("parentcustomerid_account", "accounts", 
        "accountnumber", "ACC-001")
    .CreateAsync();
```

## By Alternate Key (Type-Safe)

For compile-time safety and IntelliSense support:

```csharp
await client.Entity<Contact>()
    .Set(c => c.FirstName, "Bob")
    .Set(c => c.LastName, "Johnson")
    .SetLookupByAlternateKey<Account, string>(
        "parentcustomerid_account", 
        "accounts",
        a => a.AccountNumber,  // Type-safe property selection
        "ACC-002")
    .CreateAsync();
```

## By Composite Key

For entities with multi-part keys (e.g., D365 Finance & Operations):

```csharp
await client.Entity<OrderLine>()
    .Set(ol => ol.Quantity, 5)
    .SetLookupByAlternateKeys("productid", "products",
        new Dictionary<string, object>
        {
            { "dataareaid", "USMF" },
            { "itemnumber", "ITEM-001" }
        })
    .CreateAsync();
```

## Methods and the binding they generate

| Method | Generated `@odata.bind` value |
|--------|-------------------------------|
| `SetLookup(field, entitySet, id)` | `/accounts(12345678-1234-1234-1234-123456789012)` |
| `SetLookupByAlternateKey(field, entitySet, keyName, value)` | `/accounts(accountnumber='ACC-001')` |
| `SetLookupByAlternateKey<TRelated, TKey>(field, entitySet, a => a.AccountNumber, value)` | same as above; the key name comes from the expression and honours `[JsonPropertyName]` |
| `SetLookupByAlternateKeys(field, entitySet, dictionary)` | `/products(dataareaid='USMF',itemnumber='A0001')` |

Strings are single-quoted, numbers and GUIDs are not. Invalid arguments throw `ArgumentNullException` or `ArgumentException`
before any request is sent.

### Characters in alternate key values

Dataverse handles some characters in a key differently depending on where the key goes. The library encodes each
place correctly, and rejects a key it cannot send before any request is made:

| Used in | Works | Rejected with `ArgumentException` |
|---------|-------|----------------------------------|
| A bind (`SetLookupByAlternateKey`): the key is written as is | spaces, `'`, `&`, `+`, `%`, Unicode, most punctuation | `/`, `\`, `#`, `?` |
| Addressing a record (`UsingAlternateKey`, `Associate().From(...)`): the key is in the URL | everything else, including spaces, `'`, `#`, `%`, `:`, `*`, Unicode. Keys with `&`, `?` or `+` are sent inside a `$batch`, because IIS rejects them in a URL path | `/`, `\` |

A record whose key contains a rejected character can still be reached by its ID, or found with a filter.

## Common Lookup Field Names

| Lookup Type | Dataverse Field Name | Example |
|-------------|---------------------|---------|
| Parent Account | `parentcustomerid_account` | Contact → Account |
| Owning User | `ownerid` | Most entities → User |
| Primary Contact | `primarycontactid` | Account → Contact |
| Currency | `transactioncurrencyid` | Many entities → Currency |

## Finding Lookup Field Names

1. **Power Apps Maker Portal**: Tables → Columns → Look for "Lookup" type
2. **OData Metadata**: `https://yourorg.api.crm.dynamics.com/api/data/v9.2/$metadata`
3. **Pattern**: Usually `{relationshipname}_` or `{navigationproperty}`
4. **Add suffix**: Append `@odata.bind` when using raw `Set()` method

## Finding Entity Set Names

The entity set name is usually the plural of the table's logical name, but not always, so look it up rather than
guessing: in Power Apps (table > **Properties** > **Advanced options**), or in `$metadata` (`EntitySet Name="..."`).

| Table | Entity set |
|-------|------------|
| account | accounts |
| opportunity | opportunities |
| systemuser | systemusers |
| transactioncurrency | transactioncurrencies |

## Complete Examples

### Example 1: Create Contact with Parent Account

```csharp
// By ID
var contact1 = await client.Entity<Contact>()
    .Set(c => c.FirstName, "Alice")
    .Set(c => c.LastName, "Williams")
    .SetLookup("parentcustomerid_account", "accounts", accountId)
    .ReturnRepresentation()
    .CreateAsync();

// By Alternate Key (string-based)
var contact2 = await client.Entity<Contact>()
    .Set(c => c.FirstName, "Bob")
    .Set(c => c.LastName, "Martinez")
    .SetLookupByAlternateKey("parentcustomerid_account", "accounts",
        "accountnumber", "ACC-12345")
    .ReturnRepresentation()
    .CreateAsync();

// By Alternate Key (type-safe)
var contact3 = await client.Entity<Contact>()
    .Set(c => c.FirstName, "Carol")
    .Set(c => c.LastName, "Davis")
    .SetLookupByAlternateKey<Account, string>("parentcustomerid_account", "accounts",
        a => a.AccountNumber, "ACC-67890")
    .ReturnRepresentation()
    .CreateAsync();
```

### Example 2: Create Opportunity with Multiple Lookups

```csharp
var opportunity = await client.Entity<Opportunity>()
    .Set(o => o.Name, "Big Deal 2024")
    .Set(o => o.EstimatedValue, 500000m)
    .SetLookup("customerid_account", "accounts", accountId)  // Parent account
    .SetLookup("ownerid", "systemusers", userId)  // Owner
    .SetLookup("transactioncurrencyid", "transactioncurrencies", currencyId)  // Currency
    .ReturnRepresentation()
    .CreateAsync();
```

### Example 3: Import External Data

```csharp
// When importing from external system with external IDs
foreach (var externalOrder in externalSystem.GetOrders())
{
    await client.Entity<Order>()
        .Set(o => o.OrderNumber, externalOrder.OrderNumber)
        .Set(o => o.OrderDate, externalOrder.Date)
        .SetLookupByAlternateKey("customerid_account", "accounts",
            "accountnumber", externalOrder.CustomerCode)  // Map external ID
        .SetLookupByAlternateKey<Product, string>("productid", "products",
            p => p.ProductNumber, externalOrder.ProductSKU)  // Map external SKU
        .CreateAsync();
}
```

### Example 4: Update Existing Entity's Lookup

```csharp
// Change parent account of a contact
await client.Entity<Contact>()
    .WithId(contactId)
    .SetLookupByAlternateKey("parentcustomerid_account", "accounts",
        "accountnumber", "NEW-ACCOUNT-001")
    .UpdateAsync();
```

## Tips

**DO:**
- Use type-safe methods for better IntelliSense and refactoring
- Verify alternate keys exist in Dataverse before using them
- Use meaningful variable names for clarity
- Handle errors with try-catch blocks

**DON'T:**
- Don't manually construct `@odata.bind` strings (use these methods instead)
- Don't guess entity set names; look them up
- Don't mix up the lookup field name with the entity set name
- Don't use SetLookup methods for N:N relationships (use Associate instead)

## Error Handling

```csharp
try
{
    await client.Entity<Contact>()
        .Set(c => c.FirstName, "Test")
        .Set(c => c.LastName, "User")
        .SetLookupByAlternateKey("parentcustomerid_account", "accounts",
            "accountnumber", "INVALID-NUMBER")
        .CreateAsync();
}
catch (DataverseException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    Console.WriteLine($"Account with that number not found: {ex.Message}");
}
catch (DataverseException ex)
{
    Console.WriteLine($"Error: {ex.ErrorCode} - {ex.Message}");
}
```

## Troubleshooting

| Problem | Check |
|---------|-------|
| 400 naming the lookup | The navigation property name is wrong. For a custom lookup it is usually the schema name (`new_RegionId`), and for a polymorphic lookup it names the target (`parentcustomerid_account`). |
| 400 about the key | The alternate key is not defined on the target table, or the key columns are wrong. |
| 404 | No record matches the ID or alternate key value. |
| The wrong record type is linked | The entity set does not match the target table. |

## See Also

- [API Reference: single records](API_REFERENCE.md#single-records)
- [Dataverse Alternate Keys Documentation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/define-alternate-keys-entity)
- [Dataverse Lookup Fields Documentation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/associate-disassociate-entities-using-web-api)
