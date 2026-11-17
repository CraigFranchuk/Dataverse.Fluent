using System;
using System.Net.Http;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Dataverse.Fluent;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Microsoft.VSDiagnostics;

[CPUUsageDiagnoser]
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class FluentDataverseBenchmarks
{
    private DataverseClient _client = null!;
    private HttpClient _httpClient = null!;

    [EntitySet("accounts")]
    public class BenchAccount
    {
        [JsonPropertyName("accountid")]
        public Guid AccountId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("accountnumber")]
        public string? AccountNumber { get; set; }

        [JsonPropertyName("revenue")]
        public decimal Revenue { get; set; }

        [JsonPropertyName("numberofemployees")]
        public int NumberOfEmployees { get; set; }

        [JsonPropertyName("createdon")]
        public DateTime CreatedOn { get; set; }

        [JsonPropertyName("statecode")]
        public int StateCode { get; set; }
    }

    [GlobalSetup]
    public void Setup()
    {
        _httpClient = new HttpClient();
        _client = new DataverseClient("https://bench.crm.dynamics.com/api/data/v9.2", () => Task.FromResult("bench-token"), _httpClient);
    }

    [Benchmark]
    [BenchmarkCategory("OData")]
    public string ODataFilter_SimpleComparison()
    {
        // Exercises ODataExpressionVisitor internally through QueryBuilder.Where
        var query = _client.Query<BenchAccount>()
            .Where(x => x.Revenue > 1000m);
        return "done";
    }

    [Benchmark]
    [BenchmarkCategory("OData")]
    public string ODataFilter_CapturedVariable()
    {
        var threshold = 5000m;
        var query = _client.Query<BenchAccount>()
            .Where(x => x.Revenue > threshold);
        return "done";
    }

    [Benchmark]
    [BenchmarkCategory("OData")]
    public string ODataFilter_ComplexExpression()
    {
        var name = "Contoso";
        var query = _client.Query<BenchAccount>()
            .Where(x => x.Name == name && x.Revenue > 1000m && x.StateCode == 0);
        return "done";
    }

    [Benchmark]
    [BenchmarkCategory("QueryBuilder")]
    public string QueryBuilder_SelectWithLambda()
    {
        var query = _client.Query<BenchAccount>().Select(e => new { e.Name, e.Revenue, e.CreatedOn });
        // We can't execute but we can build URL by calling GetListAsync pattern
        // Just exercising the Select path which uses ExtractPropertyNames + GetODataPropertyName
        return "done";
    }

    [Benchmark]
    [BenchmarkCategory("QueryBuilder")]
    public string QueryBuilder_WhereWithLambda()
    {
        var minRevenue = 50000m;
        var query = _client.Query<BenchAccount>().Where(x => x.Revenue > minRevenue && x.StateCode == 0).Select(e => new { e.Name, e.Revenue }).OrderBy(e => e.Name).Top(100);
        return "done";
    }

    [Benchmark]
    [BenchmarkCategory("EntityBuilder")]
    public object EntityBuilder_SetProperties()
    {
        var builder = _client.Entity<BenchAccount>()
            .Set(x => x.Name, "Test Account")
            .Set(x => x.Revenue, 100000m)
            .Set(x => x.NumberOfEmployees, 50)
            .Set(x => x.AccountNumber, "ACC-001");
        return builder;
    }

    [Benchmark]
    [BenchmarkCategory("EntityBuilder")]
    public object EntityBuilder_SelectAll()
    {
        var builder = _client.Entity<BenchAccount>(Guid.NewGuid())
            .SelectAll();
        return builder;
    }

    [Benchmark]
    [BenchmarkCategory("EntityBuilder")]
    public object EntityBuilder_SetFrom()
    {
        var account = new BenchAccount
        {
            Name = "Test Account",
            Revenue = 100000m,
            NumberOfEmployees = 50,
            AccountNumber = "ACC-001",
            StateCode = 0
        };
        var builder = _client.Entity<BenchAccount>()
            .SetFrom(account);
        return builder;
    }
}