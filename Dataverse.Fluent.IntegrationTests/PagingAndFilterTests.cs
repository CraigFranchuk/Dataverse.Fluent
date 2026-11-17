using System.Net;
using Dataverse.Fluent.Builders;
using Dataverse.Fluent.IntegrationTests.Models;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.IntegrationTests;

/// <summary>
/// Live tests for behaviour that mocked unit tests cannot prove: that paging really returns rows
/// beyond the first page, that mixed <c>&amp;&amp;</c>/<c>||</c> filters are grouped correctly by the
/// server, that creates return the new ID, and that updates never create records.
/// Each test seeds its own uniquely named accounts and deletes them afterwards.
/// </summary>
[Collection("Dataverse Integration Tests")]
public class PagingAndFilterTests : IClassFixture<DataverseTestFixture>
{
    private const int SeedCount = 7;
    private const int PageSize = 3; // forces three pages for SeedCount rows

    private readonly DataverseTestFixture _fixture;

    public PagingAndFilterTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static string NewPrefix() => $"FDC-IT-{Guid.NewGuid():N}";

    private async Task<List<Guid>> CreateAccountsAsync(string prefix, int count, Func<int, int?>? employees = null)
    {
        var ids = new List<Guid>(count);
        for (var i = 0; i < count; i++)
        {
            var result = await _fixture.Client.Entity<Account>()
                .Set(a => a.Name, $"{prefix}-{i:D2}")
                .Set(a => a.NumberOfEmployees, employees?.Invoke(i))
                .CreateAsync();

            ids.Add(result.Id ?? throw new InvalidOperationException("CreateAsync did not return the new record ID."));
        }
        return ids;
    }

