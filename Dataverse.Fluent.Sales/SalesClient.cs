using System.Text.Json.Nodes;
using Dataverse.Fluent;
using Dataverse.Fluent.Sales.Actions;
using Dataverse.Fluent.Sales.Entities;

namespace Dataverse.Fluent.Sales;

/// <summary>
/// Fluent Dynamics 365 Sales client built on top of <see cref="IDataverseClient"/>.
/// Provides typed helpers for CRM-specific actions, queries, and entity operations.
/// </summary>
/// <remarks>
/// <para>
/// The underlying <see cref="IDataverseClient"/> is exposed via the <see cref="Dataverse"/> property
/// for generic operations not covered by the Sales-specific helpers.
/// </para>
/// <para>
/// Each action has two overloads: one that takes the common values as arguments, and one that takes the action's
/// request object, for the parameters the first does not offer. The first calls the second, and every method is
/// virtual, so a derived client can change an action in one place.
/// </para>
/// </remarks>
public class SalesClient
{
    private readonly IDataverseClient _dataverse;

    /// <summary>
    /// Access the underlying Dataverse client for generic operations.
    /// </summary>
    public IDataverseClient Dataverse => _dataverse;

    public SalesClient(IDataverseClient dataverse)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
    }

    // ───────────────────────── Lead operations ─────────────────────────

    /// <summary>
    /// Qualifies a lead, optionally creating an Account, Contact, and/or Opportunity.
    /// Uses the QualifyLead action bound to the lead.
    /// </summary>
    /// <param name="leadId">The ID of the lead to qualify.</param>
    /// <param name="createAccount">Whether to create an Account from the lead.</param>
    /// <param name="createContact">Whether to create a Contact from the lead.</param>
    /// <param name="createOpportunity">Whether to create an Opportunity from the lead.</param>
    /// <param name="status">The status reason for the qualified lead. For a status reason the organisation added,
    /// cast its value: <c>(LeadStatus)100000001</c>.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The response containing references to the created entities.</returns>
    public virtual Task<QualifyLeadResponse> QualifyLeadAsync(
        Guid leadId,
        bool createAccount = true,
        bool createContact = true,
        bool createOpportunity = true,
        LeadStatus status = LeadStatus.Qualified,
        CancellationToken ct = default)
        => QualifyLeadAsync(
            leadId,
            new QualifyLeadRequest
            {
                CreateAccount = createAccount,
                CreateContact = createContact,
                CreateOpportunity = createOpportunity,
                Status = (int)status
            },
            ct);

    /// <summary>
    /// Qualifies a lead with the QualifyLead action, sending <paramref name="request"/> as its parameters. Use this
    /// overload for the parameters the other does not offer, such as an existing customer for the opportunity.
    /// </summary>
    /// <param name="leadId">The ID of the lead to qualify.</param>
    /// <param name="request">The action's parameters.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The response containing references to the created entities.</returns>
    public virtual async Task<QualifyLeadResponse> QualifyLeadAsync(Guid leadId, QualifyLeadRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _dataverse
            .ForEntity<Lead>(leadId)
            .Action<QualifyLeadRequest, JsonObject>("QualifyLead")
            .WithRequest(request)
            .ExecuteAsync(ct).ConfigureAwait(false);

        return new QualifyLeadResponse { CreatedEntities = ReadCreatedEntities(result) };
    }

    // QualifyLead returns Collection(mscrm.crmbaseentity): each record carries its type and is keyed by "<logicalname>id".
    private static List<CreatedEntityReference> ReadCreatedEntities(JsonObject? result)
    {
        var created = new List<CreatedEntityReference>();
        if (result?["value"] is not JsonArray records)
            return created;

        foreach (var record in records.OfType<JsonObject>())
        {
            var odataType = record["@odata.type"]?.GetValue<string>();
            var logicalName = odataType?[(odataType.LastIndexOf('.') + 1)..];

            Guid id = default;
            if (logicalName != null && record[logicalName + "id"] is JsonValue key && key.TryGetValue<string>(out var text)
                && Guid.TryParse(text, out var parsed))
            {
                id = parsed;
            }

            created.Add(new CreatedEntityReference { ODataType = odataType, LogicalName = logicalName, Id = id });
        }

        return created;
    }

    // ───────────────────────── Opportunity operations ─────────────────────────

    /// <summary>
    /// Closes an opportunity as won using the WinOpportunity unbound action.
    /// </summary>
    /// <param name="opportunityId">The ID of the opportunity to close as won.</param>
    /// <param name="actualRevenue">The actual revenue for the closed opportunity.</param>
    /// <param name="subject">Optional subject for the opportunity close activity.</param>
    /// <param name="actualCloseDate">The actual close date. Defaults to UTC now.</param>
    /// <param name="description">Optional description for the close activity.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task WinOpportunityAsync(
        Guid opportunityId,
        decimal actualRevenue,
        string? subject = null,
        DateTime? actualCloseDate = null,
        string? description = null,
        CancellationToken ct = default)
        => WinOpportunityAsync(
            new WinOpportunityRequest
            {
                OpportunityClose = new OpportunityCloseRecord
                {
                    Subject = subject ?? "Opportunity Won",
                    ActualRevenue = actualRevenue,
                    ActualEnd = actualCloseDate ?? DateTime.UtcNow,
                    Description = description,
                    OpportunityODataBind = $"/opportunities({opportunityId})"
                },
                Status = (int)OpportunityStatus.Won
            },
            ct);

    /// <summary>
    /// Closes an opportunity as won with the WinOpportunity action, sending <paramref name="request"/> as its parameters.
    /// </summary>
    /// <param name="request">The action's parameters. The opportunity is named by the close record's bind.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task WinOpportunityAsync(WinOpportunityRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _dataverse.Action<WinOpportunityRequest>("WinOpportunity").WithRequest(request).ExecuteAsync(ct);
    }

    /// <summary>
    /// Closes an opportunity as lost using the LoseOpportunity unbound action.
    /// </summary>
    /// <param name="opportunityId">The ID of the opportunity to close as lost.</param>
    /// <param name="subject">Optional subject for the opportunity close activity.</param>
    /// <param name="actualCloseDate">The actual close date. Defaults to UTC now.</param>
    /// <param name="description">Optional description for the close activity.</param>
    /// <param name="status">The status reason for the lost opportunity: <see cref="OpportunityStatus.Canceled"/> or
    /// <see cref="OpportunityStatus.OutSold"/>, or a cast value for one the organisation added.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task LoseOpportunityAsync(
        Guid opportunityId,
        string? subject = null,
        DateTime? actualCloseDate = null,
        string? description = null,
        OpportunityStatus status = OpportunityStatus.Canceled,
        CancellationToken ct = default)
        => LoseOpportunityAsync(
            new LoseOpportunityRequest
            {
                OpportunityClose = new OpportunityCloseRecord
                {
                    Subject = subject ?? "Opportunity Lost",
                    ActualEnd = actualCloseDate ?? DateTime.UtcNow,
                    Description = description,
                    OpportunityODataBind = $"/opportunities({opportunityId})"
                },
                Status = (int)status
            },
            ct);

    /// <summary>
    /// Closes an opportunity as lost with the LoseOpportunity action, sending <paramref name="request"/> as its parameters.
    /// </summary>
    /// <param name="request">The action's parameters. The opportunity is named by the close record's bind.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task LoseOpportunityAsync(LoseOpportunityRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _dataverse.Action<LoseOpportunityRequest>("LoseOpportunity").WithRequest(request).ExecuteAsync(ct);
    }

    /// <summary>
    /// Triggers price recalculation on an opportunity using the CalculatePrice action.
    /// </summary>
    /// <param name="opportunityId">The ID of the opportunity to recalculate.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task CalculateOpportunityPriceAsync(Guid opportunityId, CancellationToken ct = default)
        => CalculatePriceAsync(
            new CalculatePriceRequest
            {
                Target = new CalculatePriceTarget { ODataType = "Microsoft.Dynamics.CRM.opportunity", OpportunityId = opportunityId }
            },
            ct);

    /// <summary>
    /// Recalculates the price of a record with the CalculatePrice action, sending <paramref name="request"/> as its parameters.
    /// </summary>
    /// <param name="request">The action's parameters.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task CalculatePriceAsync(CalculatePriceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _dataverse.Action<CalculatePriceRequest>("CalculatePrice").WithRequest(request).ExecuteAsync(ct);
    }

    // ───────────────────────── Incident (Case) operations ─────────────────────────

    /// <summary>
    /// Closes (resolves) a case using the CloseIncident unbound action.
    /// </summary>
    /// <param name="incidentId">The ID of the case to resolve.</param>
    /// <param name="subject">Subject of the resolution activity.</param>
    /// <param name="description">Optional description of the resolution.</param>
    /// <param name="timeSpent">Optional time spent in minutes.</param>
    /// <param name="status">The status reason for the resolved case. For a status reason the organisation added,
    /// cast its value: <c>(IncidentStatus)100000001</c>.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task CloseIncidentAsync(
        Guid incidentId,
        string subject = "Case Resolved",
        string? description = null,
        int? timeSpent = null,
        IncidentStatus status = IncidentStatus.ProblemSolved,
        CancellationToken ct = default)
        => CloseIncidentAsync(
            new CloseIncidentRequest
            {
                IncidentResolution = new IncidentResolutionRecord
                {
                    Subject = subject,
                    Description = description,
                    TimeSpent = timeSpent,
                    IncidentODataBind = $"/incidents({incidentId})"
                },
                Status = (int)status
            },
            ct);

    /// <summary>
    /// Resolves a case with the CloseIncident action, sending <paramref name="request"/> as its parameters.
    /// </summary>
    /// <param name="request">The action's parameters. The case is named by the resolution record's bind.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task CloseIncidentAsync(CloseIncidentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _dataverse.Action<CloseIncidentRequest>("CloseIncident").WithRequest(request).ExecuteAsync(ct);
    }

    // ───────────────────────── Quote operations ─────────────────────────

    /// <summary>
    /// Closes an active quote as won using the WinQuote unbound action.
    /// </summary>
    /// <param name="quoteId">The ID of the quote. It must be active.</param>
    /// <param name="subject">Optional subject for the quote close activity.</param>
    /// <param name="description">Optional description for the close activity.</param>
    /// <param name="status">The status reason for the won quote.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task WinQuoteAsync(
        Guid quoteId,
        string? subject = null,
        string? description = null,
        QuoteStatus status = QuoteStatus.Won,
        CancellationToken ct = default)
        => WinQuoteAsync(
            new WinQuoteRequest
            {
                QuoteClose = new QuoteCloseRecord
                {
                    Subject = subject ?? "Quote Won",
                    Description = description,
                    QuoteODataBind = $"/quotes({quoteId})"
                },
                Status = (int)status
            },
            ct);

    /// <summary>
    /// Closes a quote as won with the WinQuote action, sending <paramref name="request"/> as its parameters.
    /// </summary>
    /// <param name="request">The action's parameters. The quote is named by the close record's bind.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task WinQuoteAsync(WinQuoteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _dataverse.Action<WinQuoteRequest>("WinQuote").WithRequest(request).ExecuteAsync(ct);
    }

    /// <summary>
    /// Closes a quote as lost, canceled or revised using the CloseQuote unbound action.
    /// </summary>
    /// <param name="quoteId">The ID of the quote.</param>
    /// <param name="subject">Optional subject for the quote close activity.</param>
    /// <param name="description">Optional description for the close activity.</param>
    /// <param name="status">The status reason for the closed quote: <see cref="QuoteStatus.Lost"/>,
    /// <see cref="QuoteStatus.Canceled"/> or <see cref="QuoteStatus.Revised"/>.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task CloseQuoteAsync(
        Guid quoteId,
        string? subject = null,
        string? description = null,
        QuoteStatus status = QuoteStatus.Canceled,
        CancellationToken ct = default)
        => CloseQuoteAsync(
            new CloseQuoteRequest
            {
                QuoteClose = new QuoteCloseRecord
                {
                    Subject = subject ?? "Quote Closed",
                    Description = description,
                    QuoteODataBind = $"/quotes({quoteId})"
                },
                Status = (int)status
            },
            ct);

    /// <summary>
    /// Closes a quote with the CloseQuote action, sending <paramref name="request"/> as its parameters.
    /// </summary>
    /// <param name="request">The action's parameters. The quote is named by the close record's bind.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task CloseQuoteAsync(CloseQuoteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _dataverse.Action<CloseQuoteRequest>("CloseQuote").WithRequest(request).ExecuteAsync(ct);
    }

    /// <summary>
    /// Converts a quote to a sales order using the ConvertQuoteToSalesOrder action.
    /// </summary>
    /// <param name="quoteId">The ID of the quote to convert.</param>
    /// <param name="columns">The columns of the created order to return, by logical name. If null, all columns are returned.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The created sales order.</returns>
    public virtual Task<SalesOrder> ConvertQuoteToSalesOrderAsync(
        Guid quoteId,
        IEnumerable<string>? columns = null,
        CancellationToken ct = default)
        => ConvertQuoteToSalesOrderAsync(
            new ConvertQuoteToSalesOrderRequest { QuoteId = quoteId, ColumnSet = ToColumnSet(columns) },
            ct);

    /// <summary>
    /// Converts a quote to a sales order with the ConvertQuoteToSalesOrder action, sending <paramref name="request"/>
    /// as its parameters. Use this overload to close the quote as part of the conversion.
    /// </summary>
    /// <param name="request">The action's parameters.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The created sales order.</returns>
    public virtual async Task<SalesOrder> ConvertQuoteToSalesOrderAsync(ConvertQuoteToSalesOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var order = await _dataverse
            .Action<ConvertQuoteToSalesOrderRequest, SalesOrder>("ConvertQuoteToSalesOrder")
            .WithRequest(request)
            .ExecuteAsync(ct).ConfigureAwait(false);

        return order ?? new SalesOrder();
    }

    /// <summary>
    /// Triggers price recalculation on a quote using the CalculatePrice action.
    /// </summary>
    /// <param name="quoteId">The ID of the quote to recalculate.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task CalculateQuotePriceAsync(Guid quoteId, CancellationToken ct = default)
        => CalculatePriceAsync(
            new CalculatePriceRequest
            {
                Target = new CalculatePriceTarget { ODataType = "Microsoft.Dynamics.CRM.quote", QuoteId = quoteId }
            },
            ct);

    // ───────────────────────── Sales order operations ─────────────────────────

    /// <summary>
    /// Fulfills a sales order using the FulfillSalesOrder action.
    /// </summary>
    /// <param name="salesOrderId">The ID of the order.</param>
    /// <param name="subject">Optional subject for the order close activity.</param>
    /// <param name="description">Optional description for the close activity.</param>
    /// <param name="status">The status reason for the fulfilled order: <see cref="SalesOrderStatus.Complete"/> or
    /// <see cref="SalesOrderStatus.Partial"/>.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task FulfillSalesOrderAsync(
        Guid salesOrderId,
        string? subject = null,
        string? description = null,
        SalesOrderStatus status = SalesOrderStatus.Complete,
        CancellationToken ct = default)
        => FulfillSalesOrderAsync(
            new FulfillSalesOrderRequest
            {
                OrderClose = new OrderCloseRecord
                {
                    Subject = subject ?? "Order Fulfilled",
                    Description = description,
                    SalesOrderODataBind = $"/salesorders({salesOrderId})"
                },
                Status = (int)status
            },
            ct);

    /// <summary>
    /// Fulfills a sales order with the FulfillSalesOrder action, sending <paramref name="request"/> as its parameters.
    /// </summary>
    /// <param name="request">The action's parameters. The order is named by the close record's bind.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task FulfillSalesOrderAsync(FulfillSalesOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The action is bound to the salesorders collection, not to one order.
        return _dataverse
            .Action<FulfillSalesOrderRequest>("salesorders/Microsoft.Dynamics.CRM.FulfillSalesOrder")
            .WithRequest(request)
            .ExecuteAsync(ct);
    }

    /// <summary>
    /// Cancels a sales order using the CancelSalesOrder unbound action.
    /// </summary>
    /// <param name="salesOrderId">The ID of the order.</param>
    /// <param name="subject">Optional subject for the order close activity.</param>
    /// <param name="description">Optional description for the close activity.</param>
    /// <param name="status">The status reason for the canceled order.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task CancelSalesOrderAsync(
        Guid salesOrderId,
        string? subject = null,
        string? description = null,
        SalesOrderStatus status = SalesOrderStatus.NoMoney,
        CancellationToken ct = default)
        => CancelSalesOrderAsync(
            new CancelSalesOrderRequest
            {
                OrderClose = new OrderCloseRecord
                {
                    Subject = subject ?? "Order Canceled",
                    Description = description,
                    SalesOrderODataBind = $"/salesorders({salesOrderId})"
                },
                Status = (int)status
            },
            ct);

    /// <summary>
    /// Cancels a sales order with the CancelSalesOrder action, sending <paramref name="request"/> as its parameters.
    /// </summary>
    /// <param name="request">The action's parameters. The order is named by the close record's bind.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public virtual Task CancelSalesOrderAsync(CancelSalesOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _dataverse.Action<CancelSalesOrderRequest>("CancelSalesOrder").WithRequest(request).ExecuteAsync(ct);
    }

    /// <summary>
    /// Converts a sales order to an invoice using the ConvertSalesOrderToInvoice action.
    /// </summary>
    /// <param name="salesOrderId">The ID of the order to convert.</param>
    /// <param name="columns">The columns of the created invoice to return, by logical name. If null, all columns are returned.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The created invoice.</returns>
    public virtual Task<Invoice> ConvertSalesOrderToInvoiceAsync(
        Guid salesOrderId,
        IEnumerable<string>? columns = null,
        CancellationToken ct = default)
        => ConvertSalesOrderToInvoiceAsync(
            new ConvertSalesOrderToInvoiceRequest { SalesOrderId = salesOrderId, ColumnSet = ToColumnSet(columns) },
            ct);

    /// <summary>
    /// Converts a sales order to an invoice with the ConvertSalesOrderToInvoice action, sending
    /// <paramref name="request"/> as its parameters.
    /// </summary>
    /// <param name="request">The action's parameters.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The created invoice.</returns>
    public virtual async Task<Invoice> ConvertSalesOrderToInvoiceAsync(ConvertSalesOrderToInvoiceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invoice = await _dataverse
            .Action<ConvertSalesOrderToInvoiceRequest, Invoice>("ConvertSalesOrderToInvoice")
            .WithRequest(request)
            .ExecuteAsync(ct).ConfigureAwait(false);

        return invoice ?? new Invoice();
    }

    private static ColumnSetPayload ToColumnSet(IEnumerable<string>? columns)
        => columns != null
            ? new ColumnSetPayload { AllColumns = false, Columns = columns.ToList() }
            : new ColumnSetPayload { AllColumns = true };
}
