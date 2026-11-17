# Deep Insert

A deep insert creates a record together with related records in one request, as a single transaction: if any record
fails, none is created. Dataverse supports deep insert for creates only; to change existing related records, update
them separately.

## Creating a record with related records

```csharp
var account = new Account { Name = "Contoso", Revenue = 5_000_000 };
var contacts = new List<Contact>
{
    new() { FirstName = "John", LastName = "Doe" },
    new() { FirstName = "Jane", LastName = "Smith" }
};

var result = await client.DeepInsert(account)
    .WithRelated(contacts, "contact_customer_accounts")       // collection-valued navigation property
    .WithRelated(new Contact { LastName = "Wilson" }, "primarycontactid") // single-valued navigation property
    .ExecuteAsync();

Guid accountId = result.Id!.Value;
```

`WithRelated` takes the navigation property name from the parent's point of view:

- A **collection-valued** navigation property (one-to-many) takes a collection: `contact_customer_accounts` for an
  account's contacts, `opportunity_customer_accounts` for its opportunities.
- A **single-valued** navigation property (many-to-one) takes one record: `primarycontactid` creates the account's
  primary contact.

Find the names in the Power Apps maker portal (table > **Relationships**) or in the Web API `$metadata` document.
They are case-sensitive.

`ExecuteAsync()` returns a `DataverseCreateResult<T>`: the new parent's `Id`, and with `ReturnRepresentation()` the
created parent in `Entity` (Dataverse then omits the ID header, so read the key from the entity).

## What is sent

The parent and every related record are written the way `SetFrom` writes them:

- `null` properties are left out;
- properties marked `[DataverseReadOnly(DataverseOperation.Create)]` are left out;
- `[DataverseLookup]` properties are written as `@odata.bind`;
- navigation properties on the POCOs are not written. Add related records with `WithRelated`.

An empty collection adds nothing, so related records can be added conditionally:

```csharp
var insert = client.DeepInsert(account);
if (contacts.Count > 0)
    insert.WithRelated(contacts, "contact_customer_accounts");
await insert.ExecuteAsync();
```

## In a batch

`BatchBuilder.AddDeepInsert(parent, navigationProperty, children)` queues the same request in a changeset.

## Many-to-many relationships

Deep insert does not create many-to-many links. Create the records, then link them with `client.Associate()` or
`BatchBuilder.AddAssociate`.

## Troubleshooting

| Error | Cause |
|-------|-------|
| 400 naming the navigation property | The name is wrong, or does not belong to the parent table |
| 400 "StartObject" / "StartArray" | A single record was passed for a collection-valued property, or a collection for a single-valued one |
| 400 on a system column (`statuscode`, `createdon`) | The POCO sends it; mark it `[DataverseReadOnly]` |
| 403 | The caller cannot create one of the related tables |

See the [Web API documentation on creating related rows](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/create-entity-web-api#create-related-table-rows-in-one-operation).
