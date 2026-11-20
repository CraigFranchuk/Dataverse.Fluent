using System.Net;
using System.Text.Json.Nodes;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Testing.Tests;

public class FakeDataverseTransportTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ContactId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ────────── Routing ──────────

    [Fact]
    public async Task AQuery_IsAnsweredByItsRoute_AndRecorded()
    {
        var fake = new FakeDataverseTransport();
        fake.OnGet("accounts").RespondWithEntities([new Account { AccountId = AccountId, Name = "Contoso", PrimaryContactId = ContactId }]);

        var accounts = await fake.CreateClient().Query<Account>()
            .Where(a => a.Revenue > 1000 && a.Name!.StartsWith("Con"))
            .Select(a => a.Name)
            .GetListAsync(Ct);

        var account = Assert.Single(accounts);
        Assert.Equal("Contoso", account.Name);
        Assert.Equal(ContactId, account.PrimaryContactId);

        var request = Assert.Single(fake.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("accounts", request.Path);
        Assert.Equal("revenue gt 1000 and startswith(name, 'Con')", request.Query["$filter"]);
        Assert.Equal("name", request.Query["$select"]);
        Assert.Same(request, Assert.Single(fake.Operations));
    }

    [Fact]
    public async Task APathPattern_MatchesWithWildcardsAndIgnoresCase()
    {
        var fake = new FakeDataverseTransport();
        var route = fake.OnGet("ACCOUNTS(*)").RespondWithEntity(new Account { AccountId = AccountId, Name = "Contoso" });

        var account = await fake.CreateClient().Entity<Account>(AccountId).RetrieveAsync(Ct);

        Assert.Equal("Contoso", account!.Name);
        Assert.Equal($"accounts({AccountId})", Assert.Single(route.Requests).Path);
    }

    [Fact]
    public async Task TheRouteAddedLast_Wins()
    {
        var fake = new FakeDataverseTransport();
        fake.OnGet("accounts*").RespondWithEntities([new Account { Name = "Default" }]);
        fake.OnGet("accounts").RespondWithEntities([new Account { Name = "Override" }]);

        var accounts = await fake.CreateClient().Query<Account>().GetListAsync(Ct);

        Assert.Equal("Override", Assert.Single(accounts).Name);
    }

    [Fact]
    public async Task When_NarrowsARoute()
    {
        var fake = new FakeDataverseTransport();
        fake.OnGet("accounts").RespondWithEntities([new Account { Name = "Any" }]);
        fake.OnGet("accounts").When(r => r.GetQueryOption("$filter")?.Contains("accountnumber", StringComparison.Ordinal) == true)
            .RespondWithEntities([new Account { Name = "ByNumber" }]);

        var client = fake.CreateClient();
        var byNumber = await client.Query<Account>().Where(a => a.Number == "A-1").GetListAsync(Ct);
        var any = await client.Query<Account>().GetListAsync(Ct);

        Assert.Equal("ByNumber", Assert.Single(byNumber).Name);
        Assert.Equal("Any", Assert.Single(any).Name);
    }

    [Fact]
    public async Task APredicateRoute_MatchesAnyRequestItAccepts()
    {
        var fake = new FakeDataverseTransport();
        fake.On(r => r.Path.EndsWith("WhoAmI", StringComparison.Ordinal)).RespondWithEntity(new WhoAmIResponse { UserId = ContactId });

        var me = await fake.CreateClient().Function<WhoAmIResponse>("WhoAmI").ExecuteAsync(Ct);

        Assert.Equal(ContactId, me!.UserId);
    }

    [Fact]
    public async Task AnUnmatchedRequest_ThrowsNamingTheRequestAndTheRoutes()
    {
        var fake = new FakeDataverseTransport();
        fake.OnGet("contacts").RespondWithEntities(Array.Empty<Contact>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fake.CreateClient().Query<Account>().GetListAsync(Ct));

        Assert.Contains("GET accounts", ex.Message, StringComparison.Ordinal);
        Assert.Contains("fake.OnGet(\"accounts\")", ex.Message, StringComparison.Ordinal);
        Assert.Contains("GET contacts", ex.Message, StringComparison.Ordinal);
        // The request is still recorded.
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task ARouteWithoutAResponse_Throws()
    {
        var fake = new FakeDataverseTransport();
        fake.OnDelete("accounts(*)");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fake.CreateClient().Entity<Account>(AccountId).DeleteAsync(Ct));

        Assert.Contains("has no response", ex.Message, StringComparison.Ordinal);
    }

    // ────────── Recording ──────────

    [Fact]
    public async Task AWrite_IsRecordedWithItsPayloadAndHeaders()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPatch("accounts(*)").RespondWithNoContent();

        await fake.CreateClient()
            .WithOptions(o => o.BypassBusinessLogic = BusinessLogicBypass.CustomSync)
            .Entity<Account>(AccountId)
            .Set(a => a.Name, "Fabrikam")
            .UpdateAsync(Ct);

        var request = Assert.Single(fake.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("Fabrikam", (string?)request.Payload!["name"]);
        Assert.Equal("*", request.GetHeader("if-match"));
        Assert.Equal("CustomSync", request.GetHeader("MSCRM.BypassBusinessLogicExecution"));
        Assert.Null(request.GetHeader("X-Not-Sent"));
        Assert.Null(request.ContentId);
        Assert.Null(request.Batch);
        Assert.False(request.IsBatch);
    }

    [Fact]
    public async Task TheRecordedPayload_IsACopy()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("accounts").RespondWithCreated();
        var payload = new JsonObject { ["name"] = "Contoso" };

        await fake.CreateClient().Transport.ExecuteRequestAsync(HttpMethod.Post, "accounts", payload, ct: Ct);
        payload["name"] = "Changed";

        Assert.Equal("Contoso", (string?)Assert.Single(fake.Requests).Payload!["name"]);
    }

    [Fact]
    public async Task AnAbsoluteUrl_IsRecordedRelativeToTheBaseUrl()
    {
        var fake = new FakeDataverseTransport("https://org.crm.dynamics.com/api/data/v9.2/");
        fake.OnGet("accounts").RespondWithEntities(Array.Empty<Account>());

        await fake.CreateClient().Transport.ExecuteRequestAsync(
            HttpMethod.Get, "https://org.crm.dynamics.com/api/data/v9.2/accounts?$skiptoken=%3Ccookie%3E", ct: Ct);

        var request = Assert.Single(fake.Requests);
        Assert.Equal("https://org.crm.dynamics.com/api/data/v9.2", fake.BaseUrl);
        Assert.Equal("accounts", request.Path);
        Assert.Equal("<cookie>", request.Query["$skiptoken"]);
    }

    [Fact]
    public async Task ACancelledRequest_IsNotSent()
    {
        var fake = new FakeDataverseTransport();
        fake.OnGet("accounts").RespondWithEntities(Array.Empty<Account>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fake.CreateClient().Query<Account>().GetListAsync(new CancellationToken(canceled: true)));

        Assert.Empty(fake.Requests);
    }

    // ────────── Responses ──────────

    [Fact]
    public async Task QueuedResponses_AreReturnedInOrder_AndTheLastRepeats()
    {
        var fake = new FakeDataverseTransport();
        fake.OnGet("WhoAmI")
            .RespondWithEntity(new WhoAmIResponse { UserId = AccountId })
            .RespondWithEntity(new WhoAmIResponse { UserId = ContactId });

        var client = fake.CreateClient();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
            ids.Add((await client.Function<WhoAmIResponse>("WhoAmI").ExecuteAsync(Ct))!.UserId);

        Assert.Equal([AccountId, ContactId, ContactId], ids);
    }

    [Fact]
    public async Task Pages_AreFollowedThroughTheNextLink()
    {
        var fake = new FakeDataverseTransport();
        fake.OnGet("accounts")
            .RespondWithEntities([new Account { Name = "One" }], nextLink: $"{fake.BaseUrl}/accounts?$skiptoken=2")
            .RespondWithEntities([new Account { Name = "Two" }]);

        var all = await fake.CreateClient().Query<Account>().GetAllAsync(ct: Ct);

        Assert.Equal(["One", "Two"], all.Select(a => a.Name));
        Assert.Equal("2", fake.Requests[1].Query["$skiptoken"]);
    }

    [Fact]
    public async Task TheTotalCount_IsReturned()
    {
        var fake = new FakeDataverseTransport();
        fake.OnGet("accounts").RespondWithEntities([new Account { Name = "One" }], totalCount: 40);

        var (items, count) = await fake.CreateClient().Query<Account>().GetListWithCountAsync(Ct);

        Assert.Single(items);
        Assert.Equal(40, count);
    }

    [Fact]
    public async Task AnEntity_CarriesItsETag()
    {
        var fake = new FakeDataverseTransport();
        fake.OnGet("accounts(*)").RespondWithEntity(new Account { Name = "Contoso" }, etag: "W/\"12345\"");

        var result = await fake.CreateClient().Entity<Account>(AccountId).RetrieveWithMetadataAsync(Ct);

        Assert.Equal("Contoso", result.Entity!.Name);
        Assert.Equal("W/\"12345\"", result.ETag);
    }

    [Fact]
    public async Task APolymorphicLookup_IsReadBack()
    {
        var fake = new FakeDataverseTransport();
        fake.OnGet("contacts(*)").RespondWithEntity(new Contact { LastName = "Smith", ParentCustomerId = DataverseReference.To<Account>(AccountId) });

        var contact = await fake.CreateClient().Entity<Contact>(ContactId).RetrieveAsync(Ct);

        Assert.Equal(new DataverseReference("account", AccountId), contact!.ParentCustomerId);
    }

    [Fact]
    public async Task RespondWithCreated_ReturnsTheId()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("accounts").RespondWithCreated(AccountId);

        var result = await fake.CreateClient().Entity<Account>().SetFrom(new Account { Name = "Contoso" }).CreateAsync(Ct);

        Assert.Equal(AccountId, result.Id);
        Assert.Equal($"{fake.BaseUrl}/accounts({AccountId})", result.EntityUrl);
    }

    [Fact]
    public async Task RespondWithCreated_WithoutAnId_GivesEachRequestANewOne()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("accounts").RespondWithCreated();

        var client = fake.CreateClient();
        var first = await client.Entity<Account>().Set(a => a.Name, "A").CreateAsync(Ct);
        var second = await client.Entity<Account>().Set(a => a.Name, "B").CreateAsync(Ct);

        Assert.NotNull(first.Id);
        Assert.NotNull(second.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task RespondWithCreated_ForAnUpsertByAlternateKey_ReturnsTheId()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPatch("accounts(*)").RespondWithCreated(AccountId);

        var result = await fake.CreateClient().Entity<Account>()
            .UsingAlternateKey("accountnumber", "A-1")
            .Set(a => a.Name, "Contoso")
            .UpsertAsync(ct: Ct);

        Assert.Equal(AccountId, result.Id);
        Assert.Equal("accounts(accountnumber='A-1')", Assert.Single(fake.Requests).Path);
    }

    [Fact]
    public async Task ACreateReturningTheRecord_UsesRespondWithEntity()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("accounts").RespondWithEntity(new Account { AccountId = AccountId, Name = "Contoso" }, HttpStatusCode.Created);

        var result = await fake.CreateClient().Entity<Account>().Set(a => a.Name, "Contoso").ReturnRepresentation().CreateAsync(Ct);

        Assert.Equal(AccountId, result.Entity!.AccountId);
    }

    [Fact]
    public async Task RespondWithBulkCreated_ReturnsOneIdPerRecord()
    {
        var fake = new FakeDataverseTransport();
        var route = fake.OnPost("accounts/Microsoft.Dynamics.CRM.CreateMultiple").RespondWithBulkCreated();
        var accounts = Enumerable.Range(0, 250).Select(i => new Account { Name = $"Account {i}" }).ToList();

        var result = await fake.CreateClient().Bulk<Account>().ChunkSize(100).MaxDegreeOfParallelism(4).CreateAsync(accounts, Ct);

        Assert.True(result.AllSucceeded);
        Assert.Equal(250, result.Ids.Count);
        Assert.Equal(250, result.Ids.Distinct().Count());
        Assert.Equal(3, route.Requests.Count);
    }

    [Fact]
    public async Task RespondWithJson_ReturnsTheBody()
    {
        var fake = new FakeDataverseTransport();
        var body = new JsonObject { ["UserId"] = AccountId };
        fake.OnGet("WhoAmI").RespondWithJson(body);
        body["UserId"] = ContactId; // Not seen: the body was captured when the route was set up.

        var me = await fake.CreateClient().Function<WhoAmIResponse>("WhoAmI").ExecuteAsync(Ct);

        Assert.Equal(AccountId, me!.UserId);
    }

    [Fact]
    public async Task Respond_BuildsTheResponseFromTheRequest()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("accounts").Respond(r => DataverseResponse.FromEntity(
            new Account { AccountId = AccountId, Name = (string?)r.Payload!["name"] }, HttpStatusCode.Created));

        var result = await fake.CreateClient().Entity<Account>().Set(a => a.Name, "Echo").ReturnRepresentation().CreateAsync(Ct);

        Assert.Equal("Echo", result.Entity!.Name);
    }

    // ────────── Failures ──────────

    [Fact]
    public async Task RespondWithError_ThrowsADataverseException()
    {
        var fake = new FakeDataverseTransport();
        fake.OnGet("accounts(*)").RespondWithError(HttpStatusCode.NotFound, "0x80040217", "Account does not exist");

        var ex = await Assert.ThrowsAsync<DataverseException>(() => fake.CreateClient().Entity<Account>(AccountId).RetrieveAsync(Ct));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("0x80040217", ex.ErrorCode);
        Assert.Contains("Account does not exist", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(ex.ActivityId);
    }

    [Fact]
    public async Task RespondWithError_WithoutAMessage_DescribesTheStatus()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPatch("accounts(*)").RespondWithError(HttpStatusCode.PreconditionFailed);

        var ex = await Assert.ThrowsAsync<DataverseException>(
            () => fake.CreateClient().Entity<Account>(AccountId).Set(a => a.Name, "X").UpdateAsync(Ct));

        Assert.Equal(HttpStatusCode.PreconditionFailed, ex.StatusCode);
        Assert.Null(ex.ErrorCode);
        Assert.Contains("412", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Throw_SimulatesAFailureThatNeverReachedDataverse()
    {
        var fake = new FakeDataverseTransport();
        fake.OnGet("accounts").Throw(new HttpRequestException("Connection refused"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => fake.CreateClient().Query<Account>().GetListAsync(Ct));

        Assert.Equal("Connection refused", ex.Message);
    }

    [Fact]
    public async Task AFailedBulkChunk_IsReported()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("accounts/Microsoft.Dynamics.CRM.CreateMultiple")
            .RespondWithBulkCreated()
            .RespondWithError(HttpStatusCode.BadRequest, "0x80040237", "Duplicate");

        var accounts = Enumerable.Range(0, 4).Select(i => new Account { Name = $"Account {i}" }).ToList();
        var result = await fake.CreateClient().Bulk<Account>().ChunkSize(2).ContinueOnError().CreateAsync(accounts, Ct);

        Assert.Equal([BulkChunkStatus.Succeeded, BulkChunkStatus.Failed], result.Chunks.Select(c => c.Status));
        Assert.Equal("0x80040237", Assert.IsType<DataverseException>(result.Chunks[1].Error).ErrorCode);
    }

    // ────────── Sales and other layers ──────────

    [Fact]
    public async Task RawRequests_ThroughTheTransport_AreAnswered()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("WinOpportunity").RespondWithNoContent();

        await fake.CreateClient().Transport.ExecuteRequestAsync(
            HttpMethod.Post, "WinOpportunity", new JsonObject { ["Status"] = 3 }, ct: Ct);

        Assert.Equal(3, (int?)Assert.Single(fake.Requests).Payload!["Status"]);
    }
}
