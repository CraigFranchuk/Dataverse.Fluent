using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Sales.Actions;

/// <summary>
/// Request payload for the CalculatePrice action (bound to an entity).
/// Typically bound to opportunity, quote, salesorder, or invoice.
/// </summary>
public class CalculatePriceRequest
{
    [JsonPropertyName("Target")]
    public CalculatePriceTarget? Target { get; set; }
}

/// <summary>
/// Target entity reference for the CalculatePrice action.
/// </summary>
public class CalculatePriceTarget
{
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; set; } = string.Empty;

    [JsonPropertyName("opportunityid")]
    public Guid? OpportunityId { get; set; }

    [JsonPropertyName("quoteid")]
    public Guid? QuoteId { get; set; }
}
