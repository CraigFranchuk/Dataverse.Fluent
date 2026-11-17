using System.Collections.Generic;
using System.Text.Json.Serialization;
using Dataverse.Fluent;

namespace Dataverse.Fluent.IntegrationTests.Models;

[EntitySet("accounts")]
public class Account
{
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("accountid")]
    public Guid AccountId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("revenue")]
    public decimal? Revenue { get; set; }

    [JsonPropertyName("numberofemployees")]
    public int? NumberOfEmployees { get; set; }

    [JsonPropertyName("telephone1")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("emailaddress1")]
    public string? EmailAddress { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("createdon")]
    public DateTime CreatedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("modifiedon")]
    public DateTime ModifiedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statecode")]
    public int StateCode { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statuscode")]
    public int StatusCode { get; set; }

    /// <summary>
    /// Navigation property for related contacts (1:N relationship).
    /// Populated when $expand=contact_customer_accounts is used.
    /// </summary>
    [JsonPropertyName("contact_customer_accounts")]
    public List<Contact>? Contacts { get; set; }
}
