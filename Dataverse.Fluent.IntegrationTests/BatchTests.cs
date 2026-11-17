using Dataverse.Fluent.IntegrationTests.Models;

namespace Dataverse.Fluent.IntegrationTests;

[Collection("Dataverse Integration Tests")]
public class BatchTests : IClassFixture<DataverseTestFixture>, IAsyncDisposable
{
    private readonly DataverseTestFixture _fixture;
    private readonly List<Guid> _createdAccountIds = new();
    private readonly List<Guid> _createdContactIds = new();

    public BatchTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanExecute_BatchWithSingleCreate()
    {
        var accountName = $"Batch Test Account {Guid.NewGuid()}";
        
        var batch = _fixture.Client.Batch();
        var account = new Account
        {
            Name = accountName,
            Revenue = 100000,
            NumberOfEmployees = 10
        };
        
        batch.AddCreate(account, "accounts");
        
        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
        Assert.False(response.HasErrors);

        // Verify the account was created by querying for it
        var accounts = await _fixture.Client.Query<Account>()
            .Where($"name eq '{accountName}'")
            .GetListAsync();
        
        Assert.Single(accounts);
        Assert.Equal(accountName, accounts[0].Name);
        
        _createdAccountIds.Add(accounts[0].AccountId);
    }

    [Fact]
    public async Task Batch_ContentIdReference_LinksChildToParentCreatedInTheSameChangeset()
    {
        var accountName = $"Batch Ref Account {Guid.NewGuid()}";
        var lastName = $"BatchRef{Guid.NewGuid():N}";

        var result = await _fixture.Client.Batch()
            .AddCreate(new Account { Name = accountName }, out var parent, "accounts")
            .AddCreate(
                new Contact { FirstName = "Ref", LastName = lastName },
                new Dictionary<string, Dataverse.Fluent.Models.BatchReference> { ["parentcustomerid_account"] = parent },
                "contacts")
            .ExecuteAsync();

        if (result.Results.ElementAtOrDefault(0)?.EntityId is { } accountId)
            _createdAccountIds.Add(accountId);
        if (result.Results.ElementAtOrDefault(1)?.EntityId is { } contactId)
            _createdContactIds.Add(contactId);

        Assert.True(result.AllSucceeded, string.Join(" | ", result.Errors.Select(e => $"{e.ContentId}: {(int)e.StatusCode} {e.Body}")));
        Assert.Equal([1, 2], result.Results.Select(r => r.ContentId));

        // The contact's lookup was bound to "$1", the account created by the first operation.
        var contacts = await _fixture.Client.Query<Contact>()
            .Where($"_parentcustomerid_value eq {result.Results[0].EntityId}")
            .GetListAsync();

        var contact = Assert.Single(contacts);
        Assert.Equal(lastName, contact.LastName);
    }

    [Fact]
    public async Task Batch_WithoutContinueOnError_ReportsOperationsAfterTheFailureAsNotExecuted()
    {
        var firstName = $"Batch Stop First {Guid.NewGuid()}";
        var lastName = $"Batch Stop Last {Guid.NewGuid()}";

        var result = await _fixture.Client.Batch()
            .AddCreate(new { name = firstName }, "accounts")
            .NewChangeset()
            .AddUpdate(Guid.NewGuid(), new { name = "missing" }, "accounts") // the record does not exist
            .NewChangeset()
            .AddCreate(new { name = lastName }, "accounts")
            .ExecuteAsync();

        var first = await _fixture.Client.Query<Account>().Where($"name eq '{firstName}'").GetListAsync();
        _createdAccountIds.AddRange(first.Select(a => a.AccountId));
        var last = await _fixture.Client.Query<Account>().Where($"name eq '{lastName}'").GetListAsync();
        _createdAccountIds.AddRange(last.Select(a => a.AccountId));

        Assert.Equal([1, 2, 3], result.Results.Select(r => r.ContentId));
        Assert.True(result.Results[0].IsSuccess);
        Assert.Single(first);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, result.Results[1].StatusCode);

