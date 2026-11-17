using System.Text.Json.Serialization;
using Dataverse.Fluent;

namespace Dataverse.Fluent.Sales.Entities;

/// <summary>
/// Dynamics 365 Sales Account entity (account).
/// Inherit from this class to add org-specific custom fields.
/// </summary>
[DataverseEntity("account", "accounts")]
public class Account
{
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("accountid")]
    public virtual Guid AccountId { get; set; }

    [JsonPropertyName("name")]
    public virtual string Name { get; set; } = string.Empty;

    [JsonPropertyName("accountnumber")]
    public virtual string? AccountNumber { get; set; }

    [JsonPropertyName("revenue")]
    public virtual decimal? Revenue { get; set; }

    [JsonPropertyName("numberofemployees")]
    public virtual int? NumberOfEmployees { get; set; }

    [JsonPropertyName("industrycode")]
    public virtual int? IndustryCode { get; set; }

    [JsonPropertyName("description")]
    public virtual string? Description { get; set; }

    [JsonPropertyName("websiteurl")]
    public virtual string? WebsiteUrl { get; set; }

    [JsonPropertyName("telephone1")]
    public virtual string? PhoneNumber { get; set; }

    [JsonPropertyName("telephone2")]
    public virtual string? OtherPhone { get; set; }

    [JsonPropertyName("fax")]
    public virtual string? Fax { get; set; }

    [JsonPropertyName("emailaddress1")]
    public virtual string? EmailAddress { get; set; }

    // ───────────────────────── Address fields ─────────────────────────

    [JsonPropertyName("address1_line1")]
    public virtual string? Address1Line1 { get; set; }

    [JsonPropertyName("address1_line2")]
    public virtual string? Address1Line2 { get; set; }

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

    [DataverseLookup("primarycontactid", "contacts")]
    public virtual Guid? PrimaryContactId { get; set; }

    [DataverseLookup("parentaccountid", "accounts")]
    public virtual Guid? ParentAccountId { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("_ownerid_value")]
    public virtual Guid? OwnerId { get; set; }

    // ───────────────────────── Navigation properties ─────────────────────────

    /// <summary>
    /// Related contacts (1:N). Populated when $expand=contact_customer_accounts is used.
    /// </summary>
    [JsonPropertyName("contact_customer_accounts")]
    public virtual List<Contact>? Contacts { get; set; }

    /// <summary>
    /// Related opportunities (1:N). Populated when $expand=opportunity_customer_accounts is used.
    /// </summary>
    [JsonPropertyName("opportunity_customer_accounts")]
    public virtual List<Opportunity>? Opportunities { get; set; }
}
