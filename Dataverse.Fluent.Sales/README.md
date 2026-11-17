# Dataverse.Fluent.Sales

Typed helpers for Dynamics 365 Sales, built on [Dataverse.Fluent](https://github.com/cfranchuk/Dataverse.Fluent).

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
(`CustomerId`, `Contact.ParentCustomerId`, `OwnerId`) are read-only on the models; set them with
`SetLookup("customerid_account", "accounts", id)` and the like.

Other environments are registered with `AddKeyedDynamicsSalesClient(name, options => ...)` and resolved with
`[FromKeyedServices(name)] SalesClient`.
