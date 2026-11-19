using System.Text.Json.Serialization;

namespace Dataverse.Fluent.IntegrationTests.Models;

/// <summary>
/// The Dynamics 365 Sales competitor table. The delta tests use it because change tracking is enabled on it and the
/// reference environment has no rows in it, so the tests own all of its data.
/// </summary>
[EntitySet("competitors")]
public class Competitor
{
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("competitorid")]
    public Guid CompetitorId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("websiteurl")]
    public string? WebSiteUrl { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("createdon")]
    public DateTime? CreatedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("createdon@OData.Community.Display.V1.FormattedValue")]
    public string? CreatedOnFormatted { get; set; }
}
