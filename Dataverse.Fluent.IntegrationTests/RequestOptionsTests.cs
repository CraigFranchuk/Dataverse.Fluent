using System.Text.Json.Serialization;
using Dataverse.Fluent.IntegrationTests.Models;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.IntegrationTests;

// The calling user needs prvBypassCustomBusinessLogic (bypass tests) and prvActOnBehalfOfAnotherUser (impersonation
// tests). An application user with the System Administrator role has both.
// Records created here are deleted by the fixture when the run ends (see RecordTrackingTransport).
[Collection("Dataverse Integration Tests")]
public class RequestOptionsTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public RequestOptionsTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BypassingBusinessLogicAndFlows_CreateUpdateAndDeleteSucceed()
    {
        var loader = _fixture.Client.WithOptions(o =>
        {
            o.BypassBusinessLogic = BusinessLogicBypass.CustomSync | BusinessLogicBypass.CustomAsync;
            o.BypassPowerAutomateFlows = true;
        });
        var ct = TestContext.Current.CancellationToken;

        var created = await loader.Entity<Account>().Set(a => a.Name, $"Test Account Bypass {Guid.NewGuid()}").CreateAsync(ct);
        await loader.Entity<Account>(created.Id).Set(a => a.Revenue, 42m).UpdateAsync(ct);
        var updated = await loader.Entity<Account>(created.Id).Select(a => a.Revenue).RetrieveAsync(ct);
        await loader.Entity<Account>(created.Id).DeleteAsync(ct);

        Assert.Equal(42m, updated!.Revenue);
    }

    [Fact]
    public async Task BypassingBusinessLogic_InABatch_AppliesToEveryOperation()
    {
        var loader = _fixture.Client.WithOptions(o => o.BypassBusinessLogic = BusinessLogicBypass.CustomSync);

        var result = await loader.Batch()
            .AddCreate(new Account { Name = $"Test Account Bypass Batch 1 {Guid.NewGuid()}" })
            .AddCreate(new Account { Name = $"Test Account Bypass Batch 2 {Guid.NewGuid()}" })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        result.ThrowIfAnyFailed();
        Assert.Equal(2, result.Results.Count);
        Assert.All(result.Results, r => Assert.True(r.IsSuccess));
    }

    [Fact]
    public async Task DetectingDuplicates_AllowsAUniqueRecord()
    {
        var strict = _fixture.Client.WithOptions(o => o.DetectDuplicates = true);

        var created = await strict.Entity<Account>()
            .Set(a => a.Name, $"Test Account Duplicate Detection {Guid.NewGuid()}")
            .CreateAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, created.Id);
    }

    [Fact]
    public async Task ImpersonatingTheCallingUser_ByObjectId_CreatesTheRecordAsThatUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var me = await _fixture.Client.Function<ActionAndFunctionTests.WhoAmIResponse>("WhoAmI").ExecuteAsync(ct);
        var user = await _fixture.Client.Entity<SystemUserRow>(me!.UserId, "systemusers")
            .Select("azureactivedirectoryobjectid")
            .RetrieveAsync(ct);
        Assert.SkipWhen(user?.ObjectId is null, "The calling user has no Microsoft Entra object ID.");

        var asMe = _fixture.Client.WithOptions(o => o.CallerObjectId = user!.ObjectId);
        var created = await asMe.Entity<Account>().Set(a => a.Name, $"Test Account Impersonation {Guid.NewGuid()}").CreateAsync(ct);
        var audit = await _fixture.Client.Entity<CreatedByRow>(created.Id, "accounts")
            .Select("_createdby_value")
            .RetrieveAsync(ct);

        Assert.Equal(me.UserId, audit!.CreatedBy);
    }

    [Fact]
    public async Task ImpersonatingAnUnknownUser_IsRejected()
    {
        // Dataverse ignores headers it does not know, so a rejection shows it read the impersonation header.
        var ct = TestContext.Current.CancellationToken;
        var byObjectId = _fixture.Client.WithOptions(o => o.CallerObjectId = Guid.NewGuid());
        var bySystemUserId = _fixture.Client.WithOptions(o => o.CallerSystemUserId = Guid.NewGuid());

        await Assert.ThrowsAsync<DataverseException>(() =>
            byObjectId.Entity<Account>().Set(a => a.Name, $"Test Account Unknown Caller {Guid.NewGuid()}").CreateAsync(ct));
        await Assert.ThrowsAsync<DataverseException>(() =>
            bySystemUserId.Entity<Account>().Set(a => a.Name, $"Test Account Unknown Caller {Guid.NewGuid()}").CreateAsync(ct));
    }

    private sealed class SystemUserRow
    {
        [JsonPropertyName("azureactivedirectoryobjectid")]
        public Guid? ObjectId { get; set; }
    }

    private sealed class CreatedByRow
    {
        [JsonPropertyName("_createdby_value")]
        public Guid? CreatedBy { get; set; }
    }
}
