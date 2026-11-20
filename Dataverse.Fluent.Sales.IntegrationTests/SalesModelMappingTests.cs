using Dataverse.Fluent.IntegrationTests;
using Dataverse.Fluent.Schema;
using Dataverse.Fluent.Sales.Entities;

namespace Dataverse.Fluent.Sales.IntegrationTests;

/// <summary>
/// Checks every Sales model against the environment with the schema validator: each mapped column, lookup and
/// relationship exists under the name the model uses, each type fits its column, and SetFrom sends nothing Dataverse
/// ignores. Until 1.1.1 two models mapped columns that do not exist (quote.closedatedon, incident.satisfactioncode); a
/// retrieve without $select hid that, because an unknown property just stays null.
/// </summary>
[Collection("Dataverse Integration Tests")]
public class SalesModelMappingTests(DataverseTestFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<Type> Models =>
    [
        typeof(Account), typeof(Contact), typeof(Incident), typeof(Lead),
        typeof(Opportunity), typeof(OpportunityProduct), typeof(Quote), typeof(QuoteDetail)
    ];

    [Theory]
    [MemberData(nameof(Models))]
    public async Task Model_MatchesTheEnvironment(Type model)
    {
        // Every column, type, lookup, polymorphic target, navigation property and read-only marker, plus a live $select
        // of every mapped column (see Dataverse.Fluent.Schema).
        var report = await DataverseSchemaValidator.ValidateAsync(fixture.Client.Transport, [model], ct: Ct);

        Assert.True(report.Findings.Count == 0, report.ToString());
    }

    [Fact]
    public void Models_AreEveryModelInThePackage()
        => Assert.Equal(
            Models.Select(m => (Type)m.Data).OrderBy(t => t.Name),
            DataverseSchemaValidator.FindModels(typeof(Account).Assembly).OrderBy(t => t.Name));

    // ───────── Customer and owner ─────────

    [Fact]
    public async Task CustomerAndOwner_AreReadWithTheirTable_AndARetrievedRecordCanBeWrittenBack()
    {
        // Until 1.1.1 these were read-only Guid properties: the table was unknown and SetFrom never wrote them.
        // Created records are deleted by the fixture.
        var me = await fixture.Client.Function<ActionAndFunctionTests.WhoAmIResponse>("WhoAmI").ExecuteAsync(Ct);
        var account = await fixture.Client.Entity<Account>().Set(a => a.Name, $"Customer {Guid.NewGuid():N}").CreateAsync(Ct);
        var contact = await fixture.Client.Entity<Contact>()
            .SetFrom(new Contact { LastName = $"Customer {Guid.NewGuid():N}", ParentCustomerId = DataverseReference.To<Account>(account.Id!.Value) })
            .CreateAsync(Ct);

        var created = await fixture.Client.Entity<Incident>()
            .SetFrom(new Incident
            {
                Title = $"Polymorphic {Guid.NewGuid():N}",
                CustomerId = DataverseReference.To<Account>(account.Id.Value),
                OwnerId = new DataverseReference("systemuser", me!.UserId)
            })
            .CreateAsync(Ct);

        var incident = (await fixture.Client.Entity<Incident>(created.Id).SelectAll().RetrieveAsync(Ct))!;
        Assert.Equal(new DataverseReference("account", account.Id.Value), incident.CustomerId);
        Assert.Equal(new DataverseReference("systemuser", me.UserId), incident.OwnerId);

        // Written back pointing at another table; the owner is not sent on an update.
        incident.CustomerId = DataverseReference.To<Contact>(contact.Id!.Value);
        await fixture.Client.Entity<Incident>(incident.IncidentId).SetFrom(incident).UpdateAsync(Ct);

        var id = incident.IncidentId;
        var contactId = contact.Id.Value;
        var found = await fixture.Client.Query<Incident>()
            .Where(i => i.IncidentId == id && i.CustomerId!.Id == contactId)
            .Select(i => new { i.CustomerId, i.OwnerId })
            .GetListAsync(Ct);
        var row = Assert.Single(found);
        Assert.Equal(new DataverseReference("contact", contactId), row.CustomerId);
        Assert.Equal(new DataverseReference("systemuser", me.UserId), row.OwnerId);

        var linked = await fixture.Client.Entity<Contact>(contactId).Select(c => c.ParentCustomerId).RetrieveAsync(Ct);
        Assert.Equal(new DataverseReference("account", account.Id.Value), linked!.ParentCustomerId);
    }

    // ───────── Date-only columns ─────────

    [Fact]
    public async Task Opportunity_DateOnlyColumns_RoundTrip()
    {
        // Until 1.1.1 these were DateTime properties written as a date and time, which Dataverse rejects for a date-only
        // column (HTTP 400), so a retrieved opportunity with a close date could not be written back. Created records
        // are deleted by the fixture.
        var created = await fixture.Client.Entity<Opportunity>()
            .Set(o => o.Name, $"Date-only round trip {Guid.NewGuid():N}")
            .Set(o => o.EstimatedCloseDate, new DateOnly(2026, 3, 15))
            .ReturnRepresentation(o => new { o.OpportunityId, o.Name, o.EstimatedCloseDate })
            .CreateAsync(Ct);
        var opportunity = created.Entity!;
        Assert.Equal(new DateOnly(2026, 3, 15), opportunity.EstimatedCloseDate);

        opportunity.ActualCloseDate = new DateOnly(2026, 4, 1);
        await fixture.Client.Entity<Opportunity>(opportunity.OpportunityId).SetFrom(opportunity).UpdateAsync(Ct);

        var date = new DateOnly(2026, 4, 1);
        var id = opportunity.OpportunityId;
        var odata = await fixture.Client.Query<Opportunity>()
            .Where(o => o.OpportunityId == id && o.ActualCloseDate == date && o.EstimatedCloseDate < date)
            .Select(o => new { o.EstimatedCloseDate, o.ActualCloseDate })
            .GetListAsync(Ct);
        var fetch = await fixture.Client.FetchBuilder<Opportunity>()
            .AddAttributes(o => new { o.EstimatedCloseDate, o.ActualCloseDate })
            .AddFilter(o => o.OpportunityId == id && o.ActualCloseDate == date && o.EstimatedCloseDate < date)
            .ExecuteAsync(Ct);

        foreach (var row in new[] { Assert.Single(odata), Assert.Single(fetch) })
        {
            Assert.Equal(new DateOnly(2026, 3, 15), row.EstimatedCloseDate);
            Assert.Equal(new DateOnly(2026, 4, 1), row.ActualCloseDate);
        }
    }

    [Fact]
    public async Task Quote_DateOnlyColumns_RoundTrip()
    {
        var created = await fixture.Client.Entity<Quote>()
            .SetFrom(new Quote
            {
                Name = $"Date-only round trip {Guid.NewGuid():N}",
                ExpiresOn = new DateOnly(2026, 6, 30),
                ClosedOn = new DateOnly(2026, 6, 1),
                EffectiveFrom = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)
            })
            .ReturnRepresentation(q => new { q.QuoteId, q.ExpiresOn, q.ClosedOn, q.EffectiveFrom })
            .CreateAsync(Ct);

        var quote = created.Entity!;
        Assert.Equal(new DateOnly(2026, 6, 30), quote.ExpiresOn);
        Assert.Equal(new DateOnly(2026, 6, 1), quote.ClosedOn);
        Assert.NotNull(quote.EffectiveFrom);
    }
}
