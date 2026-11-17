using System.Net;
using Dataverse.Fluent.IntegrationTests.Models;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.IntegrationTests;

/// <summary>
/// Integration tests for upsert conflict resolution functionality.
/// Tests the four conflict resolution strategies: Upsert, PreventUpdate, PreventCreate, and IfUnmodifiedSince.
/// </summary>
[Collection("Dataverse Integration Tests")]
public class UpsertConflictResolutionTests : IAsyncDisposable
{
    private readonly DataverseTestFixture _fixture;
    private readonly List<Guid> _createdAccountIds = new();

    public UpsertConflictResolutionTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region Standard Upsert Tests

    [Fact]
    public async Task UpsertAsync_WithExistingRecord_UpdatesRecord()
    {
        // Arrange: Create an initial account
        var accountName = $"Upsert Test - Update {Guid.NewGuid()}";
        var created = (await _fixture.Client.Entity<Account>()
            .Set(x => x.Name, accountName)
            .Set(x => x.Revenue, 1000000)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdAccountIds.Add(created.AccountId);

        // Act: Upsert with new data
        var updatedRevenue = 2500000m;
        var upserted = (await _fixture.Client.Entity<Account>()
            .WithId(created.AccountId)
            .Set(x => x.Revenue, updatedRevenue)
            .ReturnRepresentation()
            .UpsertAsync()).Entity;

        // Assert
        Assert.NotNull(upserted);
        Assert.Equal(created.AccountId, upserted.AccountId);
        Assert.Equal(updatedRevenue, upserted.Revenue);
    }

    [Fact]
    public async Task UpsertAsync_WithNonExistingRecord_CreatesRecord()
    {
        // Arrange: Use a new GUID that doesn't exist
        var newAccountId = Guid.NewGuid();
        var accountName = $"Upsert Test - Create {Guid.NewGuid()}";

        // Act: Upsert a non-existing record using PreventUpdate strategy
        // Note: Standard Upsert (If-Match:*) requires the record to exist
        // To create a new record with a specific ID, use PreventUpdate (If-None-Match:*)
        var upserted = (await _fixture.Client.Entity<Account>()
            .WithId(newAccountId)
            .Set(x => x.Name, accountName)
            .Set(x => x.Revenue, 1500000)
            .ReturnRepresentation()
            .UpsertAsync(UpsertConflictResolution.PreventUpdate)).Entity;

        // Assert
        Assert.NotNull(upserted);
        Assert.Equal(accountName, upserted.Name);
        Assert.Equal(1500000m, upserted.Revenue);
        
        _createdAccountIds.Add(upserted.AccountId);
    }

    #endregion

    #region PreventUpdate (Create Only) Tests

    [Fact]
    public async Task UpsertAsync_PreventUpdate_WithNewRecord_CreatesSuccessfully()
    {
        // Arrange
        var accountName = $"PreventUpdate Test - New {Guid.NewGuid()}";
        var newAccountId = Guid.NewGuid();

        // Act: Attempt to create with PreventUpdate
        var created = (await _fixture.Client.Entity<Account>()
            .WithId(newAccountId)
            .Set(x => x.Name, accountName)
            .Set(x => x.Revenue, 1000000)
            .ReturnRepresentation()
            .UpsertAsync(UpsertConflictResolution.PreventUpdate)).Entity;

        // Assert
        Assert.NotNull(created);
        Assert.Equal(accountName, created.Name);
        
        _createdAccountIds.Add(created.AccountId);
    }

    [Fact]
    public async Task UpsertAsync_PreventUpdate_WithExistingRecord_ThrowsPreconditionFailed()
    {
        // Arrange: Create an account first
        var accountName = $"PreventUpdate Test - Existing {Guid.NewGuid()}";
        var existing = (await _fixture.Client.Entity<Account>()
            .Set(x => x.Name, accountName)
            .Set(x => x.Revenue, 1000000)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdAccountIds.Add(existing.AccountId);

        // Act & Assert: Try to upsert with PreventUpdate - should fail
        var exception = await Assert.ThrowsAsync<DataverseException>(async () =>
        {
            await _fixture.Client.Entity<Account>()
                .WithId(existing.AccountId)
                .Set(x => x.Revenue, 2000000)
                .ReturnRepresentation()
                .UpsertAsync(UpsertConflictResolution.PreventUpdate);
        });

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
        
        // Verify the original record wasn't modified
        var retrieved = await _fixture.Client.Entity<Account>()
            .WithId(existing.AccountId)
            .RetrieveAsync();
        
        Assert.Equal(1000000m, retrieved.Revenue);
    }

    #endregion

    #region PreventCreate (Update Only) Tests

    [Fact]
    public async Task UpsertAsync_PreventCreate_WithExistingRecord_UpdatesSuccessfully()
    {
        // Arrange: Create an account first
        var accountName = $"PreventCreate Test - Update {Guid.NewGuid()}";
        var existing = (await _fixture.Client.Entity<Account>()
            .Set(x => x.Name, accountName)
            .Set(x => x.Revenue, 1000000)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdAccountIds.Add(existing.AccountId);

        // Act: Update with PreventCreate
        var updatedRevenue = 3000000m;
        var updated = (await _fixture.Client.Entity<Account>()
            .WithId(existing.AccountId)
            .Set(x => x.Revenue, updatedRevenue)
            .ReturnRepresentation()
            .UpsertAsync(UpsertConflictResolution.PreventCreate)).Entity;

        // Assert
        Assert.NotNull(updated);
        Assert.Equal(existing.AccountId, updated.AccountId);
        Assert.Equal(updatedRevenue, updated.Revenue);
    }

    [Fact]
    public async Task UpsertAsync_PreventCreate_WithNonExistingRecord_ThrowsNotFound()
    {
        var nonExistentId = Guid.NewGuid();

        // PreventCreate sends If-Match: *, which Dataverse rejects with 404 when the record does not exist.
        var exception = await Assert.ThrowsAsync<DataverseException>(async () =>
        {
            await _fixture.Client.Entity<Account>()
                .WithId(nonExistentId)
                .Set(x => x.Name, "Should Not Create")
                .ReturnRepresentation()
                .UpsertAsync(UpsertConflictResolution.PreventCreate);
        });

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);

        // And nothing was created.
        var retrieve = await Assert.ThrowsAsync<DataverseException>(() =>
            _fixture.Client.Entity<Account>(nonExistentId).RetrieveAsync());
        Assert.Equal(HttpStatusCode.NotFound, retrieve.StatusCode);
    }

    #endregion

    #region Optimistic Concurrency (IfUnmodifiedSince) Tests

    [Fact]
    public async Task UpsertAsync_IfUnmodifiedSince_WithoutETag_ThrowsInvalidOperation()
    {
        // Arrange: Create an account
        var accountName = $"OptimisticConcurrency Test - No ETag {Guid.NewGuid()}";
        var existing = (await _fixture.Client.Entity<Account>()
            .Set(x => x.Name, accountName)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdAccountIds.Add(existing.AccountId);

        // Act & Assert: Try to use IfUnmodifiedSince without ETag
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _fixture.Client.Entity<Account>()
                .WithId(existing.AccountId)
                .Set(x => x.Revenue, 1000000)
                .UpsertAsync(UpsertConflictResolution.IfUnmodifiedSince);
        });

        Assert.Contains("ETag", exception.Message);
    }

    // Creates an account and returns its ID and current ETag.
    private async Task<(Guid Id, string ETag)> CreateAccountWithETagAsync(string scenario, decimal revenue)
    {
        var created = (await _fixture.Client.Entity<Account>()
            .Set(x => x.Name, $"{scenario} {Guid.NewGuid()}")
            .Set(x => x.Revenue, revenue)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;
        _createdAccountIds.Add(created.AccountId);

        return (created.AccountId, await GetETagAsync(created.AccountId));
    }

    private async Task<string> GetETagAsync(Guid id)
    {
        var result = await _fixture.Client.Entity<Account>(id).RetrieveWithMetadataAsync();
        Assert.False(string.IsNullOrEmpty(result.ETag));
        return result.ETag!;
    }

    private async Task<decimal?> GetRevenueAsync(Guid id)
        => (await _fixture.Client.Entity<Account>(id).Select(x => x.Revenue).RetrieveAsync())!.Revenue;

    [Fact]
    public async Task UpsertAsync_IfUnmodifiedSince_WithValidETag_UpdatesSuccessfully()
    {
        var (id, etag) = await CreateAccountWithETagAsync("ETag Valid", 1000000m);

        await _fixture.Client.Entity<Account>(id)
            .WithETag(etag)
            .Set(x => x.Revenue, 2000000m)
            .UpsertAsync(UpsertConflictResolution.IfUnmodifiedSince);

        Assert.Equal(2000000m, await GetRevenueAsync(id));
        Assert.NotEqual(etag, await GetETagAsync(id)); // the write produced a new version
    }

    [Fact]
    public async Task UpsertAsync_IfUnmodifiedSince_WithStaleETag_ThrowsPreconditionFailed()
    {
        var (id, staleETag) = await CreateAccountWithETagAsync("ETag Stale", 1000000m);

        // Someone else modifies the record, so the ETag read above is now stale.
        await _fixture.Client.Entity<Account>(id).Set(x => x.Revenue, 1500000m).UpdateAsync();

        var exception = await Assert.ThrowsAsync<DataverseException>(() =>
            _fixture.Client.Entity<Account>(id)
                .WithETag(staleETag)
                .Set(x => x.Revenue, 9999999m)
                .UpsertAsync(UpsertConflictResolution.IfUnmodifiedSince));

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
        Assert.Equal(1500000m, await GetRevenueAsync(id)); // the stale write was not applied
    }

    [Fact]
    public async Task UpdateAsync_WithStaleETag_ThrowsPreconditionFailed()
    {
        var (id, staleETag) = await CreateAccountWithETagAsync("ETag Stale Update", 1000000m);
        await _fixture.Client.Entity<Account>(id).Set(x => x.Revenue, 1500000m).UpdateAsync();

        var exception = await Assert.ThrowsAsync<DataverseException>(() =>
            _fixture.Client.Entity<Account>(id)
                .WithETag(staleETag)
                .Set(x => x.Revenue, 9999999m)
                .UpdateAsync());

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
        Assert.Equal(1500000m, await GetRevenueAsync(id));
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public async Task UpsertAsync_MultipleOperations_WithDifferentStrategies()
    {
        // Scenario: Demonstrate using different strategies in a workflow
        var accountName = $"Complex Scenario {Guid.NewGuid()}";

        // Step 1: Create with standard create (not upsert) to ensure record exists
        var created = (await _fixture.Client.Entity<Account>()
            .Set(x => x.Name, accountName)
            .Set(x => x.Revenue, 1000000)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdAccountIds.Add(created.AccountId);
        Assert.Equal(1000000m, created.Revenue);

        // Step 2: Update with PreventCreate (ensure exists - should succeed)
        var updated = (await _fixture.Client.Entity<Account>()
            .WithId(created.AccountId)
            .Set(x => x.Revenue, 2000000)
            .ReturnRepresentation()
            .UpsertAsync(UpsertConflictResolution.PreventCreate)).Entity;

        Assert.Equal(2000000m, updated.Revenue);

        // Step 3: Standard upsert (works for existing record)
        var upserted = (await _fixture.Client.Entity<Account>()
            .WithId(created.AccountId)
            .Set(x => x.Revenue, 3000000)
            .ReturnRepresentation()
            .UpsertAsync()).Entity; // Default strategy

        Assert.Equal(3000000m, upserted.Revenue);
        
        // Note: PreventUpdate behavior on existing records is tested separately in
        // UpsertAsync_PreventUpdate_WithExistingRecord_ThrowsPreconditionFailed
        // We don't test it here to avoid version-dependent behavior issues
    }

    [Fact]
    public async Task UpsertAsync_ConcurrentModification_SimulatedConflict()
    {
        // Two writers read the same version; only the first write succeeds.
        var (id, etag) = await CreateAccountWithETagAsync("ETag Concurrent", 1000000m);

        await _fixture.Client.Entity<Account>(id)
            .WithETag(etag)
            .Set(x => x.Revenue, 2000000m)
            .UpsertAsync(UpsertConflictResolution.IfUnmodifiedSince);

        var exception = await Assert.ThrowsAsync<DataverseException>(() =>
            _fixture.Client.Entity<Account>(id)
                .WithETag(etag)
                .Set(x => x.Revenue, 3000000m)
                .UpsertAsync(UpsertConflictResolution.IfUnmodifiedSince));

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
        Assert.Equal(2000000m, await GetRevenueAsync(id));
    }

    [Fact]
    public async Task UpsertAsync_RetryPattern_WithOptimisticConcurrency()
    {
        var (id, etag) = await CreateAccountWithETagAsync("ETag Retry", 1000000m);
        await _fixture.Client.Entity<Account>(id).Set(x => x.Revenue, 1500000m).UpdateAsync(); // concurrent change

        // Read-modify-write loop: on 412, re-read the current version and try again.
        var attempts = 0;
        while (true)
        {
            attempts++;
            try
            {
                await _fixture.Client.Entity<Account>(id)
                    .WithETag(etag)
                    .Set(x => x.Revenue, 2500000m)
                    .UpsertAsync(UpsertConflictResolution.IfUnmodifiedSince);
                break;
            }
            catch (DataverseException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed && attempts < 3)
            {
                etag = await GetETagAsync(id);
            }
        }

        Assert.Equal(2, attempts); // the first attempt used the stale ETag
        Assert.Equal(2500000m, await GetRevenueAsync(id));
    }

    #endregion

    #region Integration with SetFrom

    [Fact]
    public async Task UpsertAsync_WithSetFrom_AndConflictResolution()
    {
        // Arrange
        var accountData = new Account
        {
            Name = $"SetFrom Upsert Test {Guid.NewGuid()}",
            Revenue = 2000000,
            NumberOfEmployees = 100
        };
        
        var newId = Guid.NewGuid();

        // Act: Create using SetFrom with PreventUpdate
        var created = (await _fixture.Client.Entity<Account>()
            .WithId(newId)
            .SetFrom(accountData)
            .ReturnRepresentation()
            .UpsertAsync(UpsertConflictResolution.PreventUpdate)).Entity;

        _createdAccountIds.Add(created.AccountId);

        // Assert
        Assert.NotNull(created);
        Assert.Equal(accountData.Name, created.Name);
        Assert.Equal(accountData.Revenue, created.Revenue);

        // Now update with PreventCreate
        accountData.Revenue = 3000000;
        var updated = (await _fixture.Client.Entity<Account>()
            .WithId(created.AccountId)
            .SetFrom(accountData)
            .ReturnRepresentation()
            .UpsertAsync(UpsertConflictResolution.PreventCreate)).Entity;

        Assert.Equal(3000000m, updated.Revenue);
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        // Cleanup all created accounts
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
