using System.Text.Json.Serialization;

namespace Dataverse.Fluent.IntegrationTests;

/// <summary>
/// Lookups mapped with <see cref="DataverseLookupAttribute"/>: written as <c>@odata.bind</c> by <c>SetFrom</c>,
/// <c>Set</c> and batches, read back as <c>_x_value</c>, cleared with <c>null</c>; and a retrieved record with an
/// expanded relation can be written back without sending the related record.
/// </summary>
[Collection("Dataverse Integration Tests")]
public class LookupMappingTests : IClassFixture<DataverseTestFixture>, IAsyncDisposable
{
    private readonly DataverseTestFixture _fixture;
    private readonly List<Guid> _accountIds = [];
    private readonly List<Guid> _contactIds = [];

    public LookupMappingTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    [DataverseEntity("account", "accounts")]
    public class LookupAccount
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        public Guid AccountId { get; set; }

        public string Name { get; set; } = string.Empty;

        [DataverseLookup("primarycontactid", "contacts")]
        public Guid? PrimaryContactId { get; set; }

        [JsonPropertyName("primarycontactid")]
        public LookupContact? PrimaryContact { get; set; }
    }

    [DataverseEntity("contact", "contacts")]
    public class LookupContact
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        public Guid ContactId { get; set; }

        public string LastName { get; set; } = string.Empty;

        // The polymorphic customer lookup, written through its account navigation property.
        [DataverseLookup("parentcustomerid", "accounts", NavigationProperty = "parentcustomerid_account")]
        public Guid? ParentAccountId { get; set; }
    }

    private async Task<Guid> CreateContactAsync(string lastName)
    {
        var id = (await _fixture.Client.Entity<LookupContact>().Set(c => c.LastName, lastName).CreateAsync()).Id!.Value;
        _contactIds.Add(id);
        return id;
    }

    private async Task<Guid> CreateAccountAsync(LookupAccount account)
    {
        var id = (await _fixture.Client.Entity<LookupAccount>().SetFrom(account).CreateAsync()).Id!.Value;
        _accountIds.Add(id);
        return id;
    }

    private Task<LookupAccount?> RetrieveAccountAsync(Guid id)
        => _fixture.Client.Entity<LookupAccount>(id)
            .SelectAll()
            .Expand<LookupContact>("primarycontactid", e => e.Select(c => c.LastName))
            .RetrieveAsync();

    [Fact]
    public async Task SetFrom_WritesTheLookupAsABind_AndItReadsBackAsTheValue()
    {
        var lastName = $"LookupBind{Guid.NewGuid():N}";
        var contactId = await CreateContactAsync(lastName);

        var accountId = await CreateAccountAsync(new LookupAccount { Name = $"Lookup Bind {Guid.NewGuid()}", PrimaryContactId = contactId });

        var account = await RetrieveAccountAsync(accountId);
        Assert.Equal(contactId, account!.PrimaryContactId);
        Assert.Equal(lastName, account.PrimaryContact!.LastName);
    }

    [Fact]
    public async Task SetFrom_ARetrievedRecordWithAnExpandedRelation_UpdatesWithoutSendingTheRelation()
    {
        var lastName = $"LookupRoundTrip{Guid.NewGuid():N}";
        var contactId = await CreateContactAsync(lastName);
        var accountId = await CreateAccountAsync(new LookupAccount { Name = $"Lookup RoundTrip {Guid.NewGuid()}", PrimaryContactId = contactId });

        var retrieved = (await RetrieveAccountAsync(accountId))!;
        Assert.NotNull(retrieved.PrimaryContact);
        retrieved.Name += " (renamed)";

        await _fixture.Client.Entity<LookupAccount>(accountId).SetFrom(retrieved).UpdateAsync();

        var updated = (await RetrieveAccountAsync(accountId))!;
        Assert.Equal(retrieved.Name, updated.Name);
        Assert.Equal(contactId, updated.PrimaryContactId);
        var contacts = await _fixture.Client.Query<LookupContact>().Where(c => c.LastName == lastName).GetListAsync();
        Assert.Single(contacts); // the expanded contact was not sent back and duplicated
    }

    [Fact]
    public async Task Set_LookupToNull_ClearsIt()
    {
        var contactId = await CreateContactAsync($"LookupClear{Guid.NewGuid():N}");
        var accountId = await CreateAccountAsync(new LookupAccount { Name = $"Lookup Clear {Guid.NewGuid()}", PrimaryContactId = contactId });

        await _fixture.Client.Entity<LookupAccount>(accountId).Set(a => a.PrimaryContactId, null).UpdateAsync();

        Assert.Null((await RetrieveAccountAsync(accountId))!.PrimaryContactId);
    }

    [Fact]
    public async Task Lookup_WithACustomNavigationProperty_IsWrittenThroughIt()
    {
        var accountId = await CreateAccountAsync(new LookupAccount { Name = $"Lookup Nav {Guid.NewGuid()}" });

        var contactId = (await _fixture.Client.Entity<LookupContact>()
            .SetFrom(new LookupContact { LastName = $"LookupNav{Guid.NewGuid():N}", ParentAccountId = accountId })
            .CreateAsync()).Id!.Value;
        _contactIds.Add(contactId);

        var contact = await _fixture.Client.Entity<LookupContact>(contactId).Select(c => c.ParentAccountId).RetrieveAsync();
        Assert.Equal(accountId, contact!.ParentAccountId);
    }

    [Fact]
    public async Task Batch_BuilderRequests_CarryLookupBinds()
    {
        var contactId = await CreateContactAsync($"LookupBatch{Guid.NewGuid():N}");
        var accountName = $"Lookup Batch {Guid.NewGuid()}";

        var result = await _fixture.Client.Batch()
            .Add(_fixture.Client.Entity<LookupAccount>()
                .SetFrom(new LookupAccount { Name = accountName, PrimaryContactId = contactId })
                .ToCreateRequest())
            .ExecuteAsync();

        if (result.Results.ElementAtOrDefault(0)?.EntityId is { } created)
            _accountIds.Add(created);

        Assert.True(result.AllSucceeded, string.Join(" | ", result.Errors.Select(e => $"{(int)e.StatusCode} {e.Body}")));
        var account = (await RetrieveAccountAsync(result.Results[0].EntityId!.Value))!;
        Assert.Equal(accountName, account.Name);
        Assert.Equal(contactId, account.PrimaryContactId);
    }

    public async ValueTask DisposeAsync()
    {
        // Contacts first: deleting an account can cascade to the contacts it is the parent customer of.
        foreach (var id in _contactIds)
        {
            try { await _fixture.Client.Entity<LookupContact>(id).DeleteAsync(); }
            catch { /* already deleted by cascade, or cleanup failed */ }
        }

        foreach (var id in _accountIds)
        {
            try { await _fixture.Client.Entity<LookupAccount>(id).DeleteAsync(); }
            catch { /* ignore cleanup errors */ }
        }

        GC.SuppressFinalize(this);
    }
}
