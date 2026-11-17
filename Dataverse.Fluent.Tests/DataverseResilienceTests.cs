using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dataverse.Fluent.Tests;

public class DataverseResilienceTests
{
    [Fact]
    public void AddDataverseResilienceHandler_WithDefaults_RegistersSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDataverseClient(
                "https://test.crm.dynamics.com/api/data/v9.2",
                () => Task.FromResult("test-token"))
            .AddDataverseResilienceHandler();

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDataverseClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddDataverseResilienceHandler_WithCustomOptions_RegistersSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDataverseClient(
                "https://test.crm.dynamics.com/api/data/v9.2",
                () => Task.FromResult("test-token"))
            .AddDataverseResilienceHandler(options =>
            {
                options.MaxRetryAttempts = 5;
                options.MedianFirstRetryDelay = TimeSpan.FromSeconds(1);
                options.TotalRequestTimeout = TimeSpan.FromMinutes(3);
                options.AttemptTimeout = TimeSpan.FromSeconds(45);
            });

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDataverseClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddDataverseResilienceHandler_WithTokenProviderFactory_RegistersSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDataverseClient(
                "https://test.crm.dynamics.com/api/data/v9.2",
                sp => () => Task.FromResult("test-token"))
            .AddDataverseResilienceHandler();

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDataverseClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void DataverseResilienceOptions_HasCorrectDefaults()
    {
        var options = new DataverseResilienceOptions();

        Assert.True(options.Enabled);
        Assert.Equal(3, options.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(2), options.MedianFirstRetryDelay);
        // Room for service-protection Retry-After waits.
        Assert.Equal(TimeSpan.FromMinutes(10), options.TotalRequestTimeout);
        // Above Dataverse's 2-minute execution limit, so slow-but-successful requests are not cancelled.
        Assert.Equal(TimeSpan.FromSeconds(150), options.AttemptTimeout);
    }

    [Fact]
    public void AddDataverseResilienceHandler_ReturnsIHttpClientBuilder_ForChaining()
    {
        var services = new ServiceCollection();

        var builder = services.AddDataverseClient(
            "https://test.crm.dynamics.com/api/data/v9.2",
            () => Task.FromResult("test-token"));

        var result = builder.AddDataverseResilienceHandler();

        Assert.Same(builder, result);
    }

    [Fact]
    public async Task ResilienceHandler_RetriesOn429_EventuallySucceeds()
    {
        var attempt = 0;
        var services = new ServiceCollection();

        services.AddDataverseClient(
                "https://test.crm.dynamics.com/api/data/v9.2",
                () => Task.FromResult("test-token"),
                configureResilience: options =>
                {
                    options.MaxRetryAttempts = 3;
                    options.MedianFirstRetryDelay = TimeSpan.FromMilliseconds(50);
                    options.AttemptTimeout = TimeSpan.FromSeconds(10);
                    options.TotalRequestTimeout = TimeSpan.FromSeconds(30);
                })
            .ConfigurePrimaryHttpMessageHandler(() => new DelegatingHandlerStub(request =>
            {
                attempt++;
                if (attempt < 3)
                {
                    var response = new HttpResponseMessage((HttpStatusCode)429);
                    response.Headers.Add("Retry-After", "0");
                    return response;
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"value\":[]}", System.Text.Encoding.UTF8, "application/json")
                };
            }));

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<DataverseClient>();

        var result = await client.Query<TestAccount>().GetListAsync();

