using System.Text.Json.Nodes;
using Dataverse.Fluent;
using Dataverse.Fluent.Sales.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Dataverse.Fluent.Sales.Tests;

/// <summary>
/// The Sales models are mapped: they resolve their entity sets, read lookups by value name and write them as binds.
/// </summary>
public class SalesMappingTests
{
    private static readonly Guid ContactId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class FakeTransport : IDataverseTransport
    {
        public string BaseUrl => "https://org.crm.dynamics.com/api/data/v9.2";
        public List<DataverseRequest> Requests { get; } = [];

        public Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(DataverseResponse.FromJson(new JsonObject { ["value"] = new JsonArray() }));
        }
    }

    [Theory]
    [InlineData(typeof(Account), "accounts")]
    [InlineData(typeof(Contact), "contacts")]
    [InlineData(typeof(Incident), "incidents")]
    [InlineData(typeof(Lead), "leads")]
    [InlineData(typeof(Opportunity), "opportunities")]
    [InlineData(typeof(OpportunityProduct), "opportunityproducts")]
    [InlineData(typeof(Quote), "quotes")]
    [InlineData(typeof(QuoteDetail), "quotedetails")]
    public async Task Entities_ResolveTheirEntitySet(Type entity, string entitySet)
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport);

        var query = typeof(DataverseClient).GetMethod(nameof(DataverseClient.Query))!.MakeGenericMethod(entity).Invoke(client, [null])!;
        await (Task)query.GetType().GetMethod("GetListAsync")!.Invoke(query, [TestContext.Current.CancellationToken])!;

        Assert.Equal(entitySet, transport.Requests.Single().Url);
    }

    [Fact]
    public void SetFrom_WritesLookupsAsBinds_AndSkipsRelatedRecordsAndPolymorphicLookups()
    {
        var client = new DataverseClient(new FakeTransport());
        var retrieved = new Account
        {
            AccountId = Guid.NewGuid(),
            Name = "Contoso",
            PrimaryContactId = ContactId,
            Contacts = [new Contact { FirstName = "a" }]
        };

        var payload = client.Entity<Account>(retrieved.AccountId).SetFrom(retrieved).ToUpdateRequest().Payload!;

        Assert.Equal($"/contacts({ContactId})", (string?)payload["primarycontactid@odata.bind"]);
        Assert.False(payload.ContainsKey("_primarycontactid_value"));
        Assert.False(payload.ContainsKey("contact_customer_accounts"));

        var contact = client.Entity<Contact>(Guid.NewGuid())
            .SetFrom(new Contact { LastName = "x", ParentCustomerId = Guid.NewGuid() })
            .ToUpdateRequest().Payload!;
        Assert.DoesNotContain(contact, p => p.Key.Contains("parentcustomerid", StringComparison.Ordinal));
    }

    [Fact]
    public void FetchBuilder_UsesTheTableLogicalName()
    {
        var client = new DataverseClient(new FakeTransport());

        var fetchXml = client.FetchBuilder<OpportunityProduct>().AddAttribute(p => p.ProductId).BuildFetchXml();

        Assert.Contains("<entity name=\"opportunityproduct\">", fetchXml);
        Assert.Contains("<attribute name=\"productid\" />", fetchXml);
    }

    [Fact]
    public void KeyedRegistration_ResolvesASalesClientPerEnvironment()
    {
        var services = new ServiceCollection();
        services.AddDynamicsSalesClient(o => { o.BaseUrl = "https://a.crm.dynamics.com/api/data/v9.2"; o.TokenProvider = () => Task.FromResult("t"); });
        services.AddKeyedDynamicsSalesClient("b", o => { o.BaseUrl = "https://b.crm.dynamics.com/api/data/v9.2"; o.TokenProvider = () => Task.FromResult("t"); });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Equal("https://a.crm.dynamics.com/api/data/v9.2", scope.ServiceProvider.GetRequiredService<SalesClient>().Dataverse.Transport.BaseUrl);
        Assert.Equal("https://b.crm.dynamics.com/api/data/v9.2", scope.ServiceProvider.GetRequiredKeyedService<SalesClient>("b").Dataverse.Transport.BaseUrl);
    }
}
