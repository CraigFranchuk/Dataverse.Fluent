using System.Net;
using Dataverse.Fluent.IntegrationTests.Models;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.IntegrationTests;

// TEST_PLAN items 10 (token refresh after a 401) and 11 (annotation limits), against live Dataverse.
// Records created here are deleted by the fixture when the run ends.
[Collection("Dataverse Integration Tests")]
public class AuthAndAnnotationLimitTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public AuthAndAnnotationLimitTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ───────── Item 10: token refresh ─────────

    // Hands out the given bad token first, then valid ones, counting the calls.
    private sealed class SwitchingTokens(Func<Task<string>> valid, string bad)
    {
        public int Calls;

        public async Task<string> GetAsync() => Interlocked.Increment(ref Calls) == 1 ? bad : await valid();
    }

    // A real token with its signature broken: Dataverse rejects it (as if revoked), and its exp is still in the future,
    // so the client caches it and only a 401 can make it ask for another.
    private async Task<string> TamperedTokenAsync()
    {
        var token = await _fixture.TokenProvider();
        var lastDot = token.LastIndexOf('.');
        var signature = token[(lastDot + 1)..];
        return token[..(lastDot + 1)] + (signature[0] == 'A' ? 'B' : 'A') + signature[1..];
    }

    [Fact]
    public async Task ARejectedToken_IsRefreshed_AndTheReadRetriedOnce()
    {
        var tokens = new SwitchingTokens(_fixture.TokenProvider, await TamperedTokenAsync());
        using var client = new DataverseClient(_fixture.Client.Transport.BaseUrl, tokens.GetAsync);

        var rows = await client.Query<Account>().Select(a => new { a.AccountId }).Top(1).GetListAsync(Ct);

        Assert.Single(rows);
        Assert.Equal(2, tokens.Calls);
    }

    [Fact]
    public async Task ARejectedToken_IsRefreshed_AndTheWriteSentOnce()
    {
        var name = $"Auth retry {Guid.NewGuid()}";
        var tokens = new SwitchingTokens(_fixture.TokenProvider, "not-a-token");
        using var client = new DataverseClient(_fixture.Client.Transport.BaseUrl, tokens.GetAsync);

        var created = await client.Entity<Account>().Set(a => a.Name, name).CreateAsync(Ct);

        try
        {
            Assert.Equal(2, tokens.Calls);
            // The first attempt was rejected before anything was written, so exactly one record exists.
            var matches = await _fixture.Client.Query<Account>().Where(a => a.Name == name).GetListAsync(Ct);
            Assert.Equal(created.Id, Assert.Single(matches).AccountId);
        }
        finally
        {
            await _fixture.Client.Entity<Account>(created.Id!.Value).DeleteAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ATokenThatStaysBad_FailsWithTheReason_AfterOneRetry()
    {
        var calls = 0;
        using var client = new DataverseClient(_fixture.Client.Transport.BaseUrl, () =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult("not-a-token");
        });

        var ex = await Assert.ThrowsAsync<DataverseException>(() => client.Query<Account>().Top(1).GetListAsync(Ct));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.StartsWith("Dataverse API failed with status 401 (Unauthorized): ", ex.Message);
        Assert.Equal(2, calls); // the first, and one refresh that returned the same token: no loop
    }

    // ───────── Item 11: annotation limits ─────────

    private async Task<Guid> NewAccountAsync()
        => (await _fixture.Client.Entity<Account>().Set(a => a.Name, $"Annotation limits {Guid.NewGuid()}").CreateAsync(Ct)).Id!.Value;

    [Fact]
    public async Task Create_WithoutARepresentation_ReturnsTheNewId()
    {
        // Until 1.0.2 AnnotationId was Guid.Empty: the ID is only in the OData-EntityId header, which was not read.
        var account = await NewAccountAsync();

        var note = await _fixture.Client.Annotation().Regarding<Account>(account).WithSubject("id check").CreateAsync(Ct);

        Assert.NotEqual(Guid.Empty, note.AnnotationId);
        var read = await _fixture.Client.Annotation().RetrieveAsync(note.AnnotationId, Ct);
        Assert.Equal("id check", read.Subject);
    }

    [Theory]
    [InlineData("résumé 日本語 😀.pdf", null, "application/pdf")]
    [InlineData("data.custom", "application/x-custom-thing", "application/x-custom-thing")]
    [InlineData("O'Brien & Co #1.txt", null, "text/plain")]
    public async Task FileName_AndMimeType_RoundTrip(string fileName, string? mimeType, string expectedMime)
    {
        var account = await NewAccountAsync();
        byte[] content = [0, 1, 2, 250, 251, 252, 255];

        var note = await _fixture.Client.Annotation().Regarding<Account>(account).WithFile(fileName, content, mimeType)
            .ReturnRepresentation().CreateAsync(Ct);
        var (name, mime, body) = await _fixture.Client.Annotation().DownloadFileAsync(note.AnnotationId, Ct);

        Assert.Equal(fileName, name);
        Assert.Equal(expectedMime, mime);
        Assert.Equal(content, body);
    }

    [Fact]
    public async Task AnAttachmentOverTheLimit_FailsWithDataversesMessage()
    {
        var org = await _fixture.Client.Transport.ExecuteRequestAsync(HttpMethod.Get, "organizations?$select=maxuploadfilesize", ct: Ct);
        var limit = (int)org["value"]![0]!["maxuploadfilesize"]!;
        var account = await NewAccountAsync();

        var ex = await Assert.ThrowsAsync<DataverseException>(() => _fixture.Client.Annotation().Regarding<Account>(account)
            .WithFile("too-big.bin", new byte[limit + 1], "application/octet-stream").CreateAsync(Ct));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        // "Attachment file size is too big." in the reference environment, whose limit is 5,242,880 bytes.
        Assert.Equal("0x80044a02", ex.ErrorCode);
        Assert.Contains("too big", ex.Message);
    }
}