        Assert.NotNull(result);
        Assert.True(attempt >= 3, $"Expected at least 3 attempts but got {attempt}");
    }

    [Fact]
    public async Task ResilienceHandler_RetriesOn503_EventuallySucceeds()
    {
        var attempt = 0;
        var services = new ServiceCollection();

        services.AddDataverseClient(
                "https://test.crm.dynamics.com/api/data/v9.2",
                () => Task.FromResult("test-token"),
                configureResilience: options =>
                {
                    options.MaxRetryAttempts = 3;
                    options.MedianFirstRetryDelay = TimeSpan.FromMilliseconds(50);
                    options.AttemptTimeout = TimeSpan.FromSeconds(10);
                    options.TotalRequestTimeout = TimeSpan.FromSeconds(30);
                })
            .ConfigurePrimaryHttpMessageHandler(() => new DelegatingHandlerStub(request =>
            {
                attempt++;
                if (attempt < 2)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"value\":[]}", System.Text.Encoding.UTF8, "application/json")
                };
            }));

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<DataverseClient>();

        var result = await client.Query<TestAccount>().GetListAsync();

        Assert.NotNull(result);
        Assert.True(attempt >= 2, $"Expected at least 2 attempts but got {attempt}");
    }

    /// <summary>
    /// Simple delegating handler that returns responses from a factory function.
    /// Used to simulate transient errors in resilience tests.
    /// </summary>
    private sealed class DelegatingHandlerStub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }

    [Fact]
    public async Task AuthenticationHandler_SetsBearerToken_OnEveryRequest()
    {
        AuthenticationHeaderValue? capturedAuth = null;
        var services = new ServiceCollection();

        services.AddDataverseClient(
                "https://test.crm.dynamics.com/api/data/v9.2",
                () => Task.FromResult("expected-token"))
            .ConfigurePrimaryHttpMessageHandler(() => new DelegatingHandlerStub(request =>
            {
                capturedAuth = request.Headers.Authorization;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"value\":[]}", System.Text.Encoding.UTF8, "application/json")
                };
            }));

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<DataverseClient>();

        await client.Query<TestAccount>().GetListAsync();

        Assert.NotNull(capturedAuth);
        Assert.Equal("Bearer", capturedAuth!.Scheme);
        Assert.Equal("expected-token", capturedAuth.Parameter);
    }

    [Fact]
    public async Task AuthenticationHandler_CachesToken_WithinCacheLifetime()
    {
        var tokenCallCount = 0;
        var services = new ServiceCollection();

        services.AddDataverseClient(
                "https://test.crm.dynamics.com/api/data/v9.2",
                () =>
                {
                    tokenCallCount++;
                    return Task.FromResult($"token-{tokenCallCount}");
                })
            .ConfigurePrimaryHttpMessageHandler(() => new DelegatingHandlerStub(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"value\":[]}", System.Text.Encoding.UTF8, "application/json")
                }));

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<DataverseClient>();

        await client.Query<TestAccount>().GetListAsync();
        await client.Query<TestAccount>().GetListAsync();

        Assert.Equal(1, tokenCallCount);
    }

    [Fact]
    public async Task AuthenticationHandler_ReacquiresToken_AfterCacheExpires()
    {
        var tokenCallCount = 0;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tokens = DataverseTokenProvider.FromDelegate(
            () =>
            {
                tokenCallCount++;
                return Task.FromResult($"token-{tokenCallCount}");
            },
            time);
        var handler = new DataverseAuthenticationHandler(tokens);

        AuthenticationHeaderValue? lastAuth = null;
        var inner = new DelegatingHandlerStub(request =>
        {
            lastAuth = request.Headers.Authorization;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}", System.Text.Encoding.UTF8, "application/json")
            };
        });
        handler.InnerHandler = inner;

        using var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.com"), default);
        Assert.Equal("token-1", lastAuth?.Parameter);

        // Opaque (non-JWT) tokens are cached for the fallback lifetime; move past it.
        time.Advance(DataverseTokenProvider.FallbackLifetime);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.com"), default);
        Assert.Equal("token-2", lastAuth?.Parameter);

        Assert.Equal(2, tokenCallCount);
    }

    [Fact]
    public async Task AddDataverseClient_EnablesResilienceByDefault_RetriesTransientErrors()
    {
        var attempt = 0;
        var services = new ServiceCollection();

        // No explicit AddDataverseResilienceHandler call — resilience must be on by default.
        services.AddDataverseClient(
                "https://test.crm.dynamics.com/api/data/v9.2",
                () => Task.FromResult("test-token"),
                configureResilience: options =>
                {
                    options.MedianFirstRetryDelay = TimeSpan.FromMilliseconds(50);
                    options.AttemptTimeout = TimeSpan.FromSeconds(10);
                    options.TotalRequestTimeout = TimeSpan.FromSeconds(30);
                })
            .ConfigurePrimaryHttpMessageHandler(() => new DelegatingHandlerStub(_ =>
            {
                attempt++;
                if (attempt < 2)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"value\":[]}", System.Text.Encoding.UTF8, "application/json")
                };
            }));

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<DataverseClient>();

        var result = await client.Query<TestAccount>().GetListAsync();

        Assert.NotNull(result);
        Assert.True(attempt >= 2, $"Expected retry on by default but only saw {attempt} attempt(s)");
    }

    [Fact]
    public async Task AddDataverseClient_WhenResilienceDisabled_DoesNotRetry()
    {
        var attempt = 0;
        var services = new ServiceCollection();

        services.AddDataverseClient(
                "https://test.crm.dynamics.com/api/data/v9.2",
                () => Task.FromResult("test-token"),
                configureResilience: options => options.Enabled = false)
            .ConfigurePrimaryHttpMessageHandler(() => new DelegatingHandlerStub(_ =>
            {
                attempt++;
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }));

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<DataverseClient>();

        await Assert.ThrowsAnyAsync<Exception>(() => client.Query<TestAccount>().GetListAsync());

        Assert.Equal(1, attempt);
    }

    [Fact]
    public async Task AddDataverseClient_WithChainedResilienceHandler_DoesNotDoubleRegister()
    {
        var attempt = 0;
        var services = new ServiceCollection();

        // Default-on resilience plus an explicit chained call must not register the handler twice.
        services.AddDataverseClient(
                "https://test.crm.dynamics.com/api/data/v9.2",
                () => Task.FromResult("test-token"),
                configureResilience: options =>
                {
                    options.MaxRetryAttempts = 2;
                    options.MedianFirstRetryDelay = TimeSpan.FromMilliseconds(50);
                    options.AttemptTimeout = TimeSpan.FromSeconds(10);
                    options.TotalRequestTimeout = TimeSpan.FromSeconds(30);
                })
            .AddDataverseResilienceHandler()
            .ConfigurePrimaryHttpMessageHandler(() => new DelegatingHandlerStub(_ =>
            {
                attempt++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"value\":[]}", System.Text.Encoding.UTF8, "application/json")
                };
            }));

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<DataverseClient>();

        var result = await client.Query<TestAccount>().GetListAsync();

        Assert.NotNull(result);
        // A single successful attempt confirms the pipeline isn't stacked/misconfigured.
        Assert.Equal(1, attempt);
    }

    [Fact]
    public async Task ResilienceHandler_HonorsRetryAfterHeader_OnServiceProtectionLimit()
    {
        var attempt = 0;
        var delays = new List<TimeSpan>();
        var lastRequestTime = DateTimeOffset.UtcNow;
        var services = new ServiceCollection();

        services.AddDataverseClient(
                "https://test.crm.dynamics.com/api/data/v9.2",
                () => Task.FromResult("test-token"),
                configureResilience: options =>
                {
                    options.MaxRetryAttempts = 3;
                    // Set the exponential backoff very low so that, if Retry-After were NOT honored,
                    // the observed delay would be far below the server-specified 1 second.
                    options.MedianFirstRetryDelay = TimeSpan.FromMilliseconds(10);
                    options.AttemptTimeout = TimeSpan.FromSeconds(10);
                    options.TotalRequestTimeout = TimeSpan.FromSeconds(30);
                })
            .ConfigurePrimaryHttpMessageHandler(() => new DelegatingHandlerStub(_ =>
            {
                var now = DateTimeOffset.UtcNow;
                if (attempt > 0)
                {
                    delays.Add(now - lastRequestTime);
                }

                lastRequestTime = now;
                attempt++;

                if (attempt < 2)
                {
                    var response = new HttpResponseMessage((HttpStatusCode)429);
                    response.Headers.Add("Retry-After", "1");
                    return response;
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"value\":[]}", System.Text.Encoding.UTF8, "application/json")
                };
            }));

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<DataverseClient>();

        var result = await client.Query<TestAccount>().GetListAsync();

        Assert.NotNull(result);
        Assert.NotEmpty(delays);
        // The retry must wait at least the server-specified Retry-After (1s), not the 10ms backoff.
        Assert.True(delays[0] >= TimeSpan.FromMilliseconds(900),
            $"Expected Retry-After (~1s) to be honored but the retry delay was {delays[0]}");
    }
}
