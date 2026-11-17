using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Dataverse.Fluent.Tests;

public class TransportLayerTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";

    // ────────── Fluent layer over any transport ──────────

    [Fact]
    public async Task FluentApi_RunsOverAMinimalTransport()
    {
        // The fake implements the one transport method, SendAsync; everything else is built on it.
        var transport = new FakeTransport
        {
            Respond = _ => DataverseResponse.FromJson(new JsonObject { ["value"] = new JsonArray(new JsonObject { ["name"] = "Contoso" }) })
        };
        var client = new DataverseClient(transport);

        var accounts = await client.Query<TestAccount>().Where(a => a.Name == "Contoso").GetListAsync();

        Assert.Equal("Contoso", Assert.Single(accounts).Name);
        var call = Assert.Single(transport.Requests);
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.StartsWith("testaccounts?", Uri.UnescapeDataString(call.Url));
        Assert.Contains("name eq 'Contoso'", Uri.UnescapeDataString(call.Url));
    }

    [Fact]
    public async Task CountAsync_WorksOverAnyTransport()
    {
        // Previously CountAsync cast the client to DataverseClient and threw InvalidCastException here.
        var transport = new FakeTransport { Respond = _ => new DataverseResponse(PlainText("42")) };
        var client = new DataverseClient(transport);

        var count = await client.Query<TestAccount>().Where(a => a.StateCode == 0).CountAsync();

        Assert.Equal(42, count);
        var request = Assert.Single(transport.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("testaccounts/$count?$filter=statecode eq 0", Uri.UnescapeDataString(request.Url));
        Assert.Equal("text/plain", request.Headers["Accept"]);
    }

    [Fact]
    public async Task CountAsync_OverHttpTransport_ParsesPlainTextWithByteOrderMark()
    {
        var stub = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0xEF, 0xBB, 0xBF, (byte)'1', (byte)'7'])
            {
                Headers = { ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" } }
            }
        });
        using var client = new DataverseClient(BaseUrl, () => Task.FromResult("token"), new HttpClient(stub));

        var count = await client.Query<TestAccount>().CountAsync();

        Assert.Equal(17, count);
        Assert.Equal("text/plain", Assert.Single(stub.Requests.Single().Headers.Accept).MediaType);
    }

    [Fact]
    public async Task Client_DelegatesTransportMembers()
    {
        var transport = new FakeTransport { Respond = _ => DataverseResponse.FromJson(new JsonObject { ["name"] = "Fabrikam" }) };
        IDataverseClient client = new DataverseClient(transport);

        var json = await client.Transport.ExecuteRequestAsync(HttpMethod.Get, "testaccounts(1)");
        var typed = await client.Transport.ExecuteRequestAsync<TestAccount>(HttpMethod.Get, "testaccounts(1)");

        Assert.Equal(BaseUrl, client.Transport.BaseUrl);
        Assert.Equal("Fabrikam", (string?)json["name"]);
        Assert.Equal("Fabrikam", typed!.Name);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public void Client_DoesNotDisposeACallerSuppliedTransport()
    {
        var transport = new FakeTransport();

        new DataverseClient(transport).Dispose();

        Assert.False(transport.Disposed);
    }

    [Fact]
    public async Task Client_DisposesTheTransportItCreated()
    {
        var client = new DataverseClient(
            new DataverseClientOptions { BaseUrl = BaseUrl, TokenProvider = () => Task.FromResult("t") },
            httpClient: null, primaryHandler: new StubHandler((_, _) => StubHandler.Json()), logger: null);

        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.Transport.ExecuteRequestAsync(HttpMethod.Get, "testaccounts"));
    }

    [Fact]
    public async Task HttpTransport_CanBeDecorated()
    {
        var stub = new StubHandler((_, _) => StubHandler.Json());
        using var http = new DataverseHttpTransport(
            new DataverseClientOptions { BaseUrl = BaseUrl, TokenProvider = () => Task.FromResult("token") },
            new HttpClient(stub));
        var recording = new RecordingTransport(http);
        var client = new DataverseClient(recording);

        await client.Query<TestAccount>().GetListAsync();

        Assert.Single(recording.Urls);
        Assert.Equal(["token"], stub.AuthorizationTokens);
    }

    [Fact]
    public void DependencyInjection_ResolvesTransport_ForDefaultAndKeyedClients()
    {
        var services = new ServiceCollection();
        services.AddDataverseClient(BaseUrl, () => Task.FromResult("t"));
        services.AddKeyedDataverseClient("other", o => { o.BaseUrl = "https://other.crm.dynamics.com/api/data/v9.2"; o.TokenProvider = () => Task.FromResult("t"); });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Same(scope.ServiceProvider.GetRequiredService<IDataverseClient>().Transport, scope.ServiceProvider.GetRequiredService<IDataverseTransport>());
        Assert.Same(scope.ServiceProvider.GetRequiredKeyedService<IDataverseClient>("other").Transport, scope.ServiceProvider.GetRequiredKeyedService<IDataverseTransport>("other"));
    }

    // ────────── 401 refresh for raw requests (caller-supplied HttpClient) ──────────

    [Fact]
    public async Task InlineAuth_RawSendRequest_On401_ResendsCopyWithFreshToken()
    {
        var calls = 0;
        var stub = new StubHandler((_, n) => n == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : StubHandler.Json());
        using var client = new DataverseClient(BaseUrl, () => Task.FromResult($"token-{++calls}"), new HttpClient(stub));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/testaccounts")
        {
            Content = new StringContent("{\"name\":\"x\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Prefer", "return=representation");

        using var response = await client.Transport.SendRequestAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["token-1", "token-2"], stub.AuthorizationTokens);
        var resent = stub.Requests[1];
        Assert.Equal(HttpMethod.Post, resent.Method);
        Assert.Equal(request.RequestUri, resent.RequestUri);
        Assert.Equal("return=representation", Assert.Single(resent.Headers.GetValues("Prefer")));
        Assert.Equal("{\"name\":\"x\"}", await resent.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task InlineAuth_CountAsync_On401_RefreshesToken()
    {
        var calls = 0;
        var stub = new StubHandler((_, n) => n == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : PlainText("5"));
        using var client = new DataverseClient(BaseUrl, () => Task.FromResult($"token-{++calls}"), new HttpClient(stub));

        Assert.Equal(5, await client.Query<TestAccount>().CountAsync());
        Assert.Equal(["token-1", "token-2"], stub.AuthorizationTokens);
    }

    [Fact]
    public async Task InlineAuth_CallerSuppliedAuthorization_IsNotRefreshed()
    {
        var stub = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = new DataverseClient(BaseUrl, () => Task.FromResult("provider-token"), new HttpClient(stub));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/testaccounts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");

        await Assert.ThrowsAsync<Dataverse.Fluent.Models.DataverseException>(() => client.Transport.SendRequestAsync(request));

        Assert.Equal(["caller-token"], stub.AuthorizationTokens);
    }

    [Fact]
    public async Task InlineAuth_NonReplayableBody_IsNotResent()
    {
        var calls = 0;
        var stub = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = new DataverseClient(BaseUrl, () => Task.FromResult($"token-{++calls}"), new HttpClient(stub));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/testaccounts")
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("{}")))
        };

        await Assert.ThrowsAsync<Dataverse.Fluent.Models.DataverseException>(() => client.Transport.SendRequestAsync(request));

        Assert.Single(stub.Requests);
    }

    // ────────── Helpers ──────────

    private static HttpResponseMessage PlainText(string text)
        => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };

    private sealed class FakeTransport : IDataverseTransport, IDisposable
    {
        public string BaseUrl => TransportLayerTests.BaseUrl;
        public List<DataverseRequest> Requests { get; } = [];
        public Func<DataverseRequest, DataverseResponse> Respond { get; init; } =
            _ => DataverseResponse.FromJson(new JsonObject { ["value"] = new JsonArray() });
        public bool Disposed { get; private set; }

        public Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(Respond(request));
        }

        public void Dispose() => Disposed = true;
    }

    // A decorator that records request URLs and forwards every request to the inner transport.
    private sealed class RecordingTransport(IDataverseTransport inner) : IDataverseTransport
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
