using Dataverse.Fluent.IntegrationTests.Models;

namespace Dataverse.Fluent.IntegrationTests;

// Queries whose URL is too long for a direct GET. A 2026-11-18 probe found a direct GET fails above 29,725 characters
// (400, then 414, with an HTML error page): about 700 IDs in an OData In list, or 500 in FetchXML. Inside $batch the
// same GET works up to about 64,000 characters, so the library sends long queries there.
// Records created here are deleted by the fixture when the run ends.
[Collection("Dataverse Integration Tests")]
public class LongUrlTests : IClassFixture<DataverseTestFixture>, IAsyncLifetime
{
    private const int RealRecords = 5;

    private readonly DataverseTestFixture _fixture;
    private readonly List<Guid> _real = [];

    public LongUrlTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        for (var i = 0; i < RealRecords; i++)
        {
            var created = await _fixture.Client.Entity<Account>().Set(a => a.Name, $"Long URL {Guid.NewGuid():N}").CreateAsync();
            _real.Add(created.Id!.Value);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // The real IDs, hidden among random ones so the URL is long.
    private List<Guid> Ids(int total)
    {
        var ids = Enumerable.Range(0, total - RealRecords).Select(_ => Guid.NewGuid()).ToList();
        ids.InsertRange(ids.Count / 2, _real);
        return ids;
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(1300)]
    public async Task OData_InWithManyIds_ReturnsExactlyTheMatchingRecords(int total)
    {
        var ids = Ids(total);

        var rows = await _fixture.Client.Query<Account>()
            .Where(a => ids.Contains(a.AccountId))
            .Select(a => a.AccountId)
            .GetListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(_real.Order(), rows.Select(r => r.AccountId).Order());
    }

    [Fact]
    public async Task OData_LongQuery_PagesThroughEveryRecord()
    {
        // The next links repeat the whole filter, so they are batched too.
        var ids = Ids(1000);

        var rows = await _fixture.Client.Query<Account>()
            .Where(a => ids.Contains(a.AccountId))
            .Select(a => a.AccountId)
            .GetAllAsync(pageSize: 2, TestContext.Current.CancellationToken);

        Assert.Equal(_real.Order(), rows.Select(r => r.AccountId).Order());
    }

    [Fact]
    public async Task OData_LongFilter_CountsTheMatchingRecords()
    {
        var ids = Ids(1000);

        var count = await _fixture.Client.Query<Account>().Where(a => ids.Contains(a.AccountId)).CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RealRecords, count);
    }

    [Fact]
    public async Task FetchXml_InWithManyIds_PagesThroughEveryRecord()
    {
        var ids = Ids(800);
        var found = new List<Guid>();

        // Two rows per page, so the paging cookie is followed across pages.
        await foreach (var row in _fixture.Client.FetchBuilder<Account>("account", new Dataverse.Fluent.Builders.FetchXmlOptions { MaxPageSize = 2 })
                           .AddAttribute(a => a.AccountId)
                           .AddFilter(a => ids.Contains(a.AccountId))
                           .StreamAsync(TestContext.Current.CancellationToken))
        {
            found.Add(row.AccountId);
        }

        Assert.Equal(_real.Order(), found.Order());
    }

    [Fact]
    public async Task AQueryTooLongEvenForBatch_FailsBeforeSending()
    {
        var ids = Ids(2500);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Client.Query<Account>()
            .Where(a => ids.Contains(a.AccountId))
            .GetListAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Split", ex.Message);
    }

    [Fact]
    public async Task AQueryUnderTheLimit_IsSentDirectly()
    {
        var log = new RequestLog(_fixture.Client.Transport);
        var ids = Ids(300);

        var rows = await new DataverseClient(log).Query<Account>()
            .Where(a => ids.Contains(a.AccountId))
            .Select(a => a.AccountId)
            .GetListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RealRecords, rows.Count);
        Assert.DoesNotContain("$batch", log.Urls);
    }

    private sealed class RequestLog(IDataverseTransport inner) : IDataverseTransport
    {
        public List<string> Urls { get; } = [];
        public string BaseUrl => inner.BaseUrl;

        public Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
        {
            Urls.Add(request.Url);
            return inner.SendAsync(request, ct);
        }
    }
}
