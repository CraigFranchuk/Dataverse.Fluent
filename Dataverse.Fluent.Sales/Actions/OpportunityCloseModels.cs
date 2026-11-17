using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Sales.Actions;

/// <summary>
/// Represents an opportunity close activity record used in WinOpportunity and LoseOpportunity actions.
/// </summary>
public class OpportunityCloseRecord
{
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("actualrevenue")]
    public decimal? ActualRevenue { get; set; }

    [JsonPropertyName("actualend")]
    public DateTime? ActualEnd { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("opportunityid@odata.bind")]
    public string? OpportunityODataBind { get; set; }
}

/// <summary>
/// Request payload for the WinOpportunity unbound action.
/// </summary>
public class WinOpportunityRequest
{
    [JsonPropertyName("OpportunityClose")]
    public OpportunityCloseRecord? OpportunityClose { get; set; }

    [JsonPropertyName("Status")]
    public int Status { get; set; }
}

/// <summary>
/// Request payload for the LoseOpportunity unbound action.
/// </summary>
public class LoseOpportunityRequest
{
    [JsonPropertyName("OpportunityClose")]
    public OpportunityCloseRecord? OpportunityClose { get; set; }

    [JsonPropertyName("Status")]
    public int Status { get; set; }
}
