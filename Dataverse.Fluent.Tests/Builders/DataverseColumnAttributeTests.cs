using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Tests.Builders;

/// <summary>
/// Tests that POCOs using [DataverseColumn] (without [JsonPropertyName]) work
/// identically to those using [JsonPropertyName] across all library features.
/// </summary>
public class DataverseColumnAttributeTests
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

    // ────────── Query / Deserialization ──────────

    [Fact]
    public async Task Query_WithDataverseColumn_DeserializesCorrectly()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => true,
            new
            {
                value = new[]
                {
                    new { accountid = Guid.NewGuid(), name = "Contoso", revenue = 5000000m, numberofemployees = 100, createdon = DateTime.UtcNow, statecode = 0 }
                }
            }
        );

        var results = await client.Query<DataverseColumnAccount>()
            .GetListAsync();

        Assert.Single(results);
        Assert.Equal("Contoso", results[0].Name);
        Assert.Equal(5000000m, results[0].Revenue);
        Assert.Equal(100, results[0].NumberOfEmployees);
    }

    // ────────── Select (expression tree) ──────────

    [Fact]
    public async Task Select_WithDataverseColumn_ResolvesLogicalNames()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains("$select=name,revenue"),
            new { value = new object[] { } }
        );

        var results = await client.Query<DataverseColumnAccount>()
            .Select(a => new { a.Name, a.Revenue })
            .GetListAsync();

        Assert.NotNull(results);
    }

    // ────────── Where (expression tree → OData filter) ──────────

    [Fact]
    public async Task Where_WithDataverseColumn_ResolvesLogicalNames()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains("$filter=") &&
                   req.RequestUri.ToString().Contains("revenue"),
            new { value = new object[] { } }
        );

        var results = await client.Query<DataverseColumnAccount>()
            .Where(a => a.Revenue > 1000000m)
            .GetListAsync();

        Assert.NotNull(results);
    }

    // ────────── OrderBy (expression tree) ──────────

    [Fact]
    public async Task OrderBy_WithDataverseColumn_ResolvesLogicalNames()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.DecodedUrl().Contains("$orderby=") &&
                   req.RequestUri.ToString().Contains("name"),
            new { value = new object[] { } }
        );

        var results = await client.Query<DataverseColumnAccount>()
            .OrderBy(a => a.Name)
            .GetListAsync();

        Assert.NotNull(results);
    }

    // ────────── SetFrom serialization ──────────

    [Fact]
    public async Task SetFrom_WithDataverseColumn_SerializesWithLogicalNames()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? capturedBody = null;
        handler.AddResponse(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().Result;
            return true;
        }, new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));

        var account = new DataverseColumnAccount
        {
            Name = "Test Corp",
            Revenue = 2000000m,
            NumberOfEmployees = 50,
            StateCode = 0
        };

        await client.Entity<DataverseColumnAccount>()
            .SetFrom(account)
            .CreateAsync();

        Assert.NotNull(capturedBody);
        Assert.Contains("\"name\"", capturedBody);
        Assert.Contains("\"revenue\"", capturedBody);
        Assert.Contains("\"numberofemployees\"", capturedBody);
        Assert.Contains("Test Corp", capturedBody);
    }

    // ────────── SetFrom respects [DataverseReadOnly] ──────────

    [Fact]
    public async Task SetFrom_WithDataverseColumn_ExcludesReadOnlyProperties()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? capturedBody = null;
        handler.AddResponse(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().Result;
            return true;
        }, new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));

        var account = new DataverseColumnAccount
        {
            AccountId = Guid.NewGuid(),
            Name = "Test Corp",
            Revenue = 2000000m,
            CreatedOn = DateTime.UtcNow
        };

        await client.Entity<DataverseColumnAccount>()
            .SetFrom(account)
            .CreateAsync();

        Assert.NotNull(capturedBody);
        Assert.Contains("\"name\"", capturedBody);
        // accountid is ReadOnly for Create, should be excluded
        Assert.DoesNotContain("\"accountid\"", capturedBody);
        // createdon is ReadOnly for CreateAndUpdate, should be excluded
        Assert.DoesNotContain("\"createdon\"", capturedBody);
    }

    // ────────── Set with expression ──────────

    [Fact]
    public async Task Set_WithExpression_WithDataverseColumn_UsesLogicalName()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? capturedBody = null;
        handler.AddResponse(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().Result;
            return true;
        }, new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));

        await client.Entity<DataverseColumnAccount>()
            .Set(a => a.Name, "Expression Corp")
            .Set(a => a.Revenue, 3000000m)
            .CreateAsync();

        Assert.NotNull(capturedBody);
        Assert.Contains("\"name\"", capturedBody);
        Assert.Contains("\"revenue\"", capturedBody);
        Assert.Contains("Expression Corp", capturedBody);
    }

    // ────────── Mixed: DataverseColumn + JsonPropertyName coexistence ──────────

    [Fact]
    public async Task Query_MixedAttributes_BothWorkCorrectly()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        // TestAccount uses [JsonPropertyName], DataverseColumnAccount uses [DataverseColumn]
        // Both should work in the same application

        handler.AddJsonResponse(
            req => true,
            new { value = new[] { new { accountid = Guid.NewGuid(), name = "JsonAttr", revenue = 1000m, numberofemployees = 10, createdon = DateTime.UtcNow, statecode = 0 } } }
        );

        var jsonResults = await client.Query<TestAccount>().GetListAsync();
        Assert.Equal("JsonAttr", jsonResults[0].Name);

        handler.AddJsonResponse(
            req => true,
            new { value = new[] { new { accountid = Guid.NewGuid(), name = "DvColumn", revenue = 2000m, numberofemployees = 20, createdon = DateTime.UtcNow, statecode = 0 } } }
        );

        var dvResults = await client.Query<DataverseColumnAccount>().GetListAsync();
        Assert.Equal("DvColumn", dvResults[0].Name);
    }
}
