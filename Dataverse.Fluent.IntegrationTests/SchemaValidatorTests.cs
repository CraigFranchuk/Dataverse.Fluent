using System.Text.Json.Serialization;
using Dataverse.Fluent.Schema;

namespace Dataverse.Fluent.IntegrationTests;

/// <summary>
/// The schema validator against a live environment. The broken models reproduce mistakes the Sales models shipped with
/// until 1.1.1 (a column that does not exist, a date-only column typed DateTime, a polymorphic lookup missing a
/// table, columns Dataverse sets itself sent by SetFrom), plus the other mapping mistakes the validator can fix.
/// Every request is a read: metadata, and retrieves of random IDs for the $select check.
/// </summary>
[Collection("Dataverse Integration Tests")]
public class SchemaValidatorTests(DataverseTestFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public enum CasePriority { High = 1, Normal = 2, Low = 3, Urgent = 99 }

    [DataverseEntity("quote", "quotes")]
    public class BrokenQuote
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("quoteid")]
        public Guid QuoteId { get; set; }

        [JsonPropertyName("closedatedon")]
        public DateTime? CloseDatedon { get; set; }

        [JsonPropertyName("expireson")]
        public DateTime? ExpiresOn { get; set; }
    }

    [DataverseEntity("incident", "incidents")]
    public class BrokenIncident
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("incidentid")]
        public Guid IncidentId { get; set; }

        [JsonPropertyName("satisfactioncode")]
        public int? SatisfactionCode { get; set; }

        [JsonPropertyName("ticketnumber")]
        public string? TicketNumber { get; set; }

        [JsonPropertyName("prioritycode")]
        public CasePriority? Priority { get; set; }

        [JsonPropertyName("createdbyname")]
        public string? CreatedByName { get; set; }

        [JsonPropertyName("parentcaseid")]
        public Guid? ParentCaseId { get; set; }

        [JsonPropertyName("_subjectid_value")]
        public Guid? SubjectId { get; set; }

        [JsonPropertyName("_createdby_value")]
        public Guid? CreatedBy { get; set; }

        [DataverseLookup("customerid")]
        [DataverseLookupTarget("account", "accounts", "customerid_account")]
        public DataverseReference? CustomerId { get; set; }

        [DataverseLookup("primarycontactid", "contact", NavigationProperty = "PrimaryContactId")]
        public Guid? PrimaryContactId { get; set; }

        [JsonPropertyName("incident_tasks")]
        public List<Models.Contact>? Tasks { get; set; }
    }

    [DataverseEntity("account", "account")]
    public class WrongEntitySet
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    [DataverseEntity("acount", "acounts")]
    public class MisspelledTable
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private Task<SchemaReport> ValidateAsync(params Type[] models)
        => DataverseSchemaValidator.ValidateAsync(fixture.Client.Transport, models, ct: Ct);

    private static SchemaFinding Single(SchemaReport report, string property, string code)
    {
        var matches = report.Findings.Where(f => f.Property?.Name == property && f.Code == code).ToList();
        Assert.True(matches.Count == 1, $"Expected one {code} on {property}; the report was:{Environment.NewLine}{report}");
        return matches[0];
    }

    [Fact]
    public async Task TheTestModels_MatchTheEnvironment()
    {
        var report = await ValidateAsync(typeof(Models.Account), typeof(Models.Contact), typeof(Models.Competitor));

        Assert.True(report.Findings.Count == 0, report.ToString());
    }

    [Fact]
    public async Task BrokenQuote_ReportsTheMissingColumnAndTheDateOnlyType()
    {
        var report = await ValidateAsync(typeof(BrokenQuote));

        var missing = Single(report, nameof(BrokenQuote.CloseDatedon), SchemaCodes.ColumnNotFound);
        Assert.Contains("'closedon'", missing.Suggestion);
        var dateOnly = Single(report, nameof(BrokenQuote.ExpiresOn), SchemaCodes.IncompatibleType);
        Assert.Equal("Use DateOnly?.", dateOnly.Suggestion);
        Assert.Equal(2, report.Findings.Count);
    }

    [Fact]
    public async Task BrokenIncident_ReportsEachMistake_WithItsFix()
    {
        var report = await ValidateAsync(typeof(BrokenIncident));

        Assert.Contains("'customersatisfactioncode'", Single(report, nameof(BrokenIncident.SatisfactionCode), SchemaCodes.ColumnNotFound).Suggestion);

        var ticket = Single(report, nameof(BrokenIncident.TicketNumber), SchemaCodes.SentOnUpdate).Fix!;
        Assert.Equal(SchemaFixKind.SetReadOnly, ticket.Kind);
        Assert.Equal(DataverseOperation.Update, ticket.ReadOnlyFor);

        Assert.Contains("Urgent = 99", Single(report, nameof(BrokenIncident.Priority), SchemaCodes.EnumValueNotAnOption).Message);
        Assert.Contains("_createdby_value@OData.Community.Display.V1.FormattedValue",
            Single(report, nameof(BrokenIncident.CreatedByName), SchemaCodes.CompanionColumn).Suggestion);
        Assert.Contains("[DataverseLookup(\"parentcaseid\", \"incidents\")]",
            Single(report, nameof(BrokenIncident.ParentCaseId), SchemaCodes.LookupMappedAsColumn).Suggestion);

        var subject = Single(report, nameof(BrokenIncident.SubjectId), SchemaCodes.LookupValueWritten).Fix!;
        Assert.Equal((SchemaFixKind.MapAsLookup, "subjectid", "subjects", "subjectid"),
            (subject.Kind, subject.LookupLogicalName, subject.EntitySet, subject.NavigationProperty));

        Assert.Equal(DataverseOperation.None, subject.ReadOnlyFor);

        // A lookup mapped as a column is reported as that, and not again by the live $select check.
        Assert.Equal(SchemaCodes.LookupMappedAsColumn, Assert.Single(report.Findings, f => f.Property?.Name == nameof(BrokenIncident.ParentCaseId)).Code);

        // createdby is valid for neither create nor update, so the fix stops the write instead of binding it.
        var createdBy = Single(report, nameof(BrokenIncident.CreatedBy), SchemaCodes.LookupValueWritten).Fix!;
        Assert.Equal((SchemaFixKind.SetReadOnly, DataverseOperation.CreateAndUpdate), (createdBy.Kind, createdBy.ReadOnlyFor));

        var contact = Single(report, nameof(BrokenIncident.CustomerId), SchemaCodes.MissingLookupTarget).Fix!;
        Assert.Equal((SchemaFixKind.AddLookupTarget, "contact", "contacts", "customerid_contact"),
            (contact.Kind, contact.TargetLogicalName, contact.EntitySet, contact.NavigationProperty));

        Assert.Equal("contacts", Single(report, nameof(BrokenIncident.PrimaryContactId), SchemaCodes.LookupTargetEntitySetMismatch).Fix!.EntitySet);
        Assert.Equal("primarycontactid", Single(report, nameof(BrokenIncident.PrimaryContactId), SchemaCodes.LookupNavigationPropertyMismatch).Fix!.NavigationProperty);

        Assert.Contains("'Incident_Tasks'", Single(report, nameof(BrokenIncident.Tasks), SchemaCodes.CollectionNavigationNotFound).Suggestion);
    }

    [Fact]
    public async Task WrongEntitySet_IsFixable()
    {
        var report = await ValidateAsync(typeof(WrongEntitySet));

        var finding = Assert.Single(report.Findings);
        Assert.Equal(SchemaCodes.EntitySetMismatch, finding.Code);
        Assert.Equal("accounts", finding.Fix!.EntitySet);
    }

    [Fact]
    public async Task MisspelledTable_SuggestsTheTable()
    {
        var report = await ValidateAsync(typeof(MisspelledTable));

        var finding = Assert.Single(report.Findings);
        Assert.Equal(SchemaCodes.TableNotFound, finding.Code);
        Assert.Contains("'account'", finding.Suggestion);
    }
}
