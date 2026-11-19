using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent.Tests.Builders;

/// <summary>
/// Tests that <see cref="EntityBuilder{T}.SetFrom(T)"/> and its overloads exclude
/// [DataverseReadOnly] properties according to the operation the builder finally executes.
/// </summary>
public class EntityBuilderSetFromTests
{
    [EntitySet("testaccounts")]
    private class ReadOnlyRulesAccount
    {
        [DataverseReadOnly] // Create (the default)
        [JsonPropertyName("accountid")]
        public Guid? AccountId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("revenue")]
        public decimal? Revenue { get; set; }

        [DataverseReadOnly(DataverseOperation.Update)]
        [JsonPropertyName("accountnumber")]
        public string? AccountNumber { get; set; }

        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("createdon")]
        public DateTime? CreatedOn { get; set; }
    }

    private static ReadOnlyRulesAccount Sample() => new()
    {
        AccountId = Guid.NewGuid(),
        Name = "Contoso",
        Revenue = 1000m,
        AccountNumber = "ACC-001",
        CreatedOn = DateTime.UtcNow
    };

    [EntitySet("testaccounts")]
    private class LookupAccount
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [DataverseLookup("primarycontactid", "contacts")]
        public Guid? PrimaryContactId { get; set; }

        [DataverseLookup("parentaccountid", "accounts")]
        public Guid? ParentAccountId { get; set; }
    }

    private static LookupAccount LookupSample() => new()
    {
        Name = "Contoso",
        PrimaryContactId = Guid.NewGuid(),
        ParentAccountId = Guid.NewGuid()
    };

    [Fact]
    public async Task SetFromExcept_ExcludesALookup()
    {
        // A lookup is written as "primarycontactid@odata.bind", not under the "_primarycontactid_value" name it is
        // read by, so excluding it by that name left it in the payload.
        var sent = await SentPropertiesAsync<LookupAccount>(b => b.WithId(Guid.NewGuid())
            .SetFromExcept(LookupSample(), x => x.PrimaryContactId!).UpdateAsync());

        Assert.Equal(["name", "parentaccountid@odata.bind"], sent);
    }

    [Theory]
    [InlineData("primarycontactid")]
    [InlineData("_primarycontactid_value")]
    [InlineData("primarycontactid@odata.bind")]
    public async Task SetFrom_ExcludingALookupByName_ExcludesItsBind(string excluded)
    {
        var sent = await SentPropertiesAsync<LookupAccount>(b => b.SetFrom(LookupSample(), excluded).CreateAsync());

        Assert.Equal(["name", "parentaccountid@odata.bind"], sent);
    }

    // Runs the operation and returns the names of the properties in the request body.
    private static Task<string[]> SentPropertiesAsync(Func<EntityBuilder<ReadOnlyRulesAccount>, Task> execute)
        => SentPropertiesAsync<ReadOnlyRulesAccount>(execute);

    private static async Task<string[]> SentPropertiesAsync<T>(Func<EntityBuilder<T>, Task> execute)
    {
        var handler = new TestHttpMessageHandler();
        var client = new DataverseClient("https://test.crm.dynamics.com/api/data/v9.2", () => Task.FromResult("t"), new HttpClient(handler));

        var body = string.Empty;
        handler.AddResponse(
            req => { body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult(); return true; },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await execute(client.Entity<T>());

        using var json = JsonDocument.Parse(body);
        return json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
    }

    [Fact]
    public async Task SetFrom_OnCreate_ExcludesCreateReadOnlyProperties()
    {
        var sent = await SentPropertiesAsync(b => b.SetFrom(Sample()).CreateAsync());

        Assert.Equal(["accountnumber", "name", "revenue"], sent);
    }

    // Operations that can update a record leave out properties marked for either operation: a
    // create-only marker (e.g. on statecode) must not send a default value over an existing record.

    [Fact]
    public async Task SetFrom_OnUpdate_ExcludesPropertiesReadOnlyForEitherOperation()
    {
        var sent = await SentPropertiesAsync(b => b.WithId(Guid.NewGuid()).SetFrom(Sample()).UpdateAsync());

        Assert.Equal(["name", "revenue"], sent);
    }

    [Theory]
    [InlineData(UpsertConflictResolution.Upsert)]
    [InlineData(UpsertConflictResolution.PreventCreate)]
    [InlineData(UpsertConflictResolution.IfUnmodifiedSince)]
    public async Task SetFrom_OnUpsertThatCanUpdate_ExcludesPropertiesReadOnlyForEitherOperation(UpsertConflictResolution resolution)
    {
        var sent = await SentPropertiesAsync(b => b.WithId(Guid.NewGuid()).WithETag("W/\"1\"").SetFrom(Sample())
            .UpsertAsync(resolution));

        Assert.Equal(["name", "revenue"], sent);
    }

    [Fact]
    public async Task SetFrom_OnUpsertPreventUpdate_AppliesCreateRules()
    {
        var sent = await SentPropertiesAsync(b => b.WithId(Guid.NewGuid()).SetFrom(Sample())
            .UpsertAsync(UpsertConflictResolution.PreventUpdate));

        Assert.Equal(["accountnumber", "name", "revenue"], sent);
    }

    [Fact]
    public async Task SetFrom_WithExcludeReadOnlyFalse_SendsEveryProperty()
    {
        var sent = await SentPropertiesAsync(b => b.SetFrom(Sample(), excludeReadOnly: false).CreateAsync());

        Assert.Equal(["accountid", "accountnumber", "createdon", "name", "revenue"], sent);
    }

    [Fact]
    public async Task Set_AfterSetFrom_SendsTheExplicitValue_EvenForAReadOnlyProperty()
    {
        var sent = await SentPropertiesAsync(b => b.SetFrom(Sample()).Set(x => x.CreatedOn, DateTime.UtcNow).CreateAsync());

        Assert.Equal(["accountnumber", "createdon", "name", "revenue"], sent);
    }

    [Fact]
    public async Task SetFrom_WithExcludedNames_AlsoExcludesReadOnlyProperties()
    {
        var sent = await SentPropertiesAsync(b => b.SetFrom(Sample(), "revenue").CreateAsync());

        Assert.Equal(["accountnumber", "name"], sent);
    }

    [Fact]
    public async Task SetFromExcept_AlsoExcludesReadOnlyProperties()
    {
        var sent = await SentPropertiesAsync(b => b.WithId(Guid.NewGuid()).SetFromExcept(Sample(), x => x.Revenue!).UpdateAsync());

        Assert.Equal(["name"], sent);
    }
}
