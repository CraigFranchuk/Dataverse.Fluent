using Dataverse.Fluent.IntegrationTests.Models;

namespace Dataverse.Fluent.IntegrationTests;

[Collection("Dataverse Integration Tests")]
public class QueryTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public QueryTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanQuery_WithDataverseInFunction_AndAnArrayVariable()
    {
        // An int[] passed to DataverseFunctions.In is flattened into PropertyValues=['0','1'].
        int[] states = [0, 1];

        var accounts = await _fixture.Client.Query<Account>()
            .Where(a => Dataverse.Fluent.Builders.DataverseFunctions.In(a.StateCode, states))
            .Select(a => new { a.AccountId, a.StateCode })
            .Top(10)
            .GetListAsync();

        Assert.All(accounts, a => Assert.Contains(a.StateCode, states));
    }

    [Fact]
    public async Task CanQuery_Accounts_WithTop()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .Top(5)
            .GetListAsync();

        Assert.NotNull(accounts);
        Assert.True(accounts.Count <= 5);
    }

    [Fact]
    public async Task CanQuery_Accounts_WithSelect()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .Select("accountid", "name")
            .Top(3)
            .GetListAsync();

        Assert.NotNull(accounts);
        Assert.True(accounts.Count <= 3);
        
        if (accounts.Count > 0)
        {
            Assert.NotEqual(Guid.Empty, accounts[0].AccountId);
            Assert.NotEmpty(accounts[0].Name);
        }
    }

    [Fact]
    public async Task CanQuery_Accounts_WithStringWhere()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .Where("statecode eq 0")
            .Top(5)
            .GetListAsync();

        Assert.NotNull(accounts);
        
        if (accounts.Count > 0)
        {
            Assert.All(accounts, a => Assert.Equal(0, a.StateCode));
        }
    }

    [Fact]
    public async Task CanQuery_Accounts_WithLambdaWhere()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .Where(a => a.StateCode == 0)
            .Top(5)
            .GetListAsync();

        Assert.NotNull(accounts);
        
        if (accounts.Count > 0)
        {
            Assert.All(accounts, a => Assert.Equal(0, a.StateCode));
        }
    }

    [Fact]
    public async Task CanQuery_Accounts_WithComplexWhere()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .Where(a => a.StateCode == 0 && a.Revenue > 0)
            .Top(5)
            .GetListAsync();

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
    public async Task CanQuery_Accounts_WithOrderBy()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .OrderBy(a => a.Name)
            .Top(10)
            .GetListAsync();

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
    public async Task CanQuery_Accounts_WithOrderByDescending()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .OrderBy(a => a.CreatedOn, descending: true)
            .Top(5)
            .GetListAsync();

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
    public async Task CanQuery_Contacts()
    {
        var contacts = await _fixture.Client.Query<Contact>()
            .Select("contactid", "firstname", "lastname", "emailaddress1")
            .Top(5)
            .GetListAsync();

        Assert.NotNull(contacts);
        Assert.True(contacts.Count <= 5);
        
        if (contacts.Count > 0)
        {
            Assert.NotEqual(Guid.Empty, contacts[0].ContactId);
        }
    }

    [Fact]
    public async Task CanQuery_Contacts_WithStringContains()
    {
        var contacts = await _fixture.Client.Query<Contact>()
            .Where(c => c.LastName.Contains("Smith") || c.LastName.Contains("Johnson"))
            .Top(10)
            .GetListAsync();

        Assert.NotNull(contacts);
    }

    [Fact]
    public async Task CanStream_Accounts()
    {
        var count = 0;
        var maxToStream = 10;

        await foreach (var account in _fixture.Client.Query<Account>()
            .Where(a => a.StateCode == 0)
            .StreamAsync(pageSize: 5))
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
    public async Task CanQuery_WithCount()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .Count(true)
            .Top(5)
            .GetListAsync();

        Assert.NotNull(accounts);
    }

    [Fact]
    public async Task CanQuery_WithSelectExpression()
    {
        var accounts = await _fixture.Client.Query<Account>()
            .Select(a => new { a.AccountId, a.Name, a.Revenue })
            .Top(5)
            .GetListAsync();

        Assert.NotNull(accounts);
        
        if (accounts.Count > 0)
        {
            Assert.NotEqual(Guid.Empty, accounts[0].AccountId);
            Assert.NotEmpty(accounts[0].Name);
        }
    }
}
