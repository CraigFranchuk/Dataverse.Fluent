using System.Net;

namespace Dataverse.Fluent.Tests.Builders;

public class QueryBuilderExpandTests
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

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains("$expand=contact_customer_accounts"),
            new { value = new TestAccount[] { } }
        );

        var result = await client.Query<TestAccount>()
            .Expand("contact_customer_accounts")
            .GetListAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_WithNavigationPropertyOnly_BuildsSimpleExpand()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains("$expand=contact_customer_accounts"),
            new { value = new TestAccount[] { } }
        );

        var result = await client.Query<TestAccount>()
            .Expand<TestContact>("contact_customer_accounts")
            .GetListAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_WithNestedSelect_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("$expand=contact_customer_accounts($select=firstname,lastname)");
            },
            new { value = new TestAccount[] { } }
        );

        var result = await client.Query<TestAccount>()
            .Expand<TestContact>("contact_customer_accounts", e => e
                .Select(c => new { c.FirstName, c.LastName }))
            .GetListAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_WithNestedSelectRawStrings_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("$expand=contact_customer_accounts($select=firstname,lastname)");
            },
            new { value = new TestAccount[] { } }
        );

        var result = await client.Query<TestAccount>()
            .Expand<TestContact>("contact_customer_accounts", e => e
                .Select("firstname", "lastname"))
            .GetListAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_WithNestedFilter_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("$expand=contact_customer_accounts(") &&
                       url.Contains("$filter=");
            },
            new { value = new TestAccount[] { } }
        );

        var result = await client.Query<TestAccount>()
            .Expand<TestContact>("contact_customer_accounts", e => e
                .Where(c => c.StateCode == 0))
            .GetListAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_WithNestedFilterRawString_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("$expand=contact_customer_accounts($filter=statecode eq 0)");
            },
            new { value = new TestAccount[] { } }
        );

        var result = await client.Query<TestAccount>()
            .Expand<TestContact>("contact_customer_accounts", e => e
                .Where("statecode eq 0"))
            .GetListAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_WithNestedOrderBy_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("$expand=contact_customer_accounts(") &&
                       url.Contains("$orderby=lastname asc");
            },
            new { value = new TestAccount[] { } }
        );

        var result = await client.Query<TestAccount>()
            .Expand<TestContact>("contact_customer_accounts", e => e
                .OrderBy(c => c.LastName))
            .GetListAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_WithNestedOrderByDescending_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("$expand=contact_customer_accounts(") &&
                       url.Contains("$orderby=firstname desc");
            },
            new { value = new TestAccount[] { } }
        );

        var result = await client.Query<TestAccount>()
            .Expand<TestContact>("contact_customer_accounts", e => e
                .OrderBy(c => c.FirstName, descending: true))
            .GetListAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_WithNestedTop_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("$expand=contact_customer_accounts($top=5)");
            },
            new { value = new TestAccount[] { } }
        );

        var result = await client.Query<TestAccount>()
            .Expand<TestContact>("contact_customer_accounts", e => e.Top(5))
            .GetListAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_WithAllNestedOptions_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("$expand=contact_customer_accounts(") &&
                       url.Contains("$select=firstname,lastname") &&
                       url.Contains("$filter=") &&
                       url.Contains("$orderby=lastname asc") &&
                       url.Contains("$top=10");
            },
            new { value = new TestAccount[] { } }
        );

        var result = await client.Query<TestAccount>()
            .Expand<TestContact>("contact_customer_accounts", e => e
                .Select(c => new { c.FirstName, c.LastName })
                .Where(c => c.StateCode == 0)
                .OrderBy(c => c.LastName)
                .Top(10))
            .GetListAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_WithMultipleNavigationProperties_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("$expand=contact_customer_accounts,opportunity_customer_accounts");
            },
            new { value = new TestAccount[] { } }
        );

        var result = await client.Query<TestAccount>()
            .Expand("contact_customer_accounts")
            .Expand("opportunity_customer_accounts")
            .GetListAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Expand_CombinedWithSelectAndFilter_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("$select=name") &&
                       url.Contains("$expand=contact_customer_accounts") &&
                       url.Contains("$filter=") &&
                       url.Contains("$top=5");
            },
            new { value = new TestAccount[] { } }
        );

        var result = await client.Query<TestAccount>()
            .Select(a => a.Name)
            .Where(a => a.StateCode == 0)
            .Expand<TestContact>("contact_customer_accounts", e => e
                .Select(c => new { c.FirstName, c.LastName }))
            .Top(5)
            .GetListAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public void Expand_WithNullNavigationProperty_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var query = client.Query<TestAccount>();

        Assert.Throws<ArgumentException>(() => query.Expand<TestContact>(null!));
    }

    [Fact]
    public void Expand_WithEmptyNavigationProperty_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var query = client.Query<TestAccount>();

        Assert.Throws<ArgumentException>(() => query.Expand<TestContact>(""));
    }

    [Fact]
    public void Expand_WithWhitespaceNavigationProperty_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var query = client.Query<TestAccount>();

        Assert.Throws<ArgumentException>(() => query.Expand<TestContact>("   "));
    }

    [Fact]
    public void ExpandBuilder_Top_WithZero_ThrowsArgumentOutOfRangeException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var query = client.Query<TestAccount>();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            query.Expand<TestContact>("contact_customer_accounts", e => e.Top(0)));
    }

    [Fact]
    public void ExpandBuilder_Top_WithNegative_ThrowsArgumentOutOfRangeException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var query = client.Query<TestAccount>();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            query.Expand<TestContact>("contact_customer_accounts", e => e.Top(-1)));
    }

    [Fact]
    public void ExpandBuilder_Select_WithNullLambda_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var query = client.Query<TestAccount>();

        Assert.Throws<ArgumentNullException>(() =>
            query.Expand<TestContact>("contact_customer_accounts", e =>
                e.Select((System.Linq.Expressions.Expression<Func<TestContact, object>>)null!)));
    }

    [Fact]
    public void ExpandBuilder_Where_WithNullPredicate_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var query = client.Query<TestAccount>();

        Assert.Throws<ArgumentNullException>(() =>
            query.Expand<TestContact>("contact_customer_accounts", e =>
                e.Where((System.Linq.Expressions.Expression<Func<TestContact, bool>>)null!)));
    }

    [Fact]
    public void ExpandBuilder_OrderBy_WithNullKeySelector_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var query = client.Query<TestAccount>();

        Assert.Throws<ArgumentNullException>(() =>
            query.Expand<TestContact>("contact_customer_accounts", e =>
                e.OrderBy((System.Linq.Expressions.Expression<Func<TestContact, object>>)null!)));
    }

    private async Task<string?> SentExpandAsync(Func<Dataverse.Fluent.Builders.QueryBuilder<TestAccount>, Dataverse.Fluent.Builders.QueryBuilder<TestAccount>> configure)
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddJsonResponse(_ => true, new { value = Array.Empty<TestAccount>() });

        await configure(client.Query<TestAccount>()).GetListAsync();

        return Assert.Single(handler.Requests).QueryOption("$expand");
    }

    [Fact]
    public async Task Expand_WithNestedRawOrderBy_SendsIt()
    {
        var expand = await SentExpandAsync(q => q.Expand<TestContact>("contact_customer_accounts", e => e.OrderBy("lastname desc,firstname")));

        Assert.Equal("contact_customer_accounts($orderby=lastname desc,firstname)", expand);
    }

    [Fact]
    public async Task Expand_WithBlankRawOrderBy_SendsNoNestedOptions()
    {
        var expand = await SentExpandAsync(q => q.Expand<TestContact>("contact_customer_accounts", e => e.OrderBy(" ")));

        Assert.Equal("contact_customer_accounts", expand);
    }

    [Fact]
    public async Task Expand_WithObjectInitializerSelect_SelectsTheAssignedProperties()
    {
        var expand = await SentExpandAsync(q => q.Expand<TestContact>("contact_customer_accounts", e => e
            .Select(c => new TestContact { FirstName = c.FirstName, LastName = c.LastName })));

        Assert.Equal("contact_customer_accounts($select=firstname,lastname)", expand);
    }
}
