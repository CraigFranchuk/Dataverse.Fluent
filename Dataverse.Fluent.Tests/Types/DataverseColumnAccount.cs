namespace Dataverse.Fluent.Tests;

/// <summary>
/// A POCO that uses only [DataverseColumn] attributes (no [JsonPropertyName]).
/// Used to verify the library works without any System.Text.Json attributes.
/// </summary>
[EntitySet("testaccounts")]
internal class DataverseColumnAccount
{
    [DataverseColumn("accountid")]
    [DataverseReadOnly(DataverseOperation.Create)]
    public Guid AccountId { get; set; }

    [DataverseColumn("name")]
    public string Name { get; set; } = string.Empty;

    [DataverseColumn("revenue")]
    public decimal Revenue { get; set; }

    [DataverseColumn("numberofemployees")]
    public int NumberOfEmployees { get; set; }

    [DataverseColumn("createdon")]
    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    public DateTime CreatedOn { get; set; }

    [DataverseColumn("statecode")]
    public int StateCode { get; set; }
}
