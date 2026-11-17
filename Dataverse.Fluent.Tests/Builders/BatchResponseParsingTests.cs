using System.Net;
using System.Text;
using Dataverse.Fluent.Builders;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Tests.Builders;

/// <summary>
/// Tests that a <c>$batch</c> response is parsed and correlated with the queued operations, including
/// responses that omit Content-IDs, reorder parts, use LF line endings, or omit operations entirely.
/// </summary>
public class BatchResponseParsingTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";
    private static readonly Guid Id1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Id2 = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static async Task<BatchResult> ExecuteAsync(
        Action<BatchBuilder> configure, string responseBody,
        HttpStatusCode status = HttpStatusCode.OK, string contentType = "multipart/mixed")
    {
        var handler = new TestHttpMessageHandler();
        var client = new DataverseClient(BaseUrl, () => Task.FromResult("t"), new HttpClient(handler));
        var content = new StringContent(responseBody, Encoding.UTF8);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        handler.AddResponse(_ => true, new HttpResponseMessage(status) { Content = content });

        var batch = client.Batch();
        configure(batch);
        return await batch.ExecuteAsync();
    }

    // One application/http response part. The inner headers end with CRLF each.
    private static string Part(string statusLine, int? contentId = null, string innerHeaders = "", string body = "")
        => "Content-Type: application/http\r\n"
           + (contentId is { } id ? $"Content-ID: {id}\r\n" : "")
           + "\r\n"
           + $"HTTP/1.1 {statusLine}\r\n"
           + innerHeaders
           + "\r\n"
           + body + "\r\n";

    private static string Created(Guid id, int? contentId = null)
        => Part("201 Created", contentId, $"OData-EntityId: {BaseUrl}/testaccounts({id})\r\n");

    private static string Changeset(string boundary, params string[] parts)
        => $"Content-Type: multipart/mixed; boundary={boundary}\r\n\r\n"
           + string.Concat(parts.Select(p => $"--{boundary}\r\n{p}"))
           + $"--{boundary}--\r\n";

    private static string Batch(params string[] items)
        => string.Concat(items.Select(i => $"--batchresponse_1\r\n{i}")) + "--batchresponse_1--";

    private const string ErrorBody = "{\"error\":{\"code\":\"0x80040265\",\"message\":\"validation failed\"}}";

    [Fact]
    public async Task MissingContentIds_InMultipleChangesets_AreAssignedInOperationOrder()
    {
        var result = await ExecuteAsync(
            b => b.AddCreate(new TestAccount { Name = "A" })
                  .AddCreate(new TestAccount { Name = "B" })
                  .NewChangeset()
                  .AddUpdate(Guid.NewGuid(), new TestAccount { Name = "C" }),
            Batch(
                Changeset("changesetresponse_1", Created(Id1), Created(Id2)),
                Changeset("changesetresponse_2", Part("204 No Content"))));

        Assert.Equal([1, 2, 3], result.Results.Select(r => r.ContentId));
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.NoContent], result.Results.Select(r => r.StatusCode));
        Assert.Equal([Id1, Id2, null], result.Results.Select(r => r.EntityId));
    }

    [Fact]
    public async Task MissingContentIds_AfterAFailedChangeset_StayAlignedWithLaterOperations()
    {
        // The failed changeset returns ONE part for two operations, so positional numbering would drift.
        var result = await ExecuteAsync(
            b => b.AddCreate(new TestAccount { Name = "A" })
                  .AddCreate(new TestAccount { Name = "B" })
                  .NewChangeset()
                  .AddUpdate(Guid.NewGuid(), new TestAccount { Name = "C" })
                  .ContinueOnError(),
            Batch(
                Part("400 Bad Request", body: ErrorBody),
                Changeset("changesetresponse_2", Part("204 No Content"))));

        Assert.Equal([1, 2, 3], result.Results.Select(r => r.ContentId));
        Assert.Equal(HttpStatusCode.BadRequest, result.Results[0].StatusCode);
        Assert.True(result.Results[1].WasRolledBack);
        Assert.Equal(HttpStatusCode.NoContent, result.Results[2].StatusCode);
    }

    [Fact]
    public async Task OutOfOrderParts_InAChangeset_AreCorrelatedByContentId()
    {
        var result = await ExecuteAsync(
            b => b.AddCreate(new TestAccount { Name = "A" })
                  .AddUpdate(Guid.NewGuid(), new TestAccount { Name = "B" }),
            Batch(Changeset("changesetresponse_1",
                Part("204 No Content", contentId: 2),
                Created(Id1, contentId: 1))));

        Assert.Equal(1, result.Results[0].ContentId);
        Assert.Equal(HttpStatusCode.Created, result.Results[0].StatusCode);
        Assert.Equal(Id1, result.Results[0].EntityId);
        Assert.Equal(2, result.Results[1].ContentId);
        Assert.Equal(HttpStatusCode.NoContent, result.Results[1].StatusCode);
    }

    [Fact]
    public async Task LfOnlyLineEndings_AreParsed()
    {
        var body = Batch(
                Changeset("changesetresponse_1", Created(Id1, contentId: 1)),
                Part("200 OK", contentId: 2, "Content-Type: application/json\r\n", "{\"name\":\"Existing\"}"))
            .Replace("\r\n", "\n");

        var result = await ExecuteAsync(
            b => b.AddCreate(new TestAccount { Name = "A" }).AddRetrieve<TestAccount>(Guid.NewGuid()),
            body);

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.OK], result.Results.Select(r => r.StatusCode));
        Assert.Equal(Id1, result.Results[0].EntityId);
        Assert.Equal("{\"name\":\"Existing\"}", result.Results[1].Body);
    }

    [Fact]
    public async Task BoundaryFromTheContentTypeHeader_IsUsed()
    {
        // A preamble before the first delimiter defeats boundary sniffing, so the header value must be used.
        var body = "preamble\r\n" + Batch(Changeset("changesetresponse_1", Created(Id1, contentId: 1)));

        var result = await ExecuteAsync(
            b => b.AddCreate(new TestAccount { Name = "A" }),
            body,
            contentType: "multipart/mixed; boundary=batchresponse_1");

        Assert.Equal(Id1, Assert.Single(result.Results).EntityId);
    }

    [Fact]
    public async Task StandaloneGet_WithNoBody_HasEmptyBody()
    {
        var result = await ExecuteAsync(
            b => b.AddRetrieve<TestAccount>(Guid.NewGuid()),
            Batch(Part("404 Not Found", contentId: 1)));

        var only = Assert.Single(result.Results);
        Assert.Equal(HttpStatusCode.NotFound, only.StatusCode);
        Assert.Equal(string.Empty, only.Body);
    }

    // ────────── Operations without a response part ──────────

    [Fact]
    public async Task OperationsAfterTheBatchStopped_AreReportedAsNotExecuted()
    {
        // Without continue-on-error Dataverse stops at the first failure: later operations get no part.
        var result = await ExecuteAsync(
            b => b.AddCreate(new TestAccount { Name = "A" })
                  .NewChangeset()
                  .AddCreate(new TestAccount { Name = "B" })
                  .AddRetrieve<TestAccount>(Guid.NewGuid()),
            Batch(Changeset("changesetresponse_1", Part("400 Bad Request", contentId: 1, body: ErrorBody))),
            status: HttpStatusCode.BadRequest);

        Assert.Equal([1, 2, 3], result.Results.Select(r => r.ContentId));
        Assert.Equal(HttpStatusCode.BadRequest, result.Results[0].StatusCode);
        Assert.All(result.Results.Skip(1), r =>
        {
            Assert.Equal(HttpStatusCode.FailedDependency, r.StatusCode);
            Assert.False(r.WasRolledBack); // never executed, rather than rolled back
            Assert.Contains("No response", r.Body);
        });
        Assert.False(result.AllSucceeded);
    }

    [Fact]
    public async Task UnparseableResponse_ReportsEveryOperationAsFailed_NotSucceeded()
    {
        var result = await ExecuteAsync(
            b => b.AddCreate(new TestAccount { Name = "A" }).AddRetrieve<TestAccount>(Guid.NewGuid()),
            "--batchresponse_1\r\nnot an http response\r\n--batchresponse_1--");

        Assert.Equal(2, result.Results.Count);
        Assert.False(result.AllSucceeded);
        Assert.Throws<BatchOperationException>(result.ThrowIfAnyFailed);
    }

    // ────────── Lookup bindings to earlier operations ──────────

    [Fact]
    public async Task AddCreate_WithLookupBindingsAndReference_BindsAndReturnsItsOwnReference()
    {
        var handler = new TestHttpMessageHandler();
        var client = new DataverseClient(BaseUrl, () => Task.FromResult("t"), new HttpClient(handler));
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("--batchresponse_1--") });

        var batch = client.Batch()
            .AddCreate(new TestAccount { Name = "Parent" }, out var parent)
            .AddCreate(new TestContact { FirstName = "Child" },
                new Dictionary<string, BatchReference> { ["parentcustomerid_account"] = parent },
                out var child);
        await batch.ExecuteAsync();

        Assert.Equal(1, parent.ContentId);
        Assert.Equal(2, child.ContentId);
        Assert.Contains("\"parentcustomerid_account@odata.bind\":\"$1\"", Assert.Single(handler.Requests).Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AddCreate_WithBlankNavigationPropertyInBindings_Throws(string navigationProperty)
    {
        var client = new DataverseClient(BaseUrl, () => Task.FromResult("t"), new HttpClient(new TestHttpMessageHandler()));
        var batch = client.Batch().AddCreate(new TestAccount { Name = "Parent" }, out var parent);

        Assert.ThrowsAny<ArgumentException>(() => batch.AddCreate(
            new TestContact(), new Dictionary<string, BatchReference> { [navigationProperty] = parent }));
    }

    [Fact]
    public void AddCreate_BindingToAnOperationInAnEarlierChangeset_Throws()
    {
        // Dataverse resolves $n references only within the same changeset.
        var client = new DataverseClient(BaseUrl, () => Task.FromResult("t"), new HttpClient(new TestHttpMessageHandler()));
        var batch = client.Batch()
            .AddCreate(new TestAccount { Name = "Parent" }, out var parent)
            .NewChangeset();

        var ex = Assert.Throws<ArgumentException>(() => batch.AddCreate(
            new TestContact(), new Dictionary<string, BatchReference> { ["parentcustomerid_account"] = parent }));
        Assert.Contains("changeset", ex.Message);
    }
}
