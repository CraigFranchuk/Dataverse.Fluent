using Dataverse.Fluent.IntegrationTests.Models;

namespace Dataverse.Fluent.IntegrationTests;

[Collection("Dataverse Integration Tests")]
public class AssociationTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public AssociationTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Associate_ThenExpand_ShowsRelatedEntity()
    {
        Guid accountId = Guid.Empty;
        Guid contactId = Guid.Empty;

        try
        {
            // 1. Create an account
            var account = (await _fixture.Client.Entity<Account>()
                .Set(a => a.Name, $"Assoc Test Account {Guid.NewGuid():N}")
                .ReturnRepresentation()
                .CreateAsync()).Entity!;

            Assert.NotNull(account);
            accountId = account.AccountId;

            // 2. Create a standalone contact (no parent lookup)
            var contact = (await _fixture.Client.Entity<Contact>()
                .Set(c => c.FirstName, "Assoc")
                .Set(c => c.LastName, $"Test {Guid.NewGuid():N}")
                .ReturnRepresentation()
                .CreateAsync()).Entity!;

            Assert.NotNull(contact);
            contactId = contact.ContactId;

            // 3. Associate via primary keys
            await _fixture.Client.Associate()
                .From<Account>(accountId)
                .Via("contact_customer_accounts")
                .To<Contact>(contactId)
                .AssociateAsync();

            // 4. Verify via expand
            var retrieved = await _fixture.Client.Entity<Account>()
                .WithId(accountId)
                .Select(a => new { a.AccountId, a.Name })
                .Expand<Contact>("contact_customer_accounts", e => e
                    .Select(c => new { c.ContactId, c.FirstName }))
                .RetrieveAsync();

            Assert.NotNull(retrieved);
            Assert.NotNull(retrieved.Contacts);
            Assert.Contains(retrieved.Contacts, c => c.ContactId == contactId);
        }
        finally
        {
            if (contactId != Guid.Empty)
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

    [Fact]
    public async Task Disassociate_CollectionValued_RemovesRelationship()
    {
        Guid accountId = Guid.Empty;
        Guid contactId = Guid.Empty;

        try
        {
            // 1. Create account
            var account = (await _fixture.Client.Entity<Account>()
                .Set(a => a.Name, $"Disassoc Test Account {Guid.NewGuid():N}")
                .ReturnRepresentation()
                .CreateAsync()).Entity!;

            Assert.NotNull(account);
            accountId = account.AccountId;

            // 2. Create contact linked to account
            var contact = (await _fixture.Client.Entity<Contact>()
                .Set(c => c.FirstName, "Disassoc")
                .Set(c => c.LastName, $"Test {Guid.NewGuid():N}")
                .SetLookup("parentcustomerid_account", "accounts", accountId)
                .ReturnRepresentation()
                .CreateAsync()).Entity!;

            Assert.NotNull(contact);
            contactId = contact.ContactId;

            // 3. Verify contact is associated
            var before = await _fixture.Client.Entity<Account>()
                .WithId(accountId)
                .Expand<Contact>("contact_customer_accounts", e => e
                    .Select(c => c.ContactId))
                .RetrieveAsync();

            Assert.NotNull(before?.Contacts);
            Assert.Contains(before.Contacts, c => c.ContactId == contactId);

            // 4. Disassociate
            await _fixture.Client.Associate()
                .From<Account>(accountId)
                .Via("contact_customer_accounts")
                .To<Contact>(contactId)
                .DisassociateAsync();

            // 5. Verify contact is no longer associated
            var after = await _fixture.Client.Entity<Account>()
                .WithId(accountId)
                .Expand<Contact>("contact_customer_accounts", e => e
                    .Select(c => c.ContactId))
                .RetrieveAsync();

            Assert.NotNull(after);
            if (after.Contacts != null)
            {
                Assert.DoesNotContain(after.Contacts, c => c.ContactId == contactId);
            }
        }
        finally
        {
            if (contactId != Guid.Empty)
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

    [Fact]
    public async Task Associate_WithExplicitEntitySets_Works()
    {
        Guid accountId = Guid.Empty;
        Guid contactId = Guid.Empty;

        try
        {
            // Create account and contact
            var account = (await _fixture.Client.Entity<Account>()
                .Set(a => a.Name, $"Explicit Assoc {Guid.NewGuid():N}")
                .ReturnRepresentation()
                .CreateAsync()).Entity!;

            Assert.NotNull(account);
            accountId = account.AccountId;

            var contact = (await _fixture.Client.Entity<Contact>()
                .Set(c => c.FirstName, "Explicit")
                .Set(c => c.LastName, $"Assoc {Guid.NewGuid():N}")
                .ReturnRepresentation()
                .CreateAsync()).Entity!;

            Assert.NotNull(contact);
            contactId = contact.ContactId;

            // Associate using explicit entity set names
            await _fixture.Client.Associate()
                .From("accounts", accountId)
                .Via("contact_customer_accounts")
                .To("contacts", contactId)
                .AssociateAsync();

            // Verify
            var retrieved = await _fixture.Client.Entity<Account>()
                .WithId(accountId)
                .Expand<Contact>("contact_customer_accounts", e => e
                    .Select(c => c.ContactId))
                .RetrieveAsync();

            Assert.NotNull(retrieved?.Contacts);
            Assert.Contains(retrieved.Contacts, c => c.ContactId == contactId);
        }
        finally
        {
            if (contactId != Guid.Empty)
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

    [Fact]
    public async Task Disassociate_SingleValued_ClearsLookup()
    {
        Guid accountId = Guid.Empty;
        Guid contactId = Guid.Empty;

        try
        {
            // 1. Create account
            var account = (await _fixture.Client.Entity<Account>()
                .Set(a => a.Name, $"SingleVal Disassoc {Guid.NewGuid():N}")
                .ReturnRepresentation()
                .CreateAsync()).Entity!;

            Assert.NotNull(account);
            accountId = account.AccountId;

            // 2. Create contact linked to account
            var contact = (await _fixture.Client.Entity<Contact>()
                .Set(c => c.FirstName, "SingleVal")
                .Set(c => c.LastName, $"Disassoc {Guid.NewGuid():N}")
                .SetLookup("parentcustomerid_account", "accounts", accountId)
                .ReturnRepresentation()
                .CreateAsync()).Entity!;

            Assert.NotNull(contact);
            contactId = contact.ContactId;

            // 3. Disassociate using single-valued navigation property
            await _fixture.Client.Associate()
                .From<Contact>(contactId)
                .ViaSingleValued("parentcustomerid_account")
                .To<Account>(accountId)
                .DisassociateAsync();

            // 4. Verify the contact is no longer linked to the account
            var after = await _fixture.Client.Entity<Account>()
                .WithId(accountId)
                .Expand<Contact>("contact_customer_accounts", e => e
                    .Select(c => c.ContactId))
                .RetrieveAsync();

            Assert.NotNull(after);
            if (after.Contacts != null)
            {
                Assert.DoesNotContain(after.Contacts, c => c.ContactId == contactId);
            }
        }
        finally
        {
            if (contactId != Guid.Empty)
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
