using System.Net;
using System.Text;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Testing.Tests;

public class FakeBatchTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ContactId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EachOperation_IsAnsweredByItsRoute()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("accounts").RespondWithCreated(AccountId);
        fake.OnPatch("contacts(*)").RespondWithNoContent();
        fake.OnGet("accounts(*)").RespondWithEntity(new Account { AccountId = AccountId, Name = "Contoso" });

        var result = await fake.CreateClient().Batch()
            .AddCreate(new Account { Name = "Contoso" })
            .AddUpdate(ContactId, new Contact { LastName = "Smith" })
            .AddRetrieve<Account>(AccountId)
            .ExecuteAsync(Ct);

        Assert.True(result.AllSucceeded);
        Assert.Equal(3, result.Results.Count);
        Assert.Equal(AccountId, result.Results[0].EntityId);
        Assert.Equal(HttpStatusCode.NoContent, result.Results[1].StatusCode);
        Assert.Equal("Contoso", result.Results[2].GetEntity<Account>()!.Name);
    }

    [Fact]
    public async Task TheBatch_IsOneRequest_AndItsOperationsAreRecorded()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("accounts").RespondWithCreated();
        fake.OnPost("contacts").RespondWithCreated();

        await fake.CreateClient().Batch()
            .AddCreate(new Account { Name = "Contoso" }, out var account)
            .AddCreate(new Contact { LastName = "Smith" }, new Dictionary<string, BatchReference> { ["parentcustomerid_account"] = account })
            .ExecuteAsync(Ct);

        var batch = Assert.Single(fake.Requests);
        Assert.True(batch.IsBatch);
        Assert.Equal(2, batch.BatchOperations.Count);
        Assert.Equal(batch.BatchOperations, fake.Operations);

        var contact = fake.Operations[1];
        Assert.Equal(HttpMethod.Post, contact.Method);
        Assert.Equal("contacts", contact.Path);
        Assert.Equal(2, contact.ContentId);
        Assert.Same(batch, contact.Batch);
        Assert.Equal("Smith", (string?)contact.Payload!["lastname"]);
        Assert.Equal("$1", (string?)contact.Payload["parentcustomerid_account@odata.bind"]);
    }

    [Fact]
    public async Task RequestOptions_AreVisibleOnEachOperation()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("accounts").RespondWithCreated();

        await fake.CreateClient()
            .WithOptions(o => o.BypassPowerAutomateFlows = true)
            .Batch()
            .AddCreate(new Account { Name = "A" })
            .AddCreate(new Account { Name = "B" })
            .ExecuteAsync(Ct);

        Assert.All(fake.Operations, op => Assert.Equal("true", op.GetHeader("MSCRM.SuppressCallbackRegistrationExpanderJob")));
    }

    [Fact]
    public async Task AFailedOperation_FailsItsChangeset_AndRollsBackTheOthers()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("accounts").RespondWithCreated();
        fake.OnPost("contacts").RespondWithError(HttpStatusCode.BadRequest, "0x80040237", "Duplicate contact");

        var result = await fake.CreateClient().Batch()
            .AddCreate(new Account { Name = "Contoso" })
            .AddCreate(new Contact { LastName = "Smith" })
            .ExecuteAsync(Ct);

        Assert.False(result.AllSucceeded);
        Assert.True(result.Results[0].WasRolledBack);
        Assert.Equal(HttpStatusCode.BadRequest, result.Results[1].StatusCode);

        var ex = Assert.Throws<BatchOperationException>(result.ThrowIfAnyFailed);
        var error = Assert.IsType<DataverseException>(Assert.Single(ex.InnerExceptions, e => e is DataverseException { ErrorCode: not null }));
        Assert.Equal("0x80040237", error.ErrorCode);
        Assert.NotNull(error.ActivityId);
    }

    [Fact]
    public async Task OperationsAfterAFailureInTheSameChangeset_AreNotRun()
    {
        var fake = new FakeDataverseTransport();
        var accounts = fake.OnPost("accounts").RespondWithError(HttpStatusCode.Forbidden);
        var contacts = fake.OnPost("contacts").RespondWithCreated();

        await fake.CreateClient().Batch()
            .AddCreate(new Account { Name = "Contoso" })
            .AddCreate(new Contact { LastName = "Smith" })
            .ExecuteAsync(Ct);

        Assert.Single(accounts.Requests);
        Assert.Empty(contacts.Requests);
        // Both were sent, so both are recorded as operations.
        Assert.Equal(2, fake.Operations.Count);
    }

    [Fact]
    public async Task WithoutContinueOnError_TheBatchStopsAtTheFirstFailedChangeset()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("accounts").RespondWithError(HttpStatusCode.BadRequest, "0x1", "First fails");
        var contacts = fake.OnPost("contacts").RespondWithCreated();

        var result = await fake.CreateClient().Batch()
            .AddCreate(new Account { Name = "Contoso" })
            .NewChangeset()
            .AddCreate(new Contact { LastName = "Smith" })
            .ExecuteAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, result.Results[0].StatusCode);
        Assert.False(result.Results[1].IsSuccess);
        Assert.Contains("No response was returned", result.Results[1].Body, StringComparison.Ordinal);
        Assert.Empty(contacts.Requests);
    }

    [Fact]
    public async Task WithContinueOnError_LaterChangesetsRun()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("accounts").RespondWithError(HttpStatusCode.BadRequest, "0x1", "First fails");
        fake.OnPost("contacts").RespondWithCreated(ContactId);

        var result = await fake.CreateClient().Batch()
            .ContinueOnError()
            .AddCreate(new Account { Name = "Contoso" })
            .NewChangeset()
            .AddCreate(new Contact { LastName = "Smith" })
            .ExecuteAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, result.Results[0].StatusCode);
        Assert.True(result.Results[1].IsSuccess);
        Assert.Equal(ContactId, result.Results[1].EntityId);
        Assert.Equal("odata.continue-on-error", Assert.Single(fake.Requests).GetHeader("Prefer"));
    }

    [Fact]
    public async Task AFailedStandaloneRetrieve_IsReported()
    {
        var fake = new FakeDataverseTransport();
        fake.OnGet("accounts(*)").RespondWithError(HttpStatusCode.NotFound, "0x80040217", "Not found");

        var result = await fake.CreateClient().Batch().AddRetrieve<Account>(AccountId).ExecuteAsync(Ct);

        var operation = Assert.Single(result.Results);
        Assert.Equal(HttpStatusCode.NotFound, operation.StatusCode);
        Assert.Contains("0x80040217", operation.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnmatchedOperation_ThrowsNamingIt()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("accounts").RespondWithCreated();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fake.CreateClient().Batch()
            .AddCreate(new Account { Name = "Contoso" })
            .AddDelete<Contact>(ContactId)
            .ExecuteAsync(Ct));

        Assert.Contains($"DELETE contacts({ContactId}) (operation 2 of a $batch)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARouteForTheBatch_AnswersItInstead()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("$batch").RespondWithError(HttpStatusCode.ServiceUnavailable, message: "Down for maintenance");

        var ex = await Assert.ThrowsAsync<DataverseException>(
            () => fake.CreateClient().Batch().AddCreate(new Account { Name = "Contoso" }).ExecuteAsync(Ct));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        // The operations are still recorded, though no route answered them.
        Assert.Equal("accounts", Assert.Single(fake.Operations).Path);
    }

    [Fact]
    public async Task ADeepInsertAndAnAssociation_InABatch_AreParsed()
    {
        var fake = new FakeDataverseTransport();
        fake.OnPost("accounts").RespondWithCreated(AccountId);
        fake.OnPost("accounts(*)/contact_customer_accounts/$ref").RespondWithNoContent();

        var result = await fake.CreateClient().Batch()
            .AddDeepInsert(new Account { Name = "Parent" }, "contact_customer_accounts", new Contact { LastName = "Child" })
            .AddAssociate("accounts", AccountId, "contact_customer_accounts", "contacts", ContactId)
            .ExecuteAsync(Ct);

        Assert.True(result.AllSucceeded);
        var deepInsert = fake.Operations[0];
        Assert.Equal("Child", (string?)deepInsert.Payload!["contact_customer_accounts"]![0]!["lastname"]);
        Assert.Contains($"contacts({ContactId})", (string?)fake.Operations[1].Payload!["@odata.id"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyBatch_SendsNothing()
    {
        var fake = new FakeDataverseTransport();

        var result = await fake.CreateClient().Batch().ExecuteAsync(Ct);

        Assert.Empty(result.Results);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task ARawBatch_WithoutABoundary_Throws()
    {
        var fake = new FakeDataverseTransport();
        var request = new DataverseRequest(HttpMethod.Post, "$batch") { Content = new StringContent("not multipart", Encoding.UTF8) };
        request.Content.Headers.ContentType = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fake.SendAsync(request, Ct));
    }
}
