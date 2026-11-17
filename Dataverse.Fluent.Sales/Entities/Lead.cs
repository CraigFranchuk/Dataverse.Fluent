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

    [JsonPropertyName("revenue")]
    public virtual decimal? EstimatedRevenue { get; set; }

    [JsonPropertyName("budgetamount")]
    public virtual decimal? BudgetAmount { get; set; }

    [JsonPropertyName("leadsourcecode")]
    public virtual int? LeadSourceCode { get; set; }

    [JsonPropertyName("industrycode")]
    public virtual int? IndustryCode { get; set; }

    [JsonPropertyName("purchasetimeframe")]
    public virtual int? PurchaseTimeFrame { get; set; }

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
    public virtual int StateCode { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statuscode")]
    public virtual int StatusCode { get; set; }

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

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("_ownerid_value")]
    public virtual Guid? OwnerId { get; set; }
}
