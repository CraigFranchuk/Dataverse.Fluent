using System.Net;

namespace Dataverse.Fluent.Tests;

public class AggregateBuilderTests
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

    private sealed class RevenueByState
    {
        [System.Text.Json.Serialization.JsonPropertyName("statecode")]
        public int StateCode { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("totalrevenue")]
        public decimal TotalRevenue { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("accountcount")]
        public int AccountCount { get; set; }
    }

    [EntitySet("testaccounts")]
    private sealed class LookupAccount
    {
        [System.Text.Json.Serialization.JsonPropertyName("statecode")]
        public int StateCode { get; set; }

        [DataverseLookup("ownerid", "systemusers")]
        public Guid? OwnerId { get; set; }

        // A lookup mapped by its read name, without [DataverseLookup].
        [System.Text.Json.Serialization.JsonPropertyName("_parentaccountid_value")]
        public Guid? ParentAccountId { get; set; }
    }

    [Fact]
    public void GroupByOrAggregateOverALookup_ThrowsNotSupported()
    {
        // Dataverse rejects a lookup in $apply under every name ("_ownerid_value" is not an attribute there, and
        // "ownerid" is a navigation property), so the builder explains instead of sending a request that fails.
        var client = CreateTestClient(new TestHttpMessageHandler());

        var groupBy = Assert.Throws<NotSupportedException>(() => client.Aggregate<LookupAccount>().GroupBy(a => a.OwnerId));
        Assert.Throws<NotSupportedException>(() => client.Aggregate<LookupAccount>().GroupBy(a => new { a.StateCode, a.ParentAccountId }));
        Assert.Throws<NotSupportedException>(() => client.Aggregate<LookupAccount>().CountDistinct(a => a.OwnerId, "owners"));

        Assert.Contains("FetchXML aggregate", groupBy.Message);
        Assert.Equal("groupby((statecode),aggregate($count as total))",
            client.Aggregate<LookupAccount>().GroupBy(a => a.StateCode).Count("total").BuildApply());
    }

    [Fact]
    public void Sum_WithGroupBy_BuildsAggregateAndGroupbyTransform()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var builder = client.Aggregate<TestAccount>()
            .GroupBy(a => a.StateCode)
            .Sum(a => a.Revenue, "totalrevenue");

        var apply = builder.BuildApply();

        Assert.Equal("groupby((statecode),aggregate(revenue with sum as totalrevenue))", apply);
    }

    [Fact]
    public void Aggregate_WithoutGroupBy_BuildsAggregateTransformOnly()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var apply = client.Aggregate<TestAccount>()
            .Average(a => a.Revenue, "avgrevenue")
            .BuildApply();

        Assert.Equal("aggregate(revenue with average as avgrevenue)", apply);
    }

    [Fact]
    public void MultipleAggregates_AreCommaSeparated()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        var apply = client.Aggregate<TestAccount>()
            .Sum(a => a.Revenue, "totalrevenue")
            .Max(a => a.NumberOfEmployees, "maxemployees")
            .Min(a => a.NumberOfEmployees, "minemployees")
            .CountDistinct(a => a.Name, "distinctnames")
            .BuildApply();

        Assert.Equal(
            "aggregate(revenue with sum as totalrevenue,numberofemployees with max as maxemployees,numberofemployees with min as minemployees,name with countdistinct as distinctnames)",
            apply);
    }

    [Fact]
    public void Count_BuildsDollarCountAggregate()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        var apply = client.Aggregate<TestAccount>()
            .GroupBy(a => a.StateCode)
            .Count("accountcount")
            .BuildApply();

        Assert.Equal("groupby((statecode),aggregate($count as accountcount))", apply);
    }

    [Fact]
    public void GroupBy_WithProjection_AddsMultipleProperties()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        var apply = client.Aggregate<TestAccount>()
            .GroupBy(a => new { a.StateCode, a.AccountNumber })
            .Sum(a => a.Revenue, "totalrevenue")
            .BuildApply();

        Assert.Equal("groupby((statecode,accountnumber),aggregate(revenue with sum as totalrevenue))", apply);
    }

    [Fact]
    public void GroupByOnly_WithoutAggregate_BuildsGroupbyTransform()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        var apply = client.Aggregate<TestAccount>()
            .GroupBy(a => a.StateCode)
            .BuildApply();

        Assert.Equal("groupby((statecode))", apply);
    }

    [Fact]
    public void Where_Lambda_AddsLeadingFilterTransform()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        var apply = client.Aggregate<TestAccount>()
            .Where(a => a.Revenue > 1000000)
            .GroupBy(a => a.StateCode)
            .Sum(a => a.Revenue, "totalrevenue")
            .BuildApply();

        Assert.Equal("filter(revenue gt 1000000)/groupby((statecode),aggregate(revenue with sum as totalrevenue))", apply);
    }

    [Fact]
    public void Where_RawString_AddsLeadingFilterTransform()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        var apply = client.Aggregate<TestAccount>()
            .Where("statecode eq 0")
            .Sum(a => a.Revenue, "totalrevenue")
            .BuildApply();

        Assert.Equal("filter(statecode eq 0)/aggregate(revenue with sum as totalrevenue)", apply);
    }

    [Fact]
    public void BuildApply_WithNoAggregateOrGroupBy_Throws()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        var builder = client.Aggregate<TestAccount>();

        Assert.Throws<InvalidOperationException>(() => builder.BuildApply());
    }

    [Fact]
    public void Sum_WithNullOrWhitespaceAlias_Throws()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        Assert.Throws<ArgumentException>(() => client.Aggregate<TestAccount>().Sum(a => a.Revenue, ""));
        Assert.Throws<ArgumentException>(() => client.Aggregate<TestAccount>().Sum(a => a.Revenue, "   "));
    }

    [Fact]
    public void GroupBy_WithNullSelector_Throws()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());

        Assert.Throws<ArgumentNullException>(() =>
            client.Aggregate<TestAccount>().GroupBy<object>(null!));
    }

    [Fact]
    public async Task GetListAsync_SendsEscapedApplyQueryAndDeserializesResults()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req =>
            {
                var url = Uri.UnescapeDataString(req.RequestUri!.ToString());
                return url.Contains("/testaccounts?$apply=") &&
                       url.Contains("groupby((statecode),aggregate(revenue with sum as totalrevenue,$count as accountcount))");
            },
            new
            {
                value = new[]
                {
                    new { statecode = 0, totalrevenue = 1500000m, accountcount = 3 },
                    new { statecode = 1, totalrevenue = 250000m, accountcount = 1 }
                }
            });

        var results = await client.Aggregate<TestAccount>()
            .GroupBy(a => a.StateCode)
            .Sum(a => a.Revenue, "totalrevenue")
            .Count("accountcount")
            .GetListAsync<RevenueByState>();

        Assert.Equal(2, results.Count);
        Assert.Equal(0, results[0].StateCode);
        Assert.Equal(1500000m, results[0].TotalRevenue);
        Assert.Equal(3, results[0].AccountCount);
    }

    [Fact]
    public async Task GetListAsync_CalledTwice_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("$apply="),
            new { value = Array.Empty<object>() });

        var builder = client.Aggregate<TestAccount>()
            .Sum(a => a.Revenue, "totalrevenue");

        await builder.GetListAsync<RevenueByState>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.GetListAsync<RevenueByState>());
    }
}
