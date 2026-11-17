using Dataverse.Fluent;
using Dataverse.Fluent.Builders;
using Microsoft.Identity.Client;
using System.Text.Json.Serialization;

namespace Examples;

/// <summary>
/// FetchXML queries with the fluent builder and with raw FetchXML.
/// </summary>
public class FetchXmlRealWorldExample
{
    // POCO Models
    [DataverseEntity("account", "accounts")]
    public class Account
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("accountid")]
        public Guid AccountId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("accountnumber")]
        public string? AccountNumber { get; set; }

        [JsonPropertyName("revenue")]
        public decimal? Revenue { get; set; }

        [JsonPropertyName("numberofemployees")]
        public int? NumberOfEmployees { get; set; }

        [JsonPropertyName("createdon")]
        public DateTime CreatedOn { get; set; }

        [JsonPropertyName("statecode")]
        public int StateCode { get; set; }

        [JsonPropertyName("industrycode")]
        public int? IndustryCode { get; set; }

        [JsonPropertyName("websiteurl")]
        public string? WebsiteUrl { get; set; }
    }

    // Client setup
    private static async Task<DataverseClient> CreateClientAsync()
    {
        // Configuration (use your own values)
        var clientId = "your-client-id";
        var clientSecret = "your-client-secret";
        var tenantId = "your-tenant-id";
        var environmentUrl = "https://yourorg.crm.dynamics.com";

        // Configure MSAL for authentication
        var app = ConfidentialClientApplicationBuilder.Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority(new Uri($"https://login.microsoftonline.com/{tenantId}"))
            .Build();

        // Token provider
        async Task<string> GetTokenAsync()
        {
            var result = await app.AcquireTokenForClient(
                new[] { $"{environmentUrl}/.default" }
            ).ExecuteAsync();

            return result.AccessToken;
        }

        // Create client
        return new DataverseClient(
            baseUrl: $"{environmentUrl}/api/data/v9.2",
            tokenProvider: GetTokenAsync
        );
    }

    // Example 1: Simple Query with Fluent Builder
    public static async Task Example1_SimpleQuery()
    {
        using var client = await CreateClientAsync();

        // Get top 10 active accounts with revenue over $1M
        var accounts = await client.FetchBuilder<Account>()
            .AddAttributes(a => new { a.Name, a.Revenue, a.NumberOfEmployees })
            .AddFilter(a => a.StateCode == 0 && a.Revenue > 1000000)
            .AddOrder(a => a.Revenue, descending: true)
            .Top(10)
            .ExecuteAsync();

        Console.WriteLine("Top 10 High-Revenue Accounts:");
        foreach (var account in accounts)
        {
            Console.WriteLine($"  {account.Name}: ${account.Revenue:N2} ({account.NumberOfEmployees} employees)");
        }
    }

    // Example 2: Streaming Large Dataset
    public static async Task Example2_StreamingQuery()
    {
        using var client = await CreateClientAsync();

        Console.WriteLine("Processing all active accounts...");
        
        int count = 0;
        decimal totalRevenue = 0;

        await foreach (var account in client.FetchBuilder<Account>()
            .AddAttributes(a => new { a.Name, a.Revenue })
            .AddFilter(a => a.StateCode == 0)
            .NoLock()  // Avoid locking records
            .StreamAsync())
        {
            count++;
            totalRevenue += account.Revenue ?? 0;

            if (count % 1000 == 0)
            {
                Console.WriteLine($"  Processed {count} accounts...");
            }
        }

        Console.WriteLine($"Total: {count} accounts, ${totalRevenue:N2} in revenue");
    }

    // Example 3: Complex Filtering
    public static async Task Example3_ComplexFiltering()
    {
        using var client = await CreateClientAsync();

        // Find accounts that are either:
        // - Large companies (>500 employees AND >$5M revenue)
        // - OR high-growth startups (<100 employees AND >$2M revenue)
        var accounts = await client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddFilter(a => 
                (a.NumberOfEmployees > 500 && a.Revenue > 5000000) ||
                (a.NumberOfEmployees < 100 && a.Revenue > 2000000))
            .AddFilter(a => a.StateCode == 0)
            .AddOrder(a => a.Revenue, descending: true)
            .ExecuteAsync();

        Console.WriteLine($"Found {accounts.Count} matching accounts");
    }

    // Example 4: String Search
    public static async Task Example4_StringSearch(string searchTerm)
    {
        using var client = await CreateClientAsync();

        // Search accounts by name or account number
        var accounts = await client.FetchBuilder<Account>()
            .AddAttributes(a => new { a.Name, a.AccountNumber, a.Revenue })
            .AddFilter(a => 
                a.Name.Contains(searchTerm) || 
                (a.AccountNumber != null && a.AccountNumber.Contains(searchTerm)))
            .AddFilter(a => a.StateCode == 0)
            .Top(50)
            .ExecuteAsync();

        Console.WriteLine($"Search results for '{searchTerm}':");
        foreach (var account in accounts)
        {
            Console.WriteLine($"  {account.Name} ({account.AccountNumber})");
        }
    }

    // Example 5: Pagination
    public static async Task Example5_Pagination()
    {
        using var client = await CreateClientAsync();

        const int pageSize = 100;
        int currentPage = 1;
        bool hasMore = true;

        while (hasMore)
        {
            var accounts = await client.FetchBuilder<Account>()
                .AddAttributes(a => new { a.Name, a.Revenue })
                .AddFilter(a => a.StateCode == 0)
                .AddOrder(a => a.Name)
                .Page(currentPage, pageSize)
                .ExecuteAsync();

            Console.WriteLine($"Page {currentPage}: {accounts.Count} accounts");

            foreach (var account in accounts)
            {
                Console.WriteLine($"  {account.Name}");
            }

            hasMore = accounts.Count == pageSize;
            currentPage++;

            if (currentPage > 5) break; // Safety limit for example
        }
    }

    // Example 6: Raw FetchXML Execution
    public static async Task Example6_RawFetchXml()
    {
        using var client = await CreateClientAsync();

        // Execute pre-built FetchXML (from saved query, external source, etc.)
        string fetchXml = @"
<fetch top='25' no-lock='true'>
  <entity name='account'>
    <attribute name='name' />
    <attribute name='revenue' />
    <attribute name='industrycode' />
    <filter type='and'>
      <condition attribute='statecode' operator='eq' value='0' />
      <condition attribute='revenue' operator='gt' value='1000000' />
    </filter>
    <order attribute='revenue' descending='true' />
  </entity>
</fetch>";

        var accounts = await client.Fetch<Account>(fetchXml)
            .WithOptions(opts => opts.IncludeFormattedValues = true)
            .ExecuteAsync();

        Console.WriteLine($"Found {accounts.Count} accounts from raw FetchXML");
    }

    // Example 7: Inspect Generated FetchXML
    public static async Task Example7_InspectFetchXml()
    {
        using var client = await CreateClientAsync();

        var builder = client.FetchBuilder<Account>()
            .AddAttributes(a => new { a.Name, a.Revenue })
            .AddFilter(a => a.Revenue > 1000000 && a.StateCode == 0)
            .AddOrder(a => a.Revenue, descending: true)
            .Top(10)
            .NoLock();

        // Build and inspect FetchXML before executing
        string fetchXml = builder.BuildFetchXml();
        Console.WriteLine("Generated FetchXML:");
        Console.WriteLine(fetchXml);

        // Execute when ready
        var accounts = await builder.ExecuteAsync();
        Console.WriteLine($"\nReturned {accounts.Count} accounts");
    }

    // Example 8: Advanced Search Service
    public class AccountSearchService
    {
        private readonly DataverseClient _client;

        public AccountSearchService(DataverseClient client)
        {
            _client = client;
        }

        public async Task<List<Account>> SearchAccountsAsync(
            string? searchTerm = null,
            decimal? minRevenue = null,
            int? minEmployees = null,
            int? industryCode = null,
            int maxResults = 100,
            CancellationToken ct = default)
        {
            // Start with base query
            var builder = _client.FetchBuilder<Account>()
                .AddAttributes(a => new
                {
                    a.AccountId,
                    a.Name,
                    a.AccountNumber,
                    a.Revenue,
                    a.NumberOfEmployees,
                    a.IndustryCode,
                    a.WebsiteUrl,
                    a.CreatedOn
                })
                .AddFilter(a => a.StateCode == 0)
                .AddOrder(a => a.Revenue, descending: true)
                .NoLock()
                .Top(maxResults);

            // Add optional filters
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                builder.AddFilter(a =>
                    a.Name.Contains(searchTerm) ||
                    (a.AccountNumber != null && a.AccountNumber.Contains(searchTerm)));
            }

            if (minRevenue.HasValue)
            {
                builder.AddFilter(a => a.Revenue >= minRevenue.Value);
            }

            if (minEmployees.HasValue)
            {
                builder.AddFilter(a => a.NumberOfEmployees >= minEmployees.Value);
            }

            if (industryCode.HasValue)
            {
                builder.AddFilter(a => a.IndustryCode == industryCode.Value);
            }

            return await builder.ExecuteAsync(ct);
        }

        public async IAsyncEnumerable<Account> StreamAllAccountsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var account in _client.FetchBuilder<Account>()
                .AddAllAttributes()
                .AddFilter(a => a.StateCode == 0)
                .AddOrder(a => a.CreatedOn, descending: true)
                .NoLock()
                .StreamAsync(ct))
            {
                yield return account;
            }
        }
    }

    // Example 9: Error Handling
    public static async Task Example9_ErrorHandling()
    {
        using var client = await CreateClientAsync();

        try
        {
            // Query with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var accounts = await client.FetchBuilder<Account>()
                .AddAllAttributes()
                .ExecuteAsync(cts.Token);

            Console.WriteLine($"Successfully retrieved {accounts.Count} accounts");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Query timed out after 30 seconds");
        }
        catch (Dataverse.Fluent.Models.DataverseException ex)
        {
            Console.WriteLine($"Dataverse error occurred:");
            Console.WriteLine($"  Error Code: {ex.ErrorCode}");
            Console.WriteLine($"  Status: {ex.StatusCode}");
            Console.WriteLine($"  Activity ID: {ex.ActivityId}");
            Console.WriteLine($"  Message: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error: {ex.Message}");
        }
    }

    // Example 10: Batch Processing with Streaming
    public static async Task Example10_BatchProcessing()
    {
        using var client = await CreateClientAsync();

        const int batchSize = 100;
        var batch = new List<Account>();
        int totalProcessed = 0;

        Console.WriteLine("Processing accounts in batches...");

        await foreach (var account in client.FetchBuilder<Account>()
            .AddAllAttributes()
            .AddFilter(a => a.StateCode == 0 && a.Revenue > 1000000)
            .NoLock()
            .StreamAsync())
        {
            batch.Add(account);

            if (batch.Count >= batchSize)
            {
                // Process batch
                await ProcessBatchAsync(batch);
                totalProcessed += batch.Count;
                
                Console.WriteLine($"  Processed {totalProcessed} accounts...");
                
                batch.Clear();
            }
        }

        // Process remaining items
        if (batch.Count > 0)
        {
            await ProcessBatchAsync(batch);
            totalProcessed += batch.Count;
        }

        Console.WriteLine($"Completed: {totalProcessed} accounts processed");
    }

    private static async Task ProcessBatchAsync(List<Account> accounts)
    {
        // Simulate batch processing (e.g., update, export, analyze)
        await Task.Delay(100);
        
        // Your batch processing logic here
        foreach (var account in accounts)
        {
            // Process each account
        }
    }

    // Main entry point
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Dataverse.Fluent FetchXML Examples ===\n");

        try
        {
            // Run examples
            await Example1_SimpleQuery();
            Console.WriteLine();

            await Example4_StringSearch("Contoso");
            Console.WriteLine();

            await Example7_InspectFetchXml();
            Console.WriteLine();

            // Uncomment to run other examples:
            // await Example2_StreamingQuery();
            // await Example3_ComplexFiltering();
            // await Example5_Pagination();
            // await Example6_RawFetchXml();
            // await Example9_ErrorHandling();
            // await Example10_BatchProcessing();

            Console.WriteLine("Examples completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error running examples: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
