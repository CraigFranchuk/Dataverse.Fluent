using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Dataverse.Fluent.Builders;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Tests.Builders;

// Alternate keys in URLs and binds. Each rule here was established against a live environment on 2026-11-18; the
// live tests are in Dataverse.Fluent.IntegrationTests/AlternateKeyTests.cs:
// - In a URL path, IIS rejects an encoded &, ? or + (HTML error page), but the same request works inside $batch.
// - In a URL path, / and \ fail in any form.
// - In a JSON body (@odata.bind), Dataverse does not URL-decode the key, and / \ # ? fail in any form.
public class KeyedRequestSenderTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";

    [Fact]
    public async Task Retrieve_ByAKeyWithAnAmpersand_IsSentInsideBatch()
    {
        var transport = new FakeTransport { Respond = _ => BatchResponse("200 OK", "", """{"name":"Special"}""") };

        var account = await new DataverseClient(transport).Entity<TestAccount>()
            .UsingAlternateKey(x => x.AccountNumber, "O'Brien & Co")
            .RetrieveAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(transport.Requests);
        Assert.Equal("$batch", request.Url);
        Assert.Contains("GET https://test.crm.dynamics.com/api/data/v9.2/testaccounts(accountnumber='O''Brien%20%26%20Co') HTTP/1.1", transport.Bodies[0]);
        Assert.Equal("Special", account!.Name);
    }

    [Theory]
    [InlineData("a+b", "a%2Bb")]
    [InlineData("a?b", "a%3Fb")]
    public async Task Update_ByAKeyWithAPlusOrQuestionMark_IsSentInsideBatch(string key, string encoded)
    {
        var transport = new FakeTransport { Respond = _ => BatchResponse("204 No Content") };

        await new DataverseClient(transport).Entity<TestAccount>()
            .UsingAlternateKey(x => x.AccountNumber, key)
            .Set(x => x.Name, "x")
            .UpdateAsync(TestContext.Current.CancellationToken);

        Assert.Equal("$batch", Assert.Single(transport.Requests).Url);
        Assert.Contains($"PATCH {BaseUrl}/testaccounts(accountnumber='{encoded}') HTTP/1.1", transport.Bodies[0]);
        Assert.Contains("If-Match: *", transport.Bodies[0]);
    }

    [Fact]
    public async Task Upsert_InsideBatch_ReportsCreationAndTheEntityUrl()
    {
        var url = $"{BaseUrl}/testaccounts(accountnumber='a%26b')";
        var transport = new FakeTransport { Respond = _ => BatchResponse("201 Created", $"OData-EntityId: {url}\r\n") };

        var result = await new DataverseClient(transport).Entity<TestAccount>()
            .UsingAlternateKey(x => x.AccountNumber, "a&b")
            .Set(x => x.Name, "x")
            .UpsertAsync(ct: TestContext.Current.CancellationToken);

        Assert.True(result.Created);
        Assert.Equal(url, result.EntityUrl);
        Assert.Null(result.Id);
    }

    [Fact]
    public async Task AFailureInsideBatch_ThrowsTheSameExceptionAsADirectRequest()
    {
        var transport = new FakeTransport
        {
            Respond = _ => BatchResponse("404 Not Found", "", """{"error":{"code":"0x80060891","message":"A record with the specified key values does not exist in account entity"}}""")
        };

        var ex = await Assert.ThrowsAsync<DataverseException>(() => new DataverseClient(transport).Entity<TestAccount>()
            .UsingAlternateKey(x => x.AccountNumber, "a&b")
            .DeleteAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("0x80060891", ex.ErrorCode);
    }

    [Theory]
    [InlineData("a b", "a%20b")]
    [InlineData("o'b", "o''b")]
    [InlineData("a#b", "a%23b")]
    [InlineData("50%", "50%25")]
    public async Task Retrieve_ByAKeyIisAccepts_IsSentDirectly(string key, string encoded)
    {
        var transport = new FakeTransport { Respond = _ => DataverseResponse.FromJson(new JsonObject { ["name"] = "x" }) };

        await new DataverseClient(transport).Entity<TestAccount>()
            .UsingAlternateKey(x => x.AccountNumber, key)
            .RetrieveAsync(TestContext.Current.CancellationToken);

        Assert.Equal($"testaccounts(accountnumber='{encoded}')", Assert.Single(transport.Requests).Url);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void AKeyDataverseCannotAddress_IsRejectedWhenSet(string key)
    {
        var builder = new DataverseClient(new FakeTransport()).Entity<TestAccount>();

        var ex = Assert.Throws<ArgumentException>(() => builder.UsingAlternateKey(x => x.AccountNumber, key));

        Assert.Contains("cannot be addressed", ex.Message);
    }

    [Fact]
    public void NeedsBatch_LooksOnlyAtThePath()
    {
        // An encoded & in a query string (e.g. a filter value) is fine; only the path is checked.
        Assert.False(KeyedRequestSender.NeedsBatch(new DataverseRequest(HttpMethod.Get, "testaccounts?$filter=name eq 'a%26b'")));
        Assert.True(KeyedRequestSender.NeedsBatch(new DataverseRequest(HttpMethod.Get, "testaccounts(accountnumber='a%26b')?$select=name")));
        Assert.False(KeyedRequestSender.NeedsBatch(new DataverseRequest(HttpMethod.Get, $"{BaseUrl}/testaccounts(accountnumber='a%26b')")));
    }

    [Fact]
    public async Task SetLookupByAlternateKey_WritesTheKeyUnencoded()
    {
        // A bind is in the JSON body, which Dataverse does not URL-decode: "%20" there would be looked up literally.
        var transport = new FakeTransport { Respond = _ => DataverseResponse.Empty(entityUrl: $"{BaseUrl}/testcontacts({Guid.NewGuid()})") };

        await new DataverseClient(transport).Entity<TestContact>()
            .SetLookupByAlternateKey("parentcustomerid_account", "accounts", "accountnumber", "O'Brien & Co 50%")
            .CreateAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/accounts(accountnumber='O''Brien & Co 50%')",
            (string?)Assert.Single(transport.Requests).Payload!["parentcustomerid_account@odata.bind"]);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a#b")]
    [InlineData("a?b")]
    public void SetLookupByAlternateKey_WithAKeyDataverseCannotBind_IsRejected(string key)
    {
        var builder = new DataverseClient(new FakeTransport()).Entity<TestContact>();

        var ex = Assert.Throws<ArgumentException>(() => builder.SetLookupByAlternateKey("parentcustomerid_account", "accounts", "accountnumber", key));

        Assert.Contains("cannot be used to bind", ex.Message);
    }

    // A $batch response with one top-level part. A single write goes in a changeset; the parser reads both shapes.
    private static DataverseResponse BatchResponse(string statusLine, string innerHeaders = "", string body = "")
    {
        var part = "Content-Type: application/http\r\nContent-Transfer-Encoding: binary\r\n\r\n"
                   + $"HTTP/1.1 {statusLine}\r\n"
                   + (body.Length > 0 ? "Content-Type: application/json; odata.metadata=minimal\r\n" : "")
                   + innerHeaders + "\r\n" + body + "\r\n";
        var text = $"--batchresponse_1\r\n{part}--batchresponse_1--\r\n";

        // The batch itself answers 200; the part carries the operation's own status, which is what the sender reads.
        return new DataverseResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(text, Encoding.UTF8, "multipart/mixed")
        });
    }

    private sealed class FakeTransport : IDataverseTransport
    {
        public string BaseUrl => KeyedRequestSenderTests.BaseUrl;
        public List<DataverseRequest> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];
        public Func<DataverseRequest, DataverseResponse> Respond { get; init; } = _ => DataverseResponse.Empty();

        public Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            Bodies.Add(request.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult());
            return Task.FromResult(Respond(request));
        }
    }
}
