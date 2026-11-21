using System.Text.Json.Serialization;
using Dataverse.Fluent;

namespace Dataverse.Fluent.Sales.Entities;

/// <summary>
/// Dynamics 365 Sales Lead entity (lead).
/// Inherit from this class to add org-specific custom fields.
/// </summary>
[DataverseEntity("lead", "leads")]
public class Lead
{
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("leadid")]
    public virtual Guid LeadId { get; set; }

    [JsonPropertyName("subject")]
    public virtual string Subject { get; set; } = string.Empty;

    [JsonPropertyName("firstname")]
    public virtual string? FirstName { get; set; }

    [JsonPropertyName("lastname")]
    public virtual string LastName { get; set; } = string.Empty;

    /// <summary>The full name, composed by Dataverse from the first and last names.</summary>
    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("fullname")]
    public virtual string? FullName { get; set; }

    [JsonPropertyName("companyname")]
    public virtual string? CompanyName { get; set; }

    [JsonPropertyName("jobtitle")]
    public virtual string? JobTitle { get; set; }

    [JsonPropertyName("emailaddress1")]
    public virtual string? EmailAddress { get; set; }

    [JsonPropertyName("telephone1")]
    public virtual string? BusinessPhone { get; set; }

    [JsonPropertyName("mobilephone")]
    public virtual string? MobilePhone { get; set; }

    [JsonPropertyName("websiteurl")]
    public virtual string? WebsiteUrl { get; set; }

    [JsonPropertyName("description")]
    public virtual string? Description { get; set; }

    /// <summary>The lead company's annual revenue (<c>revenue</c>).</summary>
    [JsonPropertyName("revenue")]
    public virtual decimal? AnnualRevenue { get; set; }

    /// <summary>The estimated value of the lead (<c>estimatedamount</c>, labelled "Est. Value").</summary>
    [JsonPropertyName("estimatedamount")]
    public virtual decimal? EstimatedAmount { get; set; }

    [JsonPropertyName("budgetamount")]
    public virtual decimal? BudgetAmount { get; set; }

    [JsonPropertyName("leadsourcecode")]
    public virtual LeadSource? LeadSourceCode { get; set; }

    [JsonPropertyName("industrycode")]
    public virtual Industry? IndustryCode { get; set; }

    [JsonPropertyName("purchasetimeframe")]
    public virtual PurchaseTimeFrame? PurchaseTimeFrame { get; set; }

    [JsonPropertyName("decisionmaker")]
    public virtual bool? DecisionMaker { get; set; }

    [JsonPropertyName("donotphone")]
    public virtual bool? DoNotPhone { get; set; }

    [JsonPropertyName("donotemail")]
    public virtual bool? DoNotEmail { get; set; }

    // ───────────────────────── Address fields ─────────────────────────

    [JsonPropertyName("address1_line1")]
    public virtual string? Address1Line1 { get; set; }

    [JsonPropertyName("address1_city")]
    public virtual string? Address1City { get; set; }

    [JsonPropertyName("address1_stateorprovince")]
    public virtual string? Address1StateOrProvince { get; set; }

    [JsonPropertyName("address1_postalcode")]
    public virtual string? Address1PostalCode { get; set; }

    [JsonPropertyName("address1_country")]
    public virtual string? Address1Country { get; set; }

    // ───────────────────────── System fields ─────────────────────────

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("createdon")]
    public virtual DateTime CreatedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("modifiedon")]
    public virtual DateTime ModifiedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statecode")]
    public virtual LeadState StateCode { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statuscode")]
    public virtual LeadStatus StatusCode { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("versionnumber")]
    public virtual long? VersionNumber { get; set; }

    // ───────────────────────── Lookups ─────────────────────────

    [DataverseLookup("campaignid", "campaigns")]
    public virtual Guid? CampaignId { get; set; }

    [DataverseLookup("parentcontactid", "contacts")]
    public virtual Guid? ParentContactId { get; set; }

    [DataverseLookup("parentaccountid", "accounts")]
    public virtual Guid? ParentAccountId { get; set; }

    /// <summary>
    /// The owner, a user or a team. <c>SetFrom</c> sends it on create only, so writing back a retrieved record never
    /// reassigns the record; assign an existing record with <c>Set(x =&gt; x.OwnerId, reference)</c>.
    /// </summary>
    [DataverseReadOnly(DataverseOperation.Update)]
    [DataverseLookup("ownerid")]
    [DataverseLookupTarget("systemuser", "systemusers", "ownerid")]
    [DataverseLookupTarget("team", "teams", "ownerid")]
    public virtual DataverseReference? OwnerId { get; set; }
}
