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

    [Fact]
    public async Task Expand_ByNavigationProperty_SendsTheMappedRelationship()
    {
        var transport = new FakeTransport();

        await new DataverseClient(transport).Query<Opportunity>()
            .Expand(o => o.Products, e => e.Top(5))
            .GetListAsync(TestContext.Current.CancellationToken);

        Assert.Contains("$expand=product_opportunities($top=5)", Uri.UnescapeDataString(transport.Requests.Single().Url));
    }

    [Theory]
    [InlineData(typeof(SalesOrder), "salesorders")]
    [InlineData(typeof(SalesOrderDetail), "salesorderdetails")]
    [InlineData(typeof(Invoice), "invoices")]
    [InlineData(typeof(InvoiceDetail), "invoicedetails")]
    [InlineData(typeof(Product), "products")]
    [InlineData(typeof(PriceLevel), "pricelevels")]
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
    public void SetFrom_WritesLookupsAsBinds_AndSkipsRelatedRecords()
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

    }

    [Fact]
    public void PolymorphicLookups_AreWrittenForTheirTable_AndTheOwnerOnCreateOnly()
    {
        var client = new DataverseClient(new FakeTransport());
        var accountId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var opportunity = new Opportunity
        {
            Name = "o",
            CustomerId = DataverseReference.To<Account>(accountId),
            OwnerId = new DataverseReference("team", teamId)
        };

        var create = client.Entity<Opportunity>().SetFrom(opportunity).ToCreateRequest().Payload!;
        var update = client.Entity<Opportunity>(Guid.NewGuid()).SetFrom(opportunity).ToUpdateRequest().Payload!;
        var reassign = client.Entity<Opportunity>(Guid.NewGuid()).Set(o => o.OwnerId, new DataverseReference("systemuser", teamId)).ToUpdateRequest().Payload!;
        var contact = client.Entity<Contact>()
            .SetFrom(new Contact { LastName = "x", ParentCustomerId = DataverseReference.To<Contact>(accountId) })
            .ToCreateRequest().Payload!;

        Assert.Equal($"/accounts({accountId})", (string?)create["customerid_account@odata.bind"]);
        Assert.Equal($"/teams({teamId})", (string?)create["ownerid@odata.bind"]);
        Assert.Equal($"/accounts({accountId})", (string?)update["customerid_account@odata.bind"]);
        Assert.False(update.ContainsKey("ownerid@odata.bind"));
        Assert.Equal($"/systemusers({teamId})", (string?)reassign["ownerid@odata.bind"]);
        Assert.Equal($"/contacts({accountId})", (string?)contact["parentcustomerid_contact@odata.bind"]);
    }

    [Fact]
    public void SetFrom_LeavesOutColumnsDataverseSetsItself()
    {
        // Dataverse silently ignores these values (checked 2026-11-20), so a retrieved record written back must not
        // send them: the quote number can be given on create only, the revision number and full name never.
        var client = new DataverseClient(new FakeTransport());
        var quote = new Quote { Name = "Q", QuoteNumber = "QUO-1", RevisionNumber = 2 };

        var create = client.Entity<Quote>().SetFrom(quote).ToCreateRequest().Payload!;
        var update = client.Entity<Quote>(Guid.NewGuid()).SetFrom(quote).ToUpdateRequest().Payload!;
        var contact = client.Entity<Contact>().SetFrom(new Contact { LastName = "x", FullName = "y" }).ToCreateRequest().Payload!;

        Assert.Equal("QUO-1", (string?)create["quotenumber"]);
        Assert.False(create.ContainsKey("revisionnumber"));
        Assert.False(update.ContainsKey("quotenumber"));
        Assert.False(update.ContainsKey("revisionnumber"));
        Assert.False(contact.ContainsKey("fullname"));
    }

    [Fact]
    public void DateOnlyColumns_AreWrittenAsADate()
    {
        var client = new DataverseClient(new FakeTransport());

        var payload = client.Entity<Opportunity>()
            .SetFrom(new Opportunity { Name = "o", EstimatedCloseDate = new DateOnly(2026, 3, 15) })
            .ToCreateRequest().Payload!;

        Assert.Equal("2026-03-15", (string?)payload["estimatedclosedate"]);
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
