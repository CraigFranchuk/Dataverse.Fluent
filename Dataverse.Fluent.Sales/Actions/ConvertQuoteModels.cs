using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Sales.Actions;

/// <summary>
/// Request payload for the ConvertQuoteToSalesOrder action.
/// </summary>
public class ConvertQuoteToSalesOrderRequest
{
    [JsonPropertyName("QuoteId")]
    public Guid QuoteId { get; set; }

    [JsonPropertyName("ColumnSet")]
    public ColumnSetPayload? ColumnSet { get; set; }
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

/// <summary>
/// Response from ConvertQuoteToSalesOrder containing the created sales order entity.
/// </summary>
public class ConvertQuoteToSalesOrderResponse
{
    [JsonPropertyName("salesorderid")]
    public Guid SalesOrderId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("ordernumber")]
    public string? OrderNumber { get; set; }

    [JsonPropertyName("totalamount")]
    public decimal? TotalAmount { get; set; }
}
