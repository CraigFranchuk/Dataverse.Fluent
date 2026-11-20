using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Testing.Tests;

[DataverseEntity("account", "accounts")]
public sealed class Account
{
    [DataverseReadOnly(DataverseOperation.Create)]
    public Guid AccountId { get; set; }

    public string? Name { get; set; }

    [DataverseColumn("accountnumber")]
    public string? Number { get; set; }

    public decimal? Revenue { get; set; }

    [DataverseLookup("primarycontactid", "contacts")]
    public Guid? PrimaryContactId { get; set; }
}

[DataverseEntity("contact", "contacts")]
public sealed class Contact
{
    [DataverseReadOnly(DataverseOperation.Create)]
    public Guid ContactId { get; set; }

    public string? LastName { get; set; }

    [DataverseLookup("parentcustomerid")]
    [DataverseLookupTarget("account", "accounts", "parentcustomerid_account")]
    [DataverseLookupTarget("contact", "contacts", "parentcustomerid_contact")]
    public DataverseReference? ParentCustomerId { get; set; }
}

public sealed class WhoAmIResponse
{
    public Guid UserId { get; set; }

    [JsonPropertyName("OrganizationId")]
    public Guid OrganizationId { get; set; }
}
