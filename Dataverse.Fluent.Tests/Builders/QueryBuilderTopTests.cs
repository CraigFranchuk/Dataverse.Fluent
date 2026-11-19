using System.Text.Json.Nodes;

namespace Dataverse.Fluent.Tests.Builders;

// Dataverse answers a $top above 5,000 with 5,000 rows and a next link (Contracts/PagingContractTests in the
// integration tests). GetListAsync and GetListWithCountAsync follow the links so Top means what it says.
public class QueryBuilderTopTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";

    [Fact]
    public async Task GetListAsync_WithTopAbove5000_FollowsNextLinksUpToTop()
    {
        var transport = new PagingTransport(pageSizes: [5000, 5000, 5000]);

        var rows = await new DataverseClient(transport).Query<TestAccount>().Top(6000).GetListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(6000, rows.Count);
        Assert.Equal(2, transport.Requests.Count); // stops once Top is reached
        Assert.Contains("$top=6000", Uri.UnescapeDataString(transport.Requests[0].Url));
    }

    [Fact]
    public async Task GetListAsync_WithTopAbove5000_StopsWhenTheResultsRunOut()
    {
        var transport = new PagingTransport(pageSizes: [5000, 300]);

        var rows = await new DataverseClient(transport).Query<TestAccount>().Top(9000).GetListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5300, rows.Count);
    }

    [Fact]
    public async Task GetListWithCountAsync_WithTopAbove5000_FollowsNextLinksAndKeepsTheCount()
    {
        var transport = new PagingTransport(pageSizes: [5000, 5000], count: 5000);

        var (items, total) = await new DataverseClient(transport).Query<TestAccount>().Top(7000).Count().GetListWithCountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(7000, items.Count);
        Assert.Equal(5000, total);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(5000)]
    public async Task GetListAsync_WithTopUpTo5000OrNone_ReadsOnePage(int? top)
    {
        var transport = new PagingTransport(pageSizes: [5000, 5000]);
        var query = new DataverseClient(transport).Query<TestAccount>();
        if (top is { } t)
            query = query.Top(t);

        var rows = await query.GetListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5000, rows.Count);
        Assert.Single(transport.Requests);
    }

    // Answers each request with the next page size in turn, with a next link while pages remain.
    private sealed class PagingTransport(int[] pageSizes, long? count = null) : IDataverseTransport
    {
        public string BaseUrl => QueryBuilderTopTests.BaseUrl;
        public List<DataverseRequest> Requests { get; } = [];

        public Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            var index = Requests.Count - 1;
            var rows = new JsonArray();
            for (var i = 0; i < pageSizes[index]; i++)
                rows.Add(new JsonObject { ["name"] = $"r{index}-{i}" });

            var page = new JsonObject { ["value"] = rows };
            if (count is { } c)
                page["@odata.count"] = c;
            if (index + 1 < pageSizes.Length)
                page["@odata.nextLink"] = $"{BaseUrl}/testaccounts?$skiptoken=page{index + 1}";
            return Task.FromResult(DataverseResponse.FromJson(page));
        }
    }
}
