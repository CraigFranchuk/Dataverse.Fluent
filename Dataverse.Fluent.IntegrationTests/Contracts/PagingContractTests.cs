using System.Text.Json.Nodes;

namespace Dataverse.Fluent.IntegrationTests.Contracts;

// Dataverse's paging behaviour, as established on 2026-11-18. The library depends on each of these:
// - QueryBuilder.ThrowIfTopAndPageSizeCombined: $top is ignored when odata.maxpagesize is present.
// - QueryBuilder/DeltaBuilder re-send odata.maxpagesize on every page: a next link alone falls back to 5,000.
// - ODataPaging.MaxPageSize (5,000): a larger page size is capped, not rejected.
// - QueryBuilder.CollectUpToTopAsync: a $top above 5,000 comes back as 5,000 rows and a next link.
// Needs a table with more than 5,000 rows (Account in the reference environment).
[Collection("Dataverse Integration Tests")]
[Trait("Category", "Contract")]
public class PagingContractTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public PagingContractTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    private Task<JsonObject> GetAsync(string url, string? prefer = null)
        => _fixture.Client.Transport.ExecuteRequestAsync(HttpMethod.Get, url,
            headers: prefer is null ? null : new Dictionary<string, string> { ["Prefer"] = prefer },
            ct: TestContext.Current.CancellationToken);

    private static int Rows(JsonObject page) => page["value"]!.AsArray().Count;

    [Fact]
    public async Task TopIsIgnored_WhenAPageSizeIsRequested()
    {
        ContractTest.RequireOptIn();

        var page = await GetAsync("accounts?$select=accountid&$top=3", "odata.maxpagesize=2");

        Assert.Equal(2, Rows(page));
        Assert.NotNull(page["@odata.nextLink"]);
    }

    [Fact]
    public async Task ANextLink_DoesNotKeepThePageSize()
    {
        ContractTest.RequireOptIn();
        var first = await GetAsync("accounts?$select=accountid", "odata.maxpagesize=2");
        var next = (string)first["@odata.nextLink"]!;

        var without = await GetAsync(next);
        var with = await GetAsync(next, "odata.maxpagesize=2");

        Assert.Equal(5000, Rows(without));
        Assert.Equal(2, Rows(with));
    }

    [Fact]
    public async Task APageSizeAbove5000_IsCappedAt5000()
    {
        ContractTest.RequireOptIn();

        var page = await GetAsync("accounts?$select=accountid", "odata.maxpagesize=6000");

        Assert.Equal(5000, Rows(page));
    }

    [Fact]
    public async Task ATopAbove5000_ReturnsOnePageOf5000AndANextLink()
    {
        ContractTest.RequireOptIn();

        var page = await GetAsync("accounts?$select=accountid&$top=6000");

        Assert.Equal(5000, Rows(page));
        Assert.NotNull(page["@odata.nextLink"]);
    }
}
