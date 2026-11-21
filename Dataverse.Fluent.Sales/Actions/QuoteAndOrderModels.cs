using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Sales.Actions;

/// <summary>
/// The quote close activity that the WinQuote and CloseQuote actions create.
/// </summary>
public class QuoteCloseRecord
{
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("actualend")]
    public DateTime? ActualEnd { get; set; }

    /// <summary>The quote to close, as <c>/quotes(&lt;id&gt;)</c>.</summary>
    [JsonPropertyName("quoteid@odata.bind")]
    public string? QuoteODataBind { get; set; }
}

/// <summary>
/// Request payload for the WinQuote unbound action. The quote must be active.
/// </summary>
public class WinQuoteRequest
{
    [JsonPropertyName("QuoteClose")]
    public QuoteCloseRecord? QuoteClose { get; set; }

    /// <summary>The status reason of the won quote: a <c>QuoteStatus</c> value of the Won state.</summary>
    [JsonPropertyName("Status")]
    public int Status { get; set; }
}

/// <summary>
/// Request payload for the CloseQuote unbound action, which closes a quote as lost, canceled or revised.
/// </summary>
public class CloseQuoteRequest
{
    [JsonPropertyName("QuoteClose")]
    public QuoteCloseRecord? QuoteClose { get; set; }

    /// <summary>The status reason of the closed quote: a <c>QuoteStatus</c> value of the Closed state.</summary>
    [JsonPropertyName("Status")]
    public int Status { get; set; }
}

/// <summary>
/// The order close activity that the FulfillSalesOrder and CancelSalesOrder actions create.
/// </summary>
public class OrderCloseRecord
{
    /// <summary>The record's type. CancelSalesOrder declares the parameter as any record, so the type must be sent.</summary>
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; set; } = "Microsoft.Dynamics.CRM.orderclose";

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("actualend")]
    public DateTime? ActualEnd { get; set; }

    /// <summary>The order to close, as <c>/salesorders(&lt;id&gt;)</c>.</summary>
    [JsonPropertyName("salesorderid@odata.bind")]
    public string? SalesOrderODataBind { get; set; }
}

/// <summary>
/// Request payload for the FulfillSalesOrder action, which is bound to the <c>salesorders</c> collection.
/// </summary>
public class FulfillSalesOrderRequest
{
    [JsonPropertyName("OrderClose")]
    public OrderCloseRecord? OrderClose { get; set; }

    /// <summary>The status reason of the fulfilled order: a <c>SalesOrderStatus</c> value of the Fulfilled state.</summary>
    [JsonPropertyName("Status")]
    public int Status { get; set; }
}

/// <summary>
/// Request payload for the CancelSalesOrder unbound action.
/// </summary>
public class CancelSalesOrderRequest
{
    [JsonPropertyName("OrderClose")]
    public OrderCloseRecord? OrderClose { get; set; }

    /// <summary>The status reason of the canceled order: a <c>SalesOrderStatus</c> value of the Canceled state.</summary>
    [JsonPropertyName("Status")]
    public int Status { get; set; }
}

/// <summary>
/// Request payload for the ConvertSalesOrderToInvoice unbound action.
/// </summary>
public class ConvertSalesOrderToInvoiceRequest
{
    [JsonPropertyName("SalesOrderId")]
    public Guid SalesOrderId { get; set; }

    /// <summary>The columns of the created invoice to return.</summary>
    [JsonPropertyName("ColumnSet")]
    public ColumnSetPayload? ColumnSet { get; set; }
}
