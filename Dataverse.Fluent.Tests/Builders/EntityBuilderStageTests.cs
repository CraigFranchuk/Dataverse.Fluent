using System.Net;
using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent.Tests.Builders;

/// <summary>
/// Tests the typed stages of <see cref="EntityBuilder{T}"/>: reads continue on <see cref="EntityReadBuilder{T}"/>,
/// writes on <see cref="EntityWriteBuilder{T}"/>, and neither stage offers the other's options or terminals.
/// </summary>
public class EntityBuilderStageTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static (DataverseClient Client, TestHttpMessageHandler Handler) CreateClient(HttpStatusCode status = HttpStatusCode.NoContent)
    {
        var handler = new TestHttpMessageHandler();
        handler.AddResponse(_ => true, new HttpResponseMessage(status) { Content = new StringContent(status == HttpStatusCode.NoContent ? "" : "{}") });
        return (new DataverseClient(BaseUrl, () => Task.FromResult("t"), new HttpClient(handler)), handler);
    }

    private static string[] PublicMethodNames<TBuilder>() =>
        typeof(TBuilder).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Distinct()
            .ToArray();

    [Theory]
    [InlineData("Set")]
    [InlineData("SetFrom")]
    [InlineData("SetFromExcept")]
    [InlineData("SetLookup")]
    [InlineData("WithETag")]
    [InlineData("ReturnRepresentation")]
    [InlineData("ReturnMinimal")]
    [InlineData("CreateAsync")]
    [InlineData("UpdateAsync")]
    [InlineData("UpsertAsync")]
    [InlineData("DeleteAsync")]
    public void ReadStage_HasNoWriteMembers(string member)
    {
        Assert.DoesNotContain(member, PublicMethodNames<EntityReadBuilder<TestAccount>>());
    }

    [Theory]
    [InlineData("Select")]
    [InlineData("SelectAll")]
    [InlineData("Expand")]
    [InlineData("IncludeFormattedValues")]
    [InlineData("RetrieveAsync")]
    [InlineData("RetrieveWithMetadataAsync")]
    [InlineData("DeleteAsync")]
    public void WriteStage_HasNoReadMembers(string member)
    {
        Assert.DoesNotContain(member, PublicMethodNames<EntityWriteBuilder<TestAccount>>());
    }

    [Fact]
    public async Task Root_ReadCallsWithDiscardedResults_StillApplyToRetrieve()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK);

        var entity = client.Entity<TestAccount>(AccountId);
        entity.Select("name");
        entity.Expand("primarycontactid");
        await entity.RetrieveAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("name", request.QueryOption("$select"));
        Assert.Equal("primarycontactid", request.QueryOption("$expand"));
    }

    [Fact]
    public async Task Root_WriteCallsWithDiscardedResults_StillApplyToTheWrite()
    {
        var (client, handler) = CreateClient();

        var entity = client.Entity<TestAccount>(AccountId);
        entity.Set("name", "Contoso");
        await entity.Set("revenue", 10m).UpdateAsync(TestContext.Current.CancellationToken);

        var json = Assert.Single(handler.Requests).Json;
        Assert.Equal("Contoso", json.GetProperty("name").GetString());
        Assert.Equal(10m, json.GetProperty("revenue").GetDecimal());
    }

    [Fact]
    public async Task Target_CanBeSetAfterEnteringTheReadStage()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK);

        await client.Entity<TestAccount>()
            .Select(a => a.Name)
            .UsingAlternateKey(a => a.AccountNumber, "ACC-001")
            .RetrieveAsync(TestContext.Current.CancellationToken);

        Assert.Equal($"{BaseUrl}/testaccounts(accountnumber='ACC-001')?$select=name", Assert.Single(handler.Requests).DecodedUrl);
    }

    [Fact]
    public async Task Target_CanBeSetAfterEnteringTheWriteStage()
    {
        var (client, handler) = CreateClient();

        await client.Entity<TestAccount>()
            .Set(a => a.Name, "Contoso")
            .WithId(AccountId)
            .UpdateAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal($"{BaseUrl}/testaccounts({AccountId})", request.DecodedUrl);
    }

    [Fact]
    public async Task CreateAsync_WithAlternateKey_Throws_AndSendsNoRequest()
    {
        var (client, handler) = CreateClient();

        var write = client.Entity<TestAccount>()
            .UsingAlternateKey("accountnumber", "ACC-001")
            .Set("name", "Contoso");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => write.CreateAsync(TestContext.Current.CancellationToken));
        Assert.Contains("UpsertAsync", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DeleteAsync_WithoutTarget_Throws()
    {
        var (client, handler) = CreateClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Entity<TestAccount>().DeleteAsync(TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }
}
