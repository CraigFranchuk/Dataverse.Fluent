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

    /// <summary>The estimated close date, a date-only column.</summary>
    [JsonPropertyName("estimatedclosedate")]
    public virtual DateOnly? EstimatedCloseDate { get; set; }

    /// <summary>The actual close date, a date-only column.</summary>
    [JsonPropertyName("actualclosedate")]
    public virtual DateOnly? ActualCloseDate { get; set; }

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

    /// <summary>The customer, an account or a contact.</summary>
    [DataverseLookup("customerid")]
    [DataverseLookupTarget("account", "accounts", "customerid_account")]
    [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
    public virtual DataverseReference? CustomerId { get; set; }

    [DataverseLookup("pricelevelid", "pricelevels")]
    public virtual Guid? PriceLevelId { get; set; }

    [DataverseLookup("transactioncurrencyid", "transactioncurrencies")]
    public virtual Guid? TransactionCurrencyId { get; set; }

    [DataverseLookup("originatingleadid", "leads")]
    public virtual Guid? OriginatingLeadId { get; set; }

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
    /// Related opportunity products (1:N). Populated when $expand=product_opportunities is used.
    /// </summary>
    [JsonPropertyName("product_opportunities")]
    public virtual List<OpportunityProduct>? Products { get; set; }
}
