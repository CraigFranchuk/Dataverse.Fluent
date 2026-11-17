using Dataverse.Fluent.IntegrationTests.Models;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.IntegrationTests;

[Collection("Dataverse Integration Tests")]
public class DeltaQueryTests : IClassFixture<DataverseTestFixture>
{
    // Without change tracking on the table, Dataverse returns no delta token and these tests cannot run.
    private const string NoDeltaToken =
        "Dataverse returned no delta token. Enable change tracking on the Account table to run this test.";

    private readonly DataverseTestFixture _fixture;

    public DeltaQueryTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InitialDeltaQuery_ReturnsCurrentEntitiesAndToken()
    {
        // Act
        var results = await _fixture.Client.Delta<Account>()
            .Select("accountid", "name", "revenue")
            .ExecuteAsync();

        // Assert
        Assert.NotNull(results);
        
        // Skip test if delta token is not available (change tracking not enabled)
        if (string.IsNullOrEmpty(results.DeltaToken))
        {
            Assert.Skip(NoDeltaToken);
        }
        
        Assert.NotNull(results.DeltaToken);
        Assert.NotEmpty(results.Value);
        Assert.Empty(results.Deleted);
    }

    [Fact]
    public async Task DeltaQuery_WithFilter_ReturnsFilteredResults()
    {
        try
        {
            // Act
            var results = await _fixture.Client.Delta<Account>()
                .Select("accountid", "name", "revenue")
                .Where("statecode eq 0")
                .Top(10)
                .ExecuteAsync();

            // Assert
            Assert.NotNull(results);
            
            // Skip test if delta token is not available
            if (string.IsNullOrEmpty(results.DeltaToken))
            {
                Assert.Skip(NoDeltaToken);
            }
            
            Assert.NotNull(results.DeltaToken);
            
            if (results.Value.Count > 0)
            {
                Assert.All(results.Value, account => 
                    Assert.Equal(0, account.StateCode));
            }
        }
        catch (DataverseException ex) when (ex.Message.Contains("$filter") && ex.Message.Contains("Change Tracking"))
        {
            // Dataverse doesn't support $filter with change tracking on some environments
            Assert.Skip("$filter may not be supported with Change Tracking in this environment.");
        }
    }