        // Dataverse stopped at the failure: the third operation has no response and never ran.
        Assert.Equal(System.Net.HttpStatusCode.FailedDependency, result.Results[2].StatusCode);
        Assert.False(result.Results[2].WasRolledBack);
        Assert.Empty(last);
    }

    [Fact]
    public async Task Batch_UpdateWithTheModel_ChangesOnlyWritableColumns()
    {
        // The Account model marks accountid/statecode/statuscode [DataverseReadOnly(Create)]; an update
        // must not send their default values (Guid.Empty, 0) over the existing record.
        var created = (await _fixture.Client.Entity<Account>()
            .Set(x => x.Name, $"Batch Model Update {Guid.NewGuid()}")
            .ReturnRepresentation()
            .CreateAsync()).Entity!;
        _createdAccountIds.Add(created.AccountId);
        await _fixture.Client.Entity<Account>(created.AccountId).Set(x => x.StateCode, 1).Set(x => x.StatusCode, 2).UpdateAsync();

        var result = await _fixture.Client.Batch()
            .AddUpdate(created.AccountId, new Account { Name = "Renamed in batch", Revenue = 123m })
            .ExecuteAsync();

        result.ThrowIfAnyFailed();
        var updated = await _fixture.Client.Entity<Account>(created.AccountId).RetrieveAsync();
        Assert.Equal("Renamed in batch", updated!.Name);
        Assert.Equal(123m, updated.Revenue);
        Assert.Equal(1, updated.StateCode);  // still inactive
        Assert.Equal(2, updated.StatusCode);
    }

    [Fact]
    public async Task CanExecute_BatchWithMultipleCreates()
    {
        var account1Name = $"Batch Account 1 {Guid.NewGuid()}";
        var account2Name = $"Batch Account 2 {Guid.NewGuid()}";
        
        var batch = _fixture.Client.Batch();
        
        batch.AddCreate(new Account { Name = account1Name, Revenue = 100000 }, "accounts")
             .AddCreate(new Account { Name = account2Name, Revenue = 200000 }, "accounts");
        
        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
        Assert.False(response.HasErrors);

        // Verify both accounts were created
        var accounts1 = await _fixture.Client.Query<Account>()
            .Where($"name eq '{account1Name}'")
            .GetListAsync();
        
        var accounts2 = await _fixture.Client.Query<Account>()
            .Where($"name eq '{account2Name}'")
            .GetListAsync();
        
        Assert.NotEmpty(accounts1);
        Assert.NotEmpty(accounts2);
        Assert.Equal(account1Name, accounts1[0].Name);
        Assert.Equal(account2Name, accounts2[0].Name);
        
        _createdAccountIds.Add(accounts1[0].AccountId);
        _createdAccountIds.Add(accounts2[0].AccountId);
    }

    [Fact]
    public async Task CanExecute_BatchWithCreateAndUpdate()
    {
        // First create an account
        var initialName = $"Initial Account {Guid.NewGuid()}";
        var created = (await _fixture.Client.Entity<Account>()
            .Set("name", initialName)
            .Set("revenue", 100000)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;
        
        _createdAccountIds.Add(created.AccountId);
        
        // Now create a batch with a new account and update the existing one
        var newAccountName = $"New Batch Account {Guid.NewGuid()}";
        var updatedName = $"Updated {initialName}";
        
        var batch = _fixture.Client.Batch();
        batch.AddCreate(new Account { Name = newAccountName, Revenue = 150000 }, "accounts")
             .AddUpdate(created.AccountId, new { name = updatedName, revenue = 250000 }, "accounts");
        
        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
        Assert.False(response.HasErrors);

        // Verify the update
        var updated = await _fixture.Client.Entity<Account>()
            .WithId(created.AccountId)
            .RetrieveAsync();
        
        Assert.Equal(updatedName, updated.Name);
        Assert.Equal(250000, updated.Revenue);
        
        // Verify the new account
        var newAccounts = await _fixture.Client.Query<Account>()
            .Where($"name eq '{newAccountName}'")
            .GetListAsync();
        
        Assert.NotEmpty(newAccounts);
        _createdAccountIds.Add(newAccounts[0].AccountId);
    }

    [Fact]
    public async Task CanExecute_BatchWithMultipleDifferentOperations()
    {
        // Create two accounts first
        var account1Name = $"Account 1 {Guid.NewGuid()}";
        var account2Name = $"Account 2 {Guid.NewGuid()}";
        
        var account1 = (await _fixture.Client.Entity<Account>()
            .Set("name", account1Name)
            .Set("revenue", 100000)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;
        
        var account2 = (await _fixture.Client.Entity<Account>()
            .Set("name", account2Name)
            .Set("revenue", 200000)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;
        
        _createdAccountIds.Add(account1.AccountId);
        _createdAccountIds.Add(account2.AccountId);
        
        // Create a batch with create, updates, and associate
        var newAccountName = $"New Account {Guid.NewGuid()}";
        
        var batch = _fixture.Client.Batch()
            .AddCreate(new Account { Name = newAccountName, Revenue = 300000 }, "accounts")
            .AddUpdate(account1.AccountId, new { name = $"Updated {account1Name}" }, "accounts")
            .AddUpdate(account2.AccountId, new { revenue = 500000 }, "accounts");
        
        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
        Assert.False(response.HasErrors);

        // Verify the updates
        var updated1 = await _fixture.Client.Entity<Account>()
            .WithId(account1.AccountId)
            .RetrieveAsync();
        
        var updated2 = await _fixture.Client.Entity<Account>()
            .WithId(account2.AccountId)
            .RetrieveAsync();
        
        Assert.Equal($"Updated {account1Name}", updated1.Name);
        Assert.Equal(500000, updated2.Revenue);
        
        // Verify the new account
        var newAccounts = await _fixture.Client.Query<Account>()
            .Where($"name eq '{newAccountName}'")
            .GetListAsync();
        
        Assert.NotEmpty(newAccounts);
        _createdAccountIds.Add(newAccounts[0].AccountId);
    }

    [Fact]
    public async Task CanExecute_BatchWithDeepInsert()
    {
        var accountName = $"Parent Account {Guid.NewGuid()}";
        
        var batch = _fixture.Client.Batch();
        
        var parentAccount = new Account
        {
            Name = accountName,
            Revenue = 500000
        };
        
        // A List<T> is passed as a single params argument, so this also covers flattening it.
        var childContacts = new List<Contact>
        {
            new() { FirstName = "John", LastName = $"Contact1-{Guid.NewGuid()}" },
            new() { FirstName = "Jane", LastName = $"Contact2-{Guid.NewGuid()}" }
        };

        batch.AddDeepInsert(parentAccount, "contact_customer_accounts", childContacts);
        
        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
        Assert.False(response.HasErrors);

        // Verify the parent account was created
        var accounts = await _fixture.Client.Query<Account>()
            .Where($"name eq '{accountName}'")
            .GetListAsync();

        Assert.Single(accounts);
        _createdAccountIds.Add(accounts[0].AccountId);

        var contacts = await _fixture.Client.Query<Contact>()
            .Where($"_parentcustomerid_value eq {accounts[0].AccountId}")
            .GetListAsync();
        _createdContactIds.AddRange(contacts.Select(c => c.ContactId));

        Assert.Equal(
            childContacts.Select(c => c.LastName).Order(),
            contacts.Select(c => c.LastName).Order());
    }

    [Fact]
    public async Task CanExecute_BatchWithContinueOnError()
    {
        var validAccountName = $"Valid Account {Guid.NewGuid()}";
        var invalidAccountId = Guid.NewGuid(); // Non-existent ID for update
        
        var batch = _fixture.Client.Batch()
            .ContinueOnError(true);
        
        // A changeset is atomic, so the failing update must be in its own changeset;
        // otherwise it would roll back the create. Continue-on-error applies across changesets.
        batch.AddCreate(new Account { Name = validAccountName, Revenue = 100000 }, "accounts")
             .NewChangeset()
             .AddUpdate(invalidAccountId, new { name = "This will fail" }, "accounts");

        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
        Assert.Equal(2, response.Results.Count);
        Assert.True(response.Results[0].IsSuccess);
        Assert.False(response.Results[1].IsSuccess);
        Assert.False(response.Results[1].WasRolledBack);

        // The valid account should have been created despite the failed update
        var accounts = await _fixture.Client.Query<Account>()
            .Where($"name eq '{validAccountName}'")
            .GetListAsync();
        
        Assert.NotEmpty(accounts);
        _createdAccountIds.Add(accounts[0].AccountId);
    }

    [Fact]
    public async Task CanExecute_BatchWithCustomBatchId()
    {
        var customBatchId = Guid.NewGuid();
        var accountName = $"Batch Test {Guid.NewGuid()}";
        
        var batch = _fixture.Client.Batch(customBatchId);
        batch.AddCreate(new Account { Name = accountName, Revenue = 100000 }, "accounts");
        
        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
        Assert.False(response.HasErrors);

        // Verify the account was created
        var accounts = await _fixture.Client.Query<Account>()
            .Where($"name eq '{accountName}'")
            .GetListAsync();
        
        Assert.Single(accounts);
        _createdAccountIds.Add(accounts[0].AccountId);
    }

    [Fact]
    public async Task CanExecute_BatchWithEmptyBatch()
    {
        var batch = _fixture.Client.Batch();
        
        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
        Assert.True(response.AllSucceeded);

        // Note: Empty batches return immediately without making an HTTP request
        // This avoids the "missing Content-Type header" error from Dataverse
    }

    [Fact]
    public async Task CanExecute_BatchWithLargeNumberOfOperations()
    {
        var batch = _fixture.Client.Batch();
        var accountNames = new List<string>();
        
        // Add 10 create operations
        for (int i = 0; i < 10; i++)
        {
            var accountName = $"Batch Account {i} - {Guid.NewGuid()}";
            accountNames.Add(accountName);
            batch.AddCreate(new Account { Name = accountName, Revenue = 100000 * (i + 1) }, "accounts");
        }
        
        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
        Assert.False(response.HasErrors);

        // Verify all accounts were created
        foreach (var name in accountNames)
        {
            var accounts = await _fixture.Client.Query<Account>()
                .Where($"name eq '{name}'")
                .GetListAsync();
            
            if (accounts.Any())
            {
                _createdAccountIds.Add(accounts[0].AccountId);
            }
        }
        
        Assert.Equal(10, _createdAccountIds.Count);
    }

    [Fact]
    public async Task CanExecute_BatchWithCancellationToken()
    {
        var accountName = $"Cancellation Test {Guid.NewGuid()}";
        
        var batch = _fixture.Client.Batch();
        batch.AddCreate(new Account { Name = accountName, Revenue = 100000 }, "accounts");
        
        using var cts = new CancellationTokenSource();
        var response = await batch.ExecuteAsync(cts.Token);

        Assert.NotNull(response);
        Assert.False(response.HasErrors);

        // Clean up
        var accounts = await _fixture.Client.Query<Account>()
            .Where($"name eq '{accountName}'")
            .GetListAsync();
        
        if (accounts.Any())
        {
            _createdAccountIds.Add(accounts[0].AccountId);
        }
    }

    [Fact]
    public async Task Batch_FluentChaining_WorksCorrectly()
    {
        var account1Name = $"Fluent Account 1 {Guid.NewGuid()}";
        var account2Name = $"Fluent Account 2 {Guid.NewGuid()}";
        
        // Test fluent chaining returns the same batch instance
        var batch = _fixture.Client.Batch();
        var result = batch
            .AddCreate(new Account { Name = account1Name, Revenue = 100000 }, "accounts")
            .AddCreate(new Account { Name = account2Name, Revenue = 200000 }, "accounts")
            .ContinueOnError(true);
        
        Assert.Same(batch, result);
        
        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
        Assert.False(response.HasErrors);

        // Clean up
        var accounts1 = await _fixture.Client.Query<Account>()
            .Where($"name eq '{account1Name}'")
            .GetListAsync();
        
        var accounts2 = await _fixture.Client.Query<Account>()
            .Where($"name eq '{account2Name}'")
            .GetListAsync();
        
        if (accounts1.Any()) _createdAccountIds.Add(accounts1[0].AccountId);
        if (accounts2.Any()) _createdAccountIds.Add(accounts2[0].AccountId);
    }

    public async ValueTask DisposeAsync()
    {
        // Clean up all created accounts
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
        
        // Clean up all created contacts
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
    }
}
