using System.Net;

namespace Dataverse.Fluent.Tests.Builders;

public class EntityBuilderExpandTests
{
    private DataverseClient CreateTestClient(TestHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new DataverseClient(
            "https://test.crm.dynamics.com/api/data/v9.2",
            () => Task.FromResult("test-token"),
            httpClient
        );
    }

    [Fact]
    public async Task Expand_WithRawString_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains($"testaccounts({accountId})") &&
                       url.Contains("$expand=contact_customer_accounts");
            },
            new { accountid = accountId, name = "Test Account" }
        );

        var result = await client.Entity<TestAccount>()
            .WithId(accountId)
            .Expand("contact_customer_accounts")
            .RetrieveAsync();

        Assert.NotNull(result);
        Assert.Equal("Test Account", result.Name);
    }

    [Fact]
    public async Task Expand_WithNavigationPropertyOnly_BuildsSimpleExpand()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains("$expand=contact_customer_accounts"),
            new { accountid = accountId, name = "Test" }
        );

        var result = await client.Entity<TestAccount>()
            .WithId(accountId)
            .Expand<TestContact>("contact_customer_accounts")
            .RetrieveAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_WithNestedSelect_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req => req.DecodedUrl()
                .Contains("$expand=contact_customer_accounts($select=firstname,lastname)"),
            new { accountid = accountId, name = "Test" }
        );

        var result = await client.Entity<TestAccount>()
            .WithId(accountId)
            .Expand<TestContact>("contact_customer_accounts", e => e
                .Select(c => new { c.FirstName, c.LastName }))
            .RetrieveAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_WithNestedTop_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req => req.DecodedUrl()
                .Contains("$expand=contact_customer_accounts($top=3)"),
            new { accountid = accountId, name = "Test" }
        );

        var result = await client.Entity<TestAccount>()
            .WithId(accountId)
            .Expand<TestContact>("contact_customer_accounts", e => e.Top(3))
            .RetrieveAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_WithAllNestedOptions_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("$expand=contact_customer_accounts(") &&
                       url.Contains("$select=firstname,lastname") &&
                       url.Contains("$filter=") &&
                       url.Contains("$orderby=lastname asc") &&
                       url.Contains("$top=5");
            },
            new { accountid = accountId, name = "Test" }
        );

        var result = await client.Entity<TestAccount>()
            .WithId(accountId)
            .Expand<TestContact>("contact_customer_accounts", e => e
                .Select(c => new { c.FirstName, c.LastName })
                .Where(c => c.StateCode == 0)
                .OrderBy(c => c.LastName)
                .Top(5))
            .RetrieveAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_CombinedWithSelect_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("$select=name") &&
                       url.Contains("$expand=contact_customer_accounts");
            },
            new { accountid = accountId, name = "Test" }
        );

        var result = await client.Entity<TestAccount>()
            .WithId(accountId)
            .Select("name")
            .Expand("contact_customer_accounts")
            .RetrieveAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_MultipleNavigationProperties_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("$expand=contact_customer_accounts,opportunity_customer_accounts");
            },
            new { accountid = accountId, name = "Test" }
        );

        var result = await client.Entity<TestAccount>()
            .WithId(accountId)
            .Expand("contact_customer_accounts")
            .Expand("opportunity_customer_accounts")
            .RetrieveAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public void Expand_WithNullNavigationProperty_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        Assert.Throws<ArgumentException>(() =>
            client.Entity<TestAccount>()
                .WithId(Guid.NewGuid())
                .Expand<TestContact>(null!));
    }

    [Fact]
    public void Expand_WithEmptyNavigationProperty_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        Assert.Throws<ArgumentException>(() =>
            client.Entity<TestAccount>()
                .WithId(Guid.NewGuid())
                .Expand<TestContact>(""));
    }

    [Fact]
    public async Task Expand_WithAlternateKey_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("accountnumber='ACC-001'") &&
                       url.Contains("$expand=contact_customer_accounts");
            },
            new { accountid = Guid.NewGuid(), name = "Test", accountnumber = "ACC-001" }
        );

        var result = await client.Entity<TestAccount>()
            .UsingAlternateKey("accountnumber", "ACC-001")
            .Expand("contact_customer_accounts")
            .RetrieveAsync();

        Assert.NotNull(result);
    }
}
