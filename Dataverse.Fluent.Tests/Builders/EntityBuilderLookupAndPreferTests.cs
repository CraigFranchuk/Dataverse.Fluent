using System.Net;

namespace Dataverse.Fluent.Tests.Builders;

/// <summary>
/// Tests the lookup-binding setters and the Prefer-header options of <see cref="Dataverse.Fluent.Builders.EntityBuilder{T}"/>.
/// </summary>
public class EntityBuilderLookupAndPreferTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static (DataverseClient Client, TestHttpMessageHandler Handler) CreateClient(HttpStatusCode status = HttpStatusCode.NoContent)
    {
        var handler = new TestHttpMessageHandler();
        handler.AddResponse(_ => true, new HttpResponseMessage(status) { Content = new StringContent(status == HttpStatusCode.NoContent ? "" : "{}") });
        return (new DataverseClient("https://test.crm.dynamics.com/api/data/v9.2", () => Task.FromResult("t"), new HttpClient(handler)), handler);
    }

    private static string? Bind(CapturedRequest request, string lookupField)
        => request.Json.GetProperty($"{lookupField}@odata.bind").GetString();

    // ────────── Lookups ──────────

    [Fact]
    public async Task SetLookup_BindsById()
    {
        var (client, handler) = CreateClient();

        await client.Entity<TestContact>().SetLookup("parentcustomerid_account", "accounts", AccountId).CreateAsync();

        Assert.Equal($"/accounts({AccountId})", Bind(Assert.Single(handler.Requests), "parentcustomerid_account"));
    }

    [Fact]
    public async Task SetLookupByAlternateKey_Typed_UsesThePropertysLogicalName()
    {
        var (client, handler) = CreateClient();

        await client.Entity<TestContact>()
            .SetLookupByAlternateKey<TestAccount, string?>("parentcustomerid_account", "accounts", a => a.AccountNumber, "ACC-001")
            .CreateAsync();

        Assert.Equal("/accounts(accountnumber='ACC-001')", Bind(Assert.Single(handler.Requests), "parentcustomerid_account"));
    }

    [Fact]
    public async Task SetLookupByAlternateKeys_Composite_BindsEveryKeyPart()
    {
        var (client, handler) = CreateClient();

        await client.Entity<TestContact>()
            .SetLookupByAlternateKeys("productid", "products", new Dictionary<string, object>
            {
                ["dataareaid"] = "USMF",
                ["itemnumber"] = "A0001",
                ["revision"] = 2
            })
            .CreateAsync();

        Assert.Equal("/products(dataareaid='USMF',itemnumber='A0001',revision=2)", Bind(Assert.Single(handler.Requests), "productid"));
    }

    [Fact]
    public async Task SetLookup_ThenSetLookup_LastBindingWins()
    {
        var (client, handler) = CreateClient();
        var other = Guid.NewGuid();

        await client.Entity<TestContact>()
            .SetLookup("parentcustomerid_account", "accounts", AccountId)
            .SetLookup("parentcustomerid_account", "accounts", other)
            .CreateAsync();

        Assert.Equal($"/accounts({other})", Bind(Assert.Single(handler.Requests), "parentcustomerid_account"));
    }

    [Theory]
    [InlineData(null, "accounts")]
    [InlineData(" ", "accounts")]
    [InlineData("parentcustomerid_account", null)]
    [InlineData("parentcustomerid_account", "")]
    public void LookupSetters_WithBlankFieldOrEntitySet_Throw(string? lookupField, string? entitySet)
    {
        var (client, _) = CreateClient();
        var entity = client.Entity<TestContact>();

        Assert.ThrowsAny<ArgumentException>(() => entity.SetLookup(lookupField!, entitySet!, AccountId));
        Assert.ThrowsAny<ArgumentException>(() => entity.SetLookupByAlternateKey(lookupField!, entitySet!, "accountnumber", "x"));
        Assert.ThrowsAny<ArgumentException>(() => entity.SetLookupByAlternateKeys(lookupField!, entitySet!, new Dictionary<string, object> { ["k"] = 1 }));
    }

    [Fact]
    public void LookupSetters_WithMissingKeys_Throw()
    {
        var (client, _) = CreateClient();
        var entity = client.Entity<TestContact>();

        Assert.ThrowsAny<ArgumentException>(() => entity.SetLookupByAlternateKey("lookup", "accounts", " ", "x"));
        Assert.Throws<ArgumentException>(() => entity.SetLookupByAlternateKeys("lookup", "accounts", []));
        Assert.Throws<ArgumentException>(() => entity.SetLookupByAlternateKeys("lookup", "accounts", null!));
        Assert.Throws<ArgumentNullException>(() =>
            entity.SetLookupByAlternateKey<TestAccount, string?>("lookup", "accounts", null!, "x"));
    }

    // ────────── Prefer header ──────────

    [Theory]
    [InlineData("minimal", "return=minimal")]
    [InlineData("representation", "return=representation")]
    [InlineData("representation,minimal", "return=minimal")]        // last call wins
    [InlineData("minimal,representation", "return=representation")]
    [InlineData("representation,representation", "return=representation")] // no duplicate
    [InlineData("representation,!representation", null)]            // ReturnRepresentation(false) turns it off
    public async Task ReturnPreferences_CombineAsDocumented(string calls, string? expectedPrefer)
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK);
        var entity = client.Entity<TestAccount>().Set("name", "x");
        foreach (var call in calls.Split(','))
        {
            entity = call switch
            {
                "minimal" => entity.ReturnMinimal(),
                "representation" => entity.ReturnRepresentation(),
                "!representation" => entity.ReturnRepresentation(false),
                _ => throw new ArgumentOutOfRangeException(nameof(calls))
            };
        }

        await entity.CreateAsync();

        Assert.Equal(expectedPrefer, Assert.Single(handler.Requests).Header("Prefer"));
    }

    [Fact]
    public async Task ReturnMinimal_IsSentOnUpdate()
    {
        var (client, handler) = CreateClient();

        await client.Entity<TestAccount>(AccountId).Set("name", "x").ReturnMinimal().UpdateAsync();

        Assert.Equal("return=minimal", Assert.Single(handler.Requests).Header("Prefer"));
    }

    [Fact]
    public async Task WithPrefer_AndReturnRepresentation_MergeIntoOneHeader()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK);

        await client.Entity<TestAccount>()
            .Set("name", "x")
            .WithPrefer("odata.continue-on-error")
            .ReturnRepresentation()
            .CreateAsync();

        Assert.Equal(
            "odata.continue-on-error,return=representation",
            Assert.Single(handler.Requests).Header("Prefer"));
    }

    [Fact]
    public async Task WithPrefer_AndFormattedValues_MergeIntoOneHeaderOnRetrieve()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK);

        await client.Entity<TestAccount>(AccountId)
            .WithPrefer("odata.maxpagesize=10")
            .IncludeFormattedValues()
            .RetrieveAsync();

        Assert.Equal(
            "odata.maxpagesize=10,odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"",
            Assert.Single(handler.Requests).Header("Prefer"));
    }

    [Fact]
    public async Task IncludeFormattedValues_False_SendsNoPreferHeader()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK);

        await client.Entity<TestAccount>(AccountId).IncludeFormattedValues().IncludeFormattedValues(false).RetrieveAsync();

        Assert.Null(Assert.Single(handler.Requests).Header("Prefer"));
    }
}
