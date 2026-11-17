using System.Text.Json.Serialization;
using Dataverse.Fluent;

namespace Dataverse.Fluent.Sales.Entities;

/// <summary>
/// Dynamics 365 Sales Opportunity entity (opportunity).
/// Inherit from this class to add org-specific custom fields.
/// </summary>
[DataverseEntity("opportunity", "opportunities")]
public class Opportunity
{
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("opportunityid")]
    public virtual Guid OpportunityId { get; set; }

    [JsonPropertyName("name")]
    public virtual string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public virtual string? Description { get; set; }

    [JsonPropertyName("estimatedvalue")]
    public virtual decimal? EstimatedValue { get; set; }

    [JsonPropertyName("actualvalue")]
    public virtual decimal? ActualValue { get; set; }

    [JsonPropertyName("totalamount")]
    public virtual decimal? TotalAmount { get; set; }

    [JsonPropertyName("discountamount")]
    public virtual decimal? DiscountAmount { get; set; }

    [JsonPropertyName("discountpercentage")]
    public virtual decimal? DiscountPercentage { get; set; }

    [JsonPropertyName("freightamount")]
    public virtual decimal? FreightAmount { get; set; }

    [JsonPropertyName("totaltax")]
    public virtual decimal? TotalTax { get; set; }

    [JsonPropertyName("estimatedclosedate")]
    public virtual DateTime? EstimatedCloseDate { get; set; }

    [JsonPropertyName("actualclosedate")]
    public virtual DateTime? ActualCloseDate { get; set; }

    [JsonPropertyName("closeprobability")]
    public virtual int? CloseProbability { get; set; }

    [JsonPropertyName("opportunityratingcode")]
    public virtual int? RatingCode { get; set; }

    [JsonPropertyName("prioritycode")]
    public virtual int? PriorityCode { get; set; }

    [JsonPropertyName("salesstagecode")]
    public virtual int? SalesStageCode { get; set; }

    [JsonPropertyName("stepname")]
    public virtual string? StepName { get; set; }

    [JsonPropertyName("currentsituation")]
    public virtual string? CurrentSituation { get; set; }

    [JsonPropertyName("customerneed")]
    public virtual string? CustomerNeed { get; set; }

    [JsonPropertyName("proposedsolution")]
    public virtual string? ProposedSolution { get; set; }

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

    [DataverseLookup("parentcontactid", "contacts")]
    public virtual Guid? ParentContactId { get; set; }

    [DataverseLookup("parentaccountid", "accounts")]
    public virtual Guid? ParentAccountId { get; set; }

    /// <summary>
    /// The customer (account or contact). Read-only here because the lookup is polymorphic: set it with
    /// <c>SetLookup("customerid_account", "accounts", id)</c> or <c>SetLookup("customerid_contact", "contacts", id)</c>.
    /// </summary>
    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("_customerid_value")]
    public virtual Guid? CustomerId { get; set; }

    [DataverseLookup("pricelevelid", "pricelevels")]
    public virtual Guid? PriceLevelId { get; set; }

    [DataverseLookup("transactioncurrencyid", "transactioncurrencies")]
    public virtual Guid? TransactionCurrencyId { get; set; }

    [DataverseLookup("originatingleadid", "leads")]
    public virtual Guid? OriginatingLeadId { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("_ownerid_value")]
    public virtual Guid? OwnerId { get; set; }

    // ───────────────────────── Navigation properties ─────────────────────────

    /// <summary>
    /// Related opportunity products (1:N). Populated when $expand=product_opportunities is used.
    /// </summary>
    [JsonPropertyName("product_opportunities")]
    public virtual List<OpportunityProduct>? Products { get; set; }
}
