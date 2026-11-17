using Dataverse.Fluent.IntegrationTests.Models;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.IntegrationTests;

[Collection("Dataverse Integration Tests")]
public class DeepOperationsTests : IAsyncDisposable
{
    private readonly DataverseTestFixture _fixture;
    private readonly List<Guid> _createdAccountIds = new();
    private readonly List<Guid> _createdContactIds = new();

    public DeepOperationsTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DeepInsert_AccountWithSingleContact_CreatesParentAndContact()
    {
        var accountName = $"Test Deep Insert Account {Guid.NewGuid()}";
        var account = new Account
        {
            Name = accountName,
            Revenue = 1000000
        };

        var contact = new Contact
        {
            FirstName = "John",
            LastName = "Doe",
            EmailAddress = $"john.doe.{Guid.NewGuid()}@example.com"
        };

        // Use a list even for single contact since contact_customer_accounts is a 1:N relationship
        var result = (await _fixture.Client
            .DeepInsert(account)
            .WithRelated(new List<Contact> { contact }, "contact_customer_accounts")
            .ReturnRepresentation()
            .ExecuteAsync()).Entity;

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.AccountId);
        Assert.Equal(accountName, result.Name);
        
        _createdAccountIds.Add(result.AccountId);

        var contacts = await _fixture.Client.Query<Contact>()
            .Where($"_parentcustomerid_value eq {result.AccountId}")
            .GetListAsync();

        foreach (var c in contacts)
        {
            _createdContactIds.Add(c.ContactId);
        }

