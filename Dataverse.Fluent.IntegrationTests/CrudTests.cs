using Dataverse.Fluent.IntegrationTests.Models;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.IntegrationTests;

// Records created here are deleted by the fixture when the run ends (see RecordTrackingTransport).
[Collection("Dataverse Integration Tests")]
public class CrudTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public CrudTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanCreate_Account()
    {
        var accountName = $"Test Account {Guid.NewGuid()}";
        
        var account = (await _fixture.Client.Entity<Account>()
            .Set("name", accountName)
            .Set("revenue", 1000000)
            .Set("numberofemployees", 50)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        Assert.NotNull(account);
        Assert.NotEqual(Guid.Empty, account.AccountId);
        Assert.Equal(accountName, account.Name);
        Assert.Equal(1000000, account.Revenue);
    }

    [Fact]
    public async Task CanCreateAndUpdate_ReturningOnlySelectedProperties()
    {
        var accountName = $"Test Account Representation Select {Guid.NewGuid()}";

        var created = (await _fixture.Client.Entity<Account>()
            .Set(a => a.Name, accountName)
            .Set(a => a.Revenue, 1000000m)
            .Set(a => a.PhoneNumber, "555-1234")
            .ReturnRepresentation(a => new { a.AccountId, a.Name })
            .CreateAsync()).Entity!;
        Assert.NotEqual(Guid.Empty, created.AccountId);
        Assert.Equal(accountName, created.Name);
        Assert.Null(created.Revenue);      // Not selected
        Assert.Null(created.PhoneNumber);  // Not selected

        var updated = await _fixture.Client.Entity<Account>(created.AccountId)
            .Set(a => a.Revenue, 2000000m)
            .ReturnRepresentation(a => a.Revenue)
            .UpdateAsync();

        Assert.NotNull(updated);
        Assert.Equal(2000000m, updated.Revenue);
        Assert.Null(updated.PhoneNumber);  // Not selected
    }

    [Fact]
    public async Task CanCreate_AccountWithSetFrom()
    {
        var accountData = new Account
        {
            Name = $"Test Account SetFrom {Guid.NewGuid()}",
            Revenue = 2000000,
            NumberOfEmployees = 100,
            PhoneNumber = "555-1234"
        };
        
        // SetFrom now automatically excludes properties marked with [DataverseReadOnly]
        var account = (await _fixture.Client.Entity<Account>()
            .SetFrom(accountData)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        Assert.NotNull(account);
        Assert.NotEqual(Guid.Empty, account.AccountId);
        Assert.Equal(accountData.Name, account.Name);
        Assert.Equal(accountData.Revenue, account.Revenue);
        Assert.Equal(accountData.NumberOfEmployees, account.NumberOfEmployees);
    }

    [Fact]
    public async Task CanCreate_AccountWithSetFromExcept()
    {
        var accountData = new Account
        {
            Name = $"Test Account SetFromExcept {Guid.NewGuid()}",
            Revenue = 3000000,
            NumberOfEmployees = 150,
            PhoneNumber = "555-5678",
            EmailAddress = "test@example.com"
        };
        
        // Use SetFromExcept to exclude specific properties by expression
        var account = (await _fixture.Client.Entity<Account>()
            .SetFromExcept(accountData, 
                a => a.AccountId,  // Exclude AccountId
                a => a.StatusCode, // Exclude StatusCode
                a => a.StateCode, // Exclude StateCode
                a => a.PhoneNumber,
                a => a.EmailAddress)   
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        Assert.NotNull(account);
        Assert.NotEqual(Guid.Empty, account.AccountId);
        Assert.Equal(accountData.Name, account.Name);
        Assert.Equal(accountData.Revenue, account.Revenue);
        Assert.Null(account.EmailAddress);  // Was excluded
        Assert.Null(account.PhoneNumber);   // Was excluded
    }

    [Fact]
    public async Task CanCreate_AccountUsingPropertySetters()
    {
        var accountName = $"Test Account Property Setters {Guid.NewGuid()}";
        
        var account = (await _fixture.Client.Entity<Account>()
            .Set("name", accountName)
            .Set("revenue", 2000000)
            .Set("numberofemployees", 100)
            .Set("telephone1", "555-1234")
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        Assert.NotNull(account);
        Assert.NotEqual(Guid.Empty, account.AccountId);
        Assert.Equal(accountName, account.Name);
        Assert.Equal(2000000, account.Revenue);
        Assert.Equal(100, account.NumberOfEmployees);
    }

    [Fact]
    public async Task CanRetrieve_Account()
    {
        var accountName = $"Test Account Retrieve {Guid.NewGuid()}";
        
        var created = (await _fixture.Client.Entity<Account>()
            .Set("name", accountName)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;
        
        var retrieved = await _fixture.Client.Entity<Account>()
            .WithId(created.AccountId)
            .RetrieveAsync();

        Assert.NotNull(retrieved);
        Assert.Equal(created.AccountId, retrieved.AccountId);
        Assert.Equal(accountName, retrieved.Name);
    }

    [Fact]
    public async Task CanUpdate_Account()
    {
        var accountName = $"Test Account Update {Guid.NewGuid()}";
        
        var created = (await _fixture.Client.Entity<Account>()
            .Set("name", accountName)
            .Set("revenue", 1000000)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;
        
        var updatedName = $"Updated {accountName}";
        var updatedRevenue = 2500000m;
        
        var updated = await _fixture.Client.Entity<Account>()
            .WithId(created.AccountId)
            .Set("name", updatedName)
            .Set("revenue", updatedRevenue)
            .ReturnRepresentation()
            .UpdateAsync();

        Assert.NotNull(updated);
        Assert.Equal(created.AccountId, updated.AccountId);
        Assert.Equal(updatedName, updated.Name);
        Assert.Equal(updatedRevenue, updated.Revenue);
    }

    [Fact]
    public async Task CanDeactivateAndReactivate_AccountByUpdatingState()
    {
        // The Web API has no SetState action: state changes are a PATCH of statecode and statuscode.
        var created = (await _fixture.Client.Entity<Account>()
            .Set("name", $"Test Account State {Guid.NewGuid()}")
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        await _fixture.Client.Entity<Account>(created.AccountId)
            .Set(x => x.StateCode, 1)  // Inactive
            .Set(x => x.StatusCode, 2) // Inactive
            .UpdateAsync();

        var deactivated = await _fixture.Client.Entity<Account>(created.AccountId).RetrieveAsync();
        Assert.Equal(1, deactivated!.StateCode);
        Assert.Equal(2, deactivated.StatusCode);

        await _fixture.Client.Entity<Account>(created.AccountId)
            .Set(x => x.StateCode, 0)  // Active
            .Set(x => x.StatusCode, 1) // Active
            .UpdateAsync();

        var reactivated = await _fixture.Client.Entity<Account>(created.AccountId).RetrieveAsync();
        Assert.Equal(0, reactivated!.StateCode);
        Assert.Equal(1, reactivated.StatusCode);
    }

    [Fact]
    public async Task CanDelete_Account()
    {
        var accountName = $"Test Account Delete {Guid.NewGuid()}";
        
        var created = (await _fixture.Client.Entity<Account>()
            .Set("name", accountName)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        await _fixture.Client.Entity<Account>()
            .WithId(created.AccountId)
            .DeleteAsync();
        
        var exception = await Assert.ThrowsAsync<DataverseException>(async () =>
        {
            await _fixture.Client.Entity<Account>()
                .WithId(created.AccountId)
                .RetrieveAsync();
        });
        
        Assert.Equal(System.Net.HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task CanCreate_Contact()
    {
        var contact = (await _fixture.Client.Entity<Contact>()
            .Set("firstname", "John")
            .Set("lastname", $"TestContact-{Guid.NewGuid()}")
            .Set("emailaddress1", $"test.{Guid.NewGuid()}@contoso.com")
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        Assert.NotNull(contact);
        Assert.NotEqual(Guid.Empty, contact.ContactId);
        Assert.Equal("John", contact.FirstName);
    }

    [Fact]
    public async Task Retrieve_NonExistentAccount_ThrowsNotFoundException()
    {
        var nonExistentId = Guid.NewGuid();
        
        var exception = await Assert.ThrowsAsync<DataverseException>(async () =>
        {
            await _fixture.Client.Entity<Account>()
                .WithId(nonExistentId)
                .RetrieveAsync();
        });
        
        Assert.Equal(System.Net.HttpStatusCode.NotFound, exception.StatusCode);
    }
}
