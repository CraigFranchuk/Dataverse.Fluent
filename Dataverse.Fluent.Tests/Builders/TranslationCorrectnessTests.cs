using System.Net;
using System.Text.Json.Serialization;
using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent.Tests.Builders;

/// <summary>
/// Expressions and options that used to be translated into a wrong request without an error. Each test
/// pins either the correct request or an exception.
/// </summary>
public class TranslationCorrectnessTests
{
    [EntitySet("probes")]
    private class Probe
    {
        public Guid ProbeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public int StateCode { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateOnly? Birth { get; set; }
        public int? Employees { get; set; }
        public uint Rank { get; set; }

        [JsonPropertyName("primarycontactid")]
        public ProbeContact? Primary { get; set; }
    }

    [EntitySet("probecontacts")]
    private class ProbeContact
    {
        public string? FullName { get; set; }
    }

    private class ActionRequest
    {
        public int Status { get; set; }

        [JsonPropertyName("Renamed")]
        public string? Note { get; set; }
    }

    private static (DataverseClient Client, TestHttpMessageHandler Handler) CreateClient(string json = "{\"value\":[]}")
    {
        var handler = new TestHttpMessageHandler();
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });

        var client = new DataverseClient(
            "https://test.crm.dynamics.com/api/data/v9.2",
            () => Task.FromResult("test-token"),
            new HttpClient(handler));

