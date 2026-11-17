using System.Text.Json.Serialization;
using Dataverse.Fluent;

namespace Dataverse.Fluent.IntegrationTests.Models;

[EntitySet("contacts")]
public class Contact
{
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("contactid")]
    public Guid ContactId { get; set; }

    [JsonPropertyName("firstname")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("lastname")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("emailaddress1")]
    public string? EmailAddress { get; set; }

    [JsonPropertyName("telephone1")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("jobtitle")]
    public string? JobTitle { get; set; }

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
}
