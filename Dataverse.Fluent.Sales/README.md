# Dataverse.Fluent.Sales

Typed helpers for Dynamics 365 Sales, built on [Dataverse.Fluent](https://github.com/CraigFranchuk/Dataverse.Fluent).

- **Entity models** for accounts, contacts, leads, opportunities, quotes, orders, invoices, their line items,
  products, price lists and cases, with the Dataverse logical names already mapped. The properties are virtual, so
  you can inherit from a model to add your organisation's custom columns.
- **`SalesClient`**, which wraps the Sales actions: `QualifyLeadAsync`, `WinOpportunityAsync`,
  `LoseOpportunityAsync`, `WinQuoteAsync`, `CloseQuoteAsync`, `ConvertQuoteToSalesOrderAsync`,
  `FulfillSalesOrderAsync`, `CancelSalesOrderAsync`, `ConvertSalesOrderToInvoiceAsync`, `CloseIncidentAsync` and
  the price calculations.

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

### Quote to invoice

```csharp
await sales.WinQuoteAsync(quoteId);                                   // the quote must be active
SalesOrder order = await sales.ConvertQuoteToSalesOrderAsync(quoteId);
Invoice invoice = await sales.ConvertSalesOrderToInvoiceAsync(order.SalesOrderId);
await sales.FulfillSalesOrderAsync(order.SalesOrderId, status: SalesOrderStatus.Partial);
```

The conversions return the created record as a model. Pass `columns` to return only the columns you need.

### Parameters the short forms do not offer

Each action also has an overload that takes its request object, which carries every parameter the action has. The
short form calls it, so both send the same request:

```csharp
// Qualify a lead for an account that already exists
await sales.QualifyLeadAsync(leadId, new QualifyLeadRequest
{
    CreateOpportunity = true,
    Status = (int)LeadStatus.Qualified,
    OpportunityCustomerId = ActionRecord.Reference<Account>(accountId)
});

// Convert an active quote and close it as won in the same call
await sales.ConvertQuoteToSalesOrderAsync(new ConvertQuoteToSalesOrderRequest
{
    QuoteId = quoteId,
    ColumnSet = new ColumnSetPayload { AllColumns = true },
    QuoteCloseDate = DateTimeOffset.UtcNow,
    QuoteCloseStatus = (int)QuoteStatus.Won
});
```

The request classes keep `Status` as an `int`, because that is what the actions take; cast the enum.

The methods are virtual. To change an action everywhere, derive from `SalesClient` and override the overload that
takes the request:

```csharp
public class MySalesClient(IDataverseClient dataverse) : SalesClient(dataverse)
{
    public override Task<QualifyLeadResponse> QualifyLeadAsync(Guid leadId, QualifyLeadRequest request, CancellationToken ct = default)
    {
        request.SourceCampaignId = ActionRecord.Reference("campaign", CampaignId);
        return base.QualifyLeadAsync(leadId, request, ct);
    }
}
```

Register it in place of the default: `services.AddScoped<SalesClient>(sp => new MySalesClient(sp.GetRequiredService<IDataverseClient>()))`.

### Everything else

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

State, status reason and the other standard choice columns are enums with the values Dynamics 365 Sales ships:
`LeadState` and `LeadStatus`, `OpportunityState`, `OpportunityStatus` and `OpportunityRating`, `QuoteState`,
`SalesOrderStatus`, `InvoiceState`, `ProductState`, `PaymentTerms`, `ShippingMethod`, `IncidentStatus`,
`IncidentPriority`, `CaseOrigin`, `LeadSource`, `Industry` and so on. They are read and written as
the option's number, and filters send the number:

```csharp
var open = await sales.Dataverse.Query<Opportunity>()
    .Where(o => o.StateCode == OpportunityState.Open && o.RatingCode == OpportunityRating.Hot)
    .GetAllAsync();

await sales.LoseOpportunityAsync(opportunityId, status: OpportunityStatus.OutSold);
```

An option your organisation added has no enum member. It is still read, as its number, and you write it with a cast:
`(LeadStatus)100000001`. `Incident.CaseTypeCode`, `Incident.SeverityCode`, `Opportunity.PriorityCode` and
`Opportunity.SalesStageCode` stay `int?`, because their options are placeholders that each organisation defines.

Date-only columns (`Opportunity.EstimatedCloseDate` and `ActualCloseDate`, `Quote.ExpiresOn` and `ClosedOn`,
`Invoice.DueDate`, `Product.ValidFromDate` and `ValidToDate`) are
`DateOnly?`, the only form Dataverse accepts for them. Columns Dataverse sets itself, such as `FullName`, the quote's
revision number and the case and quote numbers after creation, are marked `[DataverseReadOnly]`, so `SetFrom` on a
retrieved record does not send values Dataverse would silently ignore. The models are checked against a live
environment with [Dataverse.Fluent.Schema](https://www.nuget.org/packages/Dataverse.Fluent.Schema); run it on a model
you derive from them to check your custom columns too.

Other environments are registered with `AddKeyedDynamicsSalesClient(name, options => ...)` and resolved with
`[FromKeyedServices(name)] SalesClient`.
