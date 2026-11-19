using Dataverse.Fluent.IntegrationTests.Models;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.IntegrationTests;

// Delta queries on Competitor, which has change tracking enabled and no rows of its own in the reference environment,
// so every row a test sees is one it created. (Account and Contact have more than 5,000 rows: an initial query there
// returns a next link and no token on its first page; see Contracts/DeltaContractTests.)
// A 2026-11-18 probe found: a change is visible to the next delta query at once; a deletion comes back as a
// $deletedEntity with a bare id (which the library read as a changed record until 1.0.2); and Dataverse rejects
// $filter, $orderby, $top and $count, so DeltaBuilder no longer has Where, OrderBy, Top or Count.
[Collection("Dataverse Integration Tests")]
public class DeltaQueryTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public DeltaQueryTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> CreateAsync(string label, string? website = null)
    {
        var created = await _fixture.Client.Entity<Competitor>()
            .SetFrom(new Competitor { Name = $"Delta {label} {Guid.NewGuid()}", WebSiteUrl = website })
            .ReturnRepresentation()
            .CreateAsync(Ct);
        return created.Entity!.CompetitorId;
    }

    private Task DeleteAsync(Guid id) => _fixture.Client.Entity<Competitor>(id).DeleteAsync(Ct);

    private async Task<string> TokenAsync()
    {
        var initial = await _fixture.Client.Delta<Competitor>().Select("name", "websiteurl").ExecuteAsync(Ct);
        Assert.False(string.IsNullOrEmpty(initial.DeltaToken), "Dataverse returned no delta token for competitors.");
        Assert.Null(initial.NextLink);
        return initial.DeltaToken!;
    }

    [Fact]
    public async Task InitialQuery_ReturnsCurrentRowsAndAToken()
    {
        var a = await CreateAsync("initial a");
        var b = await CreateAsync("initial b");

        var results = await _fixture.Client.Delta<Competitor>().Select("name").ExecuteAsync(Ct);

        Assert.StartsWith(_fixture.Client.Transport.BaseUrl.TrimEnd('/') + "/competitors?", results.DeltaToken);
        Assert.Contains("$deltatoken=", results.DeltaToken);
        Assert.Contains(results.Value, c => c.CompetitorId == a);
        Assert.Contains(results.Value, c => c.CompetitorId == b);
        Assert.Empty(results.Deleted);
        Assert.Null(results.NextLink);
    }

    [Fact]
    public async Task Since_ReturnsCreatedChangedAndDeletedRecords()
    {
        var changed = await CreateAsync("changed", "https://before.example.com");
        var deleted = await CreateAsync("deleted");
        var untouched = await CreateAsync("untouched");
        var token = await TokenAsync();

        await _fixture.Client.Entity<Competitor>(changed).Set("websiteurl", "https://after.example.com").UpdateAsync(Ct);
        await DeleteAsync(deleted);
        var created = await CreateAsync("created");

        var changes = await _fixture.Client.Delta<Competitor>().Since(token).ExecuteAsync(Ct);

        Assert.Equal("https://after.example.com", Assert.Single(changes.Value, c => c.CompetitorId == changed).WebSiteUrl);
        Assert.Contains(changes.Value, c => c.CompetitorId == created);
        Assert.DoesNotContain(changes.Value, c => c.CompetitorId == untouched);
        Assert.DoesNotContain(changes.Value, c => c.CompetitorId == deleted || c.CompetitorId == Guid.Empty);

        var removal = Assert.Single(changes.Deleted);
        Assert.Equal(deleted, removal.GetEntityId());
        Assert.Equal("deleted", removal.Removed?.Reason);
        Assert.False(string.IsNullOrEmpty(changes.DeltaToken));
    }

    [Fact]
    public async Task Since_WithNoChanges_ReturnsNothing()
    {
        await CreateAsync("quiet");
        var token = await TokenAsync();

        var changes = await _fixture.Client.Delta<Competitor>().Since(token).ExecuteAsync(Ct);

        Assert.Empty(changes.Value);
        Assert.Empty(changes.Deleted);
        Assert.False(string.IsNullOrEmpty(changes.DeltaToken));
    }

    [Fact]
    public async Task EachToken_ReturnsOnlyTheChangesAfterIt()
    {
        var token1 = await TokenAsync();
        var first = await CreateAsync("first");

        var changes1 = await _fixture.Client.Delta<Competitor>().Since(token1).ExecuteAsync(Ct);
        Assert.Contains(changes1.Value, c => c.CompetitorId == first);

        var second = await CreateAsync("second");
        var changes2 = await _fixture.Client.Delta<Competitor>().Since(changes1.DeltaToken!).ExecuteAsync(Ct);

        Assert.Contains(changes2.Value, c => c.CompetitorId == second);
        Assert.DoesNotContain(changes2.Value, c => c.CompetitorId == first);
    }

    [Fact]
    public async Task Since_KeepsTheSelectOfTheInitialQuery()
    {
        // The delta link carries the initial query's $select; a later Select() cannot change it.
        var id = await CreateAsync("select", "https://example.com");
        var initial = await _fixture.Client.Delta<Competitor>().Select("name").ExecuteAsync(Ct);

        var row = Assert.Single(initial.Value, c => c.CompetitorId == id);
        Assert.Null(row.WebSiteUrl);
        Assert.Contains("$select=name&", initial.DeltaToken);
    }

    [Fact]
    public async Task StreamAsync_PagesTheInitialQuery_AndTheTokenComesWithTheLastPage()
    {
        var ids = new[] { await CreateAsync("page 1"), await CreateAsync("page 2"), await CreateAsync("page 3") };
        var pages = new List<DeltaResults<Competitor>>();

        await foreach (var page in _fixture.Client.Delta<Competitor>().Select("name").StreamAsync(pageSize: 2, Ct))
            pages.Add(page);

        Assert.True(pages.Count >= 2, $"Expected at least 2 pages of 2, got {pages.Count}.");
        Assert.All(pages, p => Assert.True(p.Value.Count <= 2));
        Assert.All(pages[..^1], p => { Assert.Null(p.DeltaToken); Assert.NotNull(p.NextLink); });
        Assert.False(string.IsNullOrEmpty(pages[^1].DeltaToken));
        Assert.Null(pages[^1].NextLink);

        var seen = pages.SelectMany(p => p.Value).Select(c => c.CompetitorId).ToList();
        Assert.All(ids, id => Assert.Contains(id, seen));
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    [Fact]
    public async Task StreamAsync_Since_ReturnsTheChanges()
    {
        var token = await TokenAsync();
        var created = await CreateAsync("streamed");

        var pages = new List<DeltaResults<Competitor>>();
        await foreach (var page in _fixture.Client.Delta<Competitor>().Since(token).StreamAsync(ct: Ct))
            pages.Add(page);

        Assert.Contains(pages.SelectMany(p => p.Value), c => c.CompetitorId == created);
        Assert.False(string.IsNullOrEmpty(pages[^1].DeltaToken));
    }

    [Fact]
    public async Task IncludeFormattedValues_ReturnsTheAnnotations()
    {
        var id = await CreateAsync("formatted");

        var results = await _fixture.Client.Delta<Competitor>()
            .Select("name", "createdon")
            .IncludeFormattedValues()
            .ExecuteAsync(Ct);

        var row = Assert.Single(results.Value, c => c.CompetitorId == id);
        Assert.NotNull(row.CreatedOn);
        Assert.False(string.IsNullOrEmpty(row.CreatedOnFormatted));
        Assert.False(string.IsNullOrEmpty(results.DeltaToken));
    }

    [Fact]
    public async Task WithHeaders_SendsThemAlongsideChangeTracking()
    {
        var results = await _fixture.Client.Delta<Competitor>()
            .WithHeaders(new Dictionary<string, string> { ["MSCRM.SuppressDuplicateDetection"] = "true" })
            .ExecuteAsync(Ct);

        Assert.False(string.IsNullOrEmpty(results.DeltaToken));
    }

    [Fact]
    public async Task AnInvalidToken_FailsWithADataverseException()
    {
        var token = await TokenAsync();
        var broken = token[..token.IndexOf("$deltatoken=", StringComparison.Ordinal)] + "$deltatoken=not-a-token";

        var ex = await Assert.ThrowsAsync<DataverseException>(() =>
            _fixture.Client.Delta<Competitor>().Since(broken).ExecuteAsync(Ct));

        Assert.True((int)ex.StatusCode is >= 400 and < 500, $"Expected a 4xx, got {(int)ex.StatusCode}: {ex.Message}");
    }

    [Fact]
    public void Since_WithNullOrEmptyToken_Throws()
    {
        Assert.Throws<ArgumentException>(() => _fixture.Client.Delta<Competitor>().Since(null!));
        Assert.Throws<ArgumentException>(() => _fixture.Client.Delta<Competitor>().Since(string.Empty));
    }
}
