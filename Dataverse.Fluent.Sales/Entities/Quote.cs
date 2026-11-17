using System.Text.Json.Serialization;
using Dataverse.Fluent;

namespace Dataverse.Fluent.Sales.Entities;

/// <summary>
/// Dynamics 365 Sales Quote entity (quote).
/// Inherit from this class to add org-specific custom fields.
/// </summary>
[DataverseEntity("quote", "quotes")]
public class Quote
{
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("quoteid")]
    public virtual Guid QuoteId { get; set; }

    [JsonPropertyName("name")]
    public virtual string Name { get; set; } = string.Empty;

    [JsonPropertyName("quotenumber")]
    public virtual string? QuoteNumber { get; set; }

    [JsonPropertyName("description")]
    public virtual string? Description { get; set; }

    [JsonPropertyName("totalamount")]
    public virtual decimal? TotalAmount { get; set; }

    [JsonPropertyName("totallineitemamount")]
    public virtual decimal? TotalLineItemAmount { get; set; }

    [JsonPropertyName("totaldiscountamount")]
    public virtual decimal? TotalDiscountAmount { get; set; }

    [JsonPropertyName("totaltax")]
    public virtual decimal? TotalTax { get; set; }

    [JsonPropertyName("discountamount")]
    public virtual decimal? DiscountAmount { get; set; }

    [JsonPropertyName("discountpercentage")]
    public virtual decimal? DiscountPercentage { get; set; }

    [JsonPropertyName("freightamount")]
    public virtual decimal? FreightAmount { get; set; }

    [JsonPropertyName("effectivefrom")]
    public virtual DateTime? EffectiveFrom { get; set; }

    [JsonPropertyName("effectiveto")]
    public virtual DateTime? EffectiveTo { get; set; }

    [JsonPropertyName("expireson")]
    public virtual DateTime? ExpiresOn { get; set; }

    [JsonPropertyName("closedatedon")]
    public virtual DateTime? CloseDatedon { get; set; }

    [JsonPropertyName("revisionnumber")]
    public virtual int? RevisionNumber { get; set; }

    // ───────────────────────── System fields ─────────────────────────

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("createdon")]
    public virtual DateTime CreatedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("modifiedon")]
    public virtual DateTime ModifiedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statecode")]
    public virtual int StateCode { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statuscode")]
    public virtual int StatusCode { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("versionnumber")]
    public virtual long? VersionNumber { get; set; }

    // ───────────────────────── Lookups ─────────────────────────

    /// <summary>
    /// The customer (account or contact). Read-only here because the lookup is polymorphic: set it with
    /// <c>SetLookup("customerid_account", "accounts", id)</c> or <c>SetLookup("customerid_contact", "contacts", id)</c>.
    /// </summary>
    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("_customerid_value")]
    public virtual Guid? CustomerId { get; set; }

    [DataverseLookup("opportunityid", "opportunities")]
    public virtual Guid? OpportunityId { get; set; }

    [DataverseLookup("pricelevelid", "pricelevels")]
    public virtual Guid? PriceLevelId { get; set; }

    [DataverseLookup("transactioncurrencyid", "transactioncurrencies")]
    public virtual Guid? TransactionCurrencyId { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("_ownerid_value")]
    public virtual Guid? OwnerId { get; set; }

    // ───────────────────────── Navigation properties ─────────────────────────

    /// <summary>
    /// Related quote details / line items (1:N). Populated when $expand=quote_details is used.
    /// </summary>
    [JsonPropertyName("quote_details")]
    public virtual List<QuoteDetail>? Details { get; set; }
}
