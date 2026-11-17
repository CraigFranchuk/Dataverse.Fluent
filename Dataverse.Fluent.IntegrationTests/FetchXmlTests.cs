using Dataverse.Fluent.Builders;
using Dataverse.Fluent.IntegrationTests.Models;

namespace Dataverse.Fluent.IntegrationTests;

[Collection("Dataverse Integration Tests")]
public class FetchXmlTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public FetchXmlTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region Basic FetchBuilder Tests

    [Fact]
    public async Task FetchBuilder_CanQuery_Accounts_WithAllAttributes()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .Top(5)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        Assert.True(accounts.Count <= 5);
        
        if (accounts.Count > 0)
        {
            Assert.NotEqual(Guid.Empty, accounts[0].AccountId);
            Assert.NotEmpty(accounts[0].Name);
        }
    }

    [Fact]
    public async Task FetchBuilder_CanQuery_Accounts_WithSpecificAttributes()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAttributes(a => new { a.AccountId, a.Name, a.Revenue })
            .Top(5)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        Assert.True(accounts.Count <= 5);
        
        if (accounts.Count > 0)
        {
            Assert.NotEqual(Guid.Empty, accounts[0].AccountId);
            Assert.NotEmpty(accounts[0].Name);
        }
    }

    [Fact]
    public async Task FetchBuilder_CanQuery_Accounts_WithSingleAttribute()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAttribute(a => a.Name)
            .AddAttribute(a => a.AccountId)
            .Top(5)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        Assert.True(accounts.Count <= 5);
        
        if (accounts.Count > 0)
        {
            Assert.NotEqual(Guid.Empty, accounts[0].AccountId);
            Assert.NotEmpty(accounts[0].Name);
        }
    }

    #endregion

    #region Filter Tests

    [Fact]
    public async Task FetchBuilder_CanFilter_ByStateCode()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddFilter(a => a.StateCode == 0)
            .Top(5)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        
        if (accounts.Count > 0)
        {
            Assert.All(accounts, a => Assert.Equal(0, a.StateCode));
        }
    }

    [Fact]
    public async Task FetchBuilder_CanFilter_WithComplexCondition()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddFilter(a => a.StateCode == 0 && a.Revenue > 0)
            .Top(5)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        
        if (accounts.Count > 0)
        {
            Assert.All(accounts, a =>
            {
                Assert.Equal(0, a.StateCode);
                Assert.True(a.Revenue > 0);
            });
        }
    }

    [Fact]
    public async Task FetchBuilder_CanFilter_WithOrCondition()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddFilter(a => a.StateCode == 0 || a.StateCode == 1)
            .Top(10)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        
        if (accounts.Count > 0)
        {
            Assert.All(accounts, a => Assert.True(a.StateCode == 0 || a.StateCode == 1));
        }
    }

    [Fact]
    public async Task FetchBuilder_CanFilter_WithStringContains()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddFilter(a => a.Name.Contains("a") || a.Name.Contains("A"))
            .Top(5)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        
        if (accounts.Count > 0)
        {
            Assert.All(accounts, a => 
                Assert.True(a.Name.Contains("a", StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public async Task FetchBuilder_CanFilter_WithNotNull()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddFilter(a => a.Revenue != null)
            .Top(5)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        
        if (accounts.Count > 0)
        {
            Assert.All(accounts, a => Assert.NotNull(a.Revenue));
        }
    }

    [Fact]
    public async Task FetchBuilder_CanFilter_WithGreaterThan()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddFilter(a => a.Revenue > 100000)
            .Top(5)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        
        if (accounts.Count > 0)
        {
            Assert.All(accounts, a => Assert.True(a.Revenue > 100000));
        }
    }

    [Fact]
    public async Task FetchBuilder_CanFilter_WithMultipleFilters()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddFilter(a => a.StateCode == 0)
            .AddFilter(a => a.Revenue > 0)
            .AddFilter(a => a.Name.Contains("a"))
            .Top(5)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        
        if (accounts.Count > 0)
        {
            Assert.All(accounts, a =>
            {
                Assert.Equal(0, a.StateCode);
                Assert.True(a.Revenue > 0);
                Assert.Contains("a", a.Name, StringComparison.OrdinalIgnoreCase);
            });
        }
    }

    #endregion

    #region Ordering Tests

    [Fact]
    public async Task FetchBuilder_CanOrder_ByName_Ascending()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddOrder(a => a.Name)
            .Top(10)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        
        if (accounts.Count > 1)
        {
            for (int i = 0; i < accounts.Count - 1; i++)
            {
                Assert.True(
                    string.Compare(accounts[i].Name, accounts[i + 1].Name, StringComparison.OrdinalIgnoreCase) <= 0,
                    $"Expected {accounts[i].Name} <= {accounts[i + 1].Name}");
            }
        }
    }

    [Fact]
    public async Task FetchBuilder_CanOrder_ByCreatedOn_Descending()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddOrder(a => a.CreatedOn, descending: true)
            .Top(10)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        
        if (accounts.Count > 1)
        {
            for (int i = 0; i < accounts.Count - 1; i++)
            {
                Assert.True(
                    accounts[i].CreatedOn >= accounts[i + 1].CreatedOn,
                    $"Expected {accounts[i].CreatedOn} >= {accounts[i + 1].CreatedOn}");
            }
        }
    }

    [Fact]
    public async Task FetchBuilder_CanOrder_WithMultipleOrders()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddFilter(a => a.StateCode == 0)
            .AddOrder(a => a.StateCode)
            .AddOrder(a => a.Name)
            .Top(10)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        Assert.True(accounts.Count <= 10);
    }

    #endregion

    #region Performance Options Tests

    [Fact]
    public async Task FetchBuilder_CanUse_NoLock()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .NoLock()
            .Top(5)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        Assert.True(accounts.Count <= 5);
    }

    [Fact]
    public async Task FetchBuilder_CanUse_Distinct()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAttribute(a => a.StateCode)
            .Distinct()
            .ExecuteAsync();

        Assert.NotNull(accounts);
    }

    [Fact]
    public async Task FetchBuilder_CanUse_Top()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .Top(3)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        Assert.True(accounts.Count <= 3);
    }

    [Fact]
    public async Task FetchBuilder_CanUse_Page()
    {
        var page1 = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddOrder(a => a.Name)
            .Page(1, 5)
            .ExecuteAsync();

        var page2 = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddOrder(a => a.Name)
            .Page(2, 5)
            .ExecuteAsync();

        Assert.NotNull(page1);
        Assert.NotNull(page2);
        Assert.True(page1.Count <= 5);
        Assert.True(page2.Count <= 5);
        
        // If both pages have data, they should be different
        if (page1.Count > 0 && page2.Count > 0)
        {
            Assert.NotEqual(page1[0].AccountId, page2[0].AccountId);
        }
    }

    #endregion

    #region Streaming Tests

    [Fact]
    public async Task FetchBuilder_CanStream_Accounts()
    {
        var count = 0;
        var maxToStream = 15;

        await foreach (var account in _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddFilter(a => a.StateCode == 0)
            .StreamAsync())
        {
            Assert.NotEqual(Guid.Empty, account.AccountId);
            Assert.NotEmpty(account.Name);
            
            count++;
            if (count >= maxToStream)
                break;
        }

        Assert.True(count > 0, "Should have streamed at least one account");
    }

    [Fact]
    public async Task FetchBuilder_StreamingHandles_LargeDataset()
    {
        var count = 0;
        var maxToStream = 50;
        var accountIds = new HashSet<Guid>();

        await foreach (var account in _fixture.Client.FetchBuilder<Account>()
            .AddAttribute(a => a.AccountId)
            .AddAttribute(a => a.Name)
            .NoLock()
            .StreamAsync())
        {
            Assert.NotEqual(Guid.Empty, account.AccountId);
            accountIds.Add(account.AccountId);
            
            count++;
            if (count >= maxToStream)
                break;
        }

        Assert.True(count > 0, "Should have streamed at least one account");
        Assert.Equal(count, accountIds.Count); // All IDs should be unique
    }

    [Fact]
    public async Task FetchBuilder_StreamingWith_Cancellation()
    {
        using var cts = new CancellationTokenSource();
        
        var count = 0;
        var targetCount = 10;

        await foreach (var account in _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .StreamAsync(cts.Token))
        {
            count++;
            
            // Cancel after processing a few records to test cancellation
            if (count == 5)
            {
                cts.Cancel();
            }
            
            // Break after target count or if cancelled
            if (count >= targetCount || cts.Token.IsCancellationRequested)
                break;
        }

        // Should have processed at least some records before cancellation/break
        Assert.True(count >= 5, "Should have processed at least 5 records");
    }

    #endregion

    #region BuildFetchXml Tests

    [Fact]
    public void FetchBuilder_CanBuild_FetchXmlString()
    {
        var fetchXml = _fixture.Client.FetchBuilder<Account>()
            .AddAttributes(a => new { a.Name, a.Revenue })
            .AddFilter(a => a.StateCode == 0)
            .AddOrder(a => a.Name)
            .Top(10)
            .NoLock()
            .BuildFetchXml();

        Assert.NotEmpty(fetchXml);
        Assert.Contains("<fetch", fetchXml);
        Assert.Contains("top=\"10\"", fetchXml);
        Assert.Contains("no-lock=\"true\"", fetchXml);
        Assert.Contains("<entity name=\"account\">", fetchXml);
        Assert.Contains("<attribute name=\"name\"", fetchXml);
        Assert.Contains("<attribute name=\"revenue\"", fetchXml);
        Assert.Contains("<order attribute=\"name\"", fetchXml);
    }

    [Fact]
    public void FetchBuilder_BuildsFetchXml_WithDistinct()
    {
        var fetchXml = _fixture.Client.FetchBuilder<Account>()
            .AddAttribute(a => a.StateCode)
            .Distinct()
            .BuildFetchXml();

        Assert.Contains("distinct=\"true\"", fetchXml);
    }

    [Fact]
    public void FetchBuilder_BuildsFetchXml_WithPage()
    {
        var fetchXml = _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .Page(2, 50)
            .BuildFetchXml();

        Assert.Contains("page=\"2\"", fetchXml);
        Assert.Contains("count=\"50\"", fetchXml);
    }

    #endregion

    #region Raw FetchXML Tests

    [Fact]
    public async Task RawFetchXml_CanExecute_SimpleQuery()
    {
        string fetchXml = @"
<fetch top='5'>
  <entity name='account'>
    <attribute name='accountid' />
    <attribute name='name' />
    <attribute name='revenue' />
  </entity>
</fetch>";

        var accounts = await _fixture.Client.Fetch<Account>(fetchXml).ExecuteAsync();

        Assert.NotNull(accounts);
        Assert.True(accounts.Count <= 5);
        
        if (accounts.Count > 0)
        {
            Assert.NotEqual(Guid.Empty, accounts[0].AccountId);
            Assert.NotEmpty(accounts[0].Name);
        }
    }

    [Fact]
    public async Task RawFetchXml_CanExecute_WithFilter()
    {
        string fetchXml = @"
<fetch top='10'>
  <entity name='account'>
    <attribute name='accountid' />
    <attribute name='name' />
    <attribute name='statecode' />
    <filter type='and'>
      <condition attribute='statecode' operator='eq' value='0' />
    </filter>
  </entity>
</fetch>";

        var accounts = await _fixture.Client.Fetch<Account>(fetchXml).ExecuteAsync();

        Assert.NotNull(accounts);
        
        if (accounts.Count > 0)
        {
            Assert.All(accounts, a => Assert.Equal(0, a.StateCode));
        }
    }

    [Fact]
    public async Task RawFetchXml_CanExecute_WithOrdering()
    {
        string fetchXml = @"
<fetch top='10'>
  <entity name='account'>
    <attribute name='accountid' />
    <attribute name='name' />
    <attribute name='createdon' />
    <order attribute='createdon' descending='true' />
  </entity>
</fetch>";

        var accounts = await _fixture.Client.Fetch<Account>(fetchXml).ExecuteAsync();

        Assert.NotNull(accounts);
        
        if (accounts.Count > 1)
        {
            for (int i = 0; i < accounts.Count - 1; i++)
            {
                Assert.True(accounts[i].CreatedOn >= accounts[i + 1].CreatedOn);
            }
        }
    }

    [Fact]
    public async Task RawFetchXml_CanExecute_WithNoLock()
    {
        string fetchXml = @"
<fetch top='5' no-lock='true'>
  <entity name='account'>
    <attribute name='accountid' />
    <attribute name='name' />
  </entity>
</fetch>";

        var accounts = await _fixture.Client.Fetch<Account>(fetchXml).ExecuteAsync();

        Assert.NotNull(accounts);
        Assert.True(accounts.Count <= 5);
    }

    [Fact]
    public async Task RawFetchXml_CanExecute_WithNoLockOption_AcrossPages()
    {
        // The NoLock option adds no-lock="true" to every page request of a paged query.
        string fetchXml = @"
<fetch>
  <entity name='account'>
    <attribute name='accountid' />
    <attribute name='name' />
  </entity>
</fetch>";

        var accounts = new List<Account>();
        await foreach (var account in _fixture.Client.Fetch<Account>(fetchXml, new FetchXmlOptions { NoLock = true, MaxPageSize = 2 }).StreamAsync())
        {
            accounts.Add(account);
            if (accounts.Count == 5) break;
        }

        Assert.InRange(accounts.Count, 1, 5);
        Assert.Equal(accounts.Count, accounts.Select(a => a.AccountId).Distinct().Count()); // pages do not overlap
    }

    [Fact]
    public async Task RawFetchXml_CanExecute_WithOptions()
    {
        string fetchXml = @"
<fetch top='5'>
  <entity name='account'>
    <attribute name='accountid' />
    <attribute name='name' />
  </entity>
</fetch>";

        var accounts = await _fixture.Client.Fetch<Account>(fetchXml)
            .WithOptions(opts =>
            {
                opts.IncludeFormattedValues = true;
                opts.MaxPageSize = 5000;
            })
            .ExecuteAsync();

        Assert.NotNull(accounts);
        Assert.True(accounts.Count <= 5);
    }

    [Fact]
    public async Task RawFetchXml_CanStream_Results()
    {
        string fetchXml = @"
<fetch>
  <entity name='account'>
    <attribute name='accountid' />
    <attribute name='name' />
    <filter type='and'>
      <condition attribute='statecode' operator='eq' value='0' />
    </filter>
  </entity>
</fetch>";

        var count = 0;
        var maxToStream = 10;

        await foreach (var account in _fixture.Client.Fetch<Account>(fetchXml).StreamAsync())
        {
            Assert.NotEqual(Guid.Empty, account.AccountId);
            
            count++;
            if (count >= maxToStream)
                break;
        }

        Assert.True(count > 0);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public async Task FetchBuilder_ComplexQuery_WithMultipleConditions()
    {
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAttributes(a => new 
            { 
                a.AccountId, 
                a.Name, 
                a.Revenue, 
                a.NumberOfEmployees,
                a.CreatedOn 
            })
            .AddFilter(a => 
                (a.Revenue > 100000 && a.NumberOfEmployees > 10) ||
                (a.Revenue > 500000))
            .AddFilter(a => a.StateCode == 0)
            .AddOrder(a => a.Revenue, descending: true)
            .AddOrder(a => a.Name)
            .NoLock()
            .Top(25)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        Assert.True(accounts.Count <= 25);
        
        if (accounts.Count > 0)
        {
            Assert.All(accounts, a => Assert.Equal(0, a.StateCode));
        }
    }

    [Fact]
    public async Task FetchBuilder_CanQuery_Contacts()
    {
        var contacts = await _fixture.Client.FetchBuilder<Contact>()
            .AddAttributes(c => new { c.ContactId, c.FirstName, c.LastName })
            .AddFilter(c => c.StateCode == 0)
            .Top(5)
            .ExecuteAsync();

        Assert.NotNull(contacts);
        Assert.True(contacts.Count <= 5);
        
        if (contacts.Count > 0)
        {
            Assert.NotEqual(Guid.Empty, contacts[0].ContactId);
        }
    }

    [Fact]
    public async Task FetchBuilder_HandlesEmpty_Results()
    {
        // Create a filter that should return no results
        var accounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddFilter(a => a.Revenue > 999999999999)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        Assert.Empty(accounts);
    }

    [Fact]
    public async Task FetchBuilder_ComparingWith_QueryBuilder()
    {
        // Same query using both FetchBuilder and QueryBuilder
        var fetchAccounts = await _fixture.Client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddFilter(a => a.StateCode == 0)
            .AddOrder(a => a.Name)
            .Top(5)
            .ExecuteAsync();

        var queryAccounts = await _fixture.Client.Query<Account>()
            .Where(a => a.StateCode == 0)
            .OrderBy(a => a.Name)
            .Top(5)
            .GetListAsync();

        Assert.NotNull(fetchAccounts);
        Assert.NotNull(queryAccounts);
        
        // Both should return similar results (exact match depends on data)
        Assert.Equal(fetchAccounts.Count, queryAccounts.Count);
        
        if (fetchAccounts.Count > 0 && queryAccounts.Count > 0)
        {
            // First items should match when ordered the same way
            Assert.Equal(fetchAccounts[0].AccountId, queryAccounts[0].AccountId);
        }
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task FetchBuilder_HandlesTimeout_Gracefully()
    {
        using var cts = new CancellationTokenSource();
        
        // Cancel immediately to test cancellation handling
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            var accounts = await _fixture.Client.FetchBuilder<Account>()
                .AddAllAttributes()
                .ExecuteAsync(cts.Token);
        });
    }

    [Fact]
    public async Task RawFetchXml_WithInvalidFetchXml_ThrowsBadRequest()
    {
        string invalidFetchXml = "<invalid>xml</invalid>";

        // Dataverse rejects the query; any other exception would be a client-side failure.
        var exception = await Assert.ThrowsAsync<Dataverse.Fluent.Models.DataverseException>(async () =>
        {
            await _fixture.Client.Fetch<Account>(invalidFetchXml).ExecuteAsync();
        });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, exception.StatusCode);
    }

    #endregion

    #region Integration with Options

    [Fact]
    public async Task FetchBuilder_WithOptions_NoLockAndIncludeFormatted()
    {
        var options = new FetchXmlOptions
        {
            NoLock = true,
            IncludeFormattedValues = true,
            MaxPageSize = 100
        };

        var accounts = await _fixture.Client.FetchBuilder<Account>(options: options)
            .AddAllAttributes()
            .Top(5)
            .ExecuteAsync();

        Assert.NotNull(accounts);
        Assert.True(accounts.Count <= 5);
    }

    #endregion
}
