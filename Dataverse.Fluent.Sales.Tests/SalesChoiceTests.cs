using System.Text.Json.Nodes;
using Dataverse.Fluent;
using Dataverse.Fluent.Sales.Entities;

namespace Dataverse.Fluent.Sales.Tests;

/// <summary>
/// The Sales models' choice columns are enums: they are read from and written as the option's number, filters send
/// the number, and a value the organisation added is kept even though the enum has no member for it.
/// </summary>
public class SalesChoiceTests
{
    private sealed class FakeTransport : IDataverseTransport
    {
        public string BaseUrl => "https://org.crm.dynamics.com/api/data/v9.2";
        public List<DataverseRequest> Requests { get; } = [];
        public JsonObject Response { get; set; } = new() { ["value"] = new JsonArray() };

        public Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(DataverseResponse.FromJson(Response.DeepClone().AsObject()));
        }
    }

    private static string Url(FakeTransport transport) => Uri.UnescapeDataString(transport.Requests.Single().Url);

    [Fact]
    public async Task Choices_AreReadFromTheirNumbers()
    {
        var transport = new FakeTransport
        {
            Response = new JsonObject
            {
                ["value"] = new JsonArray(new JsonObject
                {
                    ["opportunityid"] = Guid.NewGuid(),
                    ["statecode"] = 1,
                    ["statuscode"] = 3,
                    ["opportunityratingcode"] = 2
                })
            }
        };

        var opportunity = (await new DataverseClient(transport).Query<Opportunity>().GetListAsync(TestContext.Current.CancellationToken)).Single();

        Assert.Equal(OpportunityState.Won, opportunity.StateCode);
        Assert.Equal(OpportunityStatus.Won, opportunity.StatusCode);
        Assert.Equal(OpportunityRating.Warm, opportunity.RatingCode);
    }

    [Fact]
    public async Task Choices_KeepAValueTheOrganisationAdded_AndReadNullAsNull()
    {
        var transport = new FakeTransport
        {
            Response = new JsonObject
            {
                ["value"] = new JsonArray(new JsonObject
                {
                    ["leadid"] = Guid.NewGuid(),
                    ["statecode"] = 0,
                    ["statuscode"] = 100000001,
                    ["leadsourcecode"] = null,
                    ["industrycode"] = 100000005
                })
            }
        };

        var lead = (await new DataverseClient(transport).Query<Lead>().GetListAsync(TestContext.Current.CancellationToken)).Single();

        Assert.Equal(100000001, (int)lead.StatusCode);
        Assert.False(Enum.IsDefined(lead.StatusCode));
        Assert.Null(lead.LeadSourceCode);
        Assert.Equal(100000005, (int)lead.IndustryCode!.Value);
    }

    [Fact]
    public void Choices_AreWrittenAsNumbers()
    {
        var client = new DataverseClient(new FakeTransport());

        var set = client.Entity<Lead>(Guid.NewGuid())
            .Set(l => l.StatusCode, LeadStatus.Contacted)
            .Set(l => l.LeadSourceCode, LeadSource.TradeShow)
            .Set(l => l.IndustryCode, (Industry)100000005)
            .ToUpdateRequest().Payload!;
        var from = client.Entity<Incident>()
            .SetFrom(new Incident { Title = "t", PriorityCode = IncidentPriority.High, CaseOriginCode = CaseOrigin.Web })
            .ToCreateRequest().Payload!;

        Assert.Equal(2, set["statuscode"]!.GetValue<int>());
        Assert.Equal(7, set["leadsourcecode"]!.GetValue<int>());
        Assert.Equal(100000005, set["industrycode"]!.GetValue<int>());
        Assert.Equal(1, from["prioritycode"]!.GetValue<int>());
        Assert.Equal(3, from["caseorigincode"]!.GetValue<int>());
        Assert.False(from.ContainsKey("customersatisfactioncode"));
    }

    [Fact]
    public async Task Filters_OnChoices_SendTheNumber()
    {
        var transport = new FakeTransport();
        var open = new[] { OpportunityStatus.InProgress, OpportunityStatus.OnHold };

        await new DataverseClient(transport).Query<Opportunity>()
            .Where(o => o.StateCode == OpportunityState.Open && o.RatingCode != OpportunityRating.Cold)
            .Where(o => open.Contains(o.StatusCode))
            .GetListAsync(TestContext.Current.CancellationToken);

        var url = Url(transport);
        Assert.Contains("statecode eq 0", url);
        Assert.Contains("opportunityratingcode ne 3", url);
        Assert.Contains("PropertyName='statuscode',PropertyValues=[\"1\",\"2\"]", url);
        Assert.DoesNotContain("Open", url);
        Assert.DoesNotContain("InProgress", url);
        Assert.DoesNotContain("Cold", url);
    }

    [Fact]
    public void FetchXmlFilters_OnChoices_SendTheNumber()
    {
        var xml = new DataverseClient(new FakeTransport()).FetchBuilder<Lead>()
            .AddFilter(l => l.StateCode == LeadState.Open && l.LeadSourceCode == LeadSource.Web)
            .BuildFetchXml();

        Assert.Contains("attribute=\"statecode\" operator=\"eq\" value=\"0\"", xml);
        Assert.Contains("attribute=\"leadsourcecode\" operator=\"eq\" value=\"8\"", xml);
    }

    [Fact]
    public async Task Actions_SendTheStatusReasonAsItsNumber()
    {
        var transport = new FakeTransport();
        var sales = new SalesClient(new DataverseClient(transport));
        var ct = TestContext.Current.CancellationToken;

        await sales.QualifyLeadAsync(Guid.NewGuid(), ct: ct);
        await sales.WinOpportunityAsync(Guid.NewGuid(), 10m, ct: ct);
        await sales.LoseOpportunityAsync(Guid.NewGuid(), ct: ct);
        await sales.LoseOpportunityAsync(Guid.NewGuid(), status: OpportunityStatus.OutSold, ct: ct);
        await sales.CloseIncidentAsync(Guid.NewGuid(), ct: ct);
        await sales.CloseIncidentAsync(Guid.NewGuid(), status: (IncidentStatus)100000001, ct: ct);

        Assert.Equal([3, 3, 4, 5, 5, 100000001], transport.Requests.Select(r => r.Payload!["Status"]!.GetValue<int>()));
    }

    [Theory]
    [InlineData(typeof(LeadStatus), typeof(LeadState))]
    [InlineData(typeof(OpportunityStatus), typeof(OpportunityState))]
    [InlineData(typeof(QuoteStatus), typeof(QuoteState))]
    [InlineData(typeof(IncidentStatus), typeof(IncidentState))]
    [InlineData(typeof(AccountStatus), typeof(AccountState))]
    [InlineData(typeof(ContactStatus), typeof(ContactState))]
    public void StatesStartAtZero_AndStatusReasonsHaveNoZero(Type status, Type state)
    {
        // An unset state is the table's default state (0); 0 is never a status reason, so an unset one is not mistaken for one.
        Assert.True(Enum.IsDefined(state, 0));
        Assert.False(Enum.IsDefined(status, 0));
    }
}
