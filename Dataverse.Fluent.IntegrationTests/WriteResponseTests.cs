using System.Net;
using System.Text.Json.Serialization;
using Dataverse.Fluent.Models;
using static Dataverse.Fluent.IntegrationTests.FilterValueTests;

namespace Dataverse.Fluent.IntegrationTests;

// What the library reports after a write, checked against live Dataverse (TEST_PLAN item 6), and formatted values
// (item 7). Records created here are deleted by the fixture when the run ends.
[Collection("Dataverse Integration Tests")]
public class WriteResponseTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public WriteResponseTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewName() => $"Write resp {Guid.NewGuid()}";

    private async Task<Guid> CreateAsync(string? name = null)
        => (await _fixture.Client.Entity<ValueAccount>().Set(x => x.Name, name ?? NewName()).CreateAsync(Ct)).Id!.Value;

    // ───────── Create ─────────

    [Fact]
    public async Task Create_WithoutARepresentation_ReturnsTheIdFromTheHeader()
    {
        var result = await _fixture.Client.Entity<ValueAccount>().Set(x => x.Name, NewName()).CreateAsync(Ct);

        Assert.NotNull(result.Id);
        Assert.EndsWith($"accounts({result.Id})", result.EntityUrl);
        Assert.Null(result.Entity);
    }

    [Fact]
    public async Task Create_WithARepresentation_ReturnsTheRecord()
    {
        var name = NewName();

        var result = await _fixture.Client.Entity<ValueAccount>().Set(x => x.Name, name).Set(x => x.Revenue, 10m)
            .ReturnRepresentation().CreateAsync(Ct);

        Assert.NotNull(result.Entity);
        Assert.NotEqual(Guid.Empty, result.Entity!.AccountId);
        Assert.Equal(name, result.Entity.Name);
        Assert.Equal(10m, result.Entity.Revenue);
    }

    [Fact]
    public async Task Create_WithAPartialRepresentation_ReturnsOnlyThoseColumns()
    {
        var result = await _fixture.Client.Entity<ValueAccount>().Set(x => x.Name, NewName()).Set(x => x.Revenue, 10m)
            .ReturnRepresentation(x => new { x.AccountId, x.Name }).CreateAsync(Ct);

        Assert.NotEqual(Guid.Empty, result.Entity!.AccountId);
        Assert.StartsWith("Write resp ", result.Entity.Name);
        Assert.Null(result.Entity.Revenue); // set, but not selected
    }

    // ───────── Upsert ─────────

    [Fact]
    public async Task Upsert_WithoutARepresentation_CannotTellCreateFromUpdate()
    {
        var id = Guid.NewGuid();

        var created = await _fixture.Client.Entity<ValueAccount>(id).Set(x => x.Name, NewName()).UpsertAsync(ct: Ct);
        var updated = await _fixture.Client.Entity<ValueAccount>(id).Set(x => x.Revenue, 5m).UpsertAsync(ct: Ct);

        // Both are 204 No Content, so Created is null; the ID comes from OData-EntityId.
        Assert.Null(created.Created);
        Assert.Null(updated.Created);
        Assert.Equal(id, created.Id);
        Assert.Equal(id, updated.Id);
    }

    [Fact]
    public async Task Upsert_WithARepresentation_SaysWhetherItCreated()
    {
        var id = Guid.NewGuid();

        var created = await _fixture.Client.Entity<ValueAccount>(id).Set(x => x.Name, NewName()).ReturnRepresentation().UpsertAsync(ct: Ct);
        var updated = await _fixture.Client.Entity<ValueAccount>(id).Set(x => x.Revenue, 5m).ReturnRepresentation().UpsertAsync(ct: Ct);

        Assert.True(created.Created);
        Assert.False(updated.Created);
        Assert.Equal(id, created.Entity!.AccountId);
        Assert.Equal(5m, updated.Entity!.Revenue);
    }

    // ───────── Update and ETags ─────────

    [Fact]
    public async Task Update_OfAMissingRecord_Is404_NotACreate()
    {
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<DataverseException>(() =>
            _fixture.Client.Entity<ValueAccount>(id).Set(x => x.Name, NewName()).UpdateAsync(Ct));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task ETag_RoundTrips_AndAStaleOneIsRejected()
    {
        var id = await CreateAsync();

        var first = await _fixture.Client.Entity<ValueAccount>(id).RetrieveWithMetadataAsync(Ct);
        Assert.Matches("^W/\"[0-9]+\"$", first.ETag!);

        // The current ETag lets the update through and changes the ETag.
        await _fixture.Client.Entity<ValueAccount>(id).WithETag(first.ETag!).Set(x => x.Revenue, 1m).UpdateAsync(Ct);
        var second = await _fixture.Client.Entity<ValueAccount>(id).RetrieveWithMetadataAsync(Ct);
        Assert.NotEqual(first.ETag, second.ETag);

        // The old one is now stale.
        var ex = await Assert.ThrowsAsync<DataverseException>(() =>
            _fixture.Client.Entity<ValueAccount>(id).WithETag(first.ETag!).Set(x => x.Revenue, 2m).UpdateAsync(Ct));
        Assert.Equal(HttpStatusCode.PreconditionFailed, ex.StatusCode);

        var after = await _fixture.Client.Entity<ValueAccount>(id).RetrieveAsync(Ct);
        Assert.Equal(1m, after!.Revenue);
    }

    [Fact]
    public async Task ETag_FromARepresentation_CanBeMappedOnThePoco()
    {
        var result = await _fixture.Client.Entity<EtagAccount>().Set(x => x.Name, NewName()).ReturnRepresentation().CreateAsync(Ct);

        Assert.Matches("^W/\"[0-9]+\"$", result.Entity!.ETag!);
        await _fixture.Client.Entity<EtagAccount>(result.Entity.AccountId).WithETag(result.Entity.ETag!).Set(x => x.Name, NewName()).UpdateAsync(Ct);
    }

    // ───────── Formatted values (item 7) ─────────

    private async Task<(Guid Account, Guid Contact)> CreateFormattedAsync()
    {
        var contact = (await _fixture.Client.Entity<ValueContact>().Set(x => x.LastName, "Formatted " + Guid.NewGuid().ToString("N")[..6])
            .ReturnRepresentation().CreateAsync(Ct)).Entity!.ContactId;
        var account = (await _fixture.Client.Entity<ValueAccount>()
            .SetFrom(new ValueAccount { Name = NewName(), Revenue = 1234.5m, Industry = Industry.Accounting, CreditOnHold = true })
            .SetLookup("primarycontactid", "contacts", contact)
            .ReturnRepresentation().CreateAsync(Ct)).Entity!.AccountId;
        return (account, contact);
    }

    [Fact]
    public async Task RetrieveWithMetadata_ReturnsFormattedValuesForEachKindOfColumn()
    {
        var (account, _) = await CreateFormattedAsync();

        var result = await _fixture.Client.Entity<FormattedAccount>(account).IncludeFormattedValues().RetrieveWithMetadataAsync(Ct);
        var f = result.FormattedValues;

        Assert.Equal("Accounting", f["industrycode"]);                    // choice
        Assert.Equal("Yes", f["creditonhold"]);                           // yes/no
        Assert.Contains("1,234.5", f["revenue"]);                         // money, with the currency symbol
        Assert.StartsWith("Formatted ", f["_primarycontactid_value"]);    // lookup: the related record's name
        Assert.False(string.IsNullOrEmpty(f["createdon"]));               // date and time
        Assert.DoesNotContain(f.Keys, k => k.Contains('@'));
    }

    [Fact]
    public async Task RetrieveWithMetadata_WithoutIncludeFormattedValues_HasNone()
    {
        var (account, _) = await CreateFormattedAsync();

        var result = await _fixture.Client.Entity<FormattedAccount>(account).RetrieveWithMetadataAsync(Ct);

        Assert.Empty(result.FormattedValues);
        Assert.NotNull(result.ETag);
    }

    [Fact]
    public async Task Query_IncludeFormattedValues_FillsAnnotatedPocoProperties()
    {
        var (account, _) = await CreateFormattedAsync();

        var rows = await _fixture.Client.Query<FormattedAccount>().Where(x => x.AccountId == account).IncludeFormattedValues().GetListAsync(Ct);

        var row = Assert.Single(rows);
        Assert.Equal(Industry.Accounting, row.Industry);
        Assert.Equal("Accounting", row.IndustryName);
        Assert.Equal("Yes", row.CreditOnHoldName);
        Assert.Contains("1,234.5", row.RevenueText);
        Assert.StartsWith("Formatted ", row.PrimaryContactName);
    }

    [Fact]
    public async Task Query_WithoutIncludeFormattedValues_LeavesThemNull()
    {
        var (account, _) = await CreateFormattedAsync();

        var row = Assert.Single(await _fixture.Client.Query<FormattedAccount>().Where(x => x.AccountId == account).GetListAsync(Ct));

        Assert.Equal(Industry.Accounting, row.Industry);
        Assert.Null(row.IndustryName);
        Assert.Null(row.PrimaryContactName);
    }

    // ───────── Models ─────────

    private const string Formatted = "@OData.Community.Display.V1.FormattedValue";

    [EntitySet("accounts")]
    public class EtagAccount
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("accountid")] public Guid AccountId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("@odata.etag")] public string? ETag { get; set; }
    }

    [EntitySet("accounts")]
    public class FormattedAccount
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("accountid")] public Guid AccountId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("industrycode")] public Industry? Industry { get; set; }
        [JsonPropertyName("revenue")] public decimal? Revenue { get; set; }
        [JsonPropertyName("creditonhold")] public bool? CreditOnHold { get; set; }
        [JsonPropertyName("_primarycontactid_value")] public Guid? PrimaryContactId { get; set; }
        [JsonPropertyName("createdon")] public DateTime CreatedOn { get; set; }

        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("industrycode" + Formatted)] public string? IndustryName { get; set; }
        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("revenue" + Formatted)] public string? RevenueText { get; set; }
        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("creditonhold" + Formatted)] public string? CreditOnHoldName { get; set; }
        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("_primarycontactid_value" + Formatted)] public string? PrimaryContactName { get; set; }
    }
}
