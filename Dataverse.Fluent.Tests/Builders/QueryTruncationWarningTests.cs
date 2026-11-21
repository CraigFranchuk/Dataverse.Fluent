using System.Text;
using Microsoft.Extensions.Logging;

namespace Dataverse.Fluent.Tests.Builders;

/// <summary>
/// <c>GetListAsync</c> returns one page. When the caller set no limit and Dataverse has more rows, the list is
/// silently incomplete, so a warning is logged.
/// </summary>
public class QueryTruncationWarningTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";
    private const string NextLink = BaseUrl + "/testaccounts?$skiptoken=page2";

    private static HttpResponseMessage Page(string? nextLink, int rows = 2)
    {
        var values = string.Join(",", Enumerable.Range(0, rows).Select(i => $"{{\"name\":\"A{i}\"}}"));
        var link = nextLink is null ? "" : $",\"@odata.nextLink\":\"{nextLink}\"";
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"@odata.count\":9,\"value\":[{values}]{link}}}", Encoding.UTF8, "application/json")
        };
    }

    private static (DataverseClient Client, TestLogger<DataverseClient> Logger) Create(params HttpResponseMessage[] pages)
    {
        var handler = new TestHttpMessageHandler();
        foreach (var page in pages)
            handler.AddResponse(_ => true, page);

        var logger = new TestLogger<DataverseClient>();
        return (new DataverseClient(BaseUrl, () => Task.FromResult("test-token"), new HttpClient(handler), logger), logger);
    }

    private static List<LogEntry> Warnings(TestLogger<DataverseClient> logger)
        => logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();

    [Fact]
    public async Task GetListAsync_WithNoLimitAndMoreRows_LogsAWarning()
    {
        var (client, logger) = Create(Page(NextLink));

        var items = await client.Query<TestAccount>().GetListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, items.Count);
        var warning = Assert.Single(Warnings(logger));
        Assert.Contains("testaccounts", warning.Message);
        Assert.Contains("returned 2 rows", warning.Message);
        Assert.Contains("GetAllAsync()", warning.Message);
        Assert.Contains("StreamAsync()", warning.Message);
    }

    [Fact]
    public async Task GetListWithCountAsync_WithNoLimitAndMoreRows_LogsAWarning()
    {
        var (client, logger) = Create(Page(NextLink));

        await client.Query<TestAccount>().GetListWithCountAsync(TestContext.Current.CancellationToken);

        Assert.Single(Warnings(logger));
    }

    [Fact]
    public async Task GetListAsync_WhenEveryRowWasReturned_LogsNothing()
    {
        var (client, logger) = Create(Page(nextLink: null));

        await client.Query<TestAccount>().GetListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(Warnings(logger));
    }

    [Fact]
    public async Task GetListAsync_WithPageSize_LogsNothing()
    {
        var (client, logger) = Create(Page(NextLink));

        await client.Query<TestAccount>().PageSize(2).GetListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(Warnings(logger));
    }

    [Fact]
    public async Task GetListAsync_WithTop_LogsNothing()
    {
        var (client, logger) = Create(Page(nextLink: null));

        await client.Query<TestAccount>().Top(2).GetListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(Warnings(logger));
    }

    [Fact]
    public async Task GetAllAsync_FollowsTheNextLinkAndLogsNothing()
    {
        var (client, logger) = Create(Page(NextLink), Page(nextLink: null));

        var items = await client.Query<TestAccount>().GetAllAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(4, items.Count);
        Assert.Empty(Warnings(logger));
    }

    [Fact]
    public async Task GetPagedAsync_LogsNothing()
    {
        var (client, logger) = Create(Page(NextLink));

        var page = await client.Query<TestAccount>().GetPagedAsync(TestContext.Current.CancellationToken);

        Assert.True(page.HasMorePages);
        Assert.Empty(Warnings(logger));
    }

    [Fact]
    public async Task GetListAsync_ThroughWithOptions_StillLogsTheWarning()
    {
        var (client, logger) = Create(Page(NextLink));

        await client.WithOptions(o => o.BypassPowerAutomateFlows = true).Query<TestAccount>().GetListAsync(TestContext.Current.CancellationToken);

        Assert.Single(Warnings(logger));
    }
}
