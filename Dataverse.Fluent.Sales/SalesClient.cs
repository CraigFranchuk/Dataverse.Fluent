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
/// The underlying <see cref="IDataverseClient"/> is exposed via the <see cref="Dataverse"/> property
/// for generic operations not covered by the Sales-specific helpers.
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
    /// <param name="status">The status reason for the qualified lead. Default is 3 (Qualified).</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The response containing references to the created entities.</returns>
    public async Task<QualifyLeadResponse> QualifyLeadAsync(
        Guid leadId,
        bool createAccount = true,
        bool createContact = true,
        bool createOpportunity = true,
        int status = 3,
        CancellationToken ct = default)
    {
        var request = new QualifyLeadRequest
        {
            CreateAccount = createAccount,
            CreateContact = createContact,
            CreateOpportunity = createOpportunity,
            Status = status
        };

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
    public async Task WinOpportunityAsync(
        Guid opportunityId,
        decimal actualRevenue,
        string? subject = null,
        DateTime? actualCloseDate = null,
        string? description = null,
        CancellationToken ct = default)
    {
        var request = new WinOpportunityRequest
        {
            OpportunityClose = new OpportunityCloseRecord
            {
                Subject = subject ?? "Opportunity Won",
                ActualRevenue = actualRevenue,
                ActualEnd = actualCloseDate ?? DateTime.UtcNow,
                Description = description,
                OpportunityODataBind = $"/opportunities({opportunityId})"
            },
            Status = 3 // Won
        };

        await _dataverse
            .Action<WinOpportunityRequest, object>("WinOpportunity")
            .WithRequest(request)
            .ExecuteAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes an opportunity as lost using the LoseOpportunity unbound action.
    /// </summary>
    /// <param name="opportunityId">The ID of the opportunity to close as lost.</param>
    /// <param name="subject">Optional subject for the opportunity close activity.</param>
    /// <param name="actualCloseDate">The actual close date. Defaults to UTC now.</param>
    /// <param name="description">Optional description for the close activity.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task LoseOpportunityAsync(
        Guid opportunityId,
        string? subject = null,
        DateTime? actualCloseDate = null,
        string? description = null,
        CancellationToken ct = default)
    {
        var request = new LoseOpportunityRequest
        {
            OpportunityClose = new OpportunityCloseRecord
            {
                Subject = subject ?? "Opportunity Lost",
                ActualEnd = actualCloseDate ?? DateTime.UtcNow,
                Description = description,
                OpportunityODataBind = $"/opportunities({opportunityId})"
            },
            Status = 4 // Lost
        };

        await _dataverse
            .Action<LoseOpportunityRequest, object>("LoseOpportunity")
            .WithRequest(request)
            .ExecuteAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Triggers price recalculation on an opportunity using the CalculatePrice action.
    /// </summary>
    /// <param name="opportunityId">The ID of the opportunity to recalculate.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task CalculateOpportunityPriceAsync(Guid opportunityId, CancellationToken ct = default)
    {
        var request = new CalculatePriceRequest
        {
            Target = new CalculatePriceTarget
            {
                ODataType = "Microsoft.Dynamics.CRM.opportunity",
                OpportunityId = opportunityId
            }
        };

        await _dataverse
            .Action<CalculatePriceRequest, object>("CalculatePrice")
            .WithRequest(request)
            .ExecuteAsync(ct).ConfigureAwait(false);
    }

    // ───────────────────────── Incident (Case) operations ─────────────────────────

    /// <summary>
    /// Closes (resolves) a case using the CloseIncident unbound action.
    /// </summary>
    /// <param name="incidentId">The ID of the case to resolve.</param>
    /// <param name="subject">Subject of the resolution activity.</param>
    /// <param name="description">Optional description of the resolution.</param>
    /// <param name="timeSpent">Optional time spent in minutes.</param>
    /// <param name="status">The status reason for the resolved case. Default is 5 (Problem Solved).</param>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task CloseIncidentAsync(
        Guid incidentId,
        string subject = "Case Resolved",
        string? description = null,
        int? timeSpent = null,
        int status = 5,
        CancellationToken ct = default)
    {
        var request = new CloseIncidentRequest
        {
            IncidentResolution = new IncidentResolutionRecord
            {
                Subject = subject,
                Description = description,
                TimeSpent = timeSpent,
                IncidentODataBind = $"/incidents({incidentId})"
            },
            Status = status
        };

        await _dataverse
            .Action<CloseIncidentRequest, object>("CloseIncident")
            .WithRequest(request)
            .ExecuteAsync(ct).ConfigureAwait(false);
    }

    // ───────────────────────── Quote operations ─────────────────────────

    /// <summary>
    /// Converts a quote to a sales order using the ConvertQuoteToSalesOrder action.
    /// </summary>
    /// <param name="quoteId">The ID of the quote to convert.</param>
    /// <param name="columns">Optional list of columns to return on the created sales order. If null, all columns are returned.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The created sales order details.</returns>
    public async Task<ConvertQuoteToSalesOrderResponse> ConvertQuoteToSalesOrderAsync(
        Guid quoteId,
        IEnumerable<string>? columns = null,
        CancellationToken ct = default)
    {
        var request = new ConvertQuoteToSalesOrderRequest
        {
            QuoteId = quoteId,
            ColumnSet = columns != null
                ? new ColumnSetPayload { AllColumns = false, Columns = columns.ToList() }
                : new ColumnSetPayload { AllColumns = true }
        };

        var result = await _dataverse
            .Action<ConvertQuoteToSalesOrderRequest, ConvertQuoteToSalesOrderResponse>("ConvertQuoteToSalesOrder")
            .WithRequest(request)
            .ExecuteAsync(ct).ConfigureAwait(false);

        return result ?? new ConvertQuoteToSalesOrderResponse();
    }

    /// <summary>
    /// Triggers price recalculation on a quote using the CalculatePrice action.
    /// </summary>
    /// <param name="quoteId">The ID of the quote to recalculate.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task CalculateQuotePriceAsync(Guid quoteId, CancellationToken ct = default)
    {
        var request = new CalculatePriceRequest
        {
            Target = new CalculatePriceTarget
            {
                ODataType = "Microsoft.Dynamics.CRM.quote",
                QuoteId = quoteId
            }
        };

        await _dataverse
            .Action<CalculatePriceRequest, object>("CalculatePrice")
            .WithRequest(request)
            .ExecuteAsync(ct).ConfigureAwait(false);
    }
}
