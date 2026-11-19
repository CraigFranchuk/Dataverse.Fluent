using Dataverse.Fluent.IntegrationTests.Models;

namespace Dataverse.Fluent.IntegrationTests;

// Addressing records by an alternate key whose value contains characters that IIS or Dataverse treat specially in a
// URL path. A 2026-11-18 probe found that &, ? and + failed every key-based request when sent directly (IIS rejects
// them with an HTML error page) but work inside $batch, and that / and \ fail by any route.
// Needs Dataverse:AccountAlternateKeyColumn. Records are deleted by the tests or by the fixture.
[Collection("Dataverse Integration Tests")]
[Trait("Requires", "AccountAlternateKey")]
public class AlternateKeyTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public AlternateKeyTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    public static TheoryData<string> AddressableValues =>
    [
        "plain", "a b", "o'b", "a&b", "a+b", "a?b", "a#b", "50%", "a:b", "a*b", "a<b>", "é日本", "a=b;c,d", "a(b)@c"
    ];

    [Theory]
    [MemberData(nameof(AddressableValues))]
    public async Task RetrieveUpdateUpsertAndDelete_ByAlternateKey_ReachTheRecord(string text)
    {
        var column = RequireAlternateKeyColumn();
        var ct = TestContext.Current.CancellationToken;
        var key = Key(text);
        var name = $"AK {text}";
        await _fixture.Client.Entity<Account>().Set(a => a.Name, name).Set(column, key).CreateAsync(ct);

        var retrieved = await _fixture.Client.Entity<Account>().UsingAlternateKey(column, key).Select(a => a.Name).RetrieveAsync(ct);
        await _fixture.Client.Entity<Account>().UsingAlternateKey(column, key).Set(a => a.Revenue, 10m).UpdateAsync(ct);
        var upserted = await _fixture.Client.Entity<Account>().UsingAlternateKey(column, key).Set(a => a.Revenue, 20m).UpsertAsync(ct: ct);
        var afterWrites = await _fixture.Client.Entity<Account>().UsingAlternateKey(column, key).Select(a => a.Revenue).RetrieveAsync(ct);
        await _fixture.Client.Entity<Account>().UsingAlternateKey(column, key).DeleteAsync(ct);

        Assert.Equal(name, retrieved!.Name);
        Assert.Equal(20m, afterWrites!.Revenue);
        Assert.NotEqual(Guid.Empty, upserted.Id);
        Assert.Empty(await _fixture.Client.Query<Account>().Where(a => a.Name == name).GetListAsync(ct));
    }

    [Theory]
    [MemberData(nameof(AddressableValues))]
    public async Task Upsert_ByAlternateKey_CreatesARecordThatDoesNotExist(string text)
    {
        var column = RequireAlternateKeyColumn();
        var ct = TestContext.Current.CancellationToken;
        var key = Key(text);

        var created = await _fixture.Client.Entity<Account>().UsingAlternateKey(column, key)
            .Set(a => a.Name, $"AK new {text}").UpsertAsync(ct: ct);
        var retrieved = await _fixture.Client.Entity<Account>().UsingAlternateKey(column, key).Select(a => a.Name).RetrieveAsync(ct);
        await _fixture.Client.Entity<Account>().UsingAlternateKey(column, key).DeleteAsync(ct);

        Assert.Equal($"AK new {text}", retrieved!.Name);
        // Dataverse identifies a record upserted by key by that key in OData-EntityId, so there is no GUID to report.
        Assert.Null(created.Id);
        Assert.Contains($"{column}=", created.EntityUrl);
    }

    [Theory]
    [InlineData("a&b")]
    [InlineData("a b")]
    public async Task Associate_FromARecordAddressedByAlternateKey_LinksTheRecords(string text)
    {
        var column = RequireAlternateKeyColumn();
        var ct = TestContext.Current.CancellationToken;
        var key = Key(text);
        var account = await _fixture.Client.Entity<Account>().Set(a => a.Name, $"AK assoc {text}").Set(column, key).CreateAsync(ct);
        var contact = await _fixture.Client.Entity<Contact>().Set(c => c.LastName, $"AK assoc {Guid.NewGuid():N}"[..30]).CreateAsync(ct);

        await _fixture.Client.Associate()
            .From<Account>(column, key)
            .Via("contact_customer_accounts")
            .To<Contact>(contact.Id!.Value)
            .AssociateAsync(ct);

        var linked = await _fixture.Client.Transport.ExecuteRequestAsync(HttpMethod.Get,
            $"contacts({contact.Id})?$select=_parentcustomerid_value", ct: ct);
        Assert.Equal(account.Id, Guid.Parse((string)linked["_parentcustomerid_value"]!));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void AKeyThatDataverseCannotAddress_IsRejectedBeforeSending(string text)
    {
        var column = RequireAlternateKeyColumn();

        var ex = Assert.Throws<ArgumentException>(() => _fixture.Client.Entity<Account>().UsingAlternateKey(column, Key(text)));

        Assert.Contains("cannot be addressed", ex.Message);
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("a&b")]
    [InlineData("a+b")]
    [InlineData("50%")]
    [InlineData("o'b")]
    [InlineData("é日本")]
    public async Task SetLookupByAlternateKey_BindsTheRecord(string text)
    {
        // A bind carries the key in the JSON body, which Dataverse does not URL-decode, so it is written as is.
        var column = RequireAlternateKeyColumn();
        var ct = TestContext.Current.CancellationToken;
        var key = Key(text);
        var parent = await _fixture.Client.Entity<Account>().Set(a => a.Name, $"AK parent {text}").Set(column, key).CreateAsync(ct);

        var contact = await _fixture.Client.Entity<Contact>()
            .Set(c => c.LastName, $"AK child {Guid.NewGuid():N}"[..30])
            .SetLookupByAlternateKey("parentcustomerid_account", "accounts", column, key)
            .CreateAsync(ct);
        var linked = await _fixture.Client.Transport.ExecuteRequestAsync(HttpMethod.Get,
            $"contacts({contact.Id})?$select=_parentcustomerid_value", ct: ct);

        Assert.Equal(parent.Id, Guid.Parse((string)linked["_parentcustomerid_value"]!));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a#b")]
    [InlineData("a?b")]
    public void SetLookupByAlternateKey_WithAKeyDataverseCannotBind_IsRejectedBeforeSending(string text)
    {
        var column = RequireAlternateKeyColumn();

        var ex = Assert.Throws<ArgumentException>(() => _fixture.Client.Entity<Contact>()
            .SetLookupByAlternateKey("parentcustomerid_account", "accounts", column, Key(text)));

        Assert.Contains("cannot be used to bind", ex.Message);
    }

    private string RequireAlternateKeyColumn()
    {
        var column = _fixture.Configuration.AccountAlternateKeyColumn;
        Assert.SkipWhen(string.IsNullOrWhiteSpace(column),
            "Set Dataverse:AccountAlternateKeyColumn to a text column with a single-column alternate key on Account.");
        return column!;
    }

    private static string Key(string text) => $"k{Guid.NewGuid().ToString("N")[..6]}{text}";
}
