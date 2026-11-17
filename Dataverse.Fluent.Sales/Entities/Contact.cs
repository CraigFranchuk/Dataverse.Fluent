using System.Text.Json.Serialization;
using Dataverse.Fluent;

namespace Dataverse.Fluent.Sales.Entities;

/// <summary>
/// Dynamics 365 Sales Contact entity (contact).
/// Inherit from this class to add org-specific custom fields.
/// </summary>
[DataverseEntity("contact", "contacts")]
public class Contact
{
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("contactid")]
    public virtual Guid ContactId { get; set; }

    [JsonPropertyName("firstname")]
    public virtual string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("lastname")]
    public virtual string LastName { get; set; } = string.Empty;

    [JsonPropertyName("fullname")]
    public virtual string? FullName { get; set; }

    [JsonPropertyName("jobtitle")]
    public virtual string? JobTitle { get; set; }

    [JsonPropertyName("department")]
    public virtual string? Department { get; set; }

    [JsonPropertyName("emailaddress1")]
    public virtual string? EmailAddress { get; set; }

    [JsonPropertyName("emailaddress2")]
    public virtual string? EmailAddress2 { get; set; }

    [JsonPropertyName("telephone1")]
    public virtual string? BusinessPhone { get; set; }

    [JsonPropertyName("telephone2")]
    public virtual string? HomePhone { get; set; }

    [JsonPropertyName("mobilephone")]
    public virtual string? MobilePhone { get; set; }

    [JsonPropertyName("fax")]
    public virtual string? Fax { get; set; }

    [JsonPropertyName("description")]
    public virtual string? Description { get; set; }

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

    /// <summary>
    /// The customer (account or contact). Read-only here because the lookup is polymorphic: set it with
    /// <c>SetLookup("parentcustomerid_account", "accounts", id)</c> or <c>SetLookup("parentcustomerid_contact", "contacts", id)</c>.
    /// </summary>
    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("_parentcustomerid_value")]
    public virtual Guid? ParentCustomerId { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("_ownerid_value")]
    public virtual Guid? OwnerId { get; set; }
}
