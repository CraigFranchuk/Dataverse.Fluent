using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Dataverse.Fluent.Builders;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Tests;

/// <summary>
/// The request model: builders produce <see cref="DataverseRequest"/>s that a batch accepts, and write
/// terminals report what Dataverse returned (IDs, created-or-updated).
/// </summary>
public class RequestModelTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FakeTransport : IDataverseTransport
    {
        public string BaseUrl => RequestModelTests.BaseUrl;
        public List<DataverseRequest> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];
        public Func<DataverseRequest, DataverseResponse> Respond { get; init; } = _ => DataverseResponse.Empty();

        public async Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? request.Payload?.ToJsonString() : await request.Content.ReadAsStringAsync(ct));
            return Respond(request);
        }
    }

    private class Req
    {
        public int Status { get; set; }
    }

    private static DataverseResponse Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    // ────────── Builders produce requests ──────────

    [Fact]
    public void ToUpsertRequest_ByAlternateKey_CarriesKeyHeadersAndPayload()
    {
        var client = new DataverseClient(new FakeTransport());

        var request = client.Entity<TestAccount>()
            .UsingAlternateKey(a => a.AccountNumber, "A-1")
            .WithETag("W/\"7\"")
            .Set(a => a.Name, "Contoso")
            .ToUpsertRequest(UpsertConflictResolution.IfUnmodifiedSince);

        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("testaccounts(accountnumber='A-1')", request.Url);
        Assert.Equal("W/\"7\"", request.Headers["If-Match"]);
        Assert.Equal("{\"name\":\"Contoso\"}", request.Payload!.ToJsonString());
    }

    [Fact]
    public void ToRequest_ConsumesTheBuilder()
    {
        var client = new DataverseClient(new FakeTransport());
        var write = client.Entity<TestAccount>().Set(a => a.Name, "x");

        write.ToCreateRequest();

        Assert.Throws<InvalidOperationException>(() => write.ToCreateRequest());
    }

    [Fact]
    public void ToRequest_PayloadIsACopy()
    {
        var client = new DataverseClient(new FakeTransport());

        var request = client.Entity<TestAccount>().Set(a => a.Name, "x").ToCreateRequest();
        request.Payload!["name"] = "changed";

        var again = client.Entity<TestAccount>().Set(a => a.Name, "x").ToCreateRequest();
        Assert.Equal("x", (string?)again.Payload!["name"]);
    }

    [Fact]
    public void ToDeleteRequest_RequiresAKey()
    {
        var client = new DataverseClient(new FakeTransport());

        Assert.Equal($"testaccounts({Id})", client.Entity<TestAccount>(Id).ToDeleteRequest().Url);
        Assert.Throws<InvalidOperationException>(() => client.Entity<TestAccount>().ToDeleteRequest());
    }

    [Fact]
    public void Action_ToRequest_IsTheBoundActionPost()
    {
        var client = new DataverseClient(new FakeTransport());

        var request = client.ForEntity<TestAccount>(Id).Action<Req, object>("Close").WithParameter(r => r.Status, 3).ToRequest();

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"testaccounts({Id})/Microsoft.Dynamics.CRM.Close", request.Url);
        Assert.Equal("{\"Status\":3}", request.Payload!.ToJsonString());
    }

    // ────────── Batches accept builder requests ──────────

    [Fact]
    public async Task Batch_Add_WritesBuilderRequestsIntoTheChangesetAndGetsAtTopLevel()
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport);

        await client.Batch()
            .Add(client.Entity<TestAccount>().Set(a => a.Name, "Parent").ToCreateRequest(), out var parent)
            .Add(client.Entity<TestAccount>().UsingAlternateKey("accountnumber", "A-1").Set("name", "Upserted").ToUpsertRequest())
            .Add(client.Entity<TestAccount>(Id).ToDeleteRequest())
            .Add(new DataverseRequest(HttpMethod.Get, "testaccounts?$top=1"))
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, parent.ContentId);

        var body = transport.Bodies.Single()!;
        var changesetEnd = body.IndexOf("--changeset_", body.LastIndexOf("Content-ID: 3", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains($"POST {BaseUrl}/testaccounts HTTP/1.1", body);
        Assert.Contains($"PATCH {BaseUrl}/testaccounts(accountnumber='A-1') HTTP/1.1", body);
        Assert.Contains($"DELETE {BaseUrl}/testaccounts({Id}) HTTP/1.1", body);
        Assert.True(body.IndexOf($"GET {BaseUrl}/testaccounts?$top=1", StringComparison.Ordinal) > changesetEnd,
            "The GET should follow the changeset at the top level of the batch.");
        Assert.Equal("$batch", transport.Requests.Single().Url);
    }

    [Fact]
    public void Batch_Add_RejectsAbsoluteUrlsAndRawContent()
    {
        var client = new DataverseClient(new FakeTransport());

        Assert.Throws<ArgumentException>(() => client.Batch().Add(new DataverseRequest(HttpMethod.Get, $"{BaseUrl}/testaccounts")));
        Assert.Throws<ArgumentException>(() => client.Batch().Add(new DataverseRequest(HttpMethod.Post, "x") { Content = new StringContent("") }));
    }

    [Fact]
    public void BatchOperationResult_GetEntity_DeserializesTheBody()
    {
        var result = new BatchOperationResult(1, HttpStatusCode.OK, "{\"name\":\"Contoso\",\"revenue\":5}");

        Assert.Equal("Contoso", result.GetEntity<TestAccount>()!.Name);
        Assert.Null(new BatchOperationResult(2, HttpStatusCode.NoContent, "").GetEntity<TestAccount>());
    }

    // ────────── Write results ──────────

    [Theory]
    [InlineData(HttpStatusCode.Created, true)]
    [InlineData(HttpStatusCode.OK, false)]
    public async Task Upsert_WithRepresentation_ReportsWhetherTheRecordWasCreated(HttpStatusCode status, bool created)
    {
        var transport = new FakeTransport { Respond = _ => Json("{\"name\":\"Contoso\"}", status) };
        var client = new DataverseClient(transport);

        var result = await client.Entity<TestAccount>(Id).Set(a => a.Name, "Contoso").ReturnRepresentation()
            .UpsertAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(created, result.Created);
        Assert.Equal("Contoso", result.Entity!.Name);
    }

    [Fact]
    public async Task Upsert_ByAlternateKey_ReturnsTheRecordId()
    {
        var transport = new FakeTransport { Respond = _ => DataverseResponse.Empty(entityUrl: $"{BaseUrl}/testaccounts({Id})") };
        var client = new DataverseClient(transport);

        var result = await client.Entity<TestAccount>().UsingAlternateKey("accountnumber", "A-1").Set("name", "x")
            .UpsertAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(Id, result.Id);
        Assert.Null(result.Created);
        Assert.Null(result.Entity);
    }

    [Fact]
    public async Task DeepInsert_ReturnsTheNewRecordId()
    {
        var transport = new FakeTransport { Respond = _ => DataverseResponse.Empty(entityUrl: $"{BaseUrl}/testaccounts({Id})") };
        var client = new DataverseClient(transport);

        var result = await client.DeepInsert(new TestAccount { Name = "Parent" })
            .WithRelated(new[] { new TestContact { FirstName = "a" } }, "contacts")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Id, result.Id);
        Assert.Null(result.Entity);
    }

    // ────────── Transport ──────────

    [Fact]
    public async Task HttpTransport_ResolvesRelativeAndAbsoluteUrlsAgainstTheBaseUrl()
    {
        var stub = new StubHandler((_, _) => StubHandler.Json());
        using var http = new DataverseHttpTransport(
            new DataverseClientOptions { BaseUrl = BaseUrl, TokenProvider = () => Task.FromResult("t") },
            new HttpClient(stub));

        using (await http.SendAsync(new DataverseRequest(HttpMethod.Get, "testaccounts"), TestContext.Current.CancellationToken)) { }
        using (await http.SendAsync(new DataverseRequest(HttpMethod.Get, "https://evil.example.com/api/data/v9.2/testaccounts?$skiptoken=1"), TestContext.Current.CancellationToken)) { }

        Assert.Equal($"{BaseUrl}/testaccounts", stub.Requests[0].RequestUri!.ToString());
        Assert.StartsWith($"{BaseUrl}/testaccounts?", stub.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task HttpTransport_ResponseBody_CanBeReadMoreThanOnce()
    {
        // A decorator that inspects the body, followed by the builder that reads it, must both see it.
        var stub = new StubHandler((_, _) => StubHandler.Json("{\"name\":\"Contoso\",\"accountid\":\"" + Id + "\"}"));
        using var http = new DataverseHttpTransport(
            new DataverseClientOptions { BaseUrl = BaseUrl, TokenProvider = () => Task.FromResult("t") },
            new HttpClient(stub));

        using var response = await http.SendAsync(new DataverseRequest(HttpMethod.Get, $"testaccounts({Id})"), TestContext.Current.CancellationToken);

        var json = await response.ReadJsonObjectAsync(TestContext.Current.CancellationToken);
        var typed = await response.ReadAsync<TestAccount>(TestContext.Current.CancellationToken);
        var text = await response.ReadStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Contoso", (string?)json["name"]);
        Assert.Equal("Contoso", typed!.Name);
        Assert.Contains("Contoso", text);
    }

    [Fact]
    public async Task HttpTransport_RejectsARequestWithBothPayloadAndContent()
    {
        using var http = new DataverseHttpTransport(
            new DataverseClientOptions { BaseUrl = BaseUrl, TokenProvider = () => Task.FromResult("t") },
            new HttpClient(new StubHandler((_, _) => StubHandler.Json())));

        await Assert.ThrowsAsync<ArgumentException>(() => http.SendAsync(
            new DataverseRequest(HttpMethod.Post, "x") { Payload = new JsonObject(), Content = new StringContent("") },
            TestContext.Current.CancellationToken));
    }
}