        var created = Assert.Single(contacts);
        Assert.Equal(contact.EmailAddress, created.EmailAddress);
    }

    [Fact]
    public async Task DeepInsert_AccountWithMultipleContacts_CreatesParentAndContacts()
    {
        var accountName = $"Test Multi-Contact Account {Guid.NewGuid()}";
        var account = new Account
        {
            Name = accountName,
            Revenue = 2000000,
            NumberOfEmployees = 50
        };

        var contacts = new List<Contact>
        {
            new Contact
            {
                FirstName = "Alice",
                LastName = "Smith",
                EmailAddress = $"alice.smith.{Guid.NewGuid()}@example.com",
                JobTitle = "Manager"
            },
            new Contact
            {
                FirstName = "Bob",
                LastName = "Johnson",
                EmailAddress = $"bob.johnson.{Guid.NewGuid()}@example.com",
                JobTitle = "Developer"
            },
            new Contact
            {
                FirstName = "Charlie",
                LastName = "Brown",
                EmailAddress = $"charlie.brown.{Guid.NewGuid()}@example.com",
                JobTitle = "Analyst"
            }
        };

        var result = (await _fixture.Client
            .DeepInsert(account)
            .WithRelated(contacts, "contact_customer_accounts")
            .ReturnRepresentation()
            .ExecuteAsync()).Entity;

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.AccountId);
        Assert.Equal(accountName, result.Name);
        
        _createdAccountIds.Add(result.AccountId);

        // Clean up any contacts that were created
        var retrievedContacts = await _fixture.Client.Query<Contact>()
            .Where($"_parentcustomerid_value eq {result.AccountId}")
            .GetListAsync();

        foreach (var contact in retrievedContacts)
        {
            _createdContactIds.Add(contact.ContactId);
        }

        Assert.Equal(
            contacts.Select(c => c.EmailAddress).Order(),
            retrievedContacts.Select(c => c.EmailAddress).Order());
    }

    [Fact]
    public async Task DeepInsert_WithCustomPreferHeader_ReturnsExpectedData()
    {
        var accountName = $"Test Prefer Header Account {Guid.NewGuid()}";
        var account = new Account
        {
            Name = accountName,
            Revenue = 500000
        };

        var contact = new Contact
        {
            FirstName = "Jane",
            LastName = "Williams",
            EmailAddress = $"jane.williams.{Guid.NewGuid()}@example.com"
        };

        var result = (await _fixture.Client
            .DeepInsert(account)
            .WithRelated(new List<Contact> { contact }, "contact_customer_accounts")
            .ReturnRepresentation()
            .WithPrefer("odata.include-annotations=\"*\"")
            .ExecuteAsync()).Entity;

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.AccountId);
        
        _createdAccountIds.Add(result.AccountId);

        var contacts = await _fixture.Client.Query<Contact>()
            .Where($"_parentcustomerid_value eq {result.AccountId}")
            .GetListAsync();

        foreach (var c in contacts)
        {
            _createdContactIds.Add(c.ContactId);
        }
    }

    [Fact]
    public async Task DeepInsert_WithoutReturnRepresentation_ReturnsNull()
    {
        var accountName = $"Test No Return Account {Guid.NewGuid()}";
        var account = new Account
        {
            Name = accountName,
            Revenue = 750000
        };

        var contact = new Contact
        {
            FirstName = "Mike",
            LastName = "Davis",
            EmailAddress = $"mike.davis.{Guid.NewGuid()}@example.com"
        };

        var result = (await _fixture.Client
            .DeepInsert(account)
            .WithRelated(new List<Contact> { contact }, "contact_customer_accounts")
            .ExecuteAsync()).Entity;

        Assert.Null(result);

        var accounts = await _fixture.Client.Query<Account>()
            .Where($"name eq '{accountName}'")
            .GetListAsync();

        Assert.NotEmpty(accounts);
        _createdAccountIds.Add(accounts[0].AccountId);

        var contacts = await _fixture.Client.Query<Contact>()
            .Where($"_parentcustomerid_value eq {accounts[0].AccountId}")
            .GetListAsync();

        foreach (var c in contacts)
        {
            _createdContactIds.Add(c.ContactId);
        }
    }

    [Fact]
    public async Task DeepInsert_WithCancellationToken_SupportsEarlyCancellation()
    {
        var accountName = $"Test Cancellation Account {Guid.NewGuid()}";
        var account = new Account
        {
            Name = accountName,
            Revenue = 1000000
        };

        var contact = new Contact
        {
            FirstName = "Test",
            LastName = "Cancel",
            EmailAddress = $"test.cancel.{Guid.NewGuid()}@example.com"
        };

        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _fixture.Client
                .DeepInsert(account)
                .WithRelated(new List<Contact> { contact }, "contact_customer_accounts")
                .ExecuteAsync(cts.Token);
        });
    }

    [Fact]
    public async Task DeepInsert_EmptyContactList_CreatesOnlyParent()
    {
        var accountName = $"Test Empty Contacts Account {Guid.NewGuid()}";
        var account = new Account
        {
            Name = accountName,
            Revenue = 800000
        };

        var emptyContacts = new List<Contact>();

        var result = (await _fixture.Client
            .DeepInsert(account)
            .WithRelated(emptyContacts, "contact_customer_accounts")
            .ReturnRepresentation()
            .ExecuteAsync()).Entity;

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.AccountId);
        Assert.Equal(accountName, result.Name);
        
        _createdAccountIds.Add(result.AccountId);

        var contacts = await _fixture.Client.Query<Contact>()
            .Where($"_parentcustomerid_value eq {result.AccountId}")
            .GetListAsync();

        Assert.Empty(contacts);
    }

    [Fact]
    public async Task DeepInsert_WithNullValues_HandlesNullsCorrectly()
    {
        var accountName = $"Test Nulls Account {Guid.NewGuid()}";
        var account = new Account
        {
            Name = accountName,
            Revenue = null,
            NumberOfEmployees = null
        };

        var contact = new Contact
        {
            FirstName = "Null",
            LastName = "Test",
            EmailAddress = $"null.test.{Guid.NewGuid()}@example.com",
            PhoneNumber = null,
            JobTitle = null
        };

        var result = (await _fixture.Client
            .DeepInsert(account)
            .WithRelated(new List<Contact> { contact }, "contact_customer_accounts")
            .ReturnRepresentation()
            .ExecuteAsync()).Entity;

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.AccountId);
        Assert.Null(result.Revenue);
        Assert.Null(result.NumberOfEmployees);
        
        _createdAccountIds.Add(result.AccountId);

        var contacts = await _fixture.Client.Query<Contact>()
            .Where($"_parentcustomerid_value eq {result.AccountId}")
            .GetListAsync();

        foreach (var c in contacts)
        {
            _createdContactIds.Add(c.ContactId);
        }
    }

    [Fact]
    public async Task DeepInsert_ChainedWithRelated_CreatesComplexHierarchy()
    {
        var accountName = $"Test Chained Account {Guid.NewGuid()}";
        var account = new Account
        {
            Name = accountName,
            Revenue = 3000000,
            NumberOfEmployees = 75
        };

        var contact1 = new Contact
        {
            FirstName = "Primary",
            LastName = "Contact",
            EmailAddress = $"primary.{Guid.NewGuid()}@example.com",
            JobTitle = "CEO"
        };

        var contact2 = new Contact
        {
            FirstName = "Secondary",
            LastName = "Contact",
            EmailAddress = $"secondary.{Guid.NewGuid()}@example.com",
            JobTitle = "CFO"
        };

        var result = (await _fixture.Client
            .DeepInsert(account)
            .WithRelated(new List<Contact> { contact1 }, "contact_customer_accounts")
            .WithRelated(new List<Contact> { contact2 }, "contact_customer_accounts")
            .ReturnRepresentation()
            .ExecuteAsync()).Entity;

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.AccountId);
        
        _createdAccountIds.Add(result.AccountId);

        var contacts = await _fixture.Client.Query<Contact>()
            .Where($"_parentcustomerid_value eq {result.AccountId}")
            .GetListAsync();

        foreach (var contact in contacts)
        {
            _createdContactIds.Add(contact.ContactId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var contactId in _createdContactIds)
        {
            try
            {
                await _fixture.Client.Entity<Contact>()
                    .WithId(contactId)
                    .DeleteAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

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
    }
}
