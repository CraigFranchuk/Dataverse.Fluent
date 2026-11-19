using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent.IntegrationTests;

// Lambda filter values by CLR type, checked by the rows they return. A 2026-11-18 probe found: Dataverse stores
// datetimes to the second and drops a literal's fractional seconds (so `eq 12:00:01.500Z` matches 12:00:01), which is
// what the library's own truncation does; offsets and scientific notation are accepted.
// Records created here are deleted by the fixture when the run ends.
[Collection("Dataverse Integration Tests")]
public class FilterValueTests : IClassFixture<DataverseTestFixture>, IAsyncLifetime
{
    private readonly DataverseTestFixture _fixture;
    private readonly string _tag = "Fv " + Guid.NewGuid().ToString("N")[..8];

    private Guid _a, _b, _contact;
    private DateTime _aCreatedOn;
    private static readonly DateOnly Birthday = new(1990, 5, 17);

    public FilterValueTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _contact = (await _fixture.Client.Entity<ValueContact>()
            .SetFrom(new ValueContact { LastName = _tag, BirthDate = Birthday })
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

    // The IDs of this class's accounts that match the filter.
    private async Task<Guid[]> MatchesAsync(Expression<Func<ValueAccount, bool>> filter)
    {
        var tag = _tag;
        var rows = await _fixture.Client.Query<ValueAccount>()
            .Select(x => new { x.AccountId })
            .Where(x => x.Name.StartsWith(tag))
            .Where(filter)
            .GetListAsync(Ct);
        return rows.Select(r => r.AccountId).OrderBy(id => id == _a ? 0 : 1).ToArray();
    }

    // ───────── Dates ─────────

    [Fact]
    public async Task DateTime_Utc_MatchesTheSecondItWasCreated()
    {
        var created = _aCreatedOn;
        Assert.Equal(DateTimeKind.Utc, created.Kind);

        Assert.Equal([_a], await MatchesAsync(x => x.Name.EndsWith(" A") && x.CreatedOn == created));
        Assert.Empty(await MatchesAsync(x => x.Name.EndsWith(" A") && x.CreatedOn < created));
        Assert.Equal([_a], await MatchesAsync(x => x.Name.EndsWith(" A") && x.CreatedOn >= created && x.CreatedOn <= created));
    }

    [Fact]
    public async Task DateTime_Local_IsConvertedToUtc()
    {
        var local = _aCreatedOn.ToLocalTime();

        Assert.Equal([_a], await MatchesAsync(x => x.Name.EndsWith(" A") && x.CreatedOn == local));
    }

    [Fact]
    public async Task DateTime_Unspecified_IsTakenAsUtc()
    {
        var unspecified = DateTime.SpecifyKind(_aCreatedOn, DateTimeKind.Unspecified);

        Assert.Equal([_a], await MatchesAsync(x => x.Name.EndsWith(" A") && x.CreatedOn == unspecified));
    }

    [Fact]
    public async Task DateTime_WithMilliseconds_MatchesTheSameSecond()
    {
        // Dataverse keeps whole seconds, and so does the literal: 12:00:01.999 still means 12:00:01.
        var withMs = _aCreatedOn.AddMilliseconds(999);

        Assert.Equal([_a], await MatchesAsync(x => x.Name.EndsWith(" A") && x.CreatedOn == withMs));
        Assert.Empty(await MatchesAsync(x => x.Name.EndsWith(" A") && x.CreatedOn > withMs));
    }

    [Fact]
    public async Task DateTimeOffset_WithANonZeroOffset_IsConvertedToUtc()
    {
        var offset = new DateTimeOffset(_aCreatedOn).ToOffset(TimeSpan.FromHours(-5));
        var tag = _tag;

        var rows = await _fixture.Client.Query<OffsetAccount>()
            .Select(x => new { x.AccountId })
            .Where(x => x.Name == tag + " A" && x.CreatedOn == offset)
            .GetListAsync(Ct);

        Assert.Equal(_a, Assert.Single(rows).AccountId);
    }

    [Fact]
    public async Task DateOnly_MatchesADateOnlyColumn()
    {
        var tag = _tag;
        var birthday = Birthday;
        var dayAfter = Birthday.AddDays(1);

        var on = await _fixture.Client.Query<ValueContact>().Where(c => c.LastName == tag && c.BirthDate == birthday).GetListAsync(Ct);
        var after = await _fixture.Client.Query<ValueContact>().Where(c => c.LastName == tag && c.BirthDate >= dayAfter).GetListAsync(Ct);

        Assert.Equal(_contact, Assert.Single(on).ContactId);
        Assert.Equal(Birthday, on[0].BirthDate);
        Assert.Empty(after);
    }

    // ───────── Numbers, booleans, enums, null ─────────

    [Fact]
    public async Task Decimal_LargeAndFractional()
    {
        // revenue is Money: Dataverse limits it to 0 to 100,000,000,000,000 (negatives are covered by Double) and rounds
        // it to the currency's precision, 2 places here, so a value with more places is not stored as sent.
        Assert.Equal([_a], await MatchesAsync(x => x.Revenue == 123456789012.35m));
        Assert.Equal([_a], await MatchesAsync(x => x.Revenue > 123456789012.3499m));
        Assert.Equal([_b], await MatchesAsync(x => x.Revenue == 0.01m));
        Assert.Equal([_b], await MatchesAsync(x => x.Revenue < 0.1m));
    }

    [Fact]
    public async Task Double_IncludingNegativeAndExponentForms()
    {
        Assert.Equal([_a], await MatchesAsync(x => x.Latitude == 47.6062));
        Assert.Equal([_b], await MatchesAsync(x => x.Latitude < -33.0));
        Assert.Equal([_a, _b], await MatchesAsync(x => x.Latitude > -1e20)); // written as -1E+20
        Assert.Equal([_a], await MatchesAsync(x => x.Latitude > 1e-5));     // written as 1E-05
    }

    [Fact]
    public async Task Integers_IntAndLong()
    {
        long zero = 0;

        Assert.Equal([_a], await MatchesAsync(x => x.Employees == 42));
        Assert.Equal([_b], await MatchesAsync(x => x.Employees == 0));
        Assert.Equal([_a, _b], await MatchesAsync(x => x.VersionNumber > zero));
        Assert.Equal([_a, _b], await MatchesAsync(x => x.VersionNumber < long.MaxValue));
    }

    [Fact]
    public async Task Bool_TrueFalseAndNegation()
    {
        Assert.Equal([_a], await MatchesAsync(x => x.CreditOnHold == true));
        Assert.Equal([_b], await MatchesAsync(x => x.CreditOnHold == false));
        Assert.Equal([_b], await MatchesAsync(x => !(x.CreditOnHold == true)));
    }

    [Fact]
    public async Task Enum_IsMatchedByItsChoiceValue()
    {
        Assert.Equal([_a], await MatchesAsync(x => x.Industry == Industry.Accounting));
        Assert.Empty(await MatchesAsync(x => x.Industry == Industry.Consulting));
    }

    [Fact]
    public async Task Null_AndHasValue()
    {
        Assert.Equal([_b], await MatchesAsync(x => x.Industry == null));
        Assert.Equal([_a], await MatchesAsync(x => x.Industry != null));
        Assert.Equal([_a], await MatchesAsync(x => x.Industry.HasValue));
        Assert.Equal([_b], await MatchesAsync(x => !x.Industry.HasValue));
    }

    // ───────── Strings ─────────

    [Theory]
    [InlineData("a+b")]
    [InlineData("a&b=c")]
    [InlineData("a#b")]
    [InlineData("a/b\\c")]
    [InlineData("a?b")]
    [InlineData("100%")]
    [InlineData("O'Brien")]
    [InlineData("\"quoted\"")]
    [InlineData("ünïcødé 日本語 😀")]
    [InlineData("two  spaces, trailing ")]
    [InlineData("line\nbreak")]
    // Not a tab: Dataverse stores one, but no eq, endswith, contains or FetchXML condition matches it (probed 2026-11-18).
    public async Task String_Eq_MatchesExactly(string suffix)
    {
        var name = $"{_tag} S {suffix}";
        var id = (await _fixture.Client.Entity<ValueAccount>().Set(x => x.Name, name).ReturnRepresentation().CreateAsync(Ct)).Entity!.AccountId;

        Assert.Equal([id], await MatchesAsync(x => x.Name == name));
        Assert.Equal([id], await MatchesAsync(x => x.Name.EndsWith(suffix)));
    }

    // ───────── Related records ─────────

    [Fact]
    public async Task NavigationProperty_FiltersOnTheRelatedRecord()
    {
        var tag = _tag;

        Assert.Equal([_a], await MatchesAsync(x => x.PrimaryContact!.LastName == tag));
        Assert.Equal([_a], await MatchesAsync(x => x.PrimaryContact!.BirthDate == Birthday));
        Assert.Empty(await MatchesAsync(x => x.PrimaryContact!.LastName == tag + " nobody"));
    }

    [Fact]
    public async Task NavigationProperty_NullMeansNoRelatedRecord()
    {
        Assert.Equal([_b], await MatchesAsync(x => x.PrimaryContact == null));
        Assert.Equal([_a], await MatchesAsync(x => x.PrimaryContact != null));
    }

    // ───────── Models ─────────

    // Choice values of account.industrycode.
    public enum Industry
    {
        Accounting = 1,
        Consulting = 7,
    }

    [EntitySet("accounts")]
    public class ValueAccount
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("accountid")] public Guid AccountId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("revenue")] public decimal? Revenue { get; set; }
        [JsonPropertyName("address1_latitude")] public double? Latitude { get; set; }
        [JsonPropertyName("numberofemployees")] public int? Employees { get; set; }
        [JsonPropertyName("creditonhold")] public bool? CreditOnHold { get; set; }
        [JsonPropertyName("industrycode")] public Industry? Industry { get; set; }

        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("versionnumber")] public long VersionNumber { get; set; }

        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("createdon")] public DateTime CreatedOn { get; set; }

        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("primarycontactid")] public ValueContact? PrimaryContact { get; set; }
    }

    [EntitySet("accounts")]
    public class OffsetAccount
    {
        [JsonPropertyName("accountid")] public Guid AccountId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("createdon")] public DateTimeOffset CreatedOn { get; set; }
    }

    [EntitySet("contacts")]
    public class ValueContact
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("contactid")] public Guid ContactId { get; set; }
        [JsonPropertyName("lastname")] public string LastName { get; set; } = string.Empty;
        [JsonPropertyName("birthdate")] public DateOnly? BirthDate { get; set; }
    }
}
