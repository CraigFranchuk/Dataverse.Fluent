using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Tests.Builders;

/// <summary>
/// The action overloads without a response type (<c>Action&lt;TRequest&gt;</c>) and without a request class (<c>Action</c>).
/// </summary>
public class ActionOverloadTests
{
    private static DataverseClient CreateTestClient(TestHttpMessageHandler handler)
        => new("https://test.crm.dynamics.com/api/data/v9.2", () => Task.FromResult("test-token"), new HttpClient(handler));

    private class RecalculateRequest
    {
        [JsonPropertyName("Mode")]
        public int Mode { get; set; }

        [JsonPropertyName("Reason")]
        public string? Reason { get; set; }
    }

    private class ScoreResult
    {
        [JsonPropertyName("Score")]
        public int Score { get; set; }
    }

    [Flags]
    private enum AccessRights { None = 0, ReadAccess = 1, WriteAccess = 2 }

    [Fact]
    public void ActionWithRequestOnly_BuildsTheSameRequestAsTheObjectResponseForm()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());
        var request = new RecalculateRequest { Mode = 2, Reason = "nightly" };

        var expected = client.Action<RecalculateRequest, object>("new_Recalculate").WithRequest(request).ToRequest();
        var actual = client.Action<RecalculateRequest>("new_Recalculate").WithRequest(request).ToRequest();

        Assert.Equal(HttpMethod.Post, actual.Method);
        Assert.Equal(expected.Url, actual.Url);
        Assert.Equal(expected.Payload!.ToJsonString(), actual.Payload!.ToJsonString());
    }

    [Fact]
    public void ActionWithRequestOnly_WithParameter_UsesTheSerializedPropertyName()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        var payload = client.Action<RecalculateRequest>("new_Recalculate")
            .WithParameter(r => r.Mode, 2)
            .WithParameter("Extra", true)
            .ToRequest().Payload!;

        Assert.Equal(2, payload["Mode"]!.GetValue<int>());
        Assert.True(payload["Extra"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ActionWithRequestOnly_ExecuteAsync_PostsTheAction()
    {
        var handler = new TestHttpMessageHandler();
        handler.AddResponse(req => req.Method == HttpMethod.Post, new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
        var client = CreateTestClient(handler);

        await client.Action<RecalculateRequest>("new_Recalculate").WithRequest(new RecalculateRequest { Mode = 1 }).ExecuteAsync();

        var sent = Assert.Single(handler.Requests);
        Assert.EndsWith("/new_Recalculate", sent.DecodedUrl);
        Assert.Equal(1, sent.Json.GetProperty("Mode").GetInt32());
    }

    [Fact]
    public void ActionByName_WritesParametersAndEnumsByMemberName()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        var request = client.Action("new_Share")
            .WithParameter("Mode", 2)
            .WithParameter("Rights", AccessRights.ReadAccess | AccessRights.WriteAccess)
            .WithParameter("Note", null)
            .ToRequest();

        Assert.Equal("new_Share", request.Url);
        Assert.Equal(2, request.Payload!["Mode"]!.GetValue<int>());
        Assert.Equal("ReadAccess, WriteAccess", request.Payload["Rights"]!.GetValue<string>());
        Assert.True(request.Payload.ContainsKey("Note"));
        Assert.Null(request.Payload["Note"]);
    }

    [Fact]
    public async Task ActionByName_ExecuteAsync_IgnoresTheResponse()
    {
        var handler = new TestHttpMessageHandler();
        handler.AddResponse(req => req.Method == HttpMethod.Post, new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
        var client = CreateTestClient(handler);

        await client.Action("new_Recalculate").WithParameter("Mode", 2).ExecuteAsync();

        Assert.Equal(2, Assert.Single(handler.Requests).Json.GetProperty("Mode").GetInt32());
    }

    [Fact]
    public async Task ActionByName_ExecuteAsyncOfT_ReturnsTheResponse()
    {
        var handler = new TestHttpMessageHandler();
        handler.AddJsonResponse(req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("new_Score"), new { Score = 42 });
        var client = CreateTestClient(handler);

        var result = await client.Action("new_Score").WithParameter("Mode", 2).ExecuteAsync<ScoreResult>();

        Assert.Equal(42, result!.Score);
    }

    [Fact]
    public void BoundActions_BuildTheBoundUrl()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());
        var id = Guid.NewGuid();

        var byName = client.ForEntity<object>(id, "leads").Action("new_Touch").ToRequest();
        var byRequest = client.ForEntity<object>(id, "leads").Action<RecalculateRequest>("new_Recalculate").ToRequest();

        Assert.Equal($"leads({id})/Microsoft.Dynamics.CRM.new_Touch", byName.Url);
        Assert.Equal($"leads({id})/Microsoft.Dynamics.CRM.new_Recalculate", byRequest.Url);
    }

    [Fact]
    public async Task Builders_AreSingleUse()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        var byName = client.Action("new_Touch");
        byName.ToRequest();
        await Assert.ThrowsAsync<InvalidOperationException>(() => byName.ExecuteAsync<ScoreResult>());

        var byRequest = client.Action<RecalculateRequest>("new_Recalculate");
        byRequest.ToRequest();
        Assert.Throws<InvalidOperationException>(() => byRequest.ToRequest());
    }

    [Fact]
    public void ActionByName_SendsThroughAClientWithOptions()
    {
        var client = CreateTestClient(new TestHttpMessageHandler()).WithOptions(o => o.BypassPowerAutomateFlows = true);

        var request = client.Action("new_Touch").ToRequest();

        Assert.Equal("new_Touch", request.Url);
    }
}
