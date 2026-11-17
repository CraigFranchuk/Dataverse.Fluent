using System.Text.Json.Serialization;
using Dataverse.Fluent.IntegrationTests.Models;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.IntegrationTests;

/// <summary>
/// Integration tests for Dataverse Actions and Functions.
/// Tests real API calls to validate the Action and Function builders.
/// </summary>
[Collection("Dataverse Integration Tests")]
public class ActionAndFunctionTests : IAsyncDisposable
{
    private readonly DataverseTestFixture _fixture;
    private readonly List<Guid> _createdAccountIds = new();
    private readonly List<Guid> _createdOpportunityIds = new();

    public ActionAndFunctionTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region Unbound Functions

    [Fact]
    public async Task UnboundFunction_WhoAmI_ReturnsCurrentUserInfo()
    {
        // WhoAmI is a standard Dataverse function that returns current user information
        var response = await _fixture.Client
            .Function<WhoAmIResponse>("WhoAmI")
            .ExecuteAsync();

        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.UserId);
        Assert.NotEqual(Guid.Empty, response.BusinessUnitId);
        Assert.NotEqual(Guid.Empty, response.OrganizationId);
    }

    [Fact]
    public async Task UnboundFunction_RetrieveVersion_ReturnsDataverseVersion()
    {
        // RetrieveVersion returns the Dataverse platform version
        var response = await _fixture.Client
            .Function<RetrieveVersionResponse>("RetrieveVersion")
            .ExecuteAsync();

        Assert.NotNull(response);
        Assert.NotNull(response.Version);
        Assert.NotEmpty(response.Version);
        // Version format should be like "9.2.24011.00167"
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", response.Version);
    }

    #endregion

    #region Bound Functions

    [Fact]
    public async Task BoundFunction_RetrieveUserPrivileges_ReturnsCallerPrivileges()
    {
        // A bound function is addressed through its record: systemusers(<id>)/Microsoft.Dynamics.CRM.RetrieveUserPrivileges
        var me = await _fixture.Client.Function<WhoAmIResponse>("WhoAmI").ExecuteAsync();

        var response = await _fixture.Client
            .ForEntity<object>(me!.UserId, "systemusers")
            .Function<RetrieveUserPrivilegesResponse>("RetrieveUserPrivileges")
            .ExecuteAsync();

        Assert.NotNull(response);
        // The integration tests read accounts, so the caller holds prvReadAccount.
        Assert.Contains(response.RolePrivileges, p => p.PrivilegeName == "prvReadAccount");
        Assert.All(response.RolePrivileges, p => Assert.NotEqual(Guid.Empty, p.PrivilegeId));
    }

    #endregion

    #region Functions with entity-reference parameters

    [Fact]
    public async Task UnboundFunction_CalculateRollupField_RecalculatesRollup()
    {
        var account = (await _fixture.Client.Entity<Account>()
            .Set("name", $"Test Account Rollup {Guid.NewGuid()}")
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdAccountIds.Add(account.AccountId);

        // CalculateRollupField is a function; its entity-reference Target is sent as a JSON parameter alias:
        // CalculateRollupField(Target=@p1,FieldName=@p2)?@p1={"@odata.id":"accounts(<id>)"}&@p2='opendeals'
        var response = await _fixture.Client
            .Function<RollupAccount>("CalculateRollupField")
            .WithParameter("Target", new Dictionary<string, string> { ["@odata.id"] = $"accounts({account.AccountId})" })
            .WithParameter("FieldName", "opendeals")
            .ExecuteAsync();

        Assert.NotNull(response);
        Assert.Equal(account.AccountId, response.AccountId);
        Assert.Equal(0, response.OpenDeals);      // the new account has no opportunities
        Assert.Equal(1, response.OpenDealsState); // 1 = calculated
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task Function_WithInvalidFunction_ThrowsNotFound()
    {
        var exception = await Assert.ThrowsAsync<DataverseException>(async () =>
        {
            await _fixture.Client
                .Function<WhoAmIResponse>("NonExistentFunction")
                .ExecuteAsync();
        });

        Assert.Equal(System.Net.HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task Function_WithCancelledToken_ThrowsTaskCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await _fixture.Client
                .Function<WhoAmIResponse>("WhoAmI")
                .ExecuteAsync(cts.Token);
        });
    }

    #endregion

    #region POCO Models for Actions and Functions

    public class WhoAmIResponse
    {
        [JsonPropertyName("UserId")]
        public Guid UserId { get; set; }

        [JsonPropertyName("BusinessUnitId")]
        public Guid BusinessUnitId { get; set; }

        [JsonPropertyName("OrganizationId")]
        public Guid OrganizationId { get; set; }
    }

    public class RetrieveVersionResponse
    {
        [JsonPropertyName("Version")]
        public string? Version { get; set; }
    }

    public class RetrieveUserPrivilegesResponse
    {
        [JsonPropertyName("RolePrivileges")]
        public List<RolePrivilege> RolePrivileges { get; set; } = [];
    }

    public class RolePrivilege
    {
        [JsonPropertyName("PrivilegeId")]
        public Guid PrivilegeId { get; set; }

        [JsonPropertyName("PrivilegeName")]
        public string? PrivilegeName { get; set; }

        [JsonPropertyName("Depth")]
        public string? Depth { get; set; }
    }

    // CalculateRollupField returns the recalculated record.
    public class RollupAccount
    {
        [JsonPropertyName("accountid")]
        public Guid AccountId { get; set; }

        [JsonPropertyName("opendeals")]
        public int? OpenDeals { get; set; }

        [JsonPropertyName("opendeals_state")]
        public int? OpenDealsState { get; set; }
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        // Clean up created accounts
        foreach (var accountId in _createdAccountIds)
        {
            try
            {
                await _fixture.Client.Entity<Account>()
                    .WithId(accountId)
                    .DeleteAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        // Clean up created opportunities
        foreach (var opportunityId in _createdOpportunityIds)
        {
            try
            {
                // Note: Opportunity cleanup would go here if we create any
                // await _fixture.Client.Entity<Opportunity>()
                //     .WithId(opportunityId)
                //     .DeleteAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
