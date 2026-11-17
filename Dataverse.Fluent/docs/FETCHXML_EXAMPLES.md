# FetchXML Examples

FetchXML queries with the fluent builder and as raw XML. The POCO type needs an entity set mapping
(`[DataverseEntity]` or `[EntitySet]`), which the query URL uses; `[DataverseEntity]` also supplies the table's
logical name for the `<entity>` element. Queries without `top`, `page` or `aggregate` are paged automatically.

## Table of Contents
- [Basic Queries](#basic-queries)
- [Filtering](#filtering)
- [Ordering](#ordering)
- [Pagination](#pagination)
- [Performance Options](#performance-options)
- [Raw FetchXML](#raw-fetchxml)
- [Advanced Scenarios](#advanced-scenarios)

## Basic Queries

### Select Specific Attributes

```csharp
using Dataverse.Fluent;

// Select specific attributes using lambda
var accounts = await client.FetchBuilder<Account>()
    .AddAttribute(a => a.Name)
    .AddAttribute(a => a.Revenue)
    .AddAttribute(a => a.CreatedOn)
    .ExecuteAsync();

// Or select multiple at once
var accounts = await client.FetchBuilder<Account>()
    .AddAttributes(a => new { a.Name, a.Revenue, a.CreatedOn })
    .ExecuteAsync();
```

### Select All Attributes

```csharp
var accounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .ExecuteAsync();
```

## Filtering

### Simple Equality Filter

```csharp
// Filter by StateCode (Active records)
var activeAccounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => a.StateCode == 0)
    .ExecuteAsync();
```

### Comparison Operators

```csharp
// Greater than
var highRevenueAccounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => a.Revenue > 1000000)
    .ExecuteAsync();

// Less than or equal
var smallCompanies = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => a.NumberOfEmployees <= 50)
    .ExecuteAsync();

// Not equal
var nonZeroRevenue = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => a.Revenue != 0)
    .ExecuteAsync();
```

### String Operations

```csharp
// Contains
var accounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => a.Name.Contains("Contoso"))
    .ExecuteAsync();

// Starts with
var accounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => a.Name.StartsWith("A"))
    .ExecuteAsync();

// Ends with
var accounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => a.Name.EndsWith("Inc"))
    .ExecuteAsync();
```

### Null Checks

```csharp
// Not null
var accountsWithNumber = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => a.AccountNumber != null)
    .ExecuteAsync();

// Is null
var accountsWithoutNumber = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => a.AccountNumber == null)
    .ExecuteAsync();
```

### Compound Filters (AND Logic)

```csharp
// Using && operator
var filteredAccounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => a.Revenue > 1000000 && a.StateCode == 0)
    .ExecuteAsync();

// Using multiple AddFilter calls (implicit AND)
var filteredAccounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => a.Revenue > 1000000)
    .AddFilter(a => a.StateCode == 0)
    .AddFilter(a => a.NumberOfEmployees > 100)
    .ExecuteAsync();
```

### OR Logic

```csharp
var accounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => a.StateCode == 0 || a.StateCode == 1)
    .ExecuteAsync();

// Complex OR with AND
var accounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => (a.StateCode == 0 && a.Revenue > 1000000) || 
                     (a.StateCode == 1 && a.Revenue > 5000000))
    .ExecuteAsync();
```

## Ordering

### Single Property Ordering

```csharp
// Ascending
var accounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddOrder(a => a.Name)
    .ExecuteAsync();

// Descending
var accounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddOrder(a => a.Revenue, descending: true)
    .ExecuteAsync();
```

### Multiple Property Ordering

```csharp
var accounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddOrder(a => a.Revenue, descending: true)
    .AddOrder(a => a.Name)
    .ExecuteAsync();
```

## Pagination

### Limit Results (Top N)

```csharp
var top10Accounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddOrder(a => a.Revenue, descending: true)
    .Top(10)
    .ExecuteAsync();
```

### Specific Page

```csharp
// Get page 2 with 100 records per page
var page2 = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .Page(page: 2, pageSize: 100)
    .ExecuteAsync();
```

### Stream All Results with Automatic Pagination

```csharp
// Process large datasets efficiently
int count = 0;
await foreach (var account in client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => a.StateCode == 0)
    .StreamAsync())
{
    Console.WriteLine($"{account.Name}: ${account.Revenue:N2}");
    count++;
}
Console.WriteLine($"Processed {count} accounts");
```

## Performance Options

### No-Lock Queries

`NoLock()` adds `no-lock="true"` to the query:

```csharp
var accounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .NoLock()
    .ExecuteAsync();
```

### Distinct Results

```csharp
var distinctIndustries = await client.FetchBuilder<Account>()
    .AddAttribute(a => a.IndustryCode)
    .Distinct()
    .ExecuteAsync();
```

### Combined Performance Options

```csharp
var accounts = await client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => a.Revenue > 1000000)
    .NoLock()
    .Top(100)
    .ExecuteAsync();
```

## Raw FetchXML

### Execute Pre-built FetchXML

```csharp
string fetchXml = @"
<fetch top='10' no-lock='true'>
  <entity name='account'>
    <attribute name='name' />
    <attribute name='revenue' />
    <filter type='and'>
      <condition attribute='revenue' operator='gt' value='1000000' />
      <condition attribute='statecode' operator='eq' value='0' />
    </filter>
    <order attribute='createdon' descending='true' />
  </entity>
</fetch>";

var accounts = await client.Fetch<Account>(fetchXml).ExecuteAsync();
```

### With Options

```csharp
var accounts = await client.Fetch<Account>(fetchXml)
    .WithOptions(opts =>
    {
        opts.IncludeFormattedValues = true;
        opts.NoLock = true;
        opts.MaxPageSize = 5000;
    })
    .ExecuteAsync();
```

### Stream Raw FetchXML

```csharp
await foreach (var account in client.Fetch<Account>(fetchXml).StreamAsync())
{
    await ProcessAccountAsync(account);
}
```

## Advanced Scenarios

### Build and Inspect FetchXML

```csharp
var builder = client.FetchBuilder<Account>()
    .AddAttributes(a => new { a.Name, a.Revenue })
    .AddFilter(a => a.Revenue > 1000000 && a.StateCode == 0)
    .AddOrder(a => a.Revenue, descending: true)
    .Top(100)
    .NoLock();

// Get the generated FetchXML without executing
string fetchXml = builder.BuildFetchXml();
Console.WriteLine(fetchXml);

// Execute when ready
var results = await builder.ExecuteAsync();
```

### Complex Filtering Example

```csharp
var accounts = await client.FetchBuilder<Account>()
    .AddAttributes(a => new 
    { 
        a.Name, 
        a.Revenue, 
        a.NumberOfEmployees, 
        a.CreatedOn 
    })
    .AddFilter(a => 
        (a.Revenue > 1000000 && a.NumberOfEmployees > 100) ||
        (a.Revenue > 5000000 && a.NumberOfEmployees > 50)
    )
    .AddFilter(a => a.StateCode == 0)
    .AddFilter(a => a.Name.Contains("Corp") || a.Name.Contains("Inc"))
    .AddOrder(a => a.Revenue, descending: true)
    .AddOrder(a => a.CreatedOn, descending: true)
    .NoLock()
    .Top(50)
    .ExecuteAsync();
```

### Batch Processing with Streaming

```csharp
var batchSize = 100;
var batch = new List<Account>();

await foreach (var account in client.FetchBuilder<Account>()
    .AddAllAttributes()
    .AddFilter(a => a.StateCode == 0)
    .StreamAsync())
{
    batch.Add(account);
    
    if (batch.Count >= batchSize)
    {
        await ProcessBatchAsync(batch);
        batch.Clear();
    }
}

// Process remaining items
if (batch.Count > 0)
{
    await ProcessBatchAsync(batch);
}
```

### Cancellation Token Support

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

try
{
    var accounts = await client.FetchBuilder<Account>()
        .AddAllAttributes()
        .ExecuteAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Query timed out");
}
```

### Error Handling

```csharp
try
{
    var accounts = await client.FetchBuilder<Account>()
        .AddAllAttributes()
        .AddFilter(a => a.Revenue > 1000000)
        .ExecuteAsync();
}
catch (DataverseException ex)
{
    Console.WriteLine($"Error Code: {ex.ErrorCode}");
    Console.WriteLine($"Status: {ex.StatusCode}");
    Console.WriteLine($"Activity ID: {ex.ActivityId}");
    Console.WriteLine($"Message: {ex.Message}");
}
```

## Performance Tips

1. **Stream large result sets** with `StreamAsync()` instead of loading every row into a list
2. **Select only the attributes you need**; `AddAllAttributes()` returns every column
3. **Use `Top()`** when you need only the first rows
4. **Use `Page()`** for user-facing paging; it runs a single request for that page
5. **Filter on the server** with `AddFilter`
6. **Pass a cancellation token** so abandoned queries stop

## Complete Example: Account Search

```csharp
public async Task<List<Account>> SearchAccountsAsync(
    string searchTerm,
    decimal? minRevenue,
    int? minEmployees,
    int maxResults = 100,
    CancellationToken ct = default)
{
    var builder = client.FetchBuilder<Account>()
        .AddAttributes(a => new 
        { 
            a.AccountId,
            a.Name, 
            a.Revenue, 
            a.NumberOfEmployees,
            a.CreatedOn 
        })
        .AddFilter(a => a.StateCode == 0)
        .AddOrder(a => a.Revenue, descending: true)
        .NoLock()
        .Top(maxResults);

    // Add optional search term filter
    if (!string.IsNullOrWhiteSpace(searchTerm))
    {
        builder.AddFilter(a => 
            a.Name.Contains(searchTerm) || 
            a.AccountNumber.Contains(searchTerm));
    }

    // Add optional revenue filter
    if (minRevenue.HasValue)
    {
        builder.AddFilter(a => a.Revenue >= minRevenue.Value);
    }

    // Add optional employee count filter
    if (minEmployees.HasValue)
    {
        builder.AddFilter(a => a.NumberOfEmployees >= minEmployees.Value);
    }

    return await builder.ExecuteAsync(ct);
}

// Usage
var results = await SearchAccountsAsync(
    searchTerm: "Contoso",
    minRevenue: 1000000,
    minEmployees: 50,
    maxResults: 25
);
```