    private async Task DeleteAccountsAsync(IEnumerable<Guid> ids)
    {
        foreach (var id in ids)
        {
            try
            {
                await _fixture.Client.Entity<Account>(id).DeleteAsync();
            }
            catch (DataverseException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }
    }

    [Fact]
    public async Task OData_StreamAsync_ReturnsEveryRowAcrossPages()
    {
        var prefix = NewPrefix();
        var ids = await CreateAccountsAsync(prefix, SeedCount);
        try
        {
            var streamed = new List<Guid>();
            await foreach (var account in _fixture.Client.Query<Account>()
                .Select(a => new { a.AccountId, a.Name })
                .Where(a => a.Name.StartsWith(prefix))
                .OrderBy(a => a.AccountId)
                .StreamAsync(pageSize: PageSize))
            {
                streamed.Add(account.AccountId);
            }

            Assert.Equal(ids.OrderBy(i => i), streamed.OrderBy(i => i));
        }
        finally
        {
            await DeleteAccountsAsync(ids);
        }
    }

    [Fact]
    public async Task OData_GetAllAsync_WithTop_CapsTotalAcrossPages()
    {
        var prefix = NewPrefix();
        var ids = await CreateAccountsAsync(prefix, SeedCount);
        try
        {
            var results = await _fixture.Client.Query<Account>()
                .Select(a => new { a.AccountId })
                .Where(a => a.Name.StartsWith(prefix))
                .Top(5)
                .GetAllAsync(pageSize: 2);

            Assert.Equal(5, results.Count);
            Assert.Equal(5, results.Select(r => r.AccountId).Distinct().Count());
        }
        finally
        {
            await DeleteAccountsAsync(ids);
        }
    }

    [Fact]
    public async Task OData_GetPagedAsync_WithPageSize_FollowsNextLinksToTheEnd()
    {
        var prefix = NewPrefix();
        var ids = await CreateAccountsAsync(prefix, SeedCount);
        try
        {
            var query = _fixture.Client.Query<Account>()
                .Select(a => new { a.AccountId })
                .Where(a => a.Name.StartsWith(prefix))
                .OrderBy(a => a.AccountId)
                .PageSize(PageSize);

            var page = await query.GetPagedAsync();
            Assert.Equal(PageSize, page.Items.Count);

            var collected = new List<Guid>(page.Items.Select(a => a.AccountId));
            var pages = 1;
            while (page.HasMorePages)
            {
                page = await query.GetNextPageAsync(page);
                Assert.True(page.Items.Count <= PageSize);
                collected.AddRange(page.Items.Select(a => a.AccountId));
                pages++;
            }

            Assert.Equal(3, pages);
            Assert.Equal(ids.OrderBy(i => i), collected.OrderBy(i => i));
        }
        finally
        {
            await DeleteAccountsAsync(ids);
        }
    }

    [Fact]
    public async Task FetchXml_ExecuteAsync_ReturnsEveryRowAcrossPages()
    {
        var prefix = NewPrefix();
        var ids = await CreateAccountsAsync(prefix, SeedCount);
        try
        {
            var fetchXml = $"""
                <fetch>
                  <entity name="account">
                    <attribute name="accountid" />
                    <attribute name="name" />
                    <filter>
                      <condition attribute="name" operator="like" value="{prefix}%" />
                    </filter>
                    <order attribute="accountid" />
                  </entity>
                </fetch>
                """;

            var results = await _fixture.Client.Fetch<Account>(fetchXml)
                .WithOptions(o => o.MaxPageSize = PageSize)
                .ExecuteAsync();

            Assert.Equal(ids.OrderBy(i => i), results.Select(r => r.AccountId).OrderBy(i => i));
        }
        finally
        {
            await DeleteAccountsAsync(ids);
        }
    }

    [Fact]
    public async Task FetchBuilder_StreamAsync_ReturnsEveryRowAcrossPages()
    {
        var prefix = NewPrefix();
        var ids = await CreateAccountsAsync(prefix, SeedCount);
        try
        {
            var streamed = new List<Guid>();
            await foreach (var account in _fixture.Client
                .FetchBuilder<Account>("account", new FetchXmlOptions { MaxPageSize = PageSize })
                .AddAttribute(a => a.AccountId)
                .AddFilter(a => a.Name.StartsWith(prefix))
                .AddOrder(a => a.AccountId)
                .StreamAsync())
            {
                streamed.Add(account.AccountId);
            }

            Assert.Equal(ids.OrderBy(i => i), streamed.OrderBy(i => i));
        }
        finally
        {
            await DeleteAccountsAsync(ids);
        }
    }

    [Fact]
    public async Task Filter_MixedAndOr_IsGroupedAsWritten()
    {
        // Inside the prefix: employees 1, 2, 3. Outside the prefix: a record with employees 2.
        // name-prefix && (employees == 1 || employees == 2) must return exactly the first two.
        // Without parentheses OData would read it as (prefix && employees == 1) || employees == 2,
        // which also matches the outside record (and any other account with 2 employees).
        var prefix = NewPrefix();
        var inside = await CreateAccountsAsync(prefix, 3, i => i + 1);
        var outside = await CreateAccountsAsync(NewPrefix(), 1, _ => 2);
        try
        {
            var results = await _fixture.Client.Query<Account>()
                .Select(a => new { a.AccountId })
                .Where(a => a.Name.StartsWith(prefix) && (a.NumberOfEmployees == 1 || a.NumberOfEmployees == 2))
                .GetAllAsync();

            Assert.Equal(inside.Take(2).OrderBy(i => i), results.Select(r => r.AccountId).OrderBy(i => i));
        }
        finally
        {
            await DeleteAccountsAsync(inside.Concat(outside));
        }
    }

    [Fact]
    public async Task CreateAsync_WithoutRepresentation_ReturnsTheNewRecordId()
    {
        var name = NewPrefix();
        var result = await _fixture.Client.Entity<Account>().Set(a => a.Name, name).CreateAsync();
        try
        {
            Assert.NotNull(result.Id);
            Assert.Null(result.Entity);

            var retrieved = await _fixture.Client.Entity<Account>(result.Id).Select(a => a.Name).RetrieveAsync();
            Assert.Equal(name, retrieved!.Name);
        }
        finally
        {
            await DeleteAccountsAsync([result.Id!.Value]);
        }
    }

    [Fact]
    public async Task UpdateAsync_OnMissingRecord_Throws404_AndDoesNotCreateIt()
    {
        var missingId = Guid.NewGuid();
        try
        {
            var ex = await Assert.ThrowsAsync<DataverseException>(() =>
                _fixture.Client.Entity<Account>(missingId).Set(a => a.Name, NewPrefix()).UpdateAsync());
            Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);

            var lookup = await Assert.ThrowsAsync<DataverseException>(() =>
                _fixture.Client.Entity<Account>(missingId).Select(a => a.Name).RetrieveAsync());
            Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        }
        finally
        {
            await DeleteAccountsAsync([missingId]);
        }
    }

    [Fact]
    public async Task BatchAddUpdate_OnMissingRecord_FailsAndDoesNotCreateIt()
    {
        var missingId = Guid.NewGuid();
        try
        {
            var result = await _fixture.Client.Batch()
                .AddUpdate(missingId, new Account { Name = NewPrefix() })
                .ExecuteAsync();

            Assert.False(result.Results.Single().IsSuccess);

            var lookup = await Assert.ThrowsAsync<DataverseException>(() =>
                _fixture.Client.Entity<Account>(missingId).Select(a => a.Name).RetrieveAsync());
            Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        }
        finally
        {
            await DeleteAccountsAsync([missingId]);
        }
    }
}
