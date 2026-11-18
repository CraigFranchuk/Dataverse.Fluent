using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Dataverse.Fluent.Tests;

public class RequestOptionsTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";

    private static readonly Guid UserObjectId = Guid.Parse("aaaaaaaa-0000-1111-2222-bbbbbbbbbbbb");
    private static readonly Guid SystemUserId = Guid.Parse("00aa00aa-bb11-cc22-dd33-44ee44ee44ee");

    // ────────── Headers ──────────

    [Theory]
    [InlineData(BusinessLogicBypass.CustomSync, "CustomSync")]
    [InlineData(BusinessLogicBypass.CustomAsync, "CustomAsync")]
    [InlineData(BusinessLogicBypass.CustomSync | BusinessLogicBypass.CustomAsync, "CustomSync,CustomAsync")]
    public async Task BypassBusinessLogic_SendsTheBypassHeader(BusinessLogicBypass bypass, string expected)
    {
        var request = await SendOneAsync(o => o.BypassBusinessLogic = bypass);

        Assert.Equal(expected, request.Headers["MSCRM.BypassBusinessLogicExecution"]);
    }

    [Fact]
    public async Task EachOption_SendsItsDocumentedHeader()
    {
        var step1 = Guid.Parse("45e0c603-0d0b-466e-a286-d7fc1cda8361");
        var step2 = Guid.Parse("d5370603-e4b9-4b92-b765-5966492a4fd7");

        var request = await SendOneAsync(o =>
        {
            o.BypassPluginStepIds.Add(step1);
            o.BypassPluginStepIds.Add(step2);
            o.BypassPowerAutomateFlows = true;
            o.DetectDuplicates = true;
            o.SolutionUniqueName = "ExampleSolution";
            o.CallerObjectId = UserObjectId;
            o.Headers["MSCRM.SessionToken"] = "207:8#142792105#7=-1";
        });

        Assert.Equal($"{step1},{step2}", request.Headers["MSCRM.BypassBusinessLogicExecutionStepIds"]);
        Assert.Equal("true", request.Headers["MSCRM.SuppressCallbackRegistrationExpanderJob"]);
        Assert.Equal("false", request.Headers["MSCRM.SuppressDuplicateDetection"]);
        Assert.Equal("ExampleSolution", request.Headers["MSCRM.SolutionUniqueName"]);
        Assert.Equal(UserObjectId.ToString(), request.Headers["CallerObjectId"]);
        Assert.Equal("207:8#142792105#7=-1", request.Headers["MSCRM.SessionToken"]);
        Assert.False(request.Headers.ContainsKey("MSCRMCallerID"));
        Assert.False(request.Headers.ContainsKey("MSCRM.BypassBusinessLogicExecution"));
    }

    [Fact]
    public async Task CallerSystemUserId_SendsTheLegacyImpersonationHeader()
    {
        var request = await SendOneAsync(o => o.CallerSystemUserId = SystemUserId);

        Assert.Equal(SystemUserId.ToString(), request.Headers["MSCRMCallerID"]);
        Assert.False(request.Headers.ContainsKey("CallerObjectId"));
    }

    [Fact]
    public async Task DuplicateStepIds_AreSentOnce()
    {
        var step = Guid.NewGuid();

        var request = await SendOneAsync(o =>
        {
            o.BypassPluginStepIds.Add(step);
            o.BypassPluginStepIds.Add(step);
        });

        Assert.Equal(step.ToString(), request.Headers["MSCRM.BypassBusinessLogicExecutionStepIds"]);
    }

    // ────────── Scope ──────────

    [Fact]
    public async Task Headers_ApplyToEveryKindOfRequest()
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport).WithOptions(o => o.CallerObjectId = UserObjectId);
        var ct = TestContext.Current.CancellationToken;

        await client.Query<TestAccount>().GetListAsync(ct);
        await client.Entity<TestAccount>().Set(a => a.Name, "Contoso").CreateAsync(ct);
        await client.Entity<TestAccount>(Guid.NewGuid()).Set(a => a.Name, "Fabrikam").UpdateAsync(ct);
        await client.Entity<TestAccount>(Guid.NewGuid()).DeleteAsync(ct);
        await client.Transport.ExecuteRequestAsync(HttpMethod.Post, "WhoAmI", ct: ct);

        Assert.Equal(5, transport.Requests.Count);
        Assert.All(transport.Requests, r => Assert.Equal(UserObjectId.ToString(), r.Headers["CallerObjectId"]));
    }

    [Fact]
    public async Task BuilderHeaders_AreKept_AlongsideTheScopedHeaders()
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport).WithOptions(o => o.DetectDuplicates = true);

        await client.Entity<TestAccount>(Guid.NewGuid()).Set(a => a.Name, "x").ReturnRepresentation().UpdateAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(transport.Requests);
        Assert.Equal("*", request.Headers["If-Match"]);
        Assert.Equal("return=representation", request.Headers["Prefer"]);
        Assert.Equal("false", request.Headers["MSCRM.SuppressDuplicateDetection"]);
    }

    [Fact]
    public async Task AHeaderTheRequestSets_TakesPrecedence()
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport).WithOptions(o => o.SolutionUniqueName = "Default");

        await client.Transport.ExecuteRequestAsync(HttpMethod.Post, "webresourceset",
            headers: new Dictionary<string, string> { ["mscrm.solutionuniquename"] = "Override" },
            ct: TestContext.Current.CancellationToken);

        Assert.Equal("Override", Assert.Single(transport.Requests).Headers["MSCRM.SolutionUniqueName"]);
    }

    [Fact]
    public async Task TheOriginalClient_IsUnchanged()
    {
        var transport = new FakeTransport();
        var original = new DataverseClient(transport);
        _ = original.WithOptions(o => o.BypassBusinessLogic = BusinessLogicBypass.CustomSync);

        await original.Query<TestAccount>().GetListAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("MSCRM.BypassBusinessLogicExecution", Assert.Single(transport.Requests).Headers.Keys);
    }

    [Fact]
    public async Task TheCallersRequest_IsNotModified()
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport).WithOptions(o => o.BypassPowerAutomateFlows = true);
        var request = new DataverseRequest(HttpMethod.Get, "testaccounts");

        using var response = await client.Transport.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Empty(request.Headers);
        Assert.Equal("true", Assert.Single(transport.Requests).Headers["MSCRM.SuppressCallbackRegistrationExpanderJob"]);
    }

    [Fact]
    public async Task OptionsChangedAfterwards_DoNotAffectTheClient()
    {
        var transport = new FakeTransport();
        var options = new DataverseRequestOptions { SolutionUniqueName = "First" };
        var client = new DataverseClient(transport).WithOptions(options);

        options.SolutionUniqueName = "Second";
        options.CallerObjectId = UserObjectId;
        await client.Query<TestAccount>().GetListAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(transport.Requests);
        Assert.Equal("First", request.Headers["MSCRM.SolutionUniqueName"]);
        Assert.False(request.Headers.ContainsKey("CallerObjectId"));
    }

    [Fact]
    public void NoOptions_ReturnsTheSameClient()
    {
        var client = new DataverseClient(new FakeTransport());

        Assert.Same(client, client.WithOptions(_ => { }));
    }

    [Fact]
    public void TheReturnedClient_SharesTheTransportsBaseUrl()
    {
        var client = new DataverseClient(new FakeTransport()).WithOptions(o => o.DetectDuplicates = true);

        Assert.Equal(BaseUrl, client.Transport.BaseUrl);
    }

    [Fact]
    public async Task NestedOptions_AreMerged_AndTheNewerValueWins()
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport)
            .WithOptions(o =>
            {
                o.BypassBusinessLogic = BusinessLogicBypass.CustomSync;
                o.SolutionUniqueName = "Inner";
                o.CallerObjectId = UserObjectId;
            })
            .WithOptions(o =>
            {
                o.SolutionUniqueName = "Outer";
                o.CallerSystemUserId = SystemUserId;
            });

        await client.Query<TestAccount>().GetListAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(transport.Requests);
        Assert.Equal("CustomSync", request.Headers["MSCRM.BypassBusinessLogicExecution"]);
        Assert.Equal("Outer", request.Headers["MSCRM.SolutionUniqueName"]);
        // Impersonating a new user replaces the previous one, whichever header named it.
        Assert.Equal(SystemUserId.ToString(), request.Headers["MSCRMCallerID"]);
        Assert.False(request.Headers.ContainsKey("CallerObjectId"));
    }

    // ────────── Batches ──────────

    [Fact]
    public async Task Batch_WritesTheHeadersIntoEveryPart()
    {
        // Dataverse does not apply the headers of the $batch request to the operations inside it.
        var (client, handler) = CreateHttpClient();
        var scoped = client.WithOptions(o => o.BypassBusinessLogic = BusinessLogicBypass.CustomSync | BusinessLogicBypass.CustomAsync);

        await scoped.Batch()
            .AddCreate(new TestAccount { Name = "One" })
            .AddCreate(new TestAccount { Name = "Two" })
            .AddRetrieve<TestAccount>(Guid.NewGuid())
            .ExecuteAsync(TestContext.Current.CancellationToken);

        var batch = Assert.Single(handler.Requests);
        Assert.Equal(3, CountOccurrences(batch.Body!, "MSCRM.BypassBusinessLogicExecution: CustomSync,CustomAsync\r\n"));
        Assert.Equal("CustomSync,CustomAsync", batch.Header("MSCRM.BypassBusinessLogicExecution"));
    }

    [Fact]
    public async Task Batch_AnOperationsOwnHeader_TakesPrecedenceInItsPart()
    {
        var (client, handler) = CreateHttpClient();
        var scoped = client.WithOptions(o => o.SolutionUniqueName = "Default");
        var request = scoped.Entity<TestAccount>().Set(a => a.Name, "x").ToCreateRequest();
        request.Headers["MSCRM.SolutionUniqueName"] = "Override";

        await scoped.Batch().Add(request).ExecuteAsync(TestContext.Current.CancellationToken);

        var body = Assert.Single(handler.Requests).Body!;
        Assert.Contains("MSCRM.SolutionUniqueName: Override\r\n", body);
        Assert.DoesNotContain("MSCRM.SolutionUniqueName: Default", body);
    }

    [Fact]
    public async Task Batch_FromNestedOptions_WritesAllTheHeadersIntoEachPart()
    {
        var (client, handler) = CreateHttpClient();
        var scoped = client
            .WithOptions(o => o.BypassPowerAutomateFlows = true)
            .WithOptions(o => o.CallerObjectId = UserObjectId);

        await scoped.Batch().AddCreate(new TestAccount { Name = "One" }).ExecuteAsync(TestContext.Current.CancellationToken);

        var body = Assert.Single(handler.Requests).Body!;
        Assert.Contains("MSCRM.SuppressCallbackRegistrationExpanderJob: true\r\n", body);
        Assert.Contains($"CallerObjectId: {UserObjectId}\r\n", body);
    }

    [Fact]
    public async Task Batch_WithoutOptions_WritesNoExtraHeaders()
    {
        var (client, handler) = CreateHttpClient();

        await client.Batch().AddCreate(new TestAccount { Name = "One" }).ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("MSCRM.", Assert.Single(handler.Requests).Body!);
    }

    // ────────── Over HTTP ──────────

    [Fact]
    public async Task Headers_ReachTheHttpRequest()
    {
        var (client, handler) = CreateHttpClient(new HttpResponseMessage(HttpStatusCode.NoContent));
        var scoped = client.WithOptions(o =>
        {
            o.BypassBusinessLogic = BusinessLogicBypass.CustomSync;
            o.CallerObjectId = UserObjectId;
        });

        await scoped.Entity<TestAccount>(Guid.NewGuid()).DeleteAsync(TestContext.Current.CancellationToken);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("CustomSync", sent.Header("MSCRM.BypassBusinessLogicExecution"));
        Assert.Equal(UserObjectId.ToString(), sent.Header("CallerObjectId"));
        Assert.Equal("Bearer t", sent.Header("Authorization"));
    }

    // ────────── Validation ──────────

    [Fact]
    public void BothImpersonationIds_AreRejected()
    {
        var client = new DataverseClient(new FakeTransport());

        var ex = Assert.Throws<ArgumentException>(() => client.WithOptions(o =>
        {
            o.CallerObjectId = UserObjectId;
            o.CallerSystemUserId = SystemUserId;
        }));
        Assert.Contains("not both", ex.Message);
    }

    [Fact]
    public void EmptyGuids_AreRejected()
    {
        var client = new DataverseClient(new FakeTransport());

        Assert.Throws<ArgumentException>(() => client.WithOptions(o => o.CallerObjectId = Guid.Empty));
        Assert.Throws<ArgumentException>(() => client.WithOptions(o => o.CallerSystemUserId = Guid.Empty));
        Assert.Throws<ArgumentException>(() => client.WithOptions(o => o.BypassPluginStepIds.Add(Guid.Empty)));
    }

    [Fact]
    public void AnUnknownBypassValue_IsRejected()
    {
        var client = new DataverseClient(new FakeTransport());

        Assert.Throws<ArgumentOutOfRangeException>(() => client.WithOptions(o => o.BypassBusinessLogic = (BusinessLogicBypass)4));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bad\r\nInjected: header")]
    public void AnInvalidSolutionName_IsRejected(string name)
    {
        var client = new DataverseClient(new FakeTransport());

        Assert.Throws<ArgumentException>(() => client.WithOptions(o => o.SolutionUniqueName = name));
    }

    [Theory]
    [InlineData("Has Space")]
    [InlineData("Colon:Name")]
    [InlineData("Line\r\nBreak")]
    public void AnInvalidHeaderName_IsRejected(string name)
    {
        var client = new DataverseClient(new FakeTransport());

        Assert.Throws<ArgumentException>(() => client.WithOptions(o => o.Headers[name] = "value"));
    }

    [Theory]
    [InlineData("a\r\nInjected: header")]
    [InlineData("a\nb")]
    [InlineData("a\0b")]
    public void AHeaderValueWithALineBreak_IsRejected(string value)
    {
        // Batch parts are written as raw text, where a line break would inject headers into the multipart body.
        var client = new DataverseClient(new FakeTransport());

        Assert.Throws<ArgumentException>(() => client.WithOptions(o => o.Headers["MSCRM.Custom"] = value));
    }

    [Fact]
    public void ACustomHeaderThatAPropertySets_IsRejected()
    {
        var client = new DataverseClient(new FakeTransport());

        var ex = Assert.Throws<ArgumentException>(() => client.WithOptions(o =>
        {
            o.DetectDuplicates = true;
            o.Headers["mscrm.suppressduplicatedetection"] = "true";
        }));
        Assert.Contains("set it there", ex.Message);
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        var client = new DataverseClient(new FakeTransport());

        Assert.Throws<ArgumentNullException>(() => client.WithOptions((Action<DataverseRequestOptions>)null!));
        Assert.Throws<ArgumentNullException>(() => client.WithOptions((DataverseRequestOptions)null!));
        Assert.Throws<ArgumentNullException>(() => ((IDataverseClient)null!).WithOptions(new DataverseRequestOptions()));
    }

    // ────────── Helpers ──────────

    private static async Task<DataverseRequest> SendOneAsync(Action<DataverseRequestOptions> configure)
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport).WithOptions(configure);

        await client.Query<TestAccount>().GetListAsync(TestContext.Current.CancellationToken);

        return Assert.Single(transport.Requests);
    }

    private static (DataverseClient Client, TestHttpMessageHandler Handler) CreateHttpClient(HttpResponseMessage? response = null)
    {
        var handler = new TestHttpMessageHandler();
        handler.AddResponse(_ => true, response ?? new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("--batch_response\r\n", Encoding.UTF8, "multipart/mixed")
        });
        return (new DataverseClient(BaseUrl, () => Task.FromResult("t"), new HttpClient(handler)), handler);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private sealed class FakeTransport : IDataverseTransport
    {
        public string BaseUrl => RequestOptionsTests.BaseUrl;
        public List<DataverseRequest> Requests { get; } = [];

        public Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            DataverseResponse response = request.Method == HttpMethod.Get
                ? DataverseResponse.FromJson(new JsonObject { ["value"] = new JsonArray() })
                : DataverseResponse.Empty(entityUrl: $"{BaseUrl}/testaccounts({Guid.NewGuid()})");
            return Task.FromResult(response);
        }
    }
}
