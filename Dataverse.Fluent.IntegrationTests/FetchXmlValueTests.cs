using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Dataverse.Fluent.Builders;
using static Dataverse.Fluent.IntegrationTests.FilterValueTests;

namespace Dataverse.Fluent.IntegrationTests;

// FetchXML filter values by CLR type, checked by the rows they return, and compared with the same OData filter.
// The FetchXML visitor formats values on its own (bool as 1/0, dates as UTC with Z), so it is tested separately
// from FilterValueTests. Records created here are deleted by the fixture when the run ends.
[Collection("Dataverse Integration Tests")]
public class FetchXmlValueTests : IClassFixture<DataverseTestFixture>, IAsyncLifetime
{
    private readonly DataverseTestFixture _fixture;
    private readonly string _tag = "Fx " + Guid.NewGuid().ToString("N")[..8];

    private Guid _a, _b, _contact;
    private DateTime _aCreatedOn;

    public FetchXmlValueTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _contact = (await _fixture.Client.Entity<ValueContact>()
            .SetFrom(new ValueContact { LastName = _tag, BirthDate = new DateOnly(1990, 5, 17) })
            .ReturnRepresentation().CreateAsync(Ct)).Entity!.ContactId;

        var a = (await _fixture.Client.Entity<ValueAccount>()
            .SetFrom(new ValueAccount
            {
                Name = $"{_tag} A", Revenue = 123456789012.35m, Latitude = 47.6062, Employees = 42, CreditOnHold = true,
                Industry = Industry.Accounting,
            })
            .SetLookup("primarycontactid", "contacts", _contact)
            .ReturnRepresentation().CreateAsync(Ct)).Entity!;
        _a = a.AccountId;
        _aCreatedOn = a.CreatedOn;

        _b = (await _fixture.Client.Entity<ValueAccount>()
            .SetFrom(new ValueAccount { Name = $"{_tag} B", Revenue = 0.01m, Latitude = -33.8688, Employees = 0, CreditOnHold = false })
            .ReturnRepresentation().CreateAsync(Ct)).Entity!.AccountId;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // The IDs of this class's accounts that match the filter, through FetchXML; and the same filter through OData.
    private async Task<Guid[]> FetchAsync(Expression<Func<FetchAccount, bool>> filter)
    {
        var tag = _tag;
        var rows = await _fixture.Client.FetchBuilder<FetchAccount>("account")
            .AddAttribute(x => x.AccountId)
            .AddFilter(x => x.Name.StartsWith(tag))
            .AddFilter(filter)
            .ExecuteAsync(Ct);
        return Order(rows.Select(r => r.AccountId));
    }

    private async Task<Guid[]> ODataAsync(Expression<Func<FetchAccount, bool>> filter)
    {
        var tag = _tag;
        var rows = await _fixture.Client.Query<FetchAccount>()
            .Select(x => new { x.AccountId })
            .Where(x => x.Name.StartsWith(tag))
            .Where(filter)
            .GetListAsync(Ct);
        return Order(rows.Select(r => r.AccountId));
    }

    private Guid[] Order(IEnumerable<Guid> ids) => ids.OrderBy(id => id == _a ? 0 : id == _b ? 1 : 2).ToArray();

    // Asserts the FetchXML and OData filters both return exactly the expected records.
    private async Task AssertBothAsync(Guid[] expected, Expression<Func<FetchAccount, bool>> filter)
    {
        Assert.Equal(expected, await FetchAsync(filter));
        Assert.Equal(expected, await ODataAsync(filter));
    }

    // ───────── Dates ─────────

    [Fact]
    public async Task DateTime_IsReadAsUtc_LikeOData()
    {
        var utc = _aCreatedOn;
        var local = _aCreatedOn.ToLocalTime();
        var unspecified = DateTime.SpecifyKind(_aCreatedOn, DateTimeKind.Unspecified);
        var withMs = _aCreatedOn.AddMilliseconds(999);

        await AssertBothAsync([_a], x => x.Name.EndsWith(" A") && x.CreatedOn == utc);
        await AssertBothAsync([_a], x => x.Name.EndsWith(" A") && x.CreatedOn == local);
        await AssertBothAsync([_a], x => x.Name.EndsWith(" A") && x.CreatedOn == unspecified);
        await AssertBothAsync([_a], x => x.Name.EndsWith(" A") && x.CreatedOn == withMs);
        await AssertBothAsync([], x => x.Name.EndsWith(" A") && x.CreatedOn < utc);
        await AssertBothAsync([], x => x.Name.EndsWith(" A") && x.CreatedOn > utc);
    }

