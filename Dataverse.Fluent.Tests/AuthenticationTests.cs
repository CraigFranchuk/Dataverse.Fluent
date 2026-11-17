using System.Net;
using System.Text;

namespace Dataverse.Fluent.Tests;

public class AuthenticationTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";

    // ────────── DataverseTokenProvider ──────────

    [Fact]
    public async Task Delegate_JwtToken_IsCachedUntilFiveMinutesBeforeExp()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var calls = 0;
        var tokens = DataverseTokenProvider.FromDelegate(() =>
        {
            calls++;
            return Task.FromResult(Jwt.WithExpiry(time.GetUtcNow() + TimeSpan.FromHours(1), $"call-{calls}"));
        }, time);

        var first = await tokens.GetTokenAsync();
        time.Advance(TimeSpan.FromMinutes(54));
        Assert.Same(first, await tokens.GetTokenAsync());
        Assert.Equal(1, calls);

        time.Advance(TimeSpan.FromMinutes(1)); // 55 minutes: inside the 5-minute refresh window
        Assert.NotEqual(first, await tokens.GetTokenAsync());
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Delegate_JwtExpiringSoonerThanFallback_IsRefreshedBeforeItExpires()
    {
        // A token near the end of its life (e.g. an MSAL cache hit) must not be cached for 5 minutes.
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var calls = 0;
        var tokens = DataverseTokenProvider.FromDelegate(() =>
        {
            calls++;
            return Task.FromResult(Jwt.WithExpiry(time.GetUtcNow() + TimeSpan.FromMinutes(2), $"call-{calls}"));
        }, time);

        await tokens.GetTokenAsync();
        time.Advance(TimeSpan.FromSeconds(61)); // past half its 2-minute lifetime
        await tokens.GetTokenAsync();

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Delegate_OpaqueToken_UsesFallbackLifetime()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var calls = 0;
        var tokens = DataverseTokenProvider.FromDelegate(() => Task.FromResult($"opaque-{++calls}"), time);

        await tokens.GetTokenAsync();
        time.Advance(DataverseTokenProvider.FallbackLifetime - TimeSpan.FromSeconds(1));
        await tokens.GetTokenAsync();
        Assert.Equal(1, calls);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("opaque-2", await tokens.GetTokenAsync());
    }

    [Fact]
    public async Task Credential_UsesRealExpiry_AndScopeDerivedFromBaseUrl()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var credential = new FakeTokenCredential(time, TimeSpan.FromHours(1));
        var tokens = DataverseTokenProvider.FromOptions(new DataverseClientOptions { BaseUrl = BaseUrl, Credential = credential }, time);

        Assert.Equal("credential-token-1", await tokens.GetTokenAsync());
        time.Advance(TimeSpan.FromMinutes(50));
        Assert.Equal("credential-token-1", await tokens.GetTokenAsync());
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal("credential-token-2", await tokens.GetTokenAsync());

        Assert.All(credential.RequestedScopes, s => Assert.Equal("https://test.crm.dynamics.com/.default", s));
    }

    [Fact]
    public async Task Credential_HonorsExplicitScope()
    {
        var credential = new FakeTokenCredential(TimeProvider.System, TimeSpan.FromHours(1));
        var tokens = DataverseTokenProvider.FromOptions(new DataverseClientOptions
        {
            BaseUrl = BaseUrl,
            Credential = credential,
            Scope = "https://custom.example/.default"
        });

        await tokens.GetTokenAsync();

        Assert.Equal(["https://custom.example/.default"], credential.RequestedScopes);
    }

    [Fact]
    public async Task Invalidate_DropsOnlyTheRejectedToken()
    {
        var calls = 0;
        var tokens = DataverseTokenProvider.FromDelegate(() => Task.FromResult($"t{++calls}"));

        var first = await tokens.GetTokenAsync();
        tokens.Invalidate("some-other-token");
        Assert.Equal(first, await tokens.GetTokenAsync());

        tokens.Invalidate(first);
        Assert.Equal("t2", await tokens.GetTokenAsync());
    }

    [Fact]
    public async Task ConcurrentCallers_AcquireTheTokenOnce()
    {
        var calls = 0;
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = DataverseTokenProvider.FromDelegate(() => { Interlocked.Increment(ref calls); return gate.Task; });

        var pending = Enumerable.Range(0, 10).Select(_ => tokens.GetTokenAsync().AsTask()).ToArray();
        gate.SetResult("shared");
        var results = await Task.WhenAll(pending);

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Equal("shared", r));
    }

    [Fact]
    public void TryReadJwtExpiry_ReturnsNullForNonJwt()
    {
        Assert.Null(DataverseTokenProvider.TryReadJwtExpiry("not-a-jwt"));
        Assert.Null(DataverseTokenProvider.TryReadJwtExpiry("a.b!!.c"));
    }

    // ────────── DataverseAuthenticationHandler ──────────

    private static (HttpMessageInvoker Invoker, StubHandler Inner) CreateHandler(DataverseTokenProvider tokens, Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var inner = new StubHandler(respond);
        var handler = new DataverseAuthenticationHandler(tokens) { InnerHandler = inner };
        return (new HttpMessageInvoker(handler), inner);
    }

    [Fact]
    public async Task Handler_On401_RefreshesTokenAndRetriesOnce()
    {
        var calls = 0;
        var tokens = DataverseTokenProvider.FromDelegate(() => Task.FromResult($"token-{++calls}"));
        var (invoker, inner) = CreateHandler(tokens, (_, n) => n == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : StubHandler.Json());

        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/accounts")
        {
            Content = new StringContent("{\"name\":\"x\"}", Encoding.UTF8, "application/json")
        };
        var response = await invoker.SendAsync(request, default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["token-1", "token-2"], inner.AuthorizationTokens);
    }

    [Fact]
    public async Task Handler_SecondConsecutive401_IsReturnedToCaller()
    {
        var calls = 0;
        var tokens = DataverseTokenProvider.FromDelegate(() => Task.FromResult($"token-{++calls}"));
        var (invoker, inner) = CreateHandler(tokens, (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/accounts"), default);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task Handler_On401_WithNonReplayableBody_DoesNotRetry()
    {
        var calls = 0;
        var tokens = DataverseTokenProvider.FromDelegate(() => Task.FromResult($"token-{++calls}"));
        var (invoker, inner) = CreateHandler(tokens, (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/accounts")
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("{}")))
        };
        var response = await invoker.SendAsync(request, default);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task Handler_On401_WhenProviderReturnsSameToken_DoesNotRetry()
    {
        var tokens = DataverseTokenProvider.FromDelegate(() => Task.FromResult("same-token"));
        var (invoker, inner) = CreateHandler(tokens, (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/accounts"), default);

        Assert.Single(inner.Requests);
    }

    [Fact]
    public void IsReplayable_RecognizesBufferedContent()
    {
        Assert.True(DataverseAuthenticationHandler.IsReplayable(null));
        Assert.True(DataverseAuthenticationHandler.IsReplayable(new StringContent("x")));
        Assert.True(DataverseAuthenticationHandler.IsReplayable(new MultipartContent { new StringContent("x") }));
        Assert.False(DataverseAuthenticationHandler.IsReplayable(new StreamContent(Stream.Null)));
        Assert.False(DataverseAuthenticationHandler.IsReplayable(new MultipartContent { new StreamContent(Stream.Null) }));
    }

    // ────────── Inline authentication (caller-supplied HttpClient) ──────────

    [Fact]
    public async Task InlineAuth_On401_RebuildsRequestWithFreshToken()
    {
        var calls = 0;
        var stub = new StubHandler((_, n) => n == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : StubHandler.Json());
        using var client = new DataverseClient(BaseUrl, () => Task.FromResult($"token-{++calls}"), new HttpClient(stub));

        var result = await client.Query<TestAccount>().GetListAsync();

        Assert.Empty(result);
        Assert.Equal(["token-1", "token-2"], stub.AuthorizationTokens);
    }

    [Fact]
    public async Task InlineAuth_Persistent401_ThrowsDataverseException()
    {
        var calls = 0;
        var stub = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = new DataverseClient(BaseUrl, () => Task.FromResult($"token-{++calls}"), new HttpClient(stub));

        var ex = await Assert.ThrowsAsync<Dataverse.Fluent.Models.DataverseException>(() => client.Query<TestAccount>().GetListAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task TokenCredentialConstructor_AuthenticatesRequests()
    {
        var credential = new FakeTokenCredential(TimeProvider.System, TimeSpan.FromHours(1));
        var stub = new StubHandler((_, _) => StubHandler.Json());
        using var client = new DataverseClient(BaseUrl, credential, new HttpClient(stub));

        await client.Query<TestAccount>().GetListAsync();

        Assert.Equal(["credential-token-1"], stub.AuthorizationTokens);
    }

    // ────────── Expired, empty and failing tokens ──────────

    [Fact]
    public async Task Delegate_AlreadyExpiredJwt_IsNotServedFromTheCache()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var calls = 0;
        var tokens = DataverseTokenProvider.FromDelegate(() =>
        {
            calls++;
            return Task.FromResult(Jwt.WithExpiry(time.GetUtcNow() - TimeSpan.FromMinutes(1), $"call-{calls}"));
        }, time);

        await tokens.GetTokenAsync();
        await tokens.GetTokenAsync(); // same instant: a cached token would be returned without a call

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Credential_AlreadyExpiredToken_IsNotServedFromTheCache()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var credential = new FakeTokenCredential(time, TimeSpan.FromSeconds(-1));
        var tokens = DataverseTokenProvider.FromCredential(credential, "https://test.crm.dynamics.com/.default", time);

        Assert.Equal("credential-token-1", await tokens.GetTokenAsync());
        Assert.Equal("credential-token-2", await tokens.GetTokenAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Delegate_EmptyToken_Throws_AndIsNotCached(string? empty)
    {
        var calls = 0;
        var tokens = DataverseTokenProvider.FromDelegate(() => Task.FromResult(++calls == 1 ? empty! : "valid-token"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tokens.GetTokenAsync().AsTask());
        Assert.Contains("empty token", ex.Message);

        Assert.Equal("valid-token", await tokens.GetTokenAsync());
    }

    [Fact]
    public async Task Delegate_FailingAcquisition_DoesNotBlockLaterCalls()
    {
        // The acquisition lock must be released when the provider throws.
        var calls = 0;
        var tokens = DataverseTokenProvider.FromDelegate(() =>
            ++calls == 1 ? throw new HttpRequestException("identity endpoint down") : Task.FromResult("token"));

        await Assert.ThrowsAsync<HttpRequestException>(() => tokens.GetTokenAsync().AsTask());

        var second = tokens.GetTokenAsync().AsTask();
        Assert.Same(second, await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.Equal("token", await second);
    }

    [Fact]
    public async Task EmptyToken_FailsTheRequest_WithoutSendingIt()
    {
        var stub = new StubHandler((_, _) => StubHandler.Json());
        using var client = new DataverseClient(BaseUrl, () => Task.FromResult(""), new HttpClient(stub));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Query<TestAccount>().GetListAsync());

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public void FromOptions_WithoutCredentialOrTokenProvider_Throws()
    {
        var options = new DataverseClientOptions { BaseUrl = BaseUrl };

        Assert.Throws<InvalidOperationException>(() => DataverseTokenProvider.FromOptions(options));
    }
}
