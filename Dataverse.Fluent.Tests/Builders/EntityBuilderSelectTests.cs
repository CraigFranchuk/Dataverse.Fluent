using Dataverse.Fluent.Models;
using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Tests.Builders;

public class EntityBuilderSelectTests
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
    public void Select_WithMultipleProperties_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains($"testaccounts({accountId})?$select=name,revenue"),
            new { accountid = accountId, name = "Test Account", revenue = 1000000 }
        );

        var result = client.Entity<TestAccount>()
            .WithId(accountId)
            .Select("name", "revenue")
            .RetrieveAsync()
            .Result;

        Assert.NotNull(result);
        Assert.Equal("Test Account", result.Name);
    }

    [Fact]
    public void Select_WithLambdaExpression_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains($"testaccounts({accountId})?$select=name,revenue"),
            new { accountid = accountId, name = "Test Account", revenue = 1000000 }
        );

        var result = client.Entity<TestAccount>()
            .WithId(accountId)
            .Select(a => new { a.Name, a.Revenue })
            .RetrieveAsync()
            .Result;

        Assert.NotNull(result);
        Assert.Equal("Test Account", result.Name);
    }

    [Fact]
    public void Select_WithSingleProperty_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains($"testaccounts({accountId})?$select=name"),
            new { accountid = accountId, name = "Test Account" }
        );

        var result = client.Entity<TestAccount>()
            .WithId(accountId)
            .Select(a => a.Name)
            .RetrieveAsync()
            .Result;

        Assert.NotNull(result);
        Assert.Equal("Test Account", result.Name);
    }

    [Fact]
    public void Select_WithJsonPropertyNameAttribute_UsesAttributeName()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains($"testaccounts({accountId})?$select=accountid,accountnumber"),
            new { accountid = accountId, accountnumber = "ACC-001" }
        );

        var result = client.Entity<TestAccount>()
            .WithId(accountId)
            .Select(a => new { a.AccountId, a.AccountNumber })
            .RetrieveAsync()
            .Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Select_WithNullLambda_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var builder = client.Entity<TestAccount>();

        Assert.Throws<ArgumentNullException>(() => 
            builder.Select((System.Linq.Expressions.Expression<Func<TestAccount, object>>)null!));
    }

    [Fact]
    public void Select_WithEmptyArray_ClearsSelectProperties()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req => !req.DecodedUrl().Contains("$select="),
            new { accountid = accountId, name = "Test Account" }
        );

        var result = client.Entity<TestAccount>()
            .WithId(accountId)
            .Select()  // Empty select
            .RetrieveAsync()
            .Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void SelectAll_IncludesAllPropertiesFromType()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("$select=") &&
                       url.Contains("accountid") &&
                       url.Contains("name") &&
                       url.Contains("revenue") &&
                       url.Contains("numberofemployees") &&
                       url.Contains("createdon") &&
                       url.Contains("statecode") &&
                       url.Contains("accountnumber");
            },
            new
            {
                accountid = accountId,
                name = "Test Account",
                revenue = 1000000,
                numberofemployees = 100,
                createdon = DateTime.UtcNow,
                statecode = 0,
                accountnumber = "ACC-001"
            }
        );

        var result = client.Entity<TestAccount>()
            .WithId(accountId)
            .SelectAll()
            .RetrieveAsync()
            .Result;

        Assert.NotNull(result);
        Assert.Equal("Test Account", result.Name);
        Assert.Equal(1000000, result.Revenue);
    }

    [Fact]
    public void SelectAll_DoesNotIncludeDuplicateProperties()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                var selectMatch = System.Text.RegularExpressions.Regex.Match(url, @"\$select=([^&]+)");
                if (!selectMatch.Success) return false;

                var properties = selectMatch.Groups[1].Value.Split(',');
                var distinctProperties = properties.Distinct().ToList();
                
                return properties.Length == distinctProperties.Count;
            },
            new { accountid = accountId, name = "Test Account" }
        );

        var result = client.Entity<TestAccount>()
            .WithId(accountId)
            .SelectAll()
            .RetrieveAsync()
            .Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Select_CalledTwice_ReplacesFirstSelection()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains("$select=revenue") &&
                   !req.RequestUri.ToString().Contains("name"),
            new { accountid = accountId, revenue = 1000000 }
        );

        var result = client.Entity<TestAccount>()
            .WithId(accountId)
            .Select(a => a.Name)  // First selection
            .Select(a => a.Revenue)  // Second selection should replace
            .RetrieveAsync()
            .Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Select_WithAlternateKey_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains("testaccounts(accountnumber='ACC-001')?$select=name,revenue"),
            new { accountid = Guid.NewGuid(), name = "Test Account", revenue = 1000000 }
        );

        var result = client.Entity<TestAccount>()
            .UsingAlternateKey(a => a.AccountNumber, "ACC-001")
            .Select(a => new { a.Name, a.Revenue })
            .RetrieveAsync()
            .Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Select_WithCompositeAlternateKey_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains("testaccounts(accountnumber='ACC-001',statecode=0)?$select=name"),
            new { accountid = Guid.NewGuid(), name = "Test Account" }
        );

        var result = client.Entity<TestAccount>()
            .UsingAlternateKey("accountnumber", "ACC-001")
            .UsingAlternateKey("statecode", 0)
            .Select(a => a.Name)
            .RetrieveAsync()
            .Result;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Create_WithoutRepresentationSelector_SendsNoSelect()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddJsonResponse(_ => true, new { accountid = Guid.NewGuid() });

        await client.Entity<TestAccount>()
            .Set(a => a.Name, "New Account")
            .ReturnRepresentation()
            .CreateAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://test.crm.dynamics.com/api/data/v9.2/testaccounts", request.DecodedUrl);
    }

    [Fact]
    public async Task ReturnRepresentation_WithSelector_SelectsTheReturnedPropertiesOnCreate()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddJsonResponse(_ => true, new { accountid = Guid.NewGuid(), name = "New Account" });

        var result = await client.Entity<TestAccount>()
            .Set(a => a.Name, "New Account")
            .ReturnRepresentation(a => new { a.Name, a.Revenue })
            .CreateAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("name,revenue", request.QueryOption("$select"));
        Assert.Equal("return=representation", request.Header("Prefer"));
        Assert.Equal("New Account", result.Entity?.Name);
    }

    [Fact]
    public async Task ReturnRepresentation_WithSelector_SelectsTheReturnedPropertiesOnUpdate()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();
        handler.AddJsonResponse(_ => true, new { accountid = accountId, name = "Updated Account" });

        var result = await client.Entity<TestAccount>()
            .WithId(accountId)
            .Set(a => a.Name, "Updated Account")
            .ReturnRepresentation(a => a.Name)
            .UpdateAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"https://test.crm.dynamics.com/api/data/v9.2/testaccounts({accountId})?$select=name", request.DecodedUrl);
        Assert.Equal("Updated Account", result?.Name);
    }

    [Fact]
    public async Task ReturnRepresentation_WithSelector_SelectsTheReturnedPropertiesOnUpsertByAlternateKey()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddJsonResponse(_ => true, new { name = "Upserted" });

        await client.Entity<TestAccount>()
            .UsingAlternateKey(a => a.AccountNumber, "ACC-001")
            .Set(a => a.Name, "Upserted")
            .ReturnRepresentation(a => a.Name)
            .UpsertAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://test.crm.dynamics.com/api/data/v9.2/testaccounts(accountnumber='ACC-001')?$select=name",
            Assert.Single(handler.Requests).DecodedUrl);
    }

    [Theory]
    [InlineData("minimal", "return=minimal")]
    [InlineData("full", "return=representation")]
    [InlineData("off", null)]
    public async Task ReturnRepresentation_WithSelector_IsClearedByALaterReturnCall(string laterCall, string? expectedPrefer)
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();
        handler.AddResponse(_ => true, new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));

        var write = client.Entity<TestAccount>(accountId)
            .Set(a => a.Name, "x")
            .ReturnRepresentation(a => a.Name);
        write = laterCall switch
        {
            "minimal" => write.ReturnMinimal(),
            "full" => write.ReturnRepresentation(),
            _ => write.ReturnRepresentation(false)
        };
        await write.UpdateAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Null(request.QueryOption("$select"));
        Assert.Equal(expectedPrefer, request.Header("Prefer"));
    }

    [Fact]
    public void SelectAll_CombinedWithOtherMethods_BuildsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains($"testaccounts({accountId})?$select=") &&
                   req.Headers.Contains("Prefer"),
            new
            {
                accountid = accountId,
                name = "Test Account",
                revenue = 1000000,
                numberofemployees = 100,
                createdon = DateTime.UtcNow,
                statecode = 0,
                accountnumber = "ACC-001"
            }
        );

        var result = client.Entity<TestAccount>()
            .WithId(accountId)
            .SelectAll()
            .IncludeFormattedValues()
            .RetrieveAsync()
            .Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Select_WithTypeWithoutJsonAttributes_UsesLowercasePropertyNames()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var id = Guid.NewGuid();

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains("?$select=") &&
                   req.RequestUri.ToString().Contains("name"),
            new { id = id, name = "Test" }
        );

        var result = client.Entity<SimpleEntity>()
            .WithId(id)
            .Select(e => e.Name)
            .RetrieveAsync()
            .Result;

        Assert.NotNull(result);
    }

    // Test POCO without JsonPropertyName attributes
    [EntitySet("simpleentities")]
    public class SimpleEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