        return (client, handler);
    }

    private static string Filter(System.Linq.Expressions.Expression<Func<Probe, bool>> predicate)
        => new ODataExpressionVisitor().ToODataFilter(predicate);

    // ───────── Partial evaluation ─────────

    [Theory]
    [InlineData(true, "revenue eq 1")]
    [InlineData(false, "revenue eq 2")]
    public void Filter_ConditionalOnCapturedValue_IsEvaluatedBeforeTranslation(bool flag, string expected)
        => Assert.Equal(expected, Filter(x => x.Revenue == (flag ? 1m : 2m)));

    [Fact]
    public void Filter_MethodCallOnCapturedValue_IsEvaluatedClientSide()
    {
        var name = "abc";

        Assert.Equal("name eq 'ABC'", Filter(x => x.Name == name.ToUpper()));
    }

    [Fact]
    public void Filter_DateArithmetic_IsEvaluatedClientSide()
    {
        var filter = Filter(x => x.CreatedOn > new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc).AddDays(-7));

        Assert.Equal("createdon gt 2026-11-08T00:00:00Z", filter);
    }

    [Fact]
    public void Filter_StaticMember_IsEvaluatedClientSide()
        => Assert.StartsWith("createdon lt 20", Filter(x => x.CreatedOn < DateTime.UtcNow.AddYears(1)));

    [Fact]
    public void Filter_ContainsOverLocalCollection_TranslatesToTheInFunction()
    {
        var states = new[] { 1, 2 };

        Assert.Equal("Microsoft.Dynamics.CRM.In(PropertyName='statecode',PropertyValues=[\"1\",\"2\"])", Filter(x => states.Contains(x.StateCode)));
    }

    [Fact]
    public void Filter_DataverseFunctionMarker_IsTranslatedNotInvoked()
    {
        var parent = Guid.Parse("11111111-1111-1111-1111-111111111111");

        Assert.Equal(
            "Microsoft.Dynamics.CRM.Under(PropertyName='probeid',PropertyValue='11111111-1111-1111-1111-111111111111')",
            Filter(x => DataverseFunctions.Under(x.ProbeId, parent)));
    }

    // ───────── Unsupported expressions fail instead of producing a wrong filter ─────────

    [Fact]
    public void Filter_ConditionalOnColumn_Throws()
        => Assert.Throws<NotSupportedException>(() => Filter(x => x.Revenue == (x.StateCode > 0 ? 1m : 2m)));

    [Fact]
    public void Filter_Coalesce_Throws()
        => Assert.Throws<NotSupportedException>(() => Filter(x => (x.Employees ?? 0) > 5));

    [Fact]
    public void Filter_MemberOfScalarColumn_Throws()
        => Assert.Throws<NotSupportedException>(() => Filter(x => x.CreatedOn.Year == 2026));

    [Fact]
    public void Filter_NavigationPath_IsStillSupported()
        => Assert.Equal("primarycontactid/fullname eq 'a'", Filter(x => x.Primary!.FullName == "a"));

    // ───────── Literals ─────────

    [Fact]
    public void Filter_DateOnly_IsAnUnquotedIsoDate()
    {
        var day = new DateOnly(2026, 11, 15);

        Assert.Equal("birth eq 2026-11-15", Filter(x => x.Birth == day));
    }

    [Fact]
    public void Filter_UnsignedInteger_IsNotQuoted()
        => Assert.Equal("rank eq 7", Filter(x => x.Rank == 7u));

    [Fact]
    public void Literal_DateTimeOffset_IsConvertedToUtc()
    {
        var value = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(-7));

        Assert.Equal("2026-11-15T16:00:00Z", ODataLiteral.Format(value));
    }

    [Fact]
    public void Literal_LocalDateTime_IsConvertedToUtc()
    {
        var local = new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Local);

        Assert.Equal(local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss") + "Z", ODataLiteral.Format(local));
    }

    [Fact]
    public async Task Function_DateParameters_AreConvertedToUtc()
    {
        var (client, handler) = CreateClient("{}");
        var value = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(-7));

        await client.Function<object>("F").WithParameter("At", value).ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal("2026-11-15T16:00:00Z", handler.Requests[0].QueryOption("@p1"));
    }

    // ───────── Projections ─────────

    [Fact]
    public void Select_PropertyOfRelatedRecord_Throws()
    {
        var (client, _) = CreateClient();

        Assert.Throws<ArgumentException>(() => client.Query<Probe>().Select(x => x.Primary!.FullName));
    }

    [Fact]
    public void Select_ComputedProjection_Throws()
    {
        var (client, _) = CreateClient();

        Assert.Throws<ArgumentException>(() => client.Query<Probe>().Select(x => new { Upper = x.Name.ToUpper() }));
    }

    [Fact]
    public async Task Select_BoxedAndNullableValueMembers_SelectTheColumn()
    {
        var (client, handler) = CreateClient();

        await client.Query<Probe>()
            .Select<object>(x => x.Revenue)
            .Select(x => x.Employees!.Value)
            .GetListAsync(TestContext.Current.CancellationToken);

        Assert.Equal("revenue,employees", handler.Requests[0].QueryOption("$select"));
    }

    // ───────── Options that accumulate ─────────

    [Fact]
    public async Task Delta_RepeatedWhere_IsCombinedIntoOneFilter()
    {
        var (client, handler) = CreateClient();

        await client.Delta<Probe>().Where("a eq 1").Where("b eq 2").Select("name").ExecuteAsync(TestContext.Current.CancellationToken);

        var request = handler.Requests[0];
        Assert.Equal("(a eq 1) and (b eq 2)", request.QueryOption("$filter"));
        Assert.Equal(1, request.Uri.Query.Split("filter=").Length - 1);
        Assert.Equal("name", request.QueryOption("$select"));
    }

    [Fact]
    public async Task Expand_RepeatedWhere_IsCombinedWithAnd()
    {
        var (client, handler) = CreateClient();

        await client.Query<Probe>()
            .Expand<ProbeContact>("contacts", e => e.Where(c => c.FullName == "a").Where("statecode eq 0"))
            .GetListAsync(TestContext.Current.CancellationToken);

        Assert.Equal("contacts($filter=(fullname eq 'a') and (statecode eq 0))", handler.Requests[0].QueryOption("$expand"));
    }

    [Fact]
    public async Task Aggregate_RepeatedWhere_IsCombinedWithAnd()
    {
        var (client, handler) = CreateClient();

        await client.Aggregate<Probe>()
            .Where(x => x.StateCode == 0)
            .Where(x => x.Revenue > 5)
            .Count("total")
            .GetListAsync<object>(TestContext.Current.CancellationToken);

        Assert.Equal(
            "filter((statecode eq 0) and (revenue gt 5))/aggregate($count as total)",
            handler.Requests[0].QueryOption("$apply"));
    }

    // ───────── Retrieve, paging, actions ─────────

    [Fact]
    public async Task Retrieve_WithoutIdOrAlternateKey_ThrowsAndSendsNothing()
    {
        var (client, handler) = CreateClient();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.Entity<Probe>().Select(x => x.Name).RetrieveAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.Entity<Probe>().RetrieveWithMetadataAsync(TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetAll_WithPageSizeAboveMaximum_Throws()
    {
        var (client, handler) = CreateClient();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.Query<Probe>().GetAllAsync(5001, TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Stream_DoesNotRequestCount()
    {
        var (client, handler) = CreateClient();

        await client.Query<Probe>().Count().GetAllAsync(ct: TestContext.Current.CancellationToken);

        Assert.Null(handler.Requests[0].QueryOption("$count"));
    }

    [Fact]
    public async Task Action_ParameterByExpression_UsesTheSerializedPropertyName()
    {
        var (client, handler) = CreateClient("{}");

        await client.Action<ActionRequest, object>("DoIt")
            .WithParameter(r => r.Status, 3)
            .WithParameter(r => r.Note, "n")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        var (requestClient, requestHandler) = CreateClient("{}");
        await requestClient.Action<ActionRequest, object>("DoIt")
            .WithRequest(new ActionRequest { Status = 3, Note = "n" })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal("{\"Status\":3,\"Renamed\":\"n\"}", handler.Requests[0].Body);
        Assert.Equal(requestHandler.Requests[0].Body, handler.Requests[0].Body);
    }

    // ───────── FetchXML filters ─────────

    private static string Fetch(System.Linq.Expressions.Expression<Func<Probe, bool>> predicate)
        => new FetchXmlFilterVisitor().ToFetchXmlFilter(predicate);

    [Fact]
    public void Fetch_StringNamedNull_IsAValueNotANullCheck()
        => Assert.Equal("<condition attribute=\"name\" operator=\"eq\" value=\"null\" />", Fetch(x => x.Name == "null"));

    [Fact]
    public void Fetch_NullLiteral_IsANullCheck()
        => Assert.Equal("<condition attribute=\"name\" operator=\"not-null\" />", Fetch(x => x.Name != null));

    [Fact]
    public void Fetch_NullableValueAndHasValue_AreSupported()
    {
        Assert.Equal("<condition attribute=\"employees\" operator=\"gt\" value=\"5\" />", Fetch(x => x.Employees!.Value > 5));
        Assert.Equal("<condition attribute=\"employees\" operator=\"not-null\" />", Fetch(x => x.Employees.HasValue));
        Assert.Equal("<condition attribute=\"employees\" operator=\"null\" />", Fetch(x => !x.Employees.HasValue));
    }

    [Fact]
    public void Fetch_DateValues_AreUtc()
    {
        var value = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(-7)).LocalDateTime;

        Assert.Equal(
            "<condition attribute=\"createdon\" operator=\"gt\" value=\"2026-11-15T16:00:00Z\" />",
            Fetch(x => x.CreatedOn > value));
    }

    [Fact]
    public void Fetch_UnsupportedExpression_Throws()
    {
        Assert.Throws<NotSupportedException>(() => Fetch(x => x.Revenue == (x.StateCode > 0 ? 1m : 2m)));
        Assert.Throws<NotSupportedException>(() => Fetch(x => x.Primary!.FullName == "a"));
    }

    // ───────── Telemetry ─────────

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Logs_IncludeTheQueryStringOnlyWhenEnabled(bool includeQuery, bool expectQuery)
    {
        var handler = new TestHttpMessageHandler();
        handler.AddJsonResponse(_ => true, new { value = Array.Empty<object>() });
        var logger = new TestLogger<DataverseClient>();
        var client = new DataverseClient(
            new DataverseClientOptions
            {
                BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2",
                TokenProvider = () => Task.FromResult("test-token"),
                IncludeQueryInTelemetry = includeQuery
            },
            new HttpClient(handler),
            logger);

        await client.Query<Probe>().Where(x => x.Name == "secret").GetListAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, entry => Assert.Contains("/probes", entry.Message));
        Assert.All(logger.Entries, entry => Assert.Equal(expectQuery, entry.Message.Contains("secret")));
    }

    // ───────── Batch ─────────

    [Fact]
    public async Task Batch_ContentId_IsAHeaderOfTheMimePart()
    {
        var handler = new TestHttpMessageHandler();
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty)
        });
        var client = new DataverseClient(
            "https://test.crm.dynamics.com/api/data/v9.2",
            () => Task.FromResult("test-token"),
            new HttpClient(handler));

        await client.Batch()
            .AddCreate(new Probe { Name = "a" })
            .AddGet("probes?$top=1")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        var body = handler.Requests[0].Body!;
        Assert.Matches(@"Content-ID: 1\r\n\r\nPOST ", body);
        Assert.Matches(@"Content-ID: 2\r\n\r\nGET ", body);
        Assert.Equal(2, body.Split("Content-ID:").Length - 1);
    }
}
