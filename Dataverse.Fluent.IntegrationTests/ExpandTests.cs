using Dataverse.Fluent.IntegrationTests.Models;

namespace Dataverse.Fluent.IntegrationTests;

[Collection("Dataverse Integration Tests")]
public class ExpandTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public ExpandTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    // ───────────────────────── QueryBuilder $expand ─────────────────────────

    [Fact]
    public async Task Query_Expand_SimpleNavigationProperty_ReturnsRelatedEntities()
    {
        // Query accounts and expand the contact_customer_accounts navigation property
        var accounts = await _fixture.Client.Query<Account>()
            .Select(a => new { a.AccountId, a.Name })
            .Expand("contact_customer_accounts")
            .Top(3)
            .GetListAsync();

        Assert.NotNull(accounts);
        // Accounts may or may not have contacts — the query should succeed regardless
    }

    [Fact]
    public async Task Query_Expand_WithNestedSelect_ReturnsFilteredProperties()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .Select(a => new { a.AccountId, a.Name })
            .Expand<Contact>("contact_customer_accounts", e => e
                .Select(c => new { c.ContactId, c.FirstName, c.LastName }))
            .Top(3)
            .GetListAsync();

        Assert.NotNull(accounts);
    }

    [Fact]
    public async Task Query_Expand_WithNestedSelectAndTop_ReturnsLimitedRelatedEntities()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .Select(a => new { a.AccountId, a.Name })
            .Expand<Contact>("contact_customer_accounts", e => e
                .Select(c => new { c.ContactId, c.FirstName, c.LastName })
                .Top(2))
            .Top(3)
            .GetListAsync();

        Assert.NotNull(accounts);
        foreach (var account in accounts)
        {
            if (account.Contacts != null)
            {
                Assert.True(account.Contacts.Count <= 2,
                    $"Expected at most 2 contacts for account {account.Name}, got {account.Contacts.Count}");
            }
        }
    }

    [Fact]
    public async Task Query_Expand_WithNestedOrderBy_ReturnsSortedRelatedEntities()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .Select(a => new { a.AccountId, a.Name })
            .Expand<Contact>("contact_customer_accounts", e => e
                .Select(c => new { c.ContactId, c.FirstName, c.LastName })
                .OrderBy(c => c.LastName))
            .Top(3)
            .GetListAsync();

        Assert.NotNull(accounts);
        foreach (var account in accounts)
        {
            if (account.Contacts != null && account.Contacts.Count > 1)
            {
                for (int i = 0; i < account.Contacts.Count - 1; i++)
                {
                    Assert.True(
                        string.Compare(account.Contacts[i].LastName, account.Contacts[i + 1].LastName, StringComparison.OrdinalIgnoreCase) <= 0,
                        $"Expected contacts sorted by last name for account {account.Name}");
                }
            }
        }
    }

    [Fact]
    public async Task Query_Expand_WithNestedFilter_ReturnsFilteredRelatedEntities()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .Select(a => new { a.AccountId, a.Name })
            .Expand<Contact>("contact_customer_accounts", e => e
                .Select(c => new { c.ContactId, c.FirstName, c.LastName, c.StateCode })
                .Where(c => c.StateCode == 0))
            .Top(3)
            .GetListAsync();

        Assert.NotNull(accounts);
        foreach (var account in accounts)
        {
            if (account.Contacts != null)
            {
                Assert.All(account.Contacts, c => Assert.Equal(0, c.StateCode));
            }
        }
    }

    [Fact]
    public async Task Query_Expand_WithAllNestedOptions_BuildsValidRequest()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .Select(a => new { a.AccountId, a.Name })
            .Expand<Contact>("contact_customer_accounts", e => e
                .Select(c => new { c.ContactId, c.FirstName, c.LastName })
                .Where(c => c.StateCode == 0)
                .OrderBy(c => c.LastName)
                .Top(5))
            .Where(a => a.StateCode == 0)
            .Top(3)
            .GetListAsync();

        Assert.NotNull(accounts);
    }

    // ───────────────────────── EntityBuilder $expand ─────────────────────────

    [Fact]
    public async Task Entity_Expand_SimpleNavigationProperty_ReturnsRelatedEntities()
    {
        // First, find an account to use
        var accounts = await _fixture.Client.Query<Account>()
            .Select(a => new { a.AccountId, a.Name })
            .Top(1)
            .GetListAsync();

        if (accounts.Count == 0)
            return; // No accounts to test against, skip

        var account = await _fixture.Client.Entity<Account>()
            .WithId(accounts[0].AccountId)
            .Select(a => new { a.AccountId, a.Name })
            .Expand("contact_customer_accounts")
            .RetrieveAsync();

        Assert.NotNull(account);
        Assert.Equal(accounts[0].AccountId, account.AccountId);
    }

    [Fact]
    public async Task Entity_Expand_WithNestedSelect_ReturnsFilteredProperties()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .Select(a => new { a.AccountId })
            .Top(1)
            .GetListAsync();

        if (accounts.Count == 0)
            return;

        var account = await _fixture.Client.Entity<Account>()
            .WithId(accounts[0].AccountId)
            .Select(a => new { a.AccountId, a.Name })
            .Expand<Contact>("contact_customer_accounts", e => e
                .Select(c => new { c.ContactId, c.FirstName, c.LastName }))
            .RetrieveAsync();

        Assert.NotNull(account);
        Assert.Equal(accounts[0].AccountId, account.AccountId);
    }

    [Fact]
    public async Task Entity_Expand_WithNestedTop_LimitsRelatedEntities()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .Select(a => new { a.AccountId })
            .Top(1)
            .GetListAsync();

        if (accounts.Count == 0)
            return;

        var account = await _fixture.Client.Entity<Account>()
            .WithId(accounts[0].AccountId)
            .Expand<Contact>("contact_customer_accounts", e => e
                .Select(c => new { c.ContactId, c.FirstName, c.LastName })
                .Top(2))
            .RetrieveAsync();

        Assert.NotNull(account);
        if (account.Contacts != null)
        {
            Assert.True(account.Contacts.Count <= 2);
        }
    }

    // ───────────────────────── CRUD + Expand round-trip ─────────────────────────

    [Fact]
    public async Task Create_ThenExpand_ReturnsRelatedEntities()
    {
        var accountName = $"Expand Test Account {Guid.NewGuid():N}";
        Guid accountId = Guid.Empty;
        var contactIds = new List<Guid>();

        try
        {
            // 1. Create parent account
            var account = (await _fixture.Client.Entity<Account>()
                .Set(a => a.Name, accountName)
                .Set(a => a.Revenue, 100000m)
                .ReturnRepresentation()
                .CreateAsync()).Entity!;

            Assert.NotNull(account);
            Assert.NotEqual(Guid.Empty, account.AccountId);
            accountId = account.AccountId;

            // 2. Create contacts linked to the account via lookup
            var alice = (await _fixture.Client.Entity<Contact>()
                .Set(c => c.FirstName, "Alice")
                .Set(c => c.LastName, "ExpandTest")
                .SetLookup("parentcustomerid_account", "accounts", accountId)
                .ReturnRepresentation()
                .CreateAsync()).Entity!;

            Assert.NotNull(alice);
            contactIds.Add(alice.ContactId);

            var bob = (await _fixture.Client.Entity<Contact>()
                .Set(c => c.FirstName, "Bob")
                .Set(c => c.LastName, "ExpandTest")
                .SetLookup("parentcustomerid_account", "accounts", accountId)
                .ReturnRepresentation()
                .CreateAsync()).Entity!;

            Assert.NotNull(bob);
            contactIds.Add(bob.ContactId);

            // 3. Retrieve account with $expand to verify contacts are returned
            var retrieved = await _fixture.Client.Entity<Account>()
                .WithId(accountId)
                .Select(a => new { a.AccountId, a.Name })
                .Expand<Contact>("contact_customer_accounts", e => e
                    .Select(c => new { c.ContactId, c.FirstName, c.LastName })
                    .OrderBy(c => c.FirstName))
                .RetrieveAsync();

            Assert.NotNull(retrieved);
            Assert.NotNull(retrieved.Contacts);
            Assert.Equal(2, retrieved.Contacts.Count);
            Assert.Equal("Alice", retrieved.Contacts[0].FirstName);
            Assert.Equal("Bob", retrieved.Contacts[1].FirstName);
        }
        finally
        {
            // Clean up: delete contacts first, then account
            foreach (var contactId in contactIds)
            {
                try { await _fixture.Client.Entity<Contact>().WithId(contactId).DeleteAsync(); }
                catch { /* best effort cleanup */ }
            }

            if (accountId != Guid.Empty)
            {
                try { await _fixture.Client.Entity<Account>().WithId(accountId).DeleteAsync(); }
                catch { /* best effort cleanup */ }
            }
        }
    }
}
