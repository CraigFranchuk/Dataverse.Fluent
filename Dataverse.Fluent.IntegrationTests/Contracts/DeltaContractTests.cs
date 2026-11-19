using System.Text.Json.Nodes;
using Dataverse.Fluent.IntegrationTests.Models;

namespace Dataverse.Fluent.IntegrationTests.Contracts;

// Dataverse's change-tracking behaviour, as established on 2026-11-18. The library depends on each of these:
// - DeltaBuilder has no Where/OrderBy/Top/Count: Dataverse rejects those options with change tracking (0x8006088a).
// - DeltaBuilder.TryReadDeleted: a deletion is {"@odata.context": "...$deletedEntity", "id", "reason"}.
// - DELTA_QUERIES.md: on a table with more rows than a page, the token comes only after the last page.
[Collection("Dataverse Integration Tests")]
[Trait("Category", "Contract")]
public class DeltaContractTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public DeltaContractTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    private Task<JsonObject> GetTrackedAsync(string url, string? extraPrefer = null)
        => _fixture.Client.Transport.ExecuteRequestAsync(HttpMethod.Get, url,
            headers: new Dictionary<string, string> { ["Prefer"] = extraPrefer is null ? "odata.track-changes" : $"odata.track-changes,{extraPrefer}" },
            ct: TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("competitors", "$filter=name ne null")]
    [InlineData("competitors", "$orderby=name")]
    [InlineData("competitors", "$top=1")]
    [InlineData("competitors", "$count=true")]
    [InlineData("accounts", "$filter=statecode eq 0")]
    [InlineData("accounts", "$orderby=createdon")]
    public async Task QueryOptionsOtherThanSelect_AreRejected(string entitySet, string option)
    {
        ContractTest.RequireOptIn();

        var ex = await Assert.ThrowsAsync<Fluent.Models.DataverseException>(() => GetTrackedAsync($"{entitySet}?$select=createdon&{option}"));

        Assert.Equal("0x8006088a", ex.ErrorCode);
        Assert.Contains("isn't supported when Change Tracking is enabled", ex.Message);
    }

    [Fact]
    public async Task ADeletion_ComesBackAsADeletedEntityWithABareId()
    {
        ContractTest.RequireOptIn();
        var ct = TestContext.Current.CancellationToken;

        var created = await _fixture.Client.Entity<Competitor>().Set("name", $"Contract {Guid.NewGuid()}").ReturnRepresentation().CreateAsync(ct);
        var id = created.Entity!.CompetitorId;
        var token = (string)(await GetTrackedAsync("competitors?$select=name"))["@odata.deltaLink"]!;
        await _fixture.Client.Entity<Competitor>(id).DeleteAsync(ct);

        var changes = await GetTrackedAsync(token);

        var removal = Assert.Single(changes["value"]!.AsArray(), r => (string?)r!["id"] == id.ToString())!;
        Assert.EndsWith("/$deletedEntity", (string)removal["@odata.context"]!);
        Assert.Equal("deleted", (string?)removal["reason"]);
        Assert.Null(removal["@removed"]);
    }

    [Fact]
    public async Task OnALargeTable_TheTokenComesOnlyAfterTheLastPage()
    {
        ContractTest.RequireOptIn();
        var ct = TestContext.Current.CancellationToken;

        // Account has more than 5,000 rows in the reference environment.
        var first = await _fixture.Client.Delta<Account>().Select("accountid").ExecuteAsync(ct);
        Assert.Null(first.DeltaToken);
        Assert.NotNull(first.NextLink);

        string? token = null;
        var pages = 0;
        await foreach (var page in _fixture.Client.Delta<Account>().Select("accountid").StreamAsync(pageSize: 5000, ct))
        {
            pages++;
            token = page.DeltaToken;
        }

        Assert.True(pages >= 2);
        Assert.False(string.IsNullOrEmpty(token));
    }
}
