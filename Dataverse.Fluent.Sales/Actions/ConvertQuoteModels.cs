using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Sales.Actions;

/// <summary>
/// Request payload for the ConvertQuoteToSalesOrder action.
/// </summary>
public class ConvertQuoteToSalesOrderRequest
{
    [JsonPropertyName("QuoteId")]
    public Guid QuoteId { get; set; }

    /// <summary>The columns of the created order to return.</summary>
    [JsonPropertyName("ColumnSet")]
    public ColumnSetPayload? ColumnSet { get; set; }

    /// <summary>When set, an active quote is closed as won on this date as part of the conversion.</summary>
    [JsonPropertyName("QuoteCloseDate")]
    public DateTimeOffset? QuoteCloseDate { get; set; }

    /// <summary>The status reason to close the quote with: a <c>QuoteStatus</c> value of the Won state.</summary>
    [JsonPropertyName("QuoteCloseStatus")]
    public int? QuoteCloseStatus { get; set; }

    /// <summary>The subject of the quote close activity.</summary>
    [JsonPropertyName("QuoteCloseSubject")]
    public string? QuoteCloseSubject { get; set; }

    /// <summary>The description of the quote close activity.</summary>
    [JsonPropertyName("QuoteCloseDescription")]
    public string? QuoteCloseDescription { get; set; }
}

/// <summary>
/// Represents the column set for specifying which columns to return.
/// </summary>
public class ColumnSetPayload
{
    [JsonPropertyName("AllColumns")]
    public bool AllColumns { get; set; }

    [JsonPropertyName("Columns")]
    public List<string>? Columns { get; set; }
}
