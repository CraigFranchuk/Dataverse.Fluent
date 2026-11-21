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

    /// <summary>The quote number. Dataverse generates it when none is given, and it cannot be changed afterwards.</summary>
    [DataverseReadOnly(DataverseOperation.Update)]
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

    /// <summary>The date the quote expires, a date-only column.</summary>
    [JsonPropertyName("expireson")]
    public virtual DateOnly? ExpiresOn { get; set; }

    /// <summary>The date the quote was closed, a date-only column.</summary>
    [JsonPropertyName("closedon")]
    public virtual DateOnly? ClosedOn { get; set; }

    /// <summary>The revision number, set by Dataverse when a quote is revised.</summary>
    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
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
    public virtual QuoteState StateCode { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statuscode")]
    public virtual QuoteStatus StatusCode { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("versionnumber")]
    public virtual long? VersionNumber { get; set; }

    // ───────────────────────── Lookups ─────────────────────────

    /// <summary>The customer, an account or a contact.</summary>
    [DataverseLookup("customerid")]
    [DataverseLookupTarget("account", "accounts", "customerid_account")]
    [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
    public virtual DataverseReference? CustomerId { get; set; }

    [DataverseLookup("opportunityid", "opportunities")]
    public virtual Guid? OpportunityId { get; set; }

    [DataverseLookup("pricelevelid", "pricelevels")]
    public virtual Guid? PriceLevelId { get; set; }

    [DataverseLookup("transactioncurrencyid", "transactioncurrencies")]
    public virtual Guid? TransactionCurrencyId { get; set; }

    /// <summary>
    /// The owner, a user or a team. <c>SetFrom</c> sends it on create only, so writing back a retrieved record never
    /// reassigns the record; assign an existing record with <c>Set(x =&gt; x.OwnerId, reference)</c>.
    /// </summary>
    [DataverseReadOnly(DataverseOperation.Update)]
    [DataverseLookup("ownerid")]
    [DataverseLookupTarget("systemuser", "systemusers", "ownerid")]
    [DataverseLookupTarget("team", "teams", "ownerid")]
    public virtual DataverseReference? OwnerId { get; set; }

    // ───────────────────────── Navigation properties ─────────────────────────

    /// <summary>
    /// Related quote details / line items (1:N). Populated when $expand=quote_details is used.
    /// </summary>
    [JsonPropertyName("quote_details")]
    public virtual List<QuoteDetail>? Details { get; set; }
}
