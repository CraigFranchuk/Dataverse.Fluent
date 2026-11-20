# Dataverse.Fluent.Sales

Typed helpers for Dynamics 365 Sales, built on [Dataverse.Fluent](https://github.com/CraigFranchuk/Dataverse.Fluent).

- **Entity models** for accounts, contacts, leads, opportunities, quotes and cases, with the Dataverse logical
  names already mapped. The properties are virtual, so you can inherit from a model to add your organisation's
  custom columns.
- **`SalesClient`**, which wraps the Sales actions: `QualifyLeadAsync`, `WinOpportunityAsync`,
  `LoseOpportunityAsync`, `CloseIncidentAsync`, `ConvertQuoteToSalesOrderAsync` and the price calculations.

## Usage

```csharp
services.AddDynamicsSalesClient(
    "https://yourorg.api.crm.dynamics.com/api/data/v9.2",
    () => GetTokenAsync());

public class LeadService(SalesClient sales)
{
    public Task QualifyAsync(Guid leadId) => sales.QualifyLeadAsync(leadId);
}
```

Anything the helpers do not cover is available through `SalesClient.Dataverse`, the underlying
`IDataverseClient`. The models carry their table mapping, so `sales.Dataverse.Query<Account>()` and
`FetchBuilder<OpportunityProduct>()` need no entity set or logical name.

Single-table lookups (`Account.PrimaryContactId`, `Opportunity.ParentAccountId`, ...) are mapped with
`[DataverseLookup]`, so `SetFrom` writes them as `@odata.bind`. The polymorphic customer and owner lookups
(`CustomerId`, `Contact.ParentCustomerId`, `OwnerId`) are `DataverseReference` properties: a read returns the table
and the ID, and a write binds to whichever table the reference points at. Filter them by ID with
`x.CustomerId!.Id == id`.

```csharp
await sales.Dataverse.Entity<Incident>()
    .SetFrom(new Incident { Title = "Printer jam", CustomerId = DataverseReference.To<Account>(accountId) })
    .CreateAsync();
```

`SetFrom` sends `OwnerId` when it creates a record but not when it updates one, so writing back a retrieved record never
reassigns it. Reassign a record explicitly with `Set(x => x.OwnerId, new DataverseReference("team", teamId))`.

Date-only columns (`Opportunity.EstimatedCloseDate` and `ActualCloseDate`, `Quote.ExpiresOn` and `ClosedOn`) are
`DateOnly?`, the only form Dataverse accepts for them. Columns Dataverse sets itself, such as `FullName`, the quote's
revision number and the case and quote numbers after creation, are marked `[DataverseReadOnly]`, so `SetFrom` on a
retrieved record does not send values Dataverse would silently ignore.

Other environments are registered with `AddKeyedDynamicsSalesClient(name, options => ...)` and resolved with
`[FromKeyedServices(name)] SalesClient`.
