using Dataverse.Fluent.Models;
using System.Linq.Expressions;
using System.Net;

namespace Dataverse.Fluent.Tests;

public class QueryBuilderTests
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
    public void Select_WithMultipleProperties_BuildsCorrectQuery()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains("$select=name,revenue"),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .Select("name", "revenue");

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Select_WithLambdaExpression_BuildsCorrectQuery()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains("$select=name,createdon"),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .Select(e => new { e.Name, e.CreatedOn });

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Select_WithSinglePropertyLambda_BuildsCorrectQuery()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$select=name"),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .Select(e => e.Name);

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Select_WithJsonPropertyNameAttribute_UsesAttributeName()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains("$select=accountid,accountnumber"),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .Select(e => new { e.AccountId, e.AccountNumber });

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Select_WithNullLambda_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var query = client.Query<TestAccount>();

        Assert.Throws<ArgumentNullException>(() => query.Select((System.Linq.Expressions.Expression<Func<TestAccount, object>>)null!));
    }

    [Fact]
    public void Select_WithLambdaAndOtherClauses_BuildsComplexQuery()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req =>
            {
                var url = req.DecodedUrl();
                return url.Contains("$select=name,revenue,numberofemployees") &&
                       url.Contains("$filter=") &&
                       url.Contains("$top=10");
            },
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .Select(e => new { e.Name, e.Revenue, e.NumberOfEmployees })
            .Where(e => e.Revenue > 100000)
            .Top(10);

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Where_WithStringFilter_EscapesAndBuildsQuery()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$filter=") && req.RequestUri.ToString().Contains("name"),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .Where("name eq 'Test Company'");

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Where_WithLambdaExpression_BuildsCorrectODataFilter()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$filter="),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .Where(x => x.Revenue > 1000000);

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Where_WithNullPredicate_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var query = client.Query<TestAccount>();

        Assert.Throws<ArgumentNullException>(() => query.Where((System.Linq.Expressions.Expression<Func<TestAccount, bool>>)null!));
    }

    [Fact]
    public void OrderBy_BuildsCorrectQuery()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$orderby=name"),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .OrderBy("name desc");

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void OrderBy_WithSinglePropertyLambdaAscending_BuildsCorrectQuery()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$orderby=") && 
                   req.RequestUri.ToString().Contains("name") &&
                   req.RequestUri.ToString().Contains("asc"),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .OrderBy(x => x.Name);

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void OrderBy_WithSinglePropertyLambdaDescending_BuildsCorrectQuery()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$orderby=") && 
                   req.RequestUri.ToString().Contains("revenue") &&
                   req.RequestUri.ToString().Contains("desc"),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .OrderBy(x => x.Revenue, descending: true);

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void OrderBy_WithMultiplePropertiesAscending_BuildsCorrectQuery()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$orderby=") && 
                   req.RequestUri.ToString().Contains("name") &&
                   req.RequestUri.ToString().Contains("revenue") &&
                   req.RequestUri.ToString().Contains("asc"),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .OrderBy(x => new { x.Name, x.Revenue });

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void OrderBy_WithMultiplePropertiesDescending_BuildsCorrectQuery()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$orderby=") && 
                   req.RequestUri.ToString().Contains("revenue") &&
                   req.RequestUri.ToString().Contains("name") &&
                   req.RequestUri.ToString().Contains("desc"),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .OrderBy(x => new { x.Revenue, x.Name }, descending: true);

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void OrderBy_WithJsonPropertyNameAttribute_UsesAttributeName()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$orderby=") && 
                   req.RequestUri.ToString().Contains("accountnumber"),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .OrderBy(x => x.AccountNumber);

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void OrderBy_WithNullSinglePropertyExpression_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var query = client.Query<TestAccount>();

        Assert.Throws<ArgumentNullException>(() => query.OrderBy((Expression<Func<TestAccount, string>>)null!));
    }

    [Fact]
    public void OrderBy_WithNullMultiplePropertiesExpression_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var query = client.Query<TestAccount>();

        Assert.Throws<ArgumentNullException>(() => query.OrderBy((Expression<Func<TestAccount, object>>)null!));
    }

    [Fact]
    public void OrderBy_WithComplexExpression_Throws()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        // A computed key such as x => x.Name + x.AccountNumber names no column. It used to be dropped,
        // returning unordered results; it is now rejected.
        Assert.Throws<ArgumentException>(() => client.Query<TestAccount>().OrderBy(x => x.Name + x.AccountNumber));
    }

    [Fact]
    public void OrderBy_CombinedWithSelectAndWhere_BuildsComplexQuery()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req =>
            {
                var url = req.RequestUri!.ToString();
                return url.Contains("$select=") && url.Contains("name") && url.Contains("revenue") &&
                       url.Contains("$filter=") &&
                       url.Contains("$orderby=") && url.Contains("desc");
            },
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .Select(x => new { x.Name, x.Revenue })
            .Where(x => x.Revenue > 1000000)
            .OrderBy(x => x.Revenue, descending: true);

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Top_WithValidValue_BuildsCorrectQuery()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$top=10"),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .Top(10);

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Top_WithZeroOrNegative_ThrowsArgumentOutOfRangeException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var query = client.Query<TestAccount>();

        Assert.Throws<ArgumentOutOfRangeException>(() => query.Top(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => query.Top(-1));
    }

    private static string GetPrefer(HttpRequestMessage request)
        => request.Headers.TryGetValues("Prefer", out var values) ? string.Join(",", values) : string.Empty;

    [Fact]
    public void Skip_IsObsoleteAsError()
    {
        // Dataverse does not support $skip, so calling Skip() is a compile-time error.
        var obsolete = typeof(Dataverse.Fluent.Builders.QueryBuilder<TestAccount>).GetMethod("Skip")!
            .GetCustomAttributes(typeof(ObsoleteAttribute), false)
            .Cast<ObsoleteAttribute>()
            .Single();

        Assert.True(obsolete.IsError);
    }

    [Fact]
    public async Task PageSize_SendsMaxPageSizePreference_AndNoTop()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        HttpRequestMessage? captured = null;
        handler.AddJsonResponse(
            req => { captured = req; return true; },
            new { value = new TestAccount[] { } }
        );

        await client.Query<TestAccount>().PageSize(50).GetPagedAsync();

        Assert.NotNull(captured);
        Assert.Contains("odata.maxpagesize=50", GetPrefer(captured!));
        Assert.DoesNotContain("$top", captured!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5001)]
    public void PageSize_OutOfRange_Throws(int pageSize)
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        Assert.Throws<ArgumentOutOfRangeException>(() => client.Query<TestAccount>().PageSize(pageSize));
    }

    [Fact]
    public async Task PageSize_CombinedWithTop_OnSinglePageRequest_Throws()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        // Dataverse ignores $top when odata.maxpagesize is present, so the combination is rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.Query<TestAccount>().Top(10).PageSize(5).GetPagedAsync());
    }

    [Fact]
    public void Count_IncludesCountParameter()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseData = new Dictionary<string, object>
        {
            ["value"] = new TestAccount[] { },
            ["@odata.count"] = 100
        };

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$count=true"),
            responseData
        );

        var query = client.Query<TestAccount>()
            .Count(true);

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void IncludeFormattedValues_AddsPreferHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.Headers.Contains("Prefer") && 
                   req.Headers.GetValues("Prefer").Any(v => v.Contains("odata.include-annotations")),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .IncludeFormattedValues(true);

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void GetListAsync_DeserializesResponseCorrectly()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var testAccounts = new[]
        {
            new
            {
                accountid = Guid.NewGuid(),
                name = "Contoso Ltd",
                revenue = 5000000.00m,
                numberofemployees = 150,
                createdon = DateTime.UtcNow,
                statecode = 0
            },
            new
            {
                accountid = Guid.NewGuid(),
                name = "Fabrikam Inc",
                revenue = 3000000.00m,
                numberofemployees = 75,
                createdon = DateTime.UtcNow,
                statecode = 0
            }
        };

        handler.AddJsonResponse(
            req => true,
            new { value = testAccounts }
        );

        var query = client.Query<TestAccount>();
        var result = query.GetListAsync().Result;

        Assert.Equal(2, result.Count);
        Assert.Equal("Contoso Ltd", result[0].Name);
        Assert.Equal("Fabrikam Inc", result[1].Name);
        Assert.Equal(5000000.00m, result[0].Revenue);
        Assert.Equal(75, result[1].NumberOfEmployees);
    }

    [Fact]
    public void GetListAsync_WithEmptyResponse_ReturnsEmptyList()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => true,
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>();
        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void FluentChaining_BuildsComplexQuery()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseData = new Dictionary<string, object>
        {
            ["value"] = new TestAccount[] { },
            ["@odata.count"] = 50
        };

        handler.AddJsonResponse(
            req =>
            {
                var url = req.RequestUri!.ToString();
                return url.Contains("$select=") &&
                       url.Contains("$filter=") &&
                       url.Contains("$orderby=") &&
                       url.Contains("$top=") &&
                       url.Contains("$count=true");
            },
            responseData
        );

        var query = client.Query<TestAccount>()
            .Select("name", "revenue")
            .Where(x => x.Revenue > 1000000)
            .OrderBy("name asc")
            .Top(25)
            .Count(true);

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Query_WithCustomEntitySet_UsesProvidedName()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("/custom_accounts"),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>("custom_accounts");
        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Query_WithoutEntitySet_UsesEntitySetAttribute()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("/testaccounts"),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>();
        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task StreamAsync_WithDefaultPageSize_StreamsResults()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var page1Data = new[]
        {
            new { accountid = Guid.NewGuid(), name = "Account 1", revenue = 1000.00m, numberofemployees = 10, createdon = DateTime.UtcNow, statecode = 0 },
            new { accountid = Guid.NewGuid(), name = "Account 2", revenue = 2000.00m, numberofemployees = 20, createdon = DateTime.UtcNow, statecode = 0 }
        };

        var page2Data = new[]
        {
            new { accountid = Guid.NewGuid(), name = "Account 3", revenue = 3000.00m, numberofemployees = 30, createdon = DateTime.UtcNow, statecode = 0 }
        };

        var page1Response = new Dictionary<string, object>
        {
            ["value"] = page1Data,
            ["@odata.nextLink"] = "https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$skiptoken=page2"
        };

        handler.AddJsonResponse(
            req => GetPrefer(req).Contains("odata.maxpagesize=2000") && !req.RequestUri!.ToString().Contains("$top"),
            page1Response
        );

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$skiptoken=page2"),
            new { value = page2Data }
        );

        var query = client.Query<TestAccount>();
        var results = new List<TestAccount>();

        await foreach (var account in query.StreamAsync())
        {
            results.Add(account);
        }

        Assert.Equal(3, results.Count);
        Assert.Equal("Account 1", results[0].Name);
        Assert.Equal("Account 2", results[1].Name);
        Assert.Equal("Account 3", results[2].Name);
    }

    [Fact]
    public async Task StreamAsync_WithCustomPageSize_UsesSpecifiedSize()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => GetPrefer(req).Contains("odata.maxpagesize=50"),
            new { value = new[] { new { accountid = Guid.NewGuid(), name = "Account 1", revenue = 1000.00m, numberofemployees = 10, createdon = DateTime.UtcNow, statecode = 0 } } }
        );

        var query = client.Query<TestAccount>();
        var results = new List<TestAccount>();

        await foreach (var account in query.StreamAsync(pageSize: 50))
        {
            results.Add(account);
        }

        Assert.Single(results);
    }

    [Fact]
    public async Task StreamAsync_WithPageSizeExceedingMax_Throws()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await foreach (var _ in client.Query<TestAccount>().StreamAsync(pageSize: 10000))
            {
            }
        });

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task StreamAsync_WithNoNextLink_StopsAfterFirstPage()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var pageData = new[]
        {
            new { accountid = Guid.NewGuid(), name = "Account 1", revenue = 1000.00m, numberofemployees = 10, createdon = DateTime.UtcNow, statecode = 0 }
        };

        handler.AddJsonResponse(
            req => true,
            new { value = pageData }
        );

        var query = client.Query<TestAccount>();
        var results = new List<TestAccount>();

        await foreach (var account in query.StreamAsync())
        {
            results.Add(account);
        }

        Assert.Single(results);
    }

    [Fact]
    public async Task StreamAsync_WithIncludeFormattedValues_AddsPreferHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.Headers.Contains("Prefer") &&
                   req.Headers.GetValues("Prefer").Any(v => v.Contains("odata.include-annotations")),
            new { value = new[] { new { accountid = Guid.NewGuid(), name = "Account 1", revenue = 1000.00m, numberofemployees = 10, createdon = DateTime.UtcNow, statecode = 0 } } }
        );

        var query = client.Query<TestAccount>()
            .IncludeFormattedValues(true);

        var results = new List<TestAccount>();

        await foreach (var account in query.StreamAsync())
        {
            results.Add(account);
        }

        Assert.Single(results);
    }

    [Fact]
    public void Select_WithNullOrEmpty_DoesNotAddSelectClause()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => !req.RequestUri!.ToString().Contains("$select="),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .Select();

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Where_WithEmptyString_DoesNotAddFilterClause()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => !req.RequestUri!.ToString().Contains("$filter="),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .Where("");

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void OrderBy_WithEmptyString_DoesNotAddOrderByClause()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => !req.RequestUri!.ToString().Contains("$orderby="),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .OrderBy("");

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void QueryBuilder_SupportsMultipleWheres_CombinesProperly()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req =>
            {
                var url = req.RequestUri!.ToString();
                var filterMatches = System.Text.RegularExpressions.Regex.Matches(url, "\\$filter=");
                return filterMatches.Count >= 1;
            },
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .Where("name eq 'Test'")
            .Where(x => x.Revenue > 1000);

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Count_WithFalse_DoesNotIncludeCountParameter()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => !req.RequestUri!.ToString().Contains("$count=true"),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .Count(false);

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void IncludeFormattedValues_WithFalse_DoesNotAddPreferHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => !req.Headers.Contains("Prefer") ||
                   !req.Headers.GetValues("Prefer").Any(v => v.Contains("odata.include-annotations")),
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .IncludeFormattedValues(false);

        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void QueryBuilder_WithComplexTypes_DeserializesCorrectly()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var testData = new[]
        {
            new
            {
                accountid = Guid.Parse("12345678-1234-1234-1234-123456789012"),
                name = "Complex Account",
                revenue = 9999999.99m,
                numberofemployees = 500,
                createdon = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc),
                statecode = 1
            }
        };

        handler.AddJsonResponse(
            req => true,
            new { value = testData }
        );

        var query = client.Query<TestAccount>();
        var result = query.GetListAsync().Result;

        Assert.Single(result);
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789012"), result[0].AccountId);
        Assert.Equal("Complex Account", result[0].Name);
        Assert.Equal(9999999.99m, result[0].Revenue);
        Assert.Equal(500, result[0].NumberOfEmployees);
        Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc), result[0].CreatedOn);
        Assert.Equal(1, result[0].StateCode);
    }

    [Fact]
    public void GetListAsync_WithApiError_ThrowsDataverseException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80040217"",
                ""message"": ""You do not have permission to access these records.""
            }
        }";

        var errorResponse = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json")
        };

        handler.AddResponse(req => true, errorResponse);

        var query = client.Query<TestAccount>();

        var exception = Assert.Throws<AggregateException>(() => query.GetListAsync().Result);
        var dataverseException = exception.InnerException as DataverseException;

        Assert.NotNull(dataverseException);
        Assert.Equal(HttpStatusCode.Forbidden, dataverseException.StatusCode);
        Assert.Equal("0x80040217", dataverseException.ErrorCode);
        Assert.Contains("permission", dataverseException.Message);
    }

    [Fact]
    public async Task StreamAsync_WithApiError_ThrowsDataverseException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80040200"",
                ""message"": ""The entity does not exist.""
            }
        }";

        var errorResponse = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json"),
            Headers = { { "x-ms-activity-id", "test-activity-id-123" } }
        };

        handler.AddResponse(req => true, errorResponse);

        var query = client.Query<TestAccount>();

        var exception = await Assert.ThrowsAsync<DataverseException>(async () =>
        {
            await foreach (var account in query.StreamAsync())
            {
                // Should throw before reaching here
            }
        });

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("0x80040200", exception.ErrorCode);
        Assert.Equal("test-activity-id-123", exception.ActivityId);
        Assert.Contains("entity does not exist", exception.Message);
    }

    [Fact]
    public async Task StreamAsync_UrlDoesNotContainDollarPrefer()
    {
        // Before the fix, StreamAsync appended "$prefer=..." as a URL query parameter.
        // OData has no $prefer system query option — Prefer is an HTTP header only.
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? capturedUrl = null;
        handler.AddJsonResponse(
            req =>
            {
                capturedUrl = req.RequestUri?.ToString();
                return true;
            },
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .IncludeFormattedValues();

        await foreach (var _ in query.StreamAsync()) { }

        Assert.NotNull(capturedUrl);
        Assert.DoesNotContain("$prefer", capturedUrl);
    }

    [Fact]
    public async Task StreamAsync_WithFormattedValues_SendsPreferAsHttpHeader()
    {
        // Prefer should be sent as an HTTP header, not a URL parameter.
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? preferHeader = null;
        handler.AddJsonResponse(
            req =>
            {
                preferHeader = req.Headers.Contains("Prefer")
                    ? string.Join(",", req.Headers.GetValues("Prefer"))
                    : null;
                return true;
            },
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>()
            .IncludeFormattedValues();

        await foreach (var _ in query.StreamAsync()) { }

        Assert.NotNull(preferHeader);
        Assert.Contains("odata.include-annotations", preferHeader);
    }

    [Fact]
    public async Task StreamAsync_WithNoQueryOptions_ProducesPlainEntitySetUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? capturedUrl = null;
        handler.AddJsonResponse(
            req =>
            {
                capturedUrl = req.RequestUri?.ToString();
                return true;
            },
            new { value = new TestAccount[] { } }
        );

        var query = client.Query<TestAccount>();

        await foreach (var _ in query.StreamAsync(pageSize: 100)) { }

        Assert.NotNull(capturedUrl);
        Assert.EndsWith("/testaccounts", capturedUrl);
    }

    [Fact]
    public async Task StreamAsync_DoesNotUseTopAsPageSize()
    {
        // $top limits the total result and disables paging in Dataverse; the page size must be
        // requested with Prefer: odata.maxpagesize, and re-sent on every page.
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var requests = new List<HttpRequestMessage>();
        handler.AddJsonResponse(
            req => { requests.Add(req); return true; },
            new Dictionary<string, object>
            {
                ["value"] = new[] { new { accountid = Guid.NewGuid(), name = "Account 1" } },
                ["@odata.nextLink"] = "https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$select=name&$skiptoken=page2"
            });
        handler.AddJsonResponse(
            req => { requests.Add(req); return true; },
            new { value = new[] { new { accountid = Guid.NewGuid(), name = "Account 2" } } });

        var query = client.Query<TestAccount>().Select("name", "revenue");
        var results = new List<TestAccount>();
        await foreach (var account in query.StreamAsync(pageSize: 500)) { results.Add(account); }

        Assert.Equal(2, results.Count);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, r =>
        {
            Assert.DoesNotContain("$top", r.RequestUri!.ToString());
            Assert.Contains("odata.maxpagesize=500", GetPrefer(r));
        });
    }

    [Fact]
    public async Task StreamAsync_WithTop_StopsAfterTopRowsAcrossPages()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        HttpRequestMessage? first = null;
        handler.AddJsonResponse(
            req => { first = req; return true; },
            new Dictionary<string, object>
            {
                ["value"] = new[]
                {
                    new { accountid = Guid.NewGuid(), name = "Account 1" },
                    new { accountid = Guid.NewGuid(), name = "Account 2" }
                },
                ["@odata.nextLink"] = "https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$skiptoken=page2"
            });
        handler.AddJsonResponse(
            req => true,
            new Dictionary<string, object>
            {
                ["value"] = new[]
                {
                    new { accountid = Guid.NewGuid(), name = "Account 3" },
                    new { accountid = Guid.NewGuid(), name = "Account 4" }
                },
                ["@odata.nextLink"] = "https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$skiptoken=page3"
            });

        var results = await client.Query<TestAccount>().Top(3).GetAllAsync(pageSize: 2);

        Assert.Equal(3, results.Count);
        Assert.DoesNotContain("$top", first!.RequestUri!.ToString());
        Assert.Contains("odata.maxpagesize=2", GetPrefer(first!));
    }

    [Fact]
    public async Task StreamAsync_WithAbsoluteNextLink_DoesNotRepeatTheBasePath()
    {
        // Dataverse returns nextLink as an absolute URL that already includes /api/data/v9.2;
        // appending its path to the base URL would send /api/data/v9.2/api/data/v9.2/...
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var requests = new List<HttpRequestMessage>();
        handler.AddJsonResponse(
            req => { requests.Add(req); return true; },
            new Dictionary<string, object>
            {
                ["value"] = new[] { new { accountid = Guid.NewGuid(), name = "Account 1" } },
                ["@odata.nextLink"] = "https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$select=name&$skiptoken=page2"
            });
        handler.AddJsonResponse(
            req => { requests.Add(req); return true; },
            new { value = new[] { new { accountid = Guid.NewGuid(), name = "Account 2" } } });

        var results = new List<TestAccount>();
        await foreach (var account in client.Query<TestAccount>().Select("name").StreamAsync(pageSize: 1)) { results.Add(account); }

        Assert.Equal(2, results.Count);
        Assert.Equal(
            "https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$select=name&$skiptoken=page2",
            requests[1].RequestUri!.OriginalString);
    }

    // ────────── GetPagedAsync Tests ──────────

    [Fact]
    public async Task GetPagedAsync_ReturnsSinglePage_WithMetadata()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseData = new Dictionary<string, object>
        {
            ["value"] = new[]
            {
                new { accountid = Guid.NewGuid(), name = "Account 1", revenue = 1000m, numberofemployees = 10, createdon = DateTime.UtcNow, statecode = 0 },
                new { accountid = Guid.NewGuid(), name = "Account 2", revenue = 2000m, numberofemployees = 20, createdon = DateTime.UtcNow, statecode = 0 }
            },
            ["@odata.count"] = 42,
            ["@odata.nextLink"] = "https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$skiptoken=page2"
        };

        handler.AddJsonResponse(req => true, responseData);

        var page = await client.Query<TestAccount>()
            .Top(2)
            .Count()
            .GetPagedAsync();

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(42, page.TotalCount);
        Assert.True(page.HasMorePages);
        Assert.NotNull(page.NextLink);
    }

    [Fact]
    public async Task GetPagedAsync_LastPage_HasNoNextLink()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => true,
            new { value = new[] { new { accountid = Guid.NewGuid(), name = "Account 1", revenue = 1000m, numberofemployees = 10, createdon = DateTime.UtcNow, statecode = 0 } } }
        );

        var page = await client.Query<TestAccount>().GetPagedAsync();

        Assert.Single(page.Items);
        Assert.Null(page.NextLink);
        Assert.Null(page.TotalCount);
        Assert.False(page.HasMorePages);
    }

    [Fact]
    public async Task GetNextPageAsync_FollowsNextLink()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var page1Response = new Dictionary<string, object>
        {
            ["value"] = new[]
            {
                new { accountid = Guid.NewGuid(), name = "Account 1", revenue = 1000m, numberofemployees = 10, createdon = DateTime.UtcNow, statecode = 0 }
            },
            ["@odata.nextLink"] = "https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$skiptoken=page1"
        };

        handler.AddJsonResponse(
            req => !req.RequestUri!.ToString().Contains("$skiptoken"),
            page1Response
        );

        // Dataverse requires the same odata.maxpagesize on every page request.
        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$skiptoken=page1") && GetPrefer(req).Contains("odata.maxpagesize=1"),
            new { value = new[] { new { accountid = Guid.NewGuid(), name = "Account 2", revenue = 2000m, numberofemployees = 20, createdon = DateTime.UtcNow, statecode = 0 } } }
        );

        var query = client.Query<TestAccount>().PageSize(1);

        var page1 = await query.GetPagedAsync();
        Assert.True(page1.HasMorePages);
        Assert.Single(page1.Items);
        Assert.Equal("Account 1", page1.Items[0].Name);
        Assert.Equal(1, page1.PageSize);

        var page2 = await query.GetNextPageAsync(page1.NextLink!);
        Assert.False(page2.HasMorePages);
        Assert.Single(page2.Items);
        Assert.Equal("Account 2", page2.Items[0].Name);
    }

    [Fact]
    public async Task GetNextPageAsync_WithPreviousPage_ResendsItsPageSize()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => GetPrefer(req).Contains("odata.maxpagesize=25"),
            new Dictionary<string, object>
            {
                ["value"] = new[] { new { accountid = Guid.NewGuid(), name = "Account 1" } },
                ["@odata.nextLink"] = "https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$skiptoken=page2"
            });
        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$skiptoken=page2") && GetPrefer(req).Contains("odata.maxpagesize=25"),
            new { value = new[] { new { accountid = Guid.NewGuid(), name = "Account 2" } } });

        var page1 = await client.Query<TestAccount>().PageSize(25).GetPagedAsync();

        // A fresh builder (as in a stateless web request) still re-sends the page size from the page.
        var page2 = await client.Query<TestAccount>().GetNextPageAsync(page1);

        Assert.Equal("Account 2", page2.Items[0].Name);
        Assert.Equal(25, page2.PageSize);
    }

    [Fact]
    public async Task GetNextPageAsync_WithLastPage_ThrowsInvalidOperationException()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());
        var lastPage = new PagedResult<TestAccount>([], nextLink: null, totalCount: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Query<TestAccount>().GetNextPageAsync(lastPage));
    }

    [Fact]
    public async Task GetNextPageAsync_WithNullNextLink_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var query = client.Query<TestAccount>();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => query.GetNextPageAsync((string)null!));
    }

    // ────────── GetAllAsync Tests ──────────

    [Fact]
    public async Task GetAllAsync_SinglePage_ReturnsAllItems()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => true,
            new
            {
                value = new[]
                {
                    new { accountid = Guid.NewGuid(), name = "Account 1", revenue = 1000m, numberofemployees = 10, createdon = DateTime.UtcNow, statecode = 0 },
                    new { accountid = Guid.NewGuid(), name = "Account 2", revenue = 2000m, numberofemployees = 20, createdon = DateTime.UtcNow, statecode = 0 }
                }
            }
        );

        var results = await client.Query<TestAccount>().GetAllAsync();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetAllAsync_MultiplePages_FollowsNextLinks()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var page1Response = new Dictionary<string, object>
        {
            ["value"] = new[]
            {
                new { accountid = Guid.NewGuid(), name = "Account 1", revenue = 1000m, numberofemployees = 10, createdon = DateTime.UtcNow, statecode = 0 },
                new { accountid = Guid.NewGuid(), name = "Account 2", revenue = 2000m, numberofemployees = 20, createdon = DateTime.UtcNow, statecode = 0 }
            },
            ["@odata.nextLink"] = "https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$skiptoken=page2"
        };

        var page2Response = new Dictionary<string, object>
        {
            ["value"] = new[]
            {
                new { accountid = Guid.NewGuid(), name = "Account 3", revenue = 3000m, numberofemployees = 30, createdon = DateTime.UtcNow, statecode = 0 }
            }
        };

        handler.AddJsonResponse(
            req => !req.RequestUri!.ToString().Contains("$skiptoken"),
            page1Response
        );

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$skiptoken=page2"),
            page2Response
        );

        var results = await client.Query<TestAccount>().GetAllAsync(pageSize: 2);

        Assert.Equal(3, results.Count);
        Assert.Equal("Account 1", results[0].Name);
        Assert.Equal("Account 2", results[1].Name);
        Assert.Equal("Account 3", results[2].Name);
    }

    [Fact]
    public async Task GetAllAsync_EmptyResult_ReturnsEmptyList()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(req => true, new { value = new TestAccount[] { } });

        var results = await client.Query<TestAccount>().GetAllAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetAllAsync_PreservesQueryOptions()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$select=name") &&
                   req.RequestUri.ToString().Contains("$filter="),
            new { value = new[] { new { accountid = Guid.NewGuid(), name = "Filtered", revenue = 0m, numberofemployees = 0, createdon = DateTime.UtcNow, statecode = 0 } } }
        );

        var results = await client.Query<TestAccount>()
            .Select("name")
            .Where(x => x.Revenue > 1000m)
            .GetAllAsync();

        Assert.Single(results);
        Assert.Equal("Filtered", results[0].Name);
    }

    // ────────── PagedResult Model Tests ──────────

    [Fact]
    public void PagedResult_WithNextLink_HasMorePagesIsTrue()
    {
        var page = new PagedResult<TestAccount>(
            [new TestAccount { Name = "Test" }],
            "https://example.com/next",
            100
        );

        Assert.True(page.HasMorePages);
        Assert.Equal(100, page.TotalCount);
        Assert.Single(page.Items);
    }

    [Fact]
    public void PagedResult_WithoutNextLink_HasMorePagesIsFalse()
    {
        var page = new PagedResult<TestAccount>(
            [new TestAccount { Name = "Test" }],
            null,
            null
        );

        Assert.False(page.HasMorePages);
        Assert.Null(page.TotalCount);
    }

    [Fact]
    public void PagedResult_WithNullItems_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new PagedResult<TestAccount>(null!, null, null));
    }

    // ────────── GetListWithCountAsync ──────────

    [Fact]
    public async Task GetListWithCountAsync_ReturnsItemsAndTheTotalCount()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddJsonResponse(_ => true, new Dictionary<string, object>
        {
            ["value"] = new[] { new { name = "A" }, new { name = "B" } },
            ["@odata.count"] = 42
        });

        var (items, total) = await client.Query<TestAccount>().Where("statecode eq 0").Count().GetListWithCountAsync();

        Assert.Equal(["A", "B"], items.Select(i => i.Name));
        Assert.Equal(42, total);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("true", request.QueryOption("$count"));
        Assert.Equal("statecode eq 0", request.QueryOption("$filter"));
    }

    [Fact]
    public async Task GetListWithCountAsync_WithoutCount_ReturnsNullTotal()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddJsonResponse(_ => true, new { value = new[] { new { name = "A" } } });

        var (items, total) = await client.Query<TestAccount>().GetListWithCountAsync();

        Assert.Single(items);
        Assert.Null(total);
        Assert.Null(Assert.Single(handler.Requests).QueryOption("$count"));
    }

    [Fact]
    public async Task GetListWithCountAsync_WithTopAndPageSize_Throws()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.Query<TestAccount>().Top(5).PageSize(10).GetListWithCountAsync());
    }
}
