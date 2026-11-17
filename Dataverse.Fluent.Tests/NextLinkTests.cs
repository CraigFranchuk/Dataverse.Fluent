using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Dataverse.Fluent.Tests;

/// <summary>
/// Tests how server-supplied <c>@odata.nextLink</c> URLs are followed: always against the configured
/// environment (so the bearer token never goes to another host), and never in a loop.
/// </summary>
public class NextLinkTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";

    private static HttpResponseMessage Page(string? nextLink, params string[] names)
    {
        var value = string.Join(",", names.Select(n => $"{{\"name\":\"{n}\"}}"));
        var link = nextLink is null ? "" : $",\"@odata.nextLink\":\"{nextLink}\"";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"value\":[{value}]{link}}}", Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task CrossHostNextLink_IsSentToTheConfiguredHost_AndLogged()
    {
        var handler = new TestHttpMessageHandler();
        var logger = new TestLogger<DataverseClient>();
        var client = new DataverseClient(BaseUrl, () => Task.FromResult("secret-token"), new HttpClient(handler), logger);
        handler.AddResponse(_ => true, Page(null, "A"));

        await client.Query<TestAccount>().GetNextPageAsync("https://attacker.example.com/api/data/v9.2/testaccounts?$skiptoken=2");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("test.crm.dynamics.com", request.Uri.Host);
        Assert.Equal("/api/data/v9.2/testaccounts", request.Uri.AbsolutePath);
        Assert.Equal("2", request.QueryOption("$skiptoken"));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("attacker.example.com"));
    }

    [Fact]
    public async Task StreamAsync_CrossHostNextLink_StaysOnTheConfiguredHost()
    {
        var handler = new TestHttpMessageHandler();
        var client = new DataverseClient(BaseUrl, () => Task.FromResult("t"), new HttpClient(handler));
        handler.AddResponse(_ => true, Page("https://attacker.example.com/api/data/v9.2/testaccounts?$skiptoken=2", "A"));
        handler.AddResponse(_ => true, Page(null, "B"));

        var names = new List<string>();
        await foreach (var account in client.Query<TestAccount>().StreamAsync(1))
            names.Add(account.Name);

        Assert.Equal(["A", "B"], names);
        Assert.All(handler.Requests, r => Assert.Equal("test.crm.dynamics.com", r.Uri.Host));
    }

    [Theory]
    [InlineData("https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$skiptoken=2")] // absolute, same host
    [InlineData("/api/data/v9.2/testaccounts?$skiptoken=2")]                             // rooted path including the API path
    [InlineData("testaccounts?$skiptoken=2")]                                            // relative to the base URL
    public async Task NextLink_InAnyForm_ResolvesUnderTheBaseUrlOnce(string nextLink)
    {
        var handler = new TestHttpMessageHandler();
        var client = new DataverseClient(BaseUrl, () => Task.FromResult("t"), new HttpClient(handler));
        handler.AddResponse(_ => true, Page(null, "A"));

        await client.Query<TestAccount>().GetNextPageAsync(nextLink);

        Assert.Equal($"{BaseUrl}/testaccounts?$skiptoken=2", Assert.Single(handler.Requests).Uri.ToString());
    }

    // ────────── A nextLink that was already followed ──────────

    [Fact]
    public async Task QueryStreamAsync_RepeatedNextLink_Throws_InsteadOfLoopingForever()
    {
        const string loop = BaseUrl + "/testaccounts?$skiptoken=same";
        var handler = new TestHttpMessageHandler();
        var client = new DataverseClient(BaseUrl, () => Task.FromResult("t"), new HttpClient(handler));
        handler.AddResponse(_ => true, Page(loop, "A"));
        handler.AddResponse(_ => true, Page(loop, "B"));
        handler.AddResponse(_ => true, Page(loop, "C")); // only reached if the loop is not detected

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.Query<TestAccount>().GetAllAsync(1));

        Assert.Contains("nextLink", ex.Message);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task QueryStreamAsync_NextLinkCycle_Throws()
    {
        const string a = BaseUrl + "/testaccounts?$skiptoken=a";
        const string b = BaseUrl + "/testaccounts?$skiptoken=b";
        var handler = new TestHttpMessageHandler();
        var client = new DataverseClient(BaseUrl, () => Task.FromResult("t"), new HttpClient(handler));
        handler.AddResponse(_ => true, Page(a, "1"));
        handler.AddResponse(_ => true, Page(b, "2"));
        handler.AddResponse(_ => true, Page(a, "3")); // back to a page already followed
        handler.AddResponse(_ => true, Page(null, "4"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Query<TestAccount>().GetAllAsync(1));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task DeltaStreamAsync_RepeatedNextLink_Throws_InsteadOfLoopingForever()
    {
        const string loop = BaseUrl + "/testaccounts?$skiptoken=same";
        var handler = new TestHttpMessageHandler();
        var client = new DataverseClient(BaseUrl, () => Task.FromResult("t"), new HttpClient(handler));
        handler.AddResponse(_ => true, Page(loop, "A"));
        handler.AddResponse(_ => true, Page(loop, "B"));
        handler.AddResponse(_ => true, Page(loop, "C"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in client.Delta<TestAccount>().StreamAsync(1)) { }
        });
        Assert.Equal(2, handler.Requests.Count);
    }
}