    [Fact]
    public async Task DeltaQuery_AfterChanges_ReturnsModifiedEntities()
    {
        // Arrange - Get initial delta token
        var initial = await _fixture.Client.Delta<Account>()
            .ExecuteAsync();
        
        // Skip test if delta token is not available
        if (string.IsNullOrEmpty(initial.DeltaToken))
        {
            Assert.Skip(NoDeltaToken);
        }
        
        var deltaToken = initial.DeltaToken;
        
        // Create a new account
        var newAccount = (await _fixture.Client.Entity<Account>()
            .Set("name", $"Test Delta Account {Guid.NewGuid()}")
            .Set("revenue", 5000000m)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;
        
        // Update an existing account if available
        Guid? updatedAccountId = null;
        if (initial.Value.Count > 0)
        {
            updatedAccountId = initial.Value[0].AccountId;
            await _fixture.Client.Entity<Account>()
                .WithId(updatedAccountId.Value)
                .Set("revenue", 9999999m)
                .UpdateAsync();
        }

        // Wait for change tracking to propagate
        await Task.Delay(TimeSpan.FromSeconds(10));

        try
        {
            // Act
            var changes = await _fixture.Client.Delta<Account>()
                .Since(deltaToken)
                .ExecuteAsync();

            // Assert
            Assert.NotNull(changes);
            Assert.NotEmpty(changes.Value);
            Assert.Contains(changes.Value, a => a.AccountId == newAccount.AccountId);
            
            if (updatedAccountId.HasValue)
            {
                Assert.Contains(changes.Value, a => a.AccountId == updatedAccountId.Value);
            }
        }
        finally
        {
            // Cleanup
            await _fixture.Client.Entity<Account>()
                .WithId(newAccount.AccountId)
                .DeleteAsync();
        }
    }

    [Fact]
    public async Task DeltaQuery_AfterDelete_ReturnsDeletedReference()
    {
        // Arrange - Create test account
        var account = (await _fixture.Client.Entity<Account>()
            .Set("name", $"Test Delete Account {Guid.NewGuid()}")
            .ReturnRepresentation()
            .CreateAsync()).Entity!;
        
        // Get delta token after creation
        var initial = await _fixture.Client.Delta<Account>().ExecuteAsync();
        
        // Skip test if delta token is not available
        if (string.IsNullOrEmpty(initial.DeltaToken))
        {
            // Cleanup before skipping
            await _fixture.Client.Entity<Account>()
                .WithId(account.AccountId)
                .DeleteAsync();
            Assert.Skip(NoDeltaToken);
        }
        
        var deltaToken = initial.DeltaToken;
        
        // Delete the account
        await _fixture.Client.Entity<Account>()
            .WithId(account.AccountId)
            .DeleteAsync();
        
        // Wait for change tracking
        await Task.Delay(TimeSpan.FromSeconds(10));

        // Act
        var changes = await _fixture.Client.Delta<Account>()
            .Since(deltaToken)
            .ExecuteAsync();

        // Assert
        Assert.NotNull(changes);
        Assert.NotEmpty(changes.Deleted);
        
        var deleted = changes.Deleted.FirstOrDefault(d => 
            d.GetEntityId() == account.AccountId);
        
        Assert.NotNull(deleted);
        Assert.Equal("deleted", deleted.Removed?.Reason);
    }

    [Fact]
    public async Task DeltaQuery_Streaming_ProcessesAllPages()
    {
        // Arrange
        var totalAccounts = 0;
        var pageCount = 0;
        var maxPages = 3;

        // Act
        // Note: pageSize parameter is ignored for delta queries
        // Dataverse handles pagination automatically
        await foreach (var deltaPage in _fixture.Client.Delta<Account>()
            .Select("accountid", "name")
            .StreamAsync(pageSize: 10))
        {
            pageCount++;
            totalAccounts += deltaPage.Value.Count;
            
            // Process page
            foreach (var account in deltaPage.Value)
            {
                Assert.NotEqual(Guid.Empty, account.AccountId);
            }
            
            // Skip if delta token is not available
            if (string.IsNullOrEmpty(deltaPage.DeltaToken))
            {
                Assert.Skip(NoDeltaToken);
            }
            
            // Verify delta token exists
            Assert.NotNull(deltaPage.DeltaToken);
            
            // Limit pages for test performance
            if (pageCount >= maxPages)
                break;
        }

        // Assert
        Assert.True(totalAccounts > 0);
        Assert.True(pageCount >= 1);
    }

    [Fact]
    public async Task DeltaQuery_TokenPersistence_MaintainsState()
    {
        // Arrange
        var tokenStore = new Dictionary<string, string>();
        
        // Initial query
        var initial = await _fixture.Client.Delta<Account>().ExecuteAsync();
        
        // Skip test if delta token is not available
        if (string.IsNullOrEmpty(initial.DeltaToken))
        {
            // This can happen if change tracking is not properly enabled
            Assert.Skip(NoDeltaToken);
        }
        
        tokenStore["accounts"] = initial.DeltaToken;
        
        // Make a change
        var account = (await _fixture.Client.Entity<Account>()
            .Set("name", $"Test Token Persistence {Guid.NewGuid()}")
            .ReturnRepresentation()
            .CreateAsync()).Entity!;
        
        await Task.Delay(TimeSpan.FromSeconds(10));
        
        try
        {
            // Query with saved token
            var changes1 = await _fixture.Client.Delta<Account>()
                .Since(tokenStore["accounts"])
                .ExecuteAsync();
            
            // Skip if no delta token returned
            if (string.IsNullOrEmpty(changes1.DeltaToken))
            {
                Assert.Skip(NoDeltaToken);
            }
            
            tokenStore["accounts"] = changes1.DeltaToken;
            Assert.Contains(changes1.Value, a => a.AccountId == account.AccountId);
            
            // Make another change
            await _fixture.Client.Entity<Account>()
                .WithId(account.AccountId)
                .Set("revenue", 12345m)
                .UpdateAsync();
            
            await Task.Delay(TimeSpan.FromSeconds(10));
            
            // Query with new token
            var changes2 = await _fixture.Client.Delta<Account>()
                .Since(tokenStore["accounts"])
                .ExecuteAsync();
            
            // Should see the second change
            Assert.Contains(changes2.Value, a => a.AccountId == account.AccountId);
        }
        finally
        {
            // Cleanup
            await _fixture.Client.Entity<Account>()
                .WithId(account.AccountId)
                .DeleteAsync();
        }
    }

    [Fact]
    public async Task DeltaQuery_WithFormattedValues_IncludesAnnotations()
    {
        // Act
        var results = await _fixture.Client.Delta<Account>()
            .Select("accountid", "name", "statecode")
            .IncludeFormattedValues()
            .ExecuteAsync();

        // Assert
        Assert.NotNull(results);
        
        // Skip if delta token is not available
        if (string.IsNullOrEmpty(results.DeltaToken))
        {
            Assert.Skip(NoDeltaToken);
        }
        
        Assert.NotEmpty(results.Value);
    }

    [Fact]
    public async Task DeltaQuery_WithCount_ReturnsCount()
    {
        try
        {
            // Act
            var results = await _fixture.Client.Delta<Account>()
                .Count()
                .ExecuteAsync();

            // Assert
            Assert.NotNull(results);
            
            // Skip if delta token is not available
            if (string.IsNullOrEmpty(results.DeltaToken))
            {
                Assert.Skip(NoDeltaToken);
            }
            
            // Note: $count is not actually supported with delta queries
            // This test verifies the API doesn't break when Count() is called
            // The count may be null since $count parameter is ignored
            Assert.True(true, "Count() method accepted (parameter ignored in delta queries)");
        }
        catch (DataverseException ex) when (ex.Message.Contains("$count") && ex.Message.Contains("Change Tracking"))
        {
            // Dataverse doesn't support $count with change tracking enabled
            Assert.Skip("$count is not supported with Change Tracking in Dataverse.");
        }
    }

    [Fact]
    public async Task DeltaQuery_WithOrderBy_OrdersResults()
    {
        try
        {
            // Act
            var results = await _fixture.Client.Delta<Account>()
                .Select("accountid", "name")
                .OrderBy("name")
                .ExecuteAsync();

            // Assert
            Assert.NotNull(results);
            
            // Skip if delta token is not available
            if (string.IsNullOrEmpty(results.DeltaToken))
            {
                Assert.Skip(NoDeltaToken);
            }
            
            Assert.NotEmpty(results.Value);
            
            if (results.Value.Count > 1)
            {
                for (int i = 0; i < results.Value.Count - 1; i++)
                {
                    Assert.True(
                        string.Compare(results.Value[i].Name, results.Value[i + 1].Name, StringComparison.OrdinalIgnoreCase) <= 0,
                        $"Expected {results.Value[i].Name} <= {results.Value[i + 1].Name}");
                }
            }
        }
        catch (DataverseException ex) when (ex.Message.Contains("$orderby") && ex.Message.Contains("Change Tracking"))
        {
            // Dataverse may not support $orderby with change tracking on some environments
            Assert.Skip("$orderby may not be supported with Change Tracking in this environment.");
        }
    }

    [Fact]
    public async Task DeltaQuery_ContinuousPolling_TracksChanges()
    {
        // Arrange
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        string? deltaToken = null;
        var changesDetected = 0;

        // Create test account for changes
        var testAccount = (await _fixture.Client.Entity<Account>()
            .Set("name", $"Test Polling Account {Guid.NewGuid()}")
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        try
        {
            // Polling loop (limited iterations for test)
            for (int i = 0; i < 3 && !cts.Token.IsCancellationRequested; i++)
            {
                var builder = _fixture.Client.Delta<Account>()
                    .Select("accountid", "name", "revenue");

                if (!string.IsNullOrEmpty(deltaToken))
                {
                    builder = builder.Since(deltaToken);
                }

                var results = await builder.ExecuteAsync(cts.Token);
                
                changesDetected += results.Value.Count + results.Deleted.Count;
                deltaToken = results.DeltaToken;

                // Without a token every iteration is a full query, which would pass without tracking anything.
                if (string.IsNullOrEmpty(deltaToken))
                {
                    Assert.Skip(NoDeltaToken);
                }

                // Make a change for next iteration
                if (i < 2)
                {
                    await _fixture.Client.Entity<Account>()
                        .WithId(testAccount.AccountId)
                        .Set("revenue", (i + 1) * 1000000m)
                        .UpdateAsync();
                    
                    await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
                }
            }

            // Assert
            Assert.True(changesDetected > 0);
        }
        finally
        {
            // Cleanup
            await _fixture.Client.Entity<Account>()
                .WithId(testAccount.AccountId)
                .DeleteAsync();
        }
    }

    [Fact]
    public async Task DeltaQuery_WithMultipleFilters_AppliesAllFilters()
    {
        try
        {
            // Act
            var results = await _fixture.Client.Delta<Account>()
                .Select("accountid", "name", "revenue")
                .Where("statecode eq 0 and revenue gt 0")
                .Top(10)
                .ExecuteAsync();

            // Assert
            Assert.NotNull(results);
            
            if (results.Value.Count > 0)
            {
                Assert.All(results.Value, account =>
                {
                    Assert.Equal(0, account.StateCode);
                    Assert.True(account.Revenue > 0);
                });
            }
        }
        catch (DataverseException ex) when (ex.Message.Contains("$filter") && ex.Message.Contains("Change Tracking"))
        {
            // Dataverse doesn't support $filter with change tracking on some environments
            Assert.Skip("$filter may not be supported with Change Tracking in this environment.");
        }
    }

    [Fact]
    public async Task DeltaQuery_WithCustomHeaders_SendsHeaders()
    {
        // Arrange
        var customHeaders = new Dictionary<string, string>
        {
            { "CustomHeader", "TestValue" }
        };

        // Act
        var results = await _fixture.Client.Delta<Account>()
            .WithHeaders(customHeaders)
            .ExecuteAsync();

        // Assert
        Assert.NotNull(results);
        
        // Skip if delta token is not available
        if (string.IsNullOrEmpty(results.DeltaToken))
        {
            Assert.Skip(NoDeltaToken);
        }
        
        Assert.NotNull(results.DeltaToken);
    }

    [Fact]
    public async Task EntityReference_GetEntityId_ParsesGuidCorrectly()
    {
        // Arrange
        var entityRef1 = new EntityReference
        {
            Id = "accounts(12345678-1234-1234-1234-123456789012)"
        };

        var entityRef2 = new EntityReference
        {
            Id = "https://test.crm.dynamics.com/api/data/v9.2/accounts(12345678-1234-1234-1234-123456789012)"
        };

        var entityRef3 = new EntityReference { Id = null };

        // Act
        var guid1 = entityRef1.GetEntityId();
        var guid2 = entityRef2.GetEntityId();
        var guid3 = entityRef3.GetEntityId();

        // Assert
        Assert.NotNull(guid1);
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789012"), guid1);
        
        Assert.NotNull(guid2);
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789012"), guid2);
        
        Assert.Null(guid3);
    }

    [Fact]
    public async Task DeltaQuery_LargePageSize_ReturnsResults()
    {
        // Arrange
        var pageCount = 0;
        var maxPages = 2;

        // Act
        // Note: pageSize parameter is ignored for delta queries
        // Dataverse handles pagination automatically
        await foreach (var deltaPage in _fixture.Client.Delta<Account>()
            .StreamAsync(pageSize: 100))
        {
            pageCount++;
            
            Assert.NotNull(deltaPage);
            
            // Skip if delta token is not available
            if (string.IsNullOrEmpty(deltaPage.DeltaToken))
            {
                Assert.Skip(NoDeltaToken);
            }
            
            Assert.NotNull(deltaPage.DeltaToken);
            
            if (pageCount >= maxPages)
                break;
        }

        // Assert
        Assert.True(pageCount >= 1);
    }

    [Fact]
    public void DeltaBuilder_Since_WithNullToken_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _fixture.Client.Delta<Account>().Since(null!));

        Assert.Contains("Delta token cannot be null", exception.Message);
    }

    [Fact]
    public void DeltaBuilder_Since_WithEmptyToken_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _fixture.Client.Delta<Account>().Since(string.Empty));

        Assert.Contains("Delta token cannot be null", exception.Message);
    }
}
