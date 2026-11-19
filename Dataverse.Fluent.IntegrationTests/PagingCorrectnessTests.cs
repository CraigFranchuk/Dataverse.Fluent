using Dataverse.Fluent.Builders;
using Dataverse.Fluent.IntegrationTests.Models;

namespace Dataverse.Fluent.IntegrationTests;

// Paging returns every row exactly once, including when the sort column has ties and with FetchXML distinct, and Top
// above 5,000 returns that many rows (a single Dataverse response holds at most 5,000).
// Records created here are deleted by the fixture when the run ends.
[Collection("Dataverse Integration Tests")]
public class PagingCorrectnessTests : IClassFixture<DataverseTestFixture>, IAsyncLifetime
{
    private static readonly string[] Names = ["A", "A", "B", "B", "C", "C", "D"];

    private readonly DataverseTestFixture _fixture;
    private readonly string _prefix = $"Pg {Guid.NewGuid().ToString("N")[..8]} ";
    private readonly List<Guid> _ids = [];

    public PagingCorrectnessTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        // The same revenue on every record, so a sort on it ties across page boundaries.
        foreach (var name in Names)
            _ids.Add((await _fixture.Client.Entity<Account>().Set(a => a.Name, _prefix + name).Set(a => a.Revenue, 100m).CreateAsync()).Id!.Value);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task OData_PagesSortedOnATiedColumn_ReturnEveryRowOnce()
    {
        var rows = await _fixture.Client.Query<Account>()
            .Where(a => a.Name.StartsWith(_prefix))
            .OrderBy(a => a.Revenue)
            .Select(a => a.AccountId)
            .GetAllAsync(pageSize: 2, TestContext.Current.CancellationToken);

        Assert.Equal(_ids.Order(), rows.Select(r => r.AccountId).Order());
    }

    [Fact]
    public async Task FetchXml_PagesSortedOnATiedColumn_ReturnEveryRowOnce()
    {
        var rows = await _fixture.Client.FetchBuilder<Account>("account", new FetchXmlOptions { MaxPageSize = 2 })
            .AddAttribute(a => a.AccountId)
            .AddFilter(a => a.Name.StartsWith(_prefix))
            .AddOrder(a => a.Revenue)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(_ids.Order(), rows.Select(r => r.AccountId).Order());
    }

    [Fact]
    public async Task FetchXml_DistinctAcrossPages_ReturnsEachValueOnce()
    {
        var rows = await _fixture.Client.FetchBuilder<Account>("account", new FetchXmlOptions { MaxPageSize = 2 })
            .AddAttribute(a => a.Name)
            .AddFilter(a => a.Name.StartsWith(_prefix))
            .AddOrder(a => a.Name)
            .Distinct()
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["A", "B", "C", "D"], rows.Select(r => r.Name[_prefix.Length..]));
    }

    [Fact]
    public async Task TopAbove5000_GetListAsync_ReturnsThatManyRows()
    {
        // Dataverse answers a $top above 5,000 with 5,000 rows and a next link; GetListAsync used to stop there.
        var ct = TestContext.Current.CancellationToken;
        var available = (await _fixture.Client.Query<Account>().Select(a => a.AccountId).Top(6000).GetAllAsync(ct: ct)).Count;
        Assert.SkipWhen(available <= 5000, "Needs more than 5,000 accounts.");

        var list = await _fixture.Client.Query<Account>().Select(a => a.AccountId).Top(6000).GetListAsync(ct);
        var (withCount, _) = await _fixture.Client.Query<Account>().Select(a => a.AccountId).Top(6000).Count().GetListWithCountAsync(ct);

        Assert.Equal(available, list.Count);
        Assert.Equal(available, withCount.Count);
        Assert.Equal(list.Count, list.Select(r => r.AccountId).Distinct().Count());
    }
}
