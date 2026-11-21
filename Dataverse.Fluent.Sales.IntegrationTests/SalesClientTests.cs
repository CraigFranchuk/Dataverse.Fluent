using Dataverse.Fluent;
using Dataverse.Fluent.IntegrationTests;
using Dataverse.Fluent.Models;
using Dataverse.Fluent.Sales;
using Dataverse.Fluent.Sales.Actions;
using Dataverse.Fluent.Sales.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Dataverse.Fluent.Sales.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="SalesClient"/> typed action wrappers
/// and <see cref="SalesServiceCollectionExtensions"/> DI registration.
/// </summary>
[Collection("Dataverse Integration Tests")]
public class SalesClientTests : IAsyncDisposable
{
    private readonly DataverseTestFixture _fixture;
    private readonly SalesClient _sales;

    private readonly List<Guid> _createdLeadIds = new();
    private readonly List<Guid> _createdAccountIds = new();
    private readonly List<Guid> _createdContactIds = new();
    private readonly List<Guid> _createdOpportunityIds = new();
    private readonly List<Guid> _createdIncidentIds = new();

    public SalesClientTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
        _sales = new SalesClient(_fixture.Client);
    }

    // ───────────────────────── SalesClient construction ─────────────────────────

    [Fact]
    public void SalesClient_ExposesDataverseClient()
    {
        Assert.NotNull(_sales.Dataverse);
        Assert.Same(_fixture.Client, _sales.Dataverse);
    }

    [Fact]
    public void SalesClient_NullDataverseClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SalesClient(null!));
    }

    // ───────────────────────── DI Registration ─────────────────────────

    [Fact]
    public void AddDynamicsSalesClient_RegistersSalesClient()
    {
        var services = new ServiceCollection();
        var config = _fixture.Configuration;

        // Register the Dataverse client first, then the SalesClient
        services.AddDataverseClient(config.BaseUrl, () => Task.FromResult("fake-token"));
        services.AddDynamicsSalesClient();

        using var provider = services.BuildServiceProvider();
        var salesClient = provider.GetService<SalesClient>();

        Assert.NotNull(salesClient);
        Assert.NotNull(salesClient.Dataverse);
    }

    [Fact]
    public void AddDynamicsSalesClient_AllInOne_RegistersBothClients()
    {
        var services = new ServiceCollection();
        var config = _fixture.Configuration;

        services.AddDynamicsSalesClient(
            config.BaseUrl,
            () => Task.FromResult("fake-token"));

        using var provider = services.BuildServiceProvider();

        var salesClient = provider.GetService<SalesClient>();
        var dataverseClient = provider.GetService<IDataverseClient>();

        Assert.NotNull(salesClient);
        Assert.NotNull(dataverseClient);
    }

    [Fact]
    public void AddDynamicsSalesClient_WithServiceProviderTokenFactory_RegistersBothClients()
    {
        var services = new ServiceCollection();
        var config = _fixture.Configuration;

        services.AddDynamicsSalesClient(
            config.BaseUrl,
            sp => () => Task.FromResult("fake-token"));

        using var provider = services.BuildServiceProvider();

        var salesClient = provider.GetService<SalesClient>();
        var dataverseClient = provider.GetService<IDataverseClient>();

        Assert.NotNull(salesClient);
        Assert.NotNull(dataverseClient);
    }

    [Fact]
    public void AddDynamicsSalesClient_AllInOne_ReturnsHttpClientBuilder()
    {
        var services = new ServiceCollection();
        var config = _fixture.Configuration;

        var builder = services.AddDynamicsSalesClient(
            config.BaseUrl,
            () => Task.FromResult("fake-token"));

        Assert.NotNull(builder);
        Assert.IsAssignableFrom<IHttpClientBuilder>(builder);
    }

    // ───────────────────────── QualifyLead ─────────────────────────

    [Fact]
    public async Task QualifyLead_CreatesAccountContactAndOpportunity()
    {
        var lead = (await _fixture.Client.Entity<Lead>(entitySet: "leads")
            .Set(l => l.Subject, $"Integration Test Lead {Guid.NewGuid():N}")
            .Set(l => l.FirstName, "Test")
            .Set(l => l.LastName, "QualifyLead")
            .Set(l => l.CompanyName, "Test Company")
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdLeadIds.Add(lead.LeadId);

        var response = await _sales.QualifyLeadAsync(
            lead.LeadId,
            createAccount: true,
            createContact: true,
            createOpportunity: true);

        Assert.NotNull(response);

        // Track created entities for cleanup
        if (response.CreatedEntities != null)
        {
            foreach (var entity in response.CreatedEntities)
            {
                switch (entity.LogicalName)
                {
                    case "account": _createdAccountIds.Add(entity.Id); break;
                    case "contact": _createdContactIds.Add(entity.Id); break;
                    case "opportunity": _createdOpportunityIds.Add(entity.Id); break;
                }
            }
        }

        Assert.NotNull(response.CreatedEntities);
        Assert.Equal(["account", "contact", "opportunity"], response.CreatedEntities.Select(e => e.LogicalName).Order());
        Assert.All(response.CreatedEntities, e => Assert.NotEqual(Guid.Empty, e.Id));

        var opportunityId = response.CreatedEntities.Single(e => e.LogicalName == "opportunity").Id;
        var opportunity = await _fixture.Client.Entity<Opportunity>(entitySet: "opportunities")
            .WithId(opportunityId)
            .RetrieveAsync();
        Assert.Equal(lead.LeadId, opportunity.OriginatingLeadId);

        var qualified = await _fixture.Client.Entity<Lead>(entitySet: "leads")
            .WithId(lead.LeadId)
            .RetrieveAsync();
        Assert.Equal(LeadState.Qualified, qualified.StateCode);
        Assert.Equal(LeadStatus.Qualified, qualified.StatusCode);
    }

    // ───────────────────────── WinOpportunity / LoseOpportunity ─────────────────────────

    [Fact]
    public async Task WinOpportunity_ClosesOpportunityAsWon()
    {
        var opportunity = (await _fixture.Client.Entity<Opportunity>(entitySet: "opportunities")
            .Set(o => o.Name, $"Win Test Opp {Guid.NewGuid():N}")
            .Set(o => o.EstimatedValue, 50000m)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdOpportunityIds.Add(opportunity.OpportunityId);

        await _sales.WinOpportunityAsync(
            opportunity.OpportunityId,
            actualRevenue: 48000m,
            subject: "Integration test win");

        // Verify it's won (statecode = 1 Won, statuscode = 3 Won)
        var closed = await _fixture.Client.Entity<Opportunity>(entitySet: "opportunities")
            .WithId(opportunity.OpportunityId)
            .RetrieveAsync();

        Assert.Equal(OpportunityState.Won, closed.StateCode);
        Assert.Equal(OpportunityStatus.Won, closed.StatusCode);
    }

    [Fact]
    public async Task LoseOpportunity_ClosesOpportunityAsLost()
    {
        var opportunity = (await _fixture.Client.Entity<Opportunity>(entitySet: "opportunities")
            .Set(o => o.Name, $"Lose Test Opp {Guid.NewGuid():N}")
            .Set(o => o.EstimatedValue, 30000m)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdOpportunityIds.Add(opportunity.OpportunityId);

        await _sales.LoseOpportunityAsync(
            opportunity.OpportunityId,
            subject: "Integration test loss",
            description: "Did not meet requirements");

        // Verify it's lost (statecode = 2 Lost)
        var closed = await _fixture.Client.Entity<Opportunity>(entitySet: "opportunities")
            .WithId(opportunity.OpportunityId)
            .RetrieveAsync();

        Assert.Equal(OpportunityState.Lost, closed.StateCode);
        Assert.Equal(OpportunityStatus.Canceled, closed.StatusCode);
    }

    // ───────────────────────── CloseIncident ─────────────────────────

    [Fact]
    public async Task CloseIncident_ResolvesCase()
    {
        // Create a customer (required for incident)
        var account = (await _fixture.Client.Entity<Account>(entitySet: "accounts")
            .Set(a => a.Name, $"Case Test Account {Guid.NewGuid():N}")
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdAccountIds.Add(account.AccountId);

        // Create an incident linked to the account
        var incident = (await _fixture.Client.Entity<Incident>(entitySet: "incidents")
            .Set(i => i.Title, $"Test Case {Guid.NewGuid():N}")
            .Set(i => i.CustomerId, DataverseReference.To<Account>(account.AccountId))
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdIncidentIds.Add(incident.IncidentId);

        await _sales.CloseIncidentAsync(
            incident.IncidentId,
            subject: "Integration test resolution",
            description: "Resolved via automated test",
            timeSpent: 30);

        // Verify it's resolved (statecode = 1 Resolved)
        var resolved = await _fixture.Client.Entity<Incident>(entitySet: "incidents")
            .WithId(incident.IncidentId)
            .RetrieveAsync();

        Assert.Equal(IncidentState.Resolved, resolved.StateCode);
        Assert.Equal(IncidentStatus.ProblemSolved, resolved.StatusCode);
    }

    // ───────────────────────── Notes ─────────────────────────

    [Fact]
    public async Task Annotation_RegardingOpportunityByLogicalName_BindsToThePluralEntitySet()
    {
        var opportunity = (await _fixture.Client.Entity<Opportunity>(entitySet: "opportunities")
            .Set(o => o.Name, $"Note Test Opp {Guid.NewGuid():N}")
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdOpportunityIds.Add(opportunity.OpportunityId);

        // "opportunity" must bind to /opportunities(...), not /opportunitys(...).
        var note = await _fixture.Client.Annotation()
            .Regarding("opportunity", opportunity.OpportunityId)
            .WithSubject("Opportunity note")
            .ReturnRepresentation()
            .CreateAsync();

        Assert.Equal(opportunity.OpportunityId, note.ObjectId);
        Assert.Equal("opportunity", note.ObjectTypeCode);
    }

    // ───────────────────────── Dataverse passthrough ─────────────────────────

    [Fact]
    public async Task DataversePassthrough_CanQueryWithSalesEntities()
    {
        // Verify that the Dataverse passthrough works for generic operations
        var accounts = await _sales.Dataverse.Query<Account>("accounts")
            .Select(a => new { a.AccountId, a.Name })
            .Top(1)
            .GetListAsync();

        Assert.NotNull(accounts);
    }

    [Fact]
    public async Task DataversePassthrough_CanCreateAndRetrieveEntity()
    {
        var accountName = $"Passthrough Test {Guid.NewGuid():N}";

        var account = (await _sales.Dataverse.Entity<Account>(entitySet: "accounts")
            .Set(a => a.Name, accountName)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdAccountIds.Add(account.AccountId);

        Assert.NotNull(account);
        Assert.Equal(accountName, account.Name);

        var retrieved = await _sales.Dataverse.Entity<Account>(entitySet: "accounts")
            .WithId(account.AccountId)
            .RetrieveAsync();

        Assert.Equal(accountName, retrieved.Name);
    }

    // ───────────────────────── Entity model inheritance ─────────────────────────

    [Fact]
    public async Task CustomEntityPoco_CanQueryViaDataversePassthrough()
    {
        // Verify that a custom POCO extending the base entity works
        var accounts = await _sales.Dataverse.Query<CustomAccount>("accounts")
            .Select(a => new { a.AccountId, a.Name })
            .Top(1)
            .GetListAsync();

        Assert.NotNull(accounts);
    }

    // ───────────────────────── Cleanup ─────────────────────────

    public async ValueTask DisposeAsync()
    {
        // Cleanup order: opportunities → incidents → contacts → leads → accounts
        foreach (var id in _createdOpportunityIds)
        {
            try { await _fixture.Client.Entity<Opportunity>(entitySet: "opportunities").WithId(id).DeleteAsync(); }
            catch { /* best effort */ }
        }

        foreach (var id in _createdIncidentIds)
        {
            try { await _fixture.Client.Entity<Incident>(entitySet: "incidents").WithId(id).DeleteAsync(); }
            catch { /* best effort */ }
        }

        foreach (var id in _createdContactIds)
        {
            try { await _fixture.Client.Entity<Contact>(entitySet: "contacts").WithId(id).DeleteAsync(); }
            catch { /* best effort */ }
        }

        foreach (var id in _createdLeadIds)
        {
            try { await _fixture.Client.Entity<Lead>(entitySet: "leads").WithId(id).DeleteAsync(); }
            catch { /* best effort */ }
        }

        foreach (var id in _createdAccountIds)
        {
            try { await _fixture.Client.Entity<Account>(entitySet: "accounts").WithId(id).DeleteAsync(); }
            catch { /* best effort */ }
        }
    }

    // ───────────────────────── Test POCOs ─────────────────────────

    /// <summary>
    /// Custom account extending the base entity model — validates
    /// that inheritance works with virtual properties and the Dataverse passthrough.
    /// </summary>
    private class CustomAccount : Account
    {
        // Simulates an org-specific custom field
        public string? CustomField { get; set; }
    }
}