    [Fact]
    public async Task DateTime_AnHourOff_DoesNotMatch()
    {
        // Proves the Z is honoured: if Dataverse read the value in the user's time zone, one of these would match.
        var earlier = _aCreatedOn.AddHours(-1);
        var later = _aCreatedOn.AddHours(1);

        await AssertBothAsync([], x => x.Name.EndsWith(" A") && (x.CreatedOn == earlier || x.CreatedOn == later));
        await AssertBothAsync([_a], x => x.Name.EndsWith(" A") && x.CreatedOn > earlier && x.CreatedOn < later);
    }

    [Fact]
    public async Task DateOnly_OnADateOnlyColumn()
    {
        var tag = _tag;
        var birthday = new DateOnly(1990, 5, 17);
        var dayAfter = birthday.AddDays(1);

        var on = await _fixture.Client.FetchBuilder<ValueContact>("contact").AddAttribute(c => c.ContactId)
            .AddFilter(c => c.LastName == tag && c.BirthDate == birthday).ExecuteAsync(Ct);
        var after = await _fixture.Client.FetchBuilder<ValueContact>("contact").AddAttribute(c => c.ContactId)
            .AddFilter(c => c.LastName == tag && c.BirthDate >= dayAfter).ExecuteAsync(Ct);

        Assert.Equal(_contact, Assert.Single(on).ContactId);
        Assert.Empty(after);
    }

    // ───────── Numbers, booleans, enums, null ─────────

    [Fact]
    public async Task Numbers()
    {
        long zero = 0;

        await AssertBothAsync([_a], x => x.Revenue == 123456789012.35m);
        await AssertBothAsync([_b], x => x.Revenue < 0.1m);
        await AssertBothAsync([_a], x => x.Latitude == 47.6062);
        await AssertBothAsync([_b], x => x.Latitude < -33.0);
        await AssertBothAsync([_a, _b], x => x.Latitude > -1e20);
        await AssertBothAsync([_a], x => x.Latitude > 1e-5);
        await AssertBothAsync([_a], x => x.Employees == 42);
        await AssertBothAsync([_a, _b], x => x.VersionNumber > zero);
    }

    [Fact]
    public async Task Bool_AsOneAndZero()
    {
        await AssertBothAsync([_a], x => x.CreditOnHold == true);
        await AssertBothAsync([_b], x => x.CreditOnHold == false);
        await AssertBothAsync([_b], x => !(x.CreditOnHold == true));
    }

    [Fact]
    public async Task Bool_UsedAsTheCondition_FiltersTheRecords()
    {
        // Until 1.1.1 both translators sent the bare column ($filter=creditonhold, or the attribute name as text in a
        // FetchXML filter), which Dataverse accepts and ignores: every record matched, with the flag set or not.
        await AssertBothAsync([_a], x => x.CreditOnHold!.Value);
        await AssertBothAsync([_b], x => !x.CreditOnHold!.Value);
        await AssertBothAsync([_a], x => x.Employees > 1 && x.CreditOnHold!.Value);
        await AssertBothAsync([_b], x => x.Employees == 0 || x.CreditOnHold!.Value && x.Employees == 0);
    }

    [Fact]
    public async Task Enum_AndNull()
    {
        await AssertBothAsync([_a], x => x.Industry == Industry.Accounting);
        await AssertBothAsync([_b], x => x.Industry == null);
        await AssertBothAsync([_a], x => x.Industry.HasValue);
        await AssertBothAsync([_b], x => !x.Industry.HasValue);
    }

    [Fact]
    public async Task ConstantOnTheLeft_MeansTheSame()
    {
        // Until 1.0.2 FetchXML sent the value as the attribute name (attribute="42"), which Dataverse rejected.
        await AssertBothAsync([_a], x => 42 == x.Employees);
        await AssertBothAsync([_a], x => 10 < x.Employees);
        await AssertBothAsync([_b], x => 10 >= x.Employees);
        await AssertBothAsync([_b], x => null == x.Industry);
    }

    [Fact]
    public async Task Negation_OfAnAndOrOr()
    {
        await AssertBothAsync([_b], x => !(x.Employees == 42 && x.CreditOnHold == true));
        await AssertBothAsync([], x => !(x.Employees == 42 || x.CreditOnHold == false));
    }

    // ───────── Strings ─────────

