using System.Net;
using Dataverse.Fluent.Models;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Timeout;

namespace Dataverse.Fluent.Tests;

/// <summary>
/// The resilience pipeline must never re-send a write that Dataverse may already have applied.
/// </summary>
public class WriteSafeResilienceTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";

    private static DataverseResilienceOptions FastOptions() => new()
    {
        MaxRetryAttempts = 3,
        MedianFirstRetryDelay = TimeSpan.Zero,
        AttemptTimeout = TimeSpan.FromSeconds(10),
        TotalRequestTimeout = TimeSpan.FromSeconds(30)
    };

    private static Outcome<HttpResponseMessage> Status(HttpStatusCode code) => Outcome.FromResult(new HttpResponseMessage(code));

    private static Outcome<HttpResponseMessage> Error(Exception ex) => Outcome.FromException<HttpResponseMessage>(ex);

    private static HttpRequestMessage Request(string method) => new(new HttpMethod(method), BaseUrl + "/accounts");

    // ────────── Retry decision table ──────────

    [Theory]
    [InlineData("GET", HttpStatusCode.TooManyRequests, true)]
    [InlineData("GET", HttpStatusCode.ServiceUnavailable, true)]
    [InlineData("GET", HttpStatusCode.BadGateway, true)]
    [InlineData("GET", HttpStatusCode.RequestTimeout, true)]
    [InlineData("GET", HttpStatusCode.BadRequest, false)]
    [InlineData("POST", HttpStatusCode.TooManyRequests, true)]
    [InlineData("POST", HttpStatusCode.ServiceUnavailable, false)]
    [InlineData("POST", HttpStatusCode.InternalServerError, false)]
    [InlineData("POST", HttpStatusCode.GatewayTimeout, false)]
    [InlineData("PATCH", HttpStatusCode.TooManyRequests, true)]
    [InlineData("PATCH", HttpStatusCode.BadGateway, false)]
    [InlineData("DELETE", HttpStatusCode.ServiceUnavailable, false)]
    public void ShouldRetry_ByMethodAndStatus(string method, HttpStatusCode status, bool expected)
    {
        Assert.Equal(expected, DataverseResiliencePipeline.ShouldRetry(Status(status), Request(method)));
    }

    [Fact]
    public void ShouldRetry_Timeout_OnlyForSafeMethods()
    {
        var timeout = Error(new TimeoutRejectedException());

        Assert.True(DataverseResiliencePipeline.ShouldRetry(timeout, Request("GET")));
        Assert.False(DataverseResiliencePipeline.ShouldRetry(timeout, Request("POST")));
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError, true)]
    [InlineData(HttpRequestError.NameResolutionError, true)]
    [InlineData(HttpRequestError.SecureConnectionError, true)]
    [InlineData(HttpRequestError.ResponseEnded, false)]
    [InlineData(HttpRequestError.Unknown, false)]
    public void ShouldRetry_WriteNetworkError_OnlyWhenTheConnectionWasNeverEstablished(HttpRequestError error, bool expected)
    {
        var outcome = Error(new HttpRequestException(error, "network"));

        Assert.Equal(expected, DataverseResiliencePipeline.ShouldRetry(outcome, Request("POST")));
    }

    [Fact]
    public void CircuitBreaker_DoesNotCountThrottling()
    {
        Assert.False(DataverseResiliencePipeline.IsFault(Status(HttpStatusCode.TooManyRequests)));
        Assert.True(DataverseResiliencePipeline.IsFault(Status(HttpStatusCode.ServiceUnavailable)));
    }

    // ────────── End to end (DI) ──────────

    private static DataverseClient CreateDiClient(StubHandler primary, Action<DataverseResilienceOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddDataverseClient(BaseUrl, () => Task.FromResult("t"), configureResilience: o =>
            {
                var fast = FastOptions();
                o.MaxRetryAttempts = fast.MaxRetryAttempts;
                o.MedianFirstRetryDelay = fast.MedianFirstRetryDelay;
                o.AttemptTimeout = fast.AttemptTimeout;
                o.TotalRequestTimeout = fast.TotalRequestTimeout;
                configure?.Invoke(o);
            })
            .ConfigurePrimaryHttpMessageHandler(() => primary);
        return services.BuildServiceProvider().GetRequiredService<DataverseClient>();
    }

    [Fact]
    public async Task Create_On503_IsNotResent()
    {
        var primary = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = CreateDiClient(primary);

        await Assert.ThrowsAsync<DataverseException>(() => client.Entity<TestAccount>().Set("name", "x").CreateAsync());

        Assert.Single(primary.Requests);
    }

    [Fact]
    public async Task Create_On429_IsRetried()
    {
        var newId = Guid.NewGuid();
        var primary = new StubHandler((_, n) =>
        {
            if (n == 1)
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);

            var created = new HttpResponseMessage(HttpStatusCode.NoContent);
            created.Headers.Add("OData-EntityId", $"{BaseUrl}/testaccounts({newId})");
            return created;
        });
        var client = CreateDiClient(primary);

        var result = await client.Entity<TestAccount>().Set("name", "x").CreateAsync();

        Assert.Equal(newId, result.Id);
        Assert.Equal(2, primary.Requests.Count);
    }

    [Fact]
    public async Task Update_ThatTimesOut_IsNotResent()
    {
        var attempts = 0;
        var services = new ServiceCollection();
        services.AddDataverseClient(BaseUrl, () => Task.FromResult("t"), configureResilience: o =>
            {
                o.MedianFirstRetryDelay = TimeSpan.Zero;
                o.AttemptTimeout = TimeSpan.FromMilliseconds(100);
                o.TotalRequestTimeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SlowHandler(() => attempts++));
        var client = services.BuildServiceProvider().GetRequiredService<DataverseClient>();

        await Assert.ThrowsAsync<TimeoutRejectedException>(() =>
            client.Entity<TestAccount>(Guid.NewGuid()).Set("name", "x").UpdateAsync());

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Query_ThatTimesOut_IsRetried()
    {
        var attempts = 0;
        var services = new ServiceCollection();
        services.AddDataverseClient(BaseUrl, () => Task.FromResult("t"), configureResilience: o =>
            {
                o.MaxRetryAttempts = 2;
                o.MedianFirstRetryDelay = TimeSpan.Zero;
                o.AttemptTimeout = TimeSpan.FromMilliseconds(100);
                o.TotalRequestTimeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SlowHandler(() => attempts++));
        var client = services.BuildServiceProvider().GetRequiredService<DataverseClient>();

        await Assert.ThrowsAsync<TimeoutRejectedException>(() => client.Query<TestAccount>().GetListAsync());

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task BatchPost_On503_IsNotResent()
    {
        var primary = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = CreateDiClient(primary);

        await Assert.ThrowsAsync<DataverseException>(() =>
            client.Batch().AddCreate(new TestAccount { Name = "x" }).ExecuteAsync());

        Assert.Single(primary.Requests);
    }

    [Fact]
    public async Task EveryRetryAttempt_GetsAnAuthorizationHeader()
    {
        // Authentication runs inside the resilience pipeline, so retries are re-authenticated.
        var primary = new StubHandler((_, n) => n < 3 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : StubHandler.Json());
        var client = CreateDiClient(primary);

        await client.Query<TestAccount>().GetListAsync();

        Assert.Equal(3, primary.AuthorizationTokens.Count);
        Assert.All(primary.AuthorizationTokens, t => Assert.Equal("t", t));
    }

    // ────────── Owned pipeline (non-DI constructor) ──────────

    [Fact]
    public async Task NonDiClient_WithoutHttpClient_GetsResilienceAndAuthentication()
    {
        var primary = new StubHandler((_, n) => n == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : StubHandler.Json());
        var options = new DataverseClientOptions { BaseUrl = BaseUrl, TokenProvider = () => Task.FromResult("owned"), Resilience = FastOptions() };
        using var client = new DataverseClient(options, httpClient: null, primaryHandler: primary, logger: null);

        var result = await client.Query<TestAccount>().GetListAsync();

        Assert.Empty(result);
        Assert.Equal(2, primary.Requests.Count);
        Assert.Equal(["owned", "owned"], primary.AuthorizationTokens);
    }

    [Fact]
    public async Task NonDiClient_WithoutHttpClient_DoesNotResendFailedWrites()
    {
        var primary = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var options = new DataverseClientOptions { BaseUrl = BaseUrl, TokenProvider = () => Task.FromResult("owned"), Resilience = FastOptions() };
        using var client = new DataverseClient(options, httpClient: null, primaryHandler: primary, logger: null);

        await Assert.ThrowsAsync<DataverseException>(() => client.Entity<TestAccount>().Set("name", "x").CreateAsync());

        Assert.Single(primary.Requests);
    }

    [Fact]
    public async Task NonDiClient_WithoutHttpClient_RefreshesTokenOn401()
    {
        var calls = 0;
        var primary = new StubHandler((_, n) => n == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : StubHandler.Json());
        var options = new DataverseClientOptions { BaseUrl = BaseUrl, TokenProvider = () => Task.FromResult($"token-{++calls}"), Resilience = FastOptions() };
        using var client = new DataverseClient(options, httpClient: null, primaryHandler: primary, logger: null);

        await client.Query<TestAccount>().GetListAsync();

        Assert.Equal(["token-1", "token-2"], primary.AuthorizationTokens);
    }

    [Fact]
    public void NonDiClient_OwnedHttpClient_HasNoHttpClientTimeout()
    {
        var tokens = DataverseTokenProvider.FromDelegate(() => Task.FromResult("t"));

        using var withResilience = DataverseHttpTransport.CreateHttpClient(new DataverseResilienceOptions(), tokens, new StubHandler((_, _) => StubHandler.Json()));
        using var withoutResilience = DataverseHttpTransport.CreateHttpClient(new DataverseResilienceOptions { Enabled = false }, tokens, new StubHandler((_, _) => StubHandler.Json()));

        Assert.Equal(Timeout.InfiniteTimeSpan, withResilience.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(100), withoutResilience.Timeout);
    }

    [Fact]
    public void Options_InvalidTimeouts_AreRejected()
    {
        var options = new DataverseClientOptions
        {
            BaseUrl = BaseUrl,
            TokenProvider = () => Task.FromResult("t"),
            Resilience = new DataverseResilienceOptions { AttemptTimeout = TimeSpan.FromMinutes(5), TotalRequestTimeout = TimeSpan.FromMinutes(1) }
        };

        var ex = Assert.Throws<ArgumentException>(() => new DataverseClient(options));
        Assert.Contains("TotalRequestTimeout", ex.Message);
    }

    private sealed class SlowHandler(Action onAttempt) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onAttempt();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
