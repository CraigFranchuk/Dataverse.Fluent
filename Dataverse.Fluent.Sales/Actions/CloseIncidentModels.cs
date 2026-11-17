using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Sales.Actions;

/// <summary>
/// Represents an incident resolution activity record used in the CloseIncident action.
/// </summary>
public class IncidentResolutionRecord
{
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("timespent")]
    public int? TimeSpent { get; set; }

    [JsonPropertyName("incidentid@odata.bind")]
    public string? IncidentODataBind { get; set; }
}

/// <summary>
/// Request payload for the CloseIncident unbound action.
/// </summary>
public class CloseIncidentRequest
{
    [JsonPropertyName("IncidentResolution")]
    public IncidentResolutionRecord? IncidentResolution { get; set; }

    [JsonPropertyName("Status")]
    public int Status { get; set; }
}
