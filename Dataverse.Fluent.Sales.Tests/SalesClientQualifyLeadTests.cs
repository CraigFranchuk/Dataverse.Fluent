using System.Text.Json.Nodes;
using Dataverse.Fluent;

namespace Dataverse.Fluent.Sales.Tests;

public class SalesClientQualifyLeadTests
{
    private static readonly Guid LeadId = Guid.Parse("fa912732-6ad1-f111-a05c-001dd80a6896");
    private static readonly Guid AccountId = Guid.Parse("2b922732-6ad1-f111-a05c-001dd80a6896");
    private static readonly Guid ContactId = Guid.Parse("2a922732-6ad1-f111-a05c-001dd80a6896");
    private static readonly Guid OpportunityId = Guid.Parse("75922732-6ad1-f111-a05c-001dd80a6896");

    [Fact]
    public async Task QualifyLeadAsync_PostsTheActionBoundToTheLead()
    {
        var transport = new FakeTransport();

        await new SalesClient(new DataverseClient(transport)).QualifyLeadAsync(LeadId);

        var call = Assert.Single(transport.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal($"leads({LeadId})/Microsoft.Dynamics.CRM.QualifyLead", call.Url);
    }

    [Fact]
    public async Task QualifyLeadAsync_SendsOnlyTheActionParameters()
    {
        var transport = new FakeTransport();

        await new SalesClient(new DataverseClient(transport))
            .QualifyLeadAsync(LeadId, createAccount: true, createContact: false, createOpportunity: true, status: 3);

        // The lead is the binding parameter in the URL; the action has no LeadId parameter.
        var payload = Assert.Single(transport.Calls).Payload!;
        Assert.Equal(["CreateAccount", "CreateContact", "CreateOpportunity", "Status"], payload.Select(p => p.Key).Order());
        Assert.True(payload["CreateAccount"]!.GetValue<bool>());
        Assert.False(payload["CreateContact"]!.GetValue<bool>());
        Assert.True(payload["CreateOpportunity"]!.GetValue<bool>());
        Assert.Equal(3, payload["Status"]!.GetValue<int>());
    }

    [Fact]
    public async Task QualifyLeadAsync_ReadsTheCreatedRecordsFromTheValueArray()
    {
        // QualifyLead returns Collection(mscrm.crmbaseentity): full records keyed by their own primary key.
        var transport = new FakeTransport
        {
            Response = new JsonObject
            {
                ["@odata.context"] = "https://org.crm.dynamics.com/api/data/v9.2/$metadata#Collection(Microsoft.Dynamics.CRM.crmbaseentity)",
                ["value"] = new JsonArray(
                    Record("account", AccountId, ("name", "Contoso")),
                    Record("contact", ContactId, ("_parentcustomerid_value", AccountId.ToString())),
                    Record("opportunity", OpportunityId, ("_parentaccountid_value", AccountId.ToString())))
            }
        };

        var response = await new SalesClient(new DataverseClient(transport)).QualifyLeadAsync(LeadId);

        Assert.NotNull(response.CreatedEntities);
        Assert.Collection(response.CreatedEntities,
            e => AssertReference(e, "account", AccountId),
            e => AssertReference(e, "contact", ContactId),
            e => AssertReference(e, "opportunity", OpportunityId));
    }

    [Fact]
    public async Task QualifyLeadAsync_WithNoCreatedRecords_ReturnsAnEmptyList()
    {
        var transport = new FakeTransport { Response = new JsonObject { ["value"] = new JsonArray() } };

        var response = await new SalesClient(new DataverseClient(transport))
            .QualifyLeadAsync(LeadId, createAccount: false, createContact: false, createOpportunity: false);

        Assert.NotNull(response.CreatedEntities);
        Assert.Empty(response.CreatedEntities);
    }

    [Fact]
    public async Task QualifyLeadAsync_WithAnEmptyBody_ReturnsAnEmptyList()
    {
        var response = await new SalesClient(new DataverseClient(new FakeTransport { Response = [] })).QualifyLeadAsync(LeadId);

        Assert.NotNull(response.CreatedEntities);
        Assert.Empty(response.CreatedEntities);
    }

    private static JsonObject Record(string logicalName, Guid id, params (string Name, string Value)[] columns)
    {
        var record = new JsonObject
        {
            ["@odata.type"] = $"#Microsoft.Dynamics.CRM.{logicalName}",
            ["@odata.etag"] = "W/\"1447583518\"",
            [$"{logicalName}id"] = id.ToString(),
        };
        foreach (var (name, value) in columns)
            record[name] = value;
        return record;
    }

    private static void AssertReference(Actions.CreatedEntityReference reference, string logicalName, Guid id)
    {
        Assert.Equal(logicalName, reference.LogicalName);
        Assert.Equal(id, reference.Id);
        Assert.Equal($"#Microsoft.Dynamics.CRM.{logicalName}", reference.ODataType);
    }

    private sealed class FakeTransport : IDataverseTransport
    {
        public string BaseUrl => "https://org.crm.dynamics.com/api/data/v9.2";
        public List<(HttpMethod Method, string Url, JsonObject? Payload)> Calls { get; } = [];
        public JsonObject Response { get; init; } = new() { ["value"] = new JsonArray() };

        public Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
        {
            Calls.Add((request.Method, request.Url, request.Payload));
            return Task.FromResult(DataverseResponse.FromJson(Response.DeepClone()));
        }
    }
}
