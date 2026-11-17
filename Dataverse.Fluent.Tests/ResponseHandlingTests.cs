using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Dataverse.Fluent.Models;
using DvBuilders = Dataverse.Fluent.Builders;

namespace Dataverse.Fluent.Tests;

/// <summary>
/// Responses are deserialized from the content stream and always disposed, on success and on failure.
/// </summary>
public class ResponseHandlingTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";

    private sealed class TrackingContent(string json) : StringContent(json, Encoding.UTF8, "application/json")
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private static (DataverseClient Client, List<TrackingContent> Contents) Create(params (HttpStatusCode Status, string Json)[] responses)
    {
        var contents = new List<TrackingContent>();
        var stub = new StubHandler((_, n) =>
        {
            var (status, json) = responses[n - 1];
            var content = new TrackingContent(json);
            contents.Add(content);
            return new HttpResponseMessage(status) { Content = content };
        });
        return (new DataverseClient(BaseUrl, () => Task.FromResult("t"), new HttpClient(stub)), contents);
    }

    [Fact]
    public async Task Query_DisposesEachPageResponse()
    {
        var (client, contents) = Create(
            (HttpStatusCode.OK, $"{{\"value\":[{{\"name\":\"a\"}}],\"@odata.nextLink\":\"{BaseUrl}/testaccounts?$skiptoken=2\"}}"),
            (HttpStatusCode.OK, "{\"value\":[{\"name\":\"b\"}]}"));

        var results = await client.Query<TestAccount>().GetAllAsync(pageSize: 1);

        Assert.Equal(["a", "b"], results.Select(r => r.Name));
        Assert.Equal(2, contents.Count);
        Assert.All(contents, c => Assert.True(c.Disposed));
    }

    [Fact]
    public async Task ExecuteRequestAsync_DisposesResponse()
    {
        var (client, contents) = Create((HttpStatusCode.OK, "{\"a\":1}"));

        var json = await client.Transport.ExecuteRequestAsync(HttpMethod.Get, "testaccounts");

        Assert.Equal(1, json["a"]!.GetValue<int>());
        Assert.True(contents.Single().Disposed);
    }

    [Fact]
    public async Task FailedRequest_DisposesResponse_AfterReadingTheError()
    {
        var (client, contents) = Create((HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"0x1\",\"message\":\"bad\"}}"));

        var ex = await Assert.ThrowsAsync<DataverseException>(() => client.Query<TestAccount>().GetListAsync());

        Assert.Equal("0x1", ex.ErrorCode);
        Assert.True(contents.Single().Disposed);
    }

    [Fact]
    public async Task GenericExecuteRequestAsync_DeserializesTypedResult_AndReturnsDefaultForNoContent()
    {
        var (client, _) = Create((HttpStatusCode.OK, "{\"name\":\"Contoso\"}"), (HttpStatusCode.NoContent, ""));

        var account = await client.Transport.ExecuteRequestAsync<TestAccount>(HttpMethod.Get, "testaccounts(1)");
        var none = await client.Transport.ExecuteRequestAsync<TestAccount>(HttpMethod.Patch, "testaccounts(1)", new JsonObject());

        Assert.Equal("Contoso", account!.Name);
        Assert.Null(none);
    }

    [Fact]
    public async Task ExtensionMethods_DeserializeOverAnyTransport()
    {
        // A transport implements only SendAsync; the typed ExecuteRequestAsync is an extension over it.
        IDataverseTransport fake = new JsonOnlyTransport(new JsonObject { ["name"] = "Fabrikam" });

        var account = await fake.ExecuteRequestAsync<TestAccount>(HttpMethod.Get, "testaccounts(1)");

        Assert.Equal("Fabrikam", account!.Name);
    }

    private sealed class JsonOnlyTransport(JsonObject response) : IDataverseTransport
    {
        public string BaseUrl => ResponseHandlingTests.BaseUrl;

        public Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
            => Task.FromResult(DataverseResponse.FromJson(response));
    }
}
