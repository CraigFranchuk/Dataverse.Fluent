using Dataverse.Fluent.IntegrationTests.Models;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.IntegrationTests;

// DataverseException built from real failures. A 2026-11-18 probe found: Dataverse sends the request ID as
// x-ms-service-request-id / REQ_ID, never x-ms-activity-id (so ActivityId was always null); IIS rejections come back
// as HTML pages, which became the whole message; and a 401 has an empty body with its reason in 401_Error_Reason.
[Collection("Dataverse Integration Tests")]
public class ErrorModelTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public ErrorModelTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A404_CarriesTheCodeMessageAndRequestId()
    {
        var ex = await Assert.ThrowsAsync<DataverseException>(() =>
            _fixture.Client.Entity<Account>(Guid.NewGuid()).RetrieveAsync(TestContext.Current.CancellationToken));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("0x80040217", ex.ErrorCode);
        Assert.Contains("Does Not Exist", ex.Message);
        Assert.True(Guid.TryParse(ex.ActivityId, out _), $"ActivityId '{ex.ActivityId}' is not a request ID.");
    }

    [Fact]
    public async Task AValidationFailure_CarriesTheCodeAndRequestId()
    {
        var ex = await Assert.ThrowsAsync<DataverseException>(() => _fixture.Client.Transport.ExecuteRequestAsync(
            HttpMethod.Get, "accounts?$select=nosuchcolumn", ct: TestContext.Current.CancellationToken));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("0x80060888", ex.ErrorCode);
        Assert.True(Guid.TryParse(ex.ActivityId, out _));
    }

    [Fact]
    public async Task AFailureInsideABatch_CarriesTheOperationsRequestId()
    {
        var result = await _fixture.Client.Batch().AddDelete<Account>(Guid.NewGuid()).ExecuteAsync(TestContext.Current.CancellationToken);

        var ex = Assert.Throws<DataverseException>(result.ThrowIfAnyFailed);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("0x80040217", ex.ErrorCode);
        Assert.True(Guid.TryParse(ex.ActivityId, out _), $"ActivityId '{ex.ActivityId}' is not a request ID.");
    }

    [Theory]
    [InlineData("accounts(name='a%26b')", 400)]
    [InlineData("accounts(name='a%2Bb')", 404)]
    public async Task AnIisRejection_HasAShortMessage_AndKeepsThePageInRawResponse(string url, int status)
    {
        // Sent raw, past the library's own routing, to reach IIS's HTML error page.
        var ex = await Assert.ThrowsAsync<DataverseException>(() =>
            _fixture.Client.Transport.ExecuteRequestAsync(HttpMethod.Get, url, ct: TestContext.Current.CancellationToken));

        Assert.Equal(status, (int)ex.StatusCode);
        Assert.StartsWith($"Dataverse API failed with status {status}", ex.Message);
        Assert.Contains("HTML page", ex.Message);
        Assert.DoesNotContain("<", ex.Message);
        Assert.StartsWith("<", ex.RawResponse.TrimStart());
        Assert.Null(ex.ActivityId); // it never reached Dataverse
    }

    [Fact]
    public async Task A401_ExplainsWhy()
    {
        var client = new DataverseClient(_fixture.Client.Transport.BaseUrl, () => Task.FromResult("not-a-token"));

        var ex = await Assert.ThrowsAsync<DataverseException>(() =>
            client.Query<Account>().Top(1).GetListAsync(TestContext.Current.CancellationToken));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.StartsWith("Dataverse API failed with status 401 (Unauthorized): ", ex.Message);
        Assert.True(ex.Message.Length > "Dataverse API failed with status 401 (Unauthorized): ".Length);
    }
}
