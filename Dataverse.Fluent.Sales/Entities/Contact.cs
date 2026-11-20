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

    /// <summary>The full name, composed by Dataverse from the first and last names.</summary>
    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
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

    /// <summary>The customer, an account or a contact.</summary>
    [DataverseLookup("parentcustomerid")]
    [DataverseLookupTarget("account", "accounts", "parentcustomerid_account")]
    [DataverseLookupTarget("contact", "contacts", "parentcustomerid_contact")]
    public virtual DataverseReference? ParentCustomerId { get; set; }

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