    [Theory]
    [InlineData("a&b<c>d")]
    [InlineData("O'Brien \"q\"")]
    [InlineData("ünïcødé 日本語 😀")]
    [InlineData("a+b#c/d?e")]
    public async Task String_Eq_MatchesExactly(string suffix)
    {
        var name = $"{_tag} S {suffix}";
        var id = (await _fixture.Client.Entity<ValueAccount>().Set(x => x.Name, name).ReturnRepresentation().CreateAsync(Ct)).Entity!.AccountId;

        await AssertBothAsync([id], x => x.Name == name);
        await AssertBothAsync([id], x => x.Name.EndsWith(suffix));
    }

    // ───────── Lookups ─────────

    [Fact]
    public async Task Lookup_EqAndIn()
    {
        // ids.Contains(x.Lookup!.Value) was rejected by the OData visitor until 1.0.2.
        var contact = _contact;
        var ids = new List<Guid> { Guid.NewGuid(), _contact };

        await AssertBothAsync([_a], x => x.PrimaryContactId == contact);
        await AssertBothAsync([_a], x => ids.Contains(x.PrimaryContactId!.Value));
        await AssertBothAsync([_b], x => x.PrimaryContactId == null);
    }

    // ───────── Options and raw FetchXML ─────────

    [Fact]
    public async Task NoLock_ReturnsTheSameRows()
    {
        var tag = _tag;

        var built = await _fixture.Client.FetchBuilder<FetchAccount>("account")
            .AddAttribute(x => x.AccountId).AddFilter(x => x.Name.StartsWith(tag)).NoLock().ExecuteAsync(Ct);
        var raw = await _fixture.Client.Fetch<FetchAccount>(
            $"<fetch><entity name='account'><attribute name='accountid'/><filter><condition attribute='name' operator='like' value='{tag}%'/></filter></entity></fetch>",
            new FetchXmlOptions { NoLock = true }).ExecuteAsync(Ct);

        Assert.Equal([_a, _b], Order(built.Select(r => r.AccountId)));
        Assert.Equal([_a, _b], Order(raw.Select(r => r.AccountId)));
    }

    [Fact]
    public async Task RawAggregate_DeserializesIntoAPoco()
    {
        var rows = await _fixture.Client.Fetch<AccountTotals>($"""
            <fetch aggregate='true'>
              <entity name='account'>
                <attribute name='accountid' alias='count' aggregate='count' />
                <attribute name='revenue' alias='total' aggregate='sum' />
                <attribute name='numberofemployees' alias='most' aggregate='max' />
                <attribute name='creditonhold' alias='onhold' groupby='true' />
                <filter><condition attribute='name' operator='like' value='{_tag}%' /></filter>
                <order alias='onhold' />
              </entity>
            </fetch>
            """).ExecuteAsync(Ct);

        Assert.Equal(2, rows.Count);
        var off = rows.Single(r => r.OnHold == false);
        var on = rows.Single(r => r.OnHold == true);
        Assert.Equal(1, on.Count);
        Assert.Equal(123456789012.35m, on.Total);
        Assert.Equal(42, on.Most);
        Assert.Equal(1, off.Count);
        Assert.Equal(0.01m, off.Total);
        Assert.Equal(0, off.Most);
    }

    // ───────── Models ─────────

    [EntitySet("accounts")]
    public class FetchAccount
    {
        [JsonPropertyName("accountid")] public Guid AccountId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("revenue")] public decimal? Revenue { get; set; }
        [JsonPropertyName("address1_latitude")] public double? Latitude { get; set; }
        [JsonPropertyName("numberofemployees")] public int? Employees { get; set; }
        [JsonPropertyName("creditonhold")] public bool? CreditOnHold { get; set; }
        [JsonPropertyName("industrycode")] public Industry? Industry { get; set; }
        [JsonPropertyName("versionnumber")] public long VersionNumber { get; set; }
        [JsonPropertyName("createdon")] public DateTime CreatedOn { get; set; }
        [JsonPropertyName("_primarycontactid_value")] public Guid? PrimaryContactId { get; set; }
    }

    // A raw FetchXML query is sent to the entity set's URL, so the POCO needs [EntitySet] even for an aggregate.
    [EntitySet("accounts")]
    public class AccountTotals
    {
        [JsonPropertyName("count")] public int Count { get; set; }
        [JsonPropertyName("total")] public decimal? Total { get; set; }
        [JsonPropertyName("most")] public int? Most { get; set; }
        [JsonPropertyName("onhold")] public bool? OnHold { get; set; }
    }
}
