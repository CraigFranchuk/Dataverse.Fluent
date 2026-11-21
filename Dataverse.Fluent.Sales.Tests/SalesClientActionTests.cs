using System.Text.Json.Nodes;
using Dataverse.Fluent;
using Dataverse.Fluent.Sales.Actions;
using Dataverse.Fluent.Sales.Entities;

namespace Dataverse.Fluent.Sales.Tests;

/// <summary>
/// The quote and order actions, the overloads that take an action's request object, and overriding an action.
/// The request shapes are the ones the integration tests run against a live environment.
/// </summary>
public class SalesClientActionTests
{
    private static readonly Guid Id = Guid.Parse("fa912732-6ad1-f111-a05c-001dd80a6896");
    private static readonly Guid OtherId = Guid.Parse("2b922732-6ad1-f111-a05c-001dd80a6896");

    private sealed class FakeTransport : IDataverseTransport
    {
        public string BaseUrl => "https://org.crm.dynamics.com/api/data/v9.2";
        public List<DataverseRequest> Requests { get; } = [];
        public JsonObject Response { get; set; } = [];

        public Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(DataverseResponse.FromJson(Response.DeepClone().AsObject()));
        }
    }

    private static (SalesClient Sales, FakeTransport Transport) Create(JsonObject? response = null)
    {
        var transport = new FakeTransport();
        if (response is not null)
            transport.Response = response;
        return (new SalesClient(new DataverseClient(transport)), transport);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ───────────────────────── Quotes ─────────────────────────

    [Fact]
    public async Task WinQuoteAsync_PostsTheCloseRecordBoundToTheQuote()
    {
        var (sales, transport) = Create();

        await sales.WinQuoteAsync(Id, description: "Signed", ct: Ct);

        var request = Assert.Single(transport.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("WinQuote", request.Url);
        Assert.Equal(4, request.Payload!["Status"]!.GetValue<int>());
        Assert.Equal($"/quotes({Id})", (string?)request.Payload["QuoteClose"]!["quoteid@odata.bind"]);
        Assert.Equal("Quote Won", (string?)request.Payload["QuoteClose"]!["subject"]);
        Assert.Equal("Signed", (string?)request.Payload["QuoteClose"]!["description"]);
    }

    [Theory]
    [InlineData(QuoteStatus.Canceled, 6)]
    [InlineData(QuoteStatus.Lost, 5)]
    [InlineData(QuoteStatus.Revised, 7)]
    public async Task CloseQuoteAsync_SendsTheStatusReason(QuoteStatus status, int expected)
    {
        var (sales, transport) = Create();

        await sales.CloseQuoteAsync(Id, status: status, ct: Ct);

        var request = Assert.Single(transport.Requests);
        Assert.Equal("CloseQuote", request.Url);
        Assert.Equal(expected, request.Payload!["Status"]!.GetValue<int>());
        Assert.Equal($"/quotes({Id})", (string?)request.Payload["QuoteClose"]!["quoteid@odata.bind"]);
    }

    [Fact]
    public async Task ConvertQuoteToSalesOrderAsync_ReturnsTheOrderAsAModel()
    {
        var (sales, transport) = Create(new JsonObject
        {
            ["salesorderid"] = OtherId,
            ["name"] = "Order",
            ["ordernumber"] = "ORD-1",
            ["statecode"] = 0,
            ["statuscode"] = 1,
            ["_quoteid_value"] = Id
        });

        var order = await sales.ConvertQuoteToSalesOrderAsync(Id, ct: Ct);

        var request = Assert.Single(transport.Requests);
        Assert.Equal("ConvertQuoteToSalesOrder", request.Url);
        Assert.Equal(Id, request.Payload!["QuoteId"]!.GetValue<Guid>());
        Assert.True(request.Payload["ColumnSet"]!["AllColumns"]!.GetValue<bool>());
        Assert.False(request.Payload.ContainsKey("QuoteCloseStatus"));

        Assert.Equal(OtherId, order.SalesOrderId);
        Assert.Equal("ORD-1", order.OrderNumber);
        Assert.Equal(SalesOrderStatus.New, order.StatusCode);
        Assert.Equal(Id, order.QuoteId);
    }

    [Fact]
    public async Task ConvertQuoteToSalesOrderAsync_WithTheRequest_SendsTheCloseParameters()
    {
        var (sales, transport) = Create();

        await sales.ConvertQuoteToSalesOrderAsync(new ConvertQuoteToSalesOrderRequest
        {
            QuoteId = Id,
            ColumnSet = new ColumnSetPayload { AllColumns = false, Columns = ["name"] },
            QuoteCloseStatus = (int)QuoteStatus.Won,
            QuoteCloseSubject = "Converted"
        }, Ct);

        var payload = Assert.Single(transport.Requests).Payload!;
        Assert.Equal(4, payload["QuoteCloseStatus"]!.GetValue<int>());
        Assert.Equal("Converted", (string?)payload["QuoteCloseSubject"]);
        Assert.Equal(["name"], payload["ColumnSet"]!["Columns"]!.AsArray().Select(c => (string?)c));
        Assert.False(payload.ContainsKey("QuoteCloseDate"));
    }

    // ───────────────────────── Orders ─────────────────────────

    [Fact]
    public async Task FulfillSalesOrderAsync_PostsToTheActionBoundToTheCollection()
    {
        var (sales, transport) = Create();

        await sales.FulfillSalesOrderAsync(Id, ct: Ct);

        var request = Assert.Single(transport.Requests);
        Assert.Equal("salesorders/Microsoft.Dynamics.CRM.FulfillSalesOrder", request.Url);
        Assert.Equal(100001, request.Payload!["Status"]!.GetValue<int>());
        Assert.Equal($"/salesorders({Id})", (string?)request.Payload["OrderClose"]!["salesorderid@odata.bind"]);
    }

    [Fact]
    public async Task CancelSalesOrderAsync_SendsTheCloseRecordWithItsType()
    {
        var (sales, transport) = Create();

        await sales.CancelSalesOrderAsync(Id, description: "Withdrawn", ct: Ct);

        var request = Assert.Single(transport.Requests);
        Assert.Equal("CancelSalesOrder", request.Url);
        Assert.Equal(4, request.Payload!["Status"]!.GetValue<int>());

        // The parameter is declared as any record, so Dataverse needs the type to read it.
        var close = request.Payload["OrderClose"]!;
        Assert.Equal("Microsoft.Dynamics.CRM.orderclose", (string?)close["@odata.type"]);
        Assert.Equal($"/salesorders({Id})", (string?)close["salesorderid@odata.bind"]);
        Assert.Equal("Withdrawn", (string?)close["description"]);
    }

    [Fact]
    public async Task ConvertSalesOrderToInvoiceAsync_ReturnsTheInvoiceAsAModel()
    {
        var (sales, transport) = Create(new JsonObject
        {
            ["invoiceid"] = OtherId,
            ["invoicenumber"] = "INV-1",
            ["statecode"] = 0,
            ["duedate"] = "2026-12-31",
            ["_salesorderid_value"] = Id
        });

        var invoice = await sales.ConvertSalesOrderToInvoiceAsync(Id, columns: ["invoicenumber", "duedate"], ct: Ct);

        var request = Assert.Single(transport.Requests);
        Assert.Equal("ConvertSalesOrderToInvoice", request.Url);
        Assert.Equal(Id, request.Payload!["SalesOrderId"]!.GetValue<Guid>());
        Assert.False(request.Payload["ColumnSet"]!["AllColumns"]!.GetValue<bool>());

        Assert.Equal(OtherId, invoice.InvoiceId);
        Assert.Equal("INV-1", invoice.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 12, 31), invoice.DueDate);
        Assert.Equal(Id, invoice.SalesOrderId);
    }

    // ───────────────────────── Request overloads ─────────────────────────

    [Fact]
    public async Task QualifyLeadAsync_WithTheRequest_SendsTheParametersTheShortFormLacks()
    {
        var (sales, transport) = Create();

        await sales.QualifyLeadAsync(Id, new QualifyLeadRequest
        {
            CreateOpportunity = true,
            Status = (int)LeadStatus.Qualified,
            OpportunityCustomerId = ActionRecord.Reference<Account>(OtherId),
            OpportunityCurrencyId = ActionRecord.Reference("transactioncurrency", OtherId)
        }, Ct);

        var request = Assert.Single(transport.Requests);
        Assert.Equal($"leads({Id})/Microsoft.Dynamics.CRM.QualifyLead", request.Url);

        var customer = request.Payload!["OpportunityCustomerId"]!;
        Assert.Equal("Microsoft.Dynamics.CRM.account", (string?)customer["@odata.type"]);
        Assert.Equal(OtherId, customer["accountid"]!.GetValue<Guid>());
        Assert.Equal(OtherId, request.Payload["OpportunityCurrencyId"]!["transactioncurrencyid"]!.GetValue<Guid>());
        Assert.False(request.Payload["CreateAccount"]!.GetValue<bool>());
        Assert.False(request.Payload.ContainsKey("SourceCampaignId"));
    }

    [Fact]
    public async Task ShortForms_SendWhatTheRequestFormsSend()
    {
        var (sales, transport) = Create();
        var close = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

        await sales.LoseOpportunityAsync(Id, "s", close, "d", OpportunityStatus.OutSold, Ct);
        await sales.LoseOpportunityAsync(new LoseOpportunityRequest
        {
            OpportunityClose = new OpportunityCloseRecord { Subject = "s", ActualEnd = close, Description = "d", OpportunityODataBind = $"/opportunities({Id})" },
            Status = 5
        }, Ct);
        await sales.CloseIncidentAsync(Id, "s", "d", 30, IncidentStatus.InformationProvided, Ct);
        await sales.CloseIncidentAsync(new CloseIncidentRequest
        {
            IncidentResolution = new IncidentResolutionRecord { Subject = "s", Description = "d", TimeSpent = 30, IncidentODataBind = $"/incidents({Id})" },
            Status = 1000
        }, Ct);

        var sent = transport.Requests.Select(r => r.Url + " " + r.Payload!.ToJsonString()).ToList();
        Assert.Equal(sent[1], sent[0]);
        Assert.Equal(sent[3], sent[2]);
    }

    [Fact]
    public async Task NullRequests_AreRejected()
    {
        var (sales, _) = Create();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sales.QualifyLeadAsync(Id, (QualifyLeadRequest)null!, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sales.WinQuoteAsync((WinQuoteRequest)null!, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sales.CancelSalesOrderAsync((CancelSalesOrderRequest)null!, Ct));
    }

    // ───────────────────────── Overriding an action ─────────────────────────

    private sealed class CampaignSalesClient(IDataverseClient dataverse, Guid campaignId) : SalesClient(dataverse)
    {
        public override Task<QualifyLeadResponse> QualifyLeadAsync(Guid leadId, QualifyLeadRequest request, CancellationToken ct = default)
        {
            request.SourceCampaignId = ActionRecord.Reference("campaign", campaignId);
            return base.QualifyLeadAsync(leadId, request, ct);
        }
    }

    [Fact]
    public async Task OverridingTheRequestOverload_AlsoChangesTheShortForm()
    {
        var transport = new FakeTransport();
        SalesClient sales = new CampaignSalesClient(new DataverseClient(transport), OtherId);

        await sales.QualifyLeadAsync(Id, createContact: false, ct: Ct);

        var payload = Assert.Single(transport.Requests).Payload!;
        Assert.Equal(OtherId, payload["SourceCampaignId"]!["campaignid"]!.GetValue<Guid>());
        Assert.False(payload["CreateContact"]!.GetValue<bool>());
    }

    [Fact]
    public void ActionRecord_Reference_NamesTheTypeAndThePrimaryKey()
    {
        var reference = ActionRecord.Reference<Contact>(Id);

        Assert.Equal("Microsoft.Dynamics.CRM.contact", (string?)reference["@odata.type"]);
        Assert.Equal(Id, reference["contactid"]!.GetValue<Guid>());
        Assert.Throws<ArgumentException>(() => ActionRecord.Reference(" ", Id));
    }
}
