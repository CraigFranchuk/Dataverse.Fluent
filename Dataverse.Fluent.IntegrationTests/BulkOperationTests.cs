using System.Diagnostics;
using Dataverse.Fluent.IntegrationTests.Models;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.IntegrationTests;

// Records created with CreateMultiple are deleted by the fixture when the run ends (see RecordTrackingTransport).
// UpsertMultiple does not report which records it created, so the upsert test deletes its records itself.
[Collection("Dataverse Integration Tests")]
public class BulkOperationTests : IClassFixture<DataverseTestFixture>
{
    private const string PerformanceSwitch = "DATAVERSE_RUN_PERFORMANCE_TESTS";

    private readonly DataverseTestFixture _fixture;

    public BulkOperationTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateMultiple_CreatesEveryRecord_AndReturnsTheIdsInOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var accounts = NewAccounts("Bulk Create", 5);

        var result = await _fixture.Client.Bulk<Account>().ChunkSize(2).CreateAsync(accounts, ct);

        result.ThrowIfAnyFailed();
        Assert.Equal(3, result.Chunks.Count);
        Assert.Equal(5, result.Ids.Count);
        for (var i = 0; i < accounts.Count; i++)
        {
            var created = await _fixture.Client.Entity<Account>(result.Ids[i]).Select(a => a.Name).RetrieveAsync(ct);
            Assert.Equal(accounts[i].Name, created!.Name);
        }
    }

    [Fact]
    public async Task UpdateMultiple_UpdatesEveryRecord()
    {
        var ct = TestContext.Current.CancellationToken;
        var originals = NewAccounts("Bulk Update", 3);
        originals.ForEach(a => a.PhoneNumber = "555-0100");
        var created = await _fixture.Client.Bulk<Account>().CreateAsync(originals, ct);
        created.ThrowIfAnyFailed();

        // Account.Name defaults to "" rather than null, so it is always sent; set it deliberately. PhoneNumber is null
        // here, so it is left out of the payload and must keep its value.
        var updates = created.Ids
            .Select((id, i) => new Account { AccountId = id, Name = $"{originals[i].Name} Updated", Revenue = 1000m * (i + 1) })
            .ToList();
        var result = await _fixture.Client.Bulk<Account>().UpdateAsync(updates, a => a.AccountId, ct);

        result.ThrowIfAnyFailed();
        for (var i = 0; i < updates.Count; i++)
        {
            var account = await _fixture.Client.Entity<Account>(updates[i].AccountId)
                .Select(a => new { a.Name, a.Revenue, a.PhoneNumber })
                .RetrieveAsync(ct);
            Assert.Equal(updates[i].Name, account!.Name);
            Assert.Equal(1000m * (i + 1), account.Revenue);
            Assert.Equal("555-0100", account.PhoneNumber);
        }
    }

    [Fact]
    public async Task UpsertMultiple_ByPrimaryKey_UpdatesExistingRecords_AndCreatesNewOnes()
    {
        var ct = TestContext.Current.CancellationToken;
        var created = await _fixture.Client.Bulk<Account>().CreateAsync(NewAccounts("Bulk Upsert Existing", 1), ct);
        created.ThrowIfAnyFailed();
        var existingId = created.Ids[0];
        var newId = Guid.NewGuid();
        var newName = $"Test Account Bulk Upsert New {Guid.NewGuid()}";

        try
        {
            var result = await _fixture.Client.Bulk<Account>().UpsertAsync(
            [
                new Account { AccountId = existingId, Name = "Test Account Bulk Upsert Updated" },
                new Account { AccountId = newId, Name = newName }
            ], a => a.AccountId, ct);

            result.ThrowIfAnyFailed();
            var existing = await _fixture.Client.Entity<Account>(existingId).Select(a => a.Name).RetrieveAsync(ct);
            var inserted = await _fixture.Client.Entity<Account>(newId).Select(a => a.Name).RetrieveAsync(ct);
            Assert.Equal("Test Account Bulk Upsert Updated", existing!.Name);
            Assert.Equal(newName, inserted!.Name);
        }
        finally
        {
            await DeleteIfExistsAsync(newId);
        }
    }

    [Fact]
    public async Task AFailedChunk_IsRolledBack_AndEarlierChunksStayWritten()
    {
        // On a standard table each chunk is one transaction.
        var ct = TestContext.Current.CancellationToken;
        var run = Guid.NewGuid().ToString("N")[..8];
        var accounts = Enumerable.Range(0, 4)
            .Select(i => new AccountWithContact { Name = $"Test Account Bulk Rollback {run} {i}" })
            .ToList();
        // A lookup to a contact that does not exist fails in any environment (column lengths and ranges can be
        // customized, so an over-long value is not reliably invalid).
        accounts[3].PrimaryContactId = Guid.NewGuid();

        var result = await _fixture.Client.Bulk<AccountWithContact>().ChunkSize(2).CreateAsync(accounts, ct);

        Assert.Equal([BulkChunkStatus.Succeeded, BulkChunkStatus.Failed], result.Chunks.Select(c => c.Status));
        Assert.IsType<DataverseException>(result.Chunks[1].Error);
        Assert.Equal(2, result.Ids.Count);
        // The valid record in the failed chunk was rolled back with it.
        Assert.Empty(await _fixture.Client.Query<Account>().Where(a => a.Name == accounts[2].Name).GetListAsync(ct));
        Assert.Throws<BulkOperationException>(result.ThrowIfAnyFailed);
    }

    [Fact]
    public async Task CreateMultiple_InParallel_WithBusinessLogicBypassed()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _fixture.Client
            .WithOptions(o => o.BypassBusinessLogic = BusinessLogicBypass.CustomSync)
            .Bulk<Account>()
            .ChunkSize(3)
            .MaxDegreeOfParallelism(3)
            .CreateAsync(NewAccounts("Bulk Parallel", 9), ct);

        result.ThrowIfAnyFailed();
        Assert.Equal(9, result.Ids.Distinct().Count());
    }

    /// <summary>
    /// Compares the time to create the same number of records with <c>$batch</c> and with <c>CreateMultiple</c>.
    /// Creates 1,500 records, so it runs only when <c>DATAVERSE_RUN_PERFORMANCE_TESTS=true</c>; the timings are written
    /// to the test output.
    /// </summary>
    [Fact]
    [Trait("Category", "Performance")]
    public async Task Performance_CreateMultiple_ComparedWithBatch()
    {
        Assert.SkipUnless(
            string.Equals(Environment.GetEnvironmentVariable(PerformanceSwitch), "true", StringComparison.OrdinalIgnoreCase),
            $"Set {PerformanceSwitch}=true to compare bulk operations with $batch.");

        const int Records = 500;
        const int PerRequest = 100;
        var ct = TestContext.Current.CancellationToken;
        var output = TestContext.Current.TestOutputHelper;

        var batchTime = Stopwatch.StartNew();
        foreach (var chunk in NewAccounts("Perf Batch", Records).Chunk(PerRequest))
        {
            var batch = _fixture.Client.Batch();
            foreach (var account in chunk)
                batch.AddCreate(account);
            (await batch.ExecuteAsync(ct)).ThrowIfAnyFailed();
        }
        batchTime.Stop();

        var bulkTime = Stopwatch.StartNew();
        (await _fixture.Client.Bulk<Account>().ChunkSize(PerRequest).CreateAsync(NewAccounts("Perf Bulk", Records), ct)).ThrowIfAnyFailed();
        bulkTime.Stop();

        var parallelTime = Stopwatch.StartNew();
        (await _fixture.Client.Bulk<Account>().ChunkSize(PerRequest).MaxDegreeOfParallelism(4)
            .CreateAsync(NewAccounts("Perf Bulk Parallel", Records), ct)).ThrowIfAnyFailed();
        parallelTime.Stop();

        output?.WriteLine($"{Records} accounts, {PerRequest} per request:");
        output?.WriteLine($"  $batch (sequential):         {batchTime.Elapsed.TotalSeconds,6:F1} s  {Records / batchTime.Elapsed.TotalSeconds,7:F0} records/s");
        output?.WriteLine($"  CreateMultiple (sequential): {bulkTime.Elapsed.TotalSeconds,6:F1} s  {Records / bulkTime.Elapsed.TotalSeconds,7:F0} records/s");
        output?.WriteLine($"  CreateMultiple (4 parallel): {parallelTime.Elapsed.TotalSeconds,6:F1} s  {Records / parallelTime.Elapsed.TotalSeconds,7:F0} records/s");
    }

    [DataverseEntity("account", "accounts")]
    private sealed class AccountWithContact
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }

        [DataverseLookup("primarycontactid", "contacts")]
        public Guid? PrimaryContactId { get; set; }
    }

    private static List<Account> NewAccounts(string label, int count)
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        return [.. Enumerable.Range(0, count).Select(i => new Account { Name = $"Test Account {label} {run} {i}" })];
    }

    private async Task DeleteIfExistsAsync(Guid id)
    {
        try
        {
            await _fixture.Client.Entity<Account>(id).DeleteAsync();
        }
        catch (DataverseException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
        }
    }
}
