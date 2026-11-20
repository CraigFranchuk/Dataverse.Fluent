using System.Text.Json.Serialization;
using Dataverse.Fluent.IntegrationTests.Models;

namespace Dataverse.Fluent.IntegrationTests;

// Contact.parentcustomerid (account or contact) and ownerid (systemuser or team) are polymorphic lookups on every
// environment. Records created here are deleted by the fixture when the run ends (see RecordTrackingTransport).
[Collection("Dataverse Integration Tests")]
public class PolymorphicLookupTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public PolymorphicLookupTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_WithAReference_AndRetrieve_ReturnsTheTableAndId()
    {
        var ct = TestContext.Current.CancellationToken;
        var accountId = await CreateAccountAsync(ct);
        var me = await _fixture.Client.Function<ActionAndFunctionTests.WhoAmIResponse>("WhoAmI").ExecuteAsync(ct);

        var created = await _fixture.Client.Entity<CustomerContact>()
            .SetFrom(new CustomerContact { LastName = Name("Create"), ParentCustomerId = new DataverseReference("account", accountId) })
            .CreateAsync(ct);

        var contact = await _fixture.Client.Entity<CustomerContact>(created.Id).RetrieveAsync(ct);
        Assert.Equal(new DataverseReference("account", accountId), contact!.ParentCustomerId);
        Assert.Equal(new DataverseReference("systemuser", me!.UserId), contact.OwnerId);
    }

    [Fact]
    public async Task Set_SwitchesToAnotherTable_AndNullClearsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var accountId = await CreateAccountAsync(ct);
        var parentContact = await _fixture.Client.Entity<CustomerContact>().Set(c => c.LastName, Name("Parent")).CreateAsync(ct);
        var created = await _fixture.Client.Entity<CustomerContact>()
            .SetFrom(new CustomerContact { LastName = Name("Switch"), ParentCustomerId = new DataverseReference("account", accountId) })
            .CreateAsync(ct);

        await _fixture.Client.Entity<CustomerContact>(created.Id)
            .Set(c => c.ParentCustomerId, new DataverseReference("contact", parentContact.Id!.Value))
            .UpdateAsync(ct);
        var switched = await _fixture.Client.Entity<CustomerContact>(created.Id).Select(c => c.ParentCustomerId).RetrieveAsync(ct);

        // The lookup now points at a contact; clearing writes null through the account navigation property.
        await _fixture.Client.Entity<CustomerContact>(created.Id).Set(c => c.ParentCustomerId, null).UpdateAsync(ct);
        var cleared = await _fixture.Client.Entity<CustomerContact>(created.Id).Select(c => c.ParentCustomerId).RetrieveAsync(ct);

        Assert.Equal(new DataverseReference("contact", parentContact.Id!.Value), switched!.ParentCustomerId);
        Assert.Null(cleared!.ParentCustomerId);
    }

    [Fact]
    public async Task ARetrievedRecord_CanBeWrittenBack_WithSetFrom()
    {
        var ct = TestContext.Current.CancellationToken;
        var accountId = await CreateAccountAsync(ct);
        var created = await _fixture.Client.Entity<CustomerContact>()
            .SetFrom(new CustomerContact { LastName = Name("Round Trip"), ParentCustomerId = new DataverseReference("account", accountId) })
            .CreateAsync(ct);

        var contact = await _fixture.Client.Entity<CustomerContact>(created.Id).RetrieveAsync(ct);
        contact!.LastName = Name("Round Trip Updated");
        await _fixture.Client.Entity<CustomerContact>(created.Id).SetFrom(contact).UpdateAsync(ct);

        var updated = await _fixture.Client.Entity<CustomerContact>(created.Id).RetrieveAsync(ct);
        Assert.Equal(contact.LastName, updated!.LastName);
        Assert.Equal(new DataverseReference("account", accountId), updated.ParentCustomerId);
    }

    [Fact]
    public async Task Query_FetchXml_AndBulk_ReadAndWriteReferences()
    {
        var ct = TestContext.Current.CancellationToken;
        var accountId = await CreateAccountAsync(ct);
        var label = Name("Bulk");

        var bulk = await _fixture.Client.Bulk<CustomerContact>().CreateAsync(
        [
            new CustomerContact { LastName = $"{label} 1", ParentCustomerId = new DataverseReference("account", accountId) },
            new CustomerContact { LastName = $"{label} 2", ParentCustomerId = new DataverseReference("account", accountId) }
        ], ct);
        bulk.ThrowIfAnyFailed();

        var byQuery = await _fixture.Client.Query<CustomerContact>()
            .Where(c => c.ParentCustomerId!.Id == accountId)
            .GetListAsync(ct);
        var byFetch = await _fixture.Client.FetchBuilder<CustomerContact>()
            .AddFilter(c => c.ParentCustomerId!.Id == accountId)
            .AddAttributes(c => new { c.LastName, c.ParentCustomerId })
            .ExecuteAsync(ct);

        Assert.Equal(2, byQuery.Count);
        Assert.All(byQuery, c => Assert.Equal(new DataverseReference("account", accountId), c.ParentCustomerId));
        Assert.Equal(2, byFetch.Count);
        Assert.All(byFetch, c => Assert.Equal(new DataverseReference("account", accountId), c.ParentCustomerId));

        // With the ID on the left, FetchXML sent the ID as the attribute name until 1.1.1.
        var idOnTheLeft = await _fixture.Client.FetchBuilder<CustomerContact>()
            .AddFilter(c => accountId == c.ParentCustomerId!.Id)
            .AddAttributes(c => new { c.LastName })
            .ExecuteAsync(ct);
        Assert.Equal(2, idOnTheLeft.Count);
    }

    [Fact]
    public async Task Batch_CreatesWithAReference_AndRetrievesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var accountId = await CreateAccountAsync(ct);

        var created = await _fixture.Client.Batch()
            .AddCreate(new CustomerContact { LastName = Name("Batch"), ParentCustomerId = new DataverseReference("account", accountId) })
            .ExecuteAsync(ct);
        created.ThrowIfAnyFailed();
        var id = Assert.Single(created.Results).EntityId!.Value;

        var retrieved = await _fixture.Client.Batch().AddRetrieve<CustomerContact>(id).ExecuteAsync(ct);

        var contact = Assert.Single(retrieved.Results).GetEntity<CustomerContact>();
        Assert.Equal(new DataverseReference("account", accountId), contact!.ParentCustomerId);
    }

    private async Task<Guid> CreateAccountAsync(CancellationToken ct)
        => (await _fixture.Client.Entity<Account>().Set(a => a.Name, Name("Customer")).CreateAsync(ct)).Id!.Value;

    private static string Name(string label) => $"Poly {label} {Guid.NewGuid().ToString("N")[..8]}"; // lastname: 50 characters

    [DataverseEntity("contact", "contacts")]
    private sealed class CustomerContact
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("contactid")]
        public Guid ContactId { get; set; }

        [JsonPropertyName("lastname")]
        public string? LastName { get; set; }

        [DataverseLookup("parentcustomerid")]
        [DataverseLookupTarget("account", "accounts", "parentcustomerid_account")]
        [DataverseLookupTarget("contact", "contacts", "parentcustomerid_contact")]
        public DataverseReference? ParentCustomerId { get; set; }

        [DataverseReadOnly(DataverseOperation.Create)]
        [DataverseLookup("ownerid")]
        [DataverseLookupTarget("systemuser", "systemusers", "ownerid")]
        [DataverseLookupTarget("team", "teams", "ownerid")]
        public DataverseReference? OwnerId { get; set; }
    }
}
