using Dataverse.Fluent.Builders;
using Dataverse.Fluent.IntegrationTests.Models;

namespace Dataverse.Fluent.IntegrationTests;

// Checks that lambda filters mean what the C# says against a live environment. Dataverse treats %, _ and [ ] as SQL
// wildcards in contains/startswith/endswith and FetchXML like, so unescaped text matched other records.
// Records created here are deleted by the fixture when the run ends (see RecordTrackingTransport).
[Collection("Dataverse Integration Tests")]
public class FilterCorrectnessTests : IClassFixture<DataverseTestFixture>, IAsyncLifetime
{
    private readonly DataverseTestFixture _fixture;
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];

    public FilterCorrectnessTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    private string Underscore => $"Wc {_tag} a_b";
    private string NotUnderscore => $"Wc {_tag} aXb";
    private string Percent => $"Wc {_tag} 50% off";
    private string NotPercent => $"Wc {_tag} 50 cents off";
    private string Bracket => $"Wc {_tag} [x] end";
    private string Apostrophe => $"Wc {_tag} O'Brien";

    public async ValueTask InitializeAsync()
    {
        foreach (var name in new[] { Underscore, NotUnderscore, Percent, NotPercent, Bracket, Apostrophe })
            await _fixture.Client.Entity<Account>().Set(a => a.Name, name).CreateAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task OData_StringFunctions_MatchTheTextLiterally()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal([Underscore], await NamesAsync(a => a.Name.Contains($"{_tag} a_b"), ct));
        Assert.Equal([Percent], await NamesAsync(a => a.Name.StartsWith($"Wc {_tag} 50%"), ct));
        Assert.Equal([Bracket], await NamesAsync(a => a.Name.Contains($"{_tag} [x"), ct));
        Assert.Equal([Underscore], await NamesAsync(a => a.Name.EndsWith($"{_tag} a_b"), ct));
    }

    [Fact]
    public async Task FetchXml_StringFunctions_MatchTheTextLiterally()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal([Underscore], await FetchNamesAsync(a => a.Name.Contains($"{_tag} a_b"), ct));
        Assert.Equal([Percent], await FetchNamesAsync(a => a.Name.StartsWith($"Wc {_tag} 50%"), ct));
        Assert.Equal([Bracket], await FetchNamesAsync(a => a.Name.Contains($"{_tag} [x"), ct));
    }

    [Fact]
    public async Task ListContains_FindsEveryNamedRecord()
    {
        var names = new List<string> { Underscore, NotUnderscore, Apostrophe };

        var found = await NamesAsync(a => names.Contains(a.Name), TestContext.Current.CancellationToken);

        Assert.Equal(names.Order(), found);
    }

    [Fact]
    public async Task DataverseFunctionsIn_AcceptsAValueWithAnApostrophe()
    {
        var found = await NamesAsync(a => DataverseFunctions.In(a.Name, Apostrophe, Percent), TestContext.Current.CancellationToken);

        Assert.Equal(new[] { Apostrophe, Percent }.Order(), found);
    }

    private async Task<List<string>> NamesAsync(System.Linq.Expressions.Expression<Func<Account, bool>> filter, CancellationToken ct)
    {
        var rows = await _fixture.Client.Query<Account>()
            .Where(filter)
            .Where(a => a.Name.StartsWith($"Wc {_tag}"))
            .Select(a => a.Name)
            .GetListAsync(ct);
        return [.. rows.Select(r => r.Name).Order()];
    }

    private async Task<List<string>> FetchNamesAsync(System.Linq.Expressions.Expression<Func<Account, bool>> filter, CancellationToken ct)
    {
        var rows = await _fixture.Client.FetchBuilder<Account>("account")
            .AddAttribute(a => a.Name)
            .AddFilter(filter)
            .AddFilter(a => a.Name.StartsWith($"Wc {_tag}"))
            .ExecuteAsync(ct);
        return [.. rows.Select(r => r.Name).Order()];
    }
}
