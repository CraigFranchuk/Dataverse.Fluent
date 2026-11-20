using System.Net;
using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Schema.Tests;

/// <summary>
/// <see cref="DataverseSchemaValidator"/> against a fake environment: each kind of finding, with and without the mistake.
/// </summary>
public class ValidatorTests
{
    // ───────── The environment ─────────

    private static FakeEnvironment Environment()
    {
        var environment = FakeEnvironment.WithStandardTables();
        environment.Table("incident", "incidents")
            .Column("incidentid", "Uniqueidentifier", update: false)
            .Column("title", "String")
            .Column("description", "Memo")
            .Column("ticketnumber", "String", update: false)
            .Column("isescalated", "Boolean")
            .Column("quantity", "Integer")
            .Column("versionnumber", "BigInt", create: false, update: false)
            .Column("revenue", "Money")
            .Column("discount", "Decimal")
            .Column("score", "Double")
            .Column("requestedroles", "Virtual", typeName: "MultiSelectPicklistType")
            .DateTime("createdon", "UserLocal", create: false, update: false)
            .DateTime("followupby", "UserLocal")
            .DateTime("duedate", "DateOnly")
            .Choice("prioritycode", "Picklist", 1, 2, 3)
            .Choice("statecode", "State", 0, 1, 2)
            .Lookup("customerid", "Customer", ("account", "customerid_account"), ("contact", "customerid_contact"))
            .Column("customeridtype", "EntityName", of: "customerid")
            .Lookup("primarycontactid", "Lookup", ("contact", "primarycontactid"))
            .Lookup("new_regionid", "Lookup", ("new_region", "new_RegionId"))
            .Owner()
            .Lookup("createdby", "Lookup", ("systemuser", "createdby"))
            .NotWritable("createdby")
            .Lookup("originid", "Lookup", ("incident", "originid"))
            .CreateOnly("originid")
            .OneToMany("Incident_Tasks", "task")
            .ManyToMany("knowledgearticle_incidents", "knowledgearticle");

        return environment;
    }

    public enum Priority { High = 1, Normal = 2, Low = 3 }

    public enum PriorityWithExtra { High = 1, Normal = 2, Low = 3, Urgent = 9 }

    public enum PriorityMissingOne { High = 1, Normal = 2 }

    [DataverseEntity("incident", "incidents")]
    public class Case
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("incidentid")]
        public Guid IncidentId { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [DataverseReadOnly(DataverseOperation.Update)]
        [JsonPropertyName("ticketnumber")]
        public string? TicketNumber { get; set; }

        [JsonPropertyName("isescalated")]
        public bool? IsEscalated { get; set; }

        [JsonPropertyName("quantity")]
        public int? Quantity { get; set; }

        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("versionnumber")]
        public long? VersionNumber { get; set; }

        [JsonPropertyName("revenue")]
        public decimal? Revenue { get; set; }

        [JsonPropertyName("discount")]
        public decimal? Discount { get; set; }

        [JsonPropertyName("score")]
        public double? Score { get; set; }

        [JsonPropertyName("requestedroles")]
        public string? RequestedRoles { get; set; }

        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("createdon")]
        public DateTime? CreatedOn { get; set; }

        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("createdon@OData.Community.Display.V1.FormattedValue")]
        public string? CreatedOnText { get; set; }

        [JsonPropertyName("followupby")]
        public DateTimeOffset? FollowUpBy { get; set; }

        [JsonPropertyName("duedate")]
        public DateOnly? DueDate { get; set; }

        [JsonPropertyName("prioritycode")]
        public Priority? Priority { get; set; }

        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("statecode")]
        public int StateCode { get; set; }

        [DataverseLookup("customerid")]
        [DataverseLookupTarget("account", "accounts", "customerid_account")]
        [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
        public DataverseReference? CustomerId { get; set; }

        [DataverseLookup("primarycontactid", "contacts")]
        public Guid? PrimaryContactId { get; set; }

        [DataverseLookup("new_regionid", "new_regions", NavigationProperty = "new_RegionId")]
        public Guid? RegionId { get; set; }

        [DataverseReadOnly(DataverseOperation.Update)]
        [DataverseLookup("ownerid")]
        [DataverseLookupTarget("systemuser", "systemusers", "ownerid")]
        [DataverseLookupTarget("team", "teams", "ownerid")]
        public DataverseReference? OwnerId { get; set; }

        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("_createdby_value")]
        public Guid? CreatedBy { get; set; }

        [JsonPropertyName("_createdby_value@OData.Community.Display.V1.FormattedValue")]
        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        public string? CreatedByName { get; set; }

        [JsonPropertyName("primarycontactid")]
        public Person? PrimaryContact { get; set; }

        [JsonPropertyName("customerid_account")]
        public Company? Account { get; set; }

        [JsonPropertyName("ownerid")]
        public User? Owner { get; set; }

        [JsonPropertyName("Incident_Tasks")]
        public List<TaskActivity>? Tasks { get; set; }

        [JsonPropertyName("knowledgearticle_incidents")]
        public List<Article>? Articles { get; set; }

        [JsonIgnore]
        public string? Scratch { get; set; }
    }

    [DataverseEntity("contact", "contacts")]
    public class Person
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    [EntitySet("accounts")]
    public class Company
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    [DataverseEntity("systemuser", "systemusers")]
    public class User
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    [DataverseEntity("task", "tasks")]
    public class TaskActivity
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    [DataverseEntity("knowledgearticle", "knowledgearticles")]
    public class Article
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private static SchemaFinding Single(SchemaReport report, string code, string? property = null)
    {
        var matches = report.Findings.Where(f => f.Code == code && (property is null || f.Property?.Name == property)).ToList();
        Assert.True(matches.Count == 1, $"Expected one {code}{(property is null ? "" : " on " + property)}; the report was:{System.Environment.NewLine}{report}");
        return matches[0];
    }

    private static void None(SchemaReport report) => Assert.True(report.Findings.Count == 0, report.ToString());

    [Fact]
    public async Task ACorrectModel_HasNoFindings()
    {
        var environment = Environment();

        var report = await environment.ValidateAsync(typeof(Case), typeof(Person), typeof(Company), typeof(User), typeof(TaskActivity), typeof(Article));

        None(report);
        Assert.Equal(6, report.Models.Count);
        Assert.False(report.HasErrors);
    }

    [Fact]
    public async Task Validation_SendsOnlyGetRequests()
    {
        var environment = Environment();

        await environment.ValidateAsync(typeof(Case));

        Assert.NotEmpty(environment.Transport.Requests);
        Assert.All(environment.Transport.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task EachTable_IsReadOnce()
    {
        var environment = Environment();

        await environment.ValidateAsync(typeof(Case), typeof(Case2));

        Assert.Single(environment.Transport.Requests, r => r.Path == "EntityDefinitions(LogicalName='incident')");
    }

    [DataverseEntity("incident", "incidents")]
    public class Case2
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    // ───────── Tables ─────────

    [DataverseEntity("incidnet", "incidnets")]
    public class MisspelledTable
    {
        public string? Title { get; set; }
    }

    [EntitySet("incidnets")]
    public class MisspelledEntitySet
    {
        public string? Title { get; set; }
    }

    [DataverseEntity("incident", "incident")]
    public class WrongEntitySet
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    [EntitySet("incidents")]
    public class EntitySetOnly
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    [Fact]
    public async Task TableNotFound_SuggestsTheClosestTable()
    {
        var finding = Single(await Environment().ValidateAsync(typeof(MisspelledTable)), SchemaCodes.TableNotFound);

        Assert.Equal("Table 'incidnet' does not exist.", finding.Message);
        Assert.Equal("Did you mean 'incident'?", finding.Suggestion);
        Assert.Equal("MisspelledTable: DVS001 Table 'incidnet' does not exist. Did you mean 'incident'?", finding.ToString());
    }

    [Fact]
    public async Task TableNotFound_ByEntitySet_SuggestsTheClosestEntitySet()
    {
        var finding = Single(await Environment().ValidateAsync(typeof(MisspelledEntitySet)), SchemaCodes.TableNotFound);

        Assert.Equal("No table has the entity set 'incidnets'.", finding.Message);
        Assert.Equal("Did you mean 'incidents'?", finding.Suggestion);
        Assert.Equal(SchemaSeverity.Error, finding.Severity);
    }

    [Fact]
    public async Task EntitySetMismatch_IsFixable()
    {
        var finding = Single(await Environment().ValidateAsync(typeof(WrongEntitySet)), SchemaCodes.EntitySetMismatch);

        Assert.Null(finding.Property);
        Assert.Equal(SchemaFixKind.SetEntitySet, finding.Fix!.Kind);
        Assert.Equal("incidents", finding.Fix.EntitySet);
    }

    [Fact]
    public async Task AModelWithOnlyAnEntitySet_IsResolvedToItsTable()
        => None(await Environment().ValidateAsync(typeof(EntitySetOnly)));

    public class BadLookupType
    {
        [DataverseLookup("primarycontactid", "contacts")]
        public string? PrimaryContactId { get; set; }
    }

    public class NotAModel
    {
        public string? Title { get; set; }
    }

    [Fact]
    public async Task InvalidMapping_IsReported()
    {
        var report = await Environment().ValidateAsync(typeof(BadLookupType), typeof(NotAModel));

        Assert.Equal(2, report.Findings.Count);
        Assert.All(report.Findings, f => Assert.Equal((SchemaCodes.InvalidMapping, SchemaSeverity.Error), (f.Code, f.Severity)));
        Assert.Contains("requires a Guid", report.Findings.Single(f => f.Model == typeof(BadLookupType)).Message);
        Assert.Contains("is not mapped to a table", report.Findings.Single(f => f.Model == typeof(NotAModel)).Message);
    }

    [DataverseEntity("incident", "incidents")]
    public class RejectedAttributeArgument
    {
        [DataverseLookup(" ", "contacts")]
        public Guid? PrimaryContactId { get; set; }
    }

    [Fact]
    public async Task AModelThatCannotBeRead_IsReported_AndTheOthersAreStillChecked()
    {
        var report = await Environment().ValidateAsync(new SchemaValidationOptions { CheckSelect = false }, typeof(RejectedAttributeArgument), typeof(ColumnMistakes));

        var finding = Single(report, SchemaCodes.InvalidMapping);
        Assert.Equal(typeof(RejectedAttributeArgument), finding.Model);
        Assert.StartsWith("RejectedAttributeArgument cannot be read (ArgumentException):", finding.Message);
        Assert.Contains(report.Findings, f => f.Model == typeof(ColumnMistakes));
    }

    // ───────── Columns ─────────

    [DataverseEntity("incident", "incidents")]
    public class ColumnMistakes
    {
        [JsonPropertyName("titel")]
        public string? Title { get; set; }

        [JsonPropertyName("Description")]
        public string? Description { get; set; }

        [JsonPropertyName("nothinglikeit")]
        public string? Other { get; set; }
    }

    [Fact]
    public async Task ColumnNotFound_SuggestsTheClosestColumn()
    {
        var report = await Environment().ValidateAsync(new SchemaValidationOptions { CheckSelect = false }, typeof(ColumnMistakes));

        var finding = Single(report, SchemaCodes.ColumnNotFound, nameof(ColumnMistakes.Title));
        Assert.Equal("Column 'titel' does not exist on incident.", finding.Message);
        Assert.Equal("Did you mean 'title'?", finding.Suggestion);
        Assert.Equal("Did you mean 'description'?", Single(report, SchemaCodes.ColumnNotFound, nameof(ColumnMistakes.Description)).Suggestion);
        Assert.Null(Single(report, SchemaCodes.ColumnNotFound, nameof(ColumnMistakes.Other)).Suggestion);
    }

    [DataverseEntity("incident", "incidents")]
    public class TypeMistakes
    {
        [JsonPropertyName("duedate")]
        public DateTime? DueDate { get; set; }

        [JsonPropertyName("followupby")]
        public DateOnly? FollowUpBy { get; set; }

        [JsonPropertyName("revenue")]
        public int? Revenue { get; set; }

        [JsonPropertyName("discount")]
        public double? Discount { get; set; }

        [JsonPropertyName("quantity")]
        public string? Quantity { get; set; }

        [JsonPropertyName("isescalated")]
        public int? IsEscalated { get; set; }

        [JsonPropertyName("requestedroles")]
        public int[]? RequestedRoles { get; set; }

        [JsonPropertyName("title")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Priority? ConvertedTitle { get; set; }
    }

    [Fact]
    public async Task TypeMismatches_AreReported_WithTheTypeToUse()
    {
        var report = await Environment().ValidateAsync(new SchemaValidationOptions { CheckSelect = false }, typeof(TypeMistakes));

        var dateOnly = Single(report, SchemaCodes.IncompatibleType, nameof(TypeMistakes.DueDate));
        Assert.Contains("date-only", dateOnly.Message);
        Assert.Equal("Use DateOnly?.", dateOnly.Suggestion);
        Assert.Equal("Use DateTime?.", Single(report, SchemaCodes.IncompatibleType, nameof(TypeMistakes.FollowUpBy)).Suggestion);
        Assert.Equal("Use decimal?.", Single(report, SchemaCodes.IncompatibleType, nameof(TypeMistakes.Revenue)).Suggestion);
        Assert.Equal("Use int?.", Single(report, SchemaCodes.IncompatibleType, nameof(TypeMistakes.Quantity)).Suggestion);
        Assert.Equal("Use bool?.", Single(report, SchemaCodes.IncompatibleType, nameof(TypeMistakes.IsEscalated)).Suggestion);
        Assert.Equal("Use string.", Single(report, SchemaCodes.IncompatibleType, nameof(TypeMistakes.RequestedRoles)).Suggestion);

        var lossy = Single(report, SchemaCodes.LossyType, nameof(TypeMistakes.Discount));
        Assert.Equal(SchemaSeverity.Warning, lossy.Severity);

        // A property with its own converter is not type-checked.
        Assert.DoesNotContain(report.Findings, f => f.Property?.Name == nameof(TypeMistakes.ConvertedTitle));
    }

    [JsonConverter(typeof(CaseIdConverter))]
    public readonly record struct CaseId(Guid Value);

    public sealed class CaseIdConverter : JsonConverter<CaseId>
    {
        public override CaseId Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
            => new(reader.GetGuid());

        public override void Write(System.Text.Json.Utf8JsonWriter writer, CaseId value, System.Text.Json.JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    [DataverseEntity("incident", "incidents")]
    public class ConvertedTypes
    {
        [DataverseReadOnly(DataverseOperation.Update)]
        [JsonPropertyName("incidentid")]
        public CaseId? Id { get; set; }

        [JsonPropertyName("title")]
        public System.Text.Json.JsonElement? Title { get; set; }

        [JsonPropertyName("quantity")]
        public object? Quantity { get; set; }
    }

    [Fact]
    public async Task ATypeWithItsOwnConverter_OrThatHoldsAnyJson_IsNotTypeChecked()
        => None(await Environment().ValidateAsync(typeof(ConvertedTypes)));

    [DataverseEntity("incident", "incidents")]
    public class LookupAsColumnSelected
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("new_regionid")]
        public Guid? RegionId { get; set; }
    }

    [Fact]
    public async Task ALookupMappedAsAColumn_IsReportedOnce_AndLeftOutOfTheSelectCheck()
    {
        var environment = Environment();
        environment["incident"].RejectedInSelect.Add("new_regionid");

        var report = await environment.ValidateAsync(typeof(LookupAsColumnSelected));

        Assert.Equal(SchemaCodes.LookupMappedAsColumn, Assert.Single(report.Findings).Code);
        var retrieve = Assert.Single(environment.Transport.Requests, r => r.Path.StartsWith("incidents(", StringComparison.Ordinal));
        Assert.Equal("title", retrieve.GetQueryOption("$select"));
    }

    [DataverseEntity("incident", "incidents")]
    public class Companions
    {
        [JsonPropertyName("createdbyname")]
        public string? CreatedByName { get; set; }

        [JsonPropertyName("prioritycodename")]
        public string? PriorityName { get; set; }

        [JsonPropertyName("customeridtype")]
        public string? CustomerTable { get; set; }
    }

    [Fact]
    public async Task CompanionColumns_PointAtTheAnnotation()
    {
        var environment = Environment();
        var report = await environment.ValidateAsync(typeof(Companions));

        Assert.Equal("Read '_createdby_value@OData.Community.Display.V1.FormattedValue' instead.",
            Single(report, SchemaCodes.CompanionColumn, nameof(Companions.CreatedByName)).Suggestion);
        Assert.Equal("Read 'prioritycode@OData.Community.Display.V1.FormattedValue' instead.",
            Single(report, SchemaCodes.CompanionColumn, nameof(Companions.PriorityName)).Suggestion);
        Assert.Contains("DataverseReference", Single(report, SchemaCodes.CompanionColumn, nameof(Companions.CustomerTable)).Suggestion);

        // Already reported, so the live $select check leaves them out.
        Assert.DoesNotContain(environment.Transport.Requests, r => r.GetQueryOption("$select")?.Contains("name", StringComparison.Ordinal) == true);
    }

    // ───────── Choices ─────────

    [DataverseEntity("incident", "incidents")]
    public class ExtraEnumMember
    {
        [JsonPropertyName("prioritycode")]
        public PriorityWithExtra? Priority { get; set; }
    }

    [DataverseEntity("incident", "incidents")]
    public class MissingEnumMember
    {
        [JsonPropertyName("prioritycode")]
        public PriorityMissingOne Priority { get; set; }
    }

    [Fact]
    public async Task EnumMembers_AreComparedWithTheOptions()
    {
        var report = await Environment().ValidateAsync(typeof(ExtraEnumMember), typeof(MissingEnumMember));

        Assert.Equal("PriorityWithExtra has members that are not options of 'prioritycode': Urgent = 9.",
            Single(report, SchemaCodes.EnumValueNotAnOption).Message);
        var info = Single(report, SchemaCodes.OptionWithoutEnumMember);
        Assert.Equal(SchemaSeverity.Info, info.Severity);
        Assert.Equal("1 option of 'prioritycode' has no PriorityMissingOne member: 3.", info.Message);
    }

    [Fact]
    public async Task EnumCheck_CanBeTurnedOff()
    {
        var environment = Environment();

        var report = await environment.ValidateAsync(new SchemaValidationOptions { CheckChoiceEnums = false }, typeof(ExtraEnumMember));

        None(report);
        Assert.DoesNotContain(environment.Transport.Requests, r => r.Path.EndsWith("PicklistAttributeMetadata", StringComparison.Ordinal));
    }

    // ───────── Lookups ─────────

    [DataverseEntity("incident", "incidents")]
    public class LookupMistakes
    {
        [DataverseLookup("primarycontact", "contacts")]
        public Guid? Missing { get; set; }

        [DataverseLookup("title", "contacts")]
        public Guid? NotALookup { get; set; }

        [DataverseLookup("new_regionid", "new_regions")]
        public Guid? DefaultNavigation { get; set; }

        [DataverseLookup("primarycontactid", "contact")]
        public Guid? WrongSet { get; set; }

        [DataverseLookup("customerid", "accounts", NavigationProperty = "customerid_account")]
        public Guid? CustomerAsAccount { get; set; }

        [JsonPropertyName("_titel_value")]
        public Guid? ValueOfMissing { get; set; }
    }

    [Fact]
    public async Task LookupMistakes_AreReported_AndTheMechanicalOnesFixable()
    {
        var report = await Environment().ValidateAsync(new SchemaValidationOptions { CheckSelect = false }, typeof(LookupMistakes));

        Assert.Equal("Did you mean 'primarycontactid'?", Single(report, SchemaCodes.LookupNotFound, nameof(LookupMistakes.Missing)).Suggestion);
        Assert.Equal("'title' is a String column, not a lookup.", Single(report, SchemaCodes.LookupNotFound, nameof(LookupMistakes.NotALookup)).Message);
        Single(report, SchemaCodes.LookupNotFound, nameof(LookupMistakes.ValueOfMissing));

        var navigation = Single(report, SchemaCodes.LookupNavigationPropertyMismatch, nameof(LookupMistakes.DefaultNavigation));
        Assert.Equal((SchemaFixKind.SetLookupNavigationProperty, "new_RegionId"), (navigation.Fix!.Kind, navigation.Fix.NavigationProperty));

        var set = Single(report, SchemaCodes.LookupTargetEntitySetMismatch, nameof(LookupMistakes.WrongSet));
        Assert.Equal((SchemaFixKind.SetLookupTargetEntitySet, "contacts"), (set.Fix!.Kind, set.Fix.EntitySet));

        var polymorphic = Single(report, SchemaCodes.PolymorphicColumnMappedAsSingleTable, nameof(LookupMistakes.CustomerAsAccount));
        Assert.Equal(SchemaSeverity.Warning, polymorphic.Severity);
        Assert.Null(polymorphic.Fix);
        Assert.DoesNotContain(report.Findings, f => f.Property?.Name == nameof(LookupMistakes.CustomerAsAccount) && f.Code != SchemaCodes.PolymorphicColumnMappedAsSingleTable);
    }

    [DataverseEntity("incident", "incidents")]
    public class PolymorphicMistakes
    {
        [DataverseLookup("customerid")]
        [DataverseLookupTarget("account", "accounts", "customerid_account")]
        public DataverseReference? MissingContact { get; set; }

        [DataverseLookup("customerid")]
        [DataverseLookupTarget("account", "account", "customerid")]
        [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
        [DataverseLookupTarget("lead", "leads", "customerid_lead")]
        public DataverseReference? WrongTargets { get; set; }
    }

    [Fact]
    public async Task PolymorphicLookupMistakes_AreReported()
    {
        var report = await Environment().ValidateAsync(new SchemaValidationOptions { CheckSelect = false }, typeof(PolymorphicMistakes));

        var missing = Single(report, SchemaCodes.MissingLookupTarget, nameof(PolymorphicMistakes.MissingContact)).Fix!;
        Assert.Equal((SchemaFixKind.AddLookupTarget, "contact", "contacts", "customerid_contact"),
            (missing.Kind, missing.TargetLogicalName, missing.EntitySet, missing.NavigationProperty));

        var wrong = Single(report, SchemaCodes.LookupTargetMismatch, nameof(PolymorphicMistakes.WrongTargets));
        Assert.Equal("Target account of 'customerid': its entity set is 'accounts', not 'account', and it is written through 'customerid_account', not 'customerid'.", wrong.Message);
        Assert.Equal((SchemaFixKind.SetLookupTarget, "account", "accounts", "customerid_account"),
            (wrong.Fix!.Kind, wrong.Fix.TargetLogicalName, wrong.Fix.EntitySet, wrong.Fix.NavigationProperty));

        Assert.Equal("'customerid' cannot point at lead; it can point at account, contact.",
            Single(report, SchemaCodes.UnknownLookupTarget, nameof(PolymorphicMistakes.WrongTargets)).Message);
    }

    [DataverseEntity("incident", "incidents")]
    public class LookupsAsColumns
    {
        [JsonPropertyName("primarycontactid")]
        public Guid? PrimaryContactId { get; set; }

        [JsonPropertyName("_primarycontactid_value")]
        public Guid? PrimaryContactValue { get; set; }

        [JsonPropertyName("_new_regionid_value")]
        public Guid? RegionValue { get; set; }

        [JsonPropertyName("_customerid_value")]
        public Guid? CustomerValue { get; set; }

        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("_customerid_value")]
        public Guid? CustomerValueReadOnly { get; set; }

        [JsonPropertyName("primarycontactid@OData.Community.Display.V1.FormattedValue")]
        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        public string? PrimaryContactName { get; set; }

        [JsonPropertyName("_createdby_value")]
        public Guid? CreatedByValue { get; set; }

        [JsonPropertyName("_originid_value")]
        public Guid? OriginValue { get; set; }
    }

    [Fact]
    public async Task LookupsMappedAsColumns_AreReported()
    {
        var report = await Environment().ValidateAsync(new SchemaValidationOptions { CheckSelect = false }, typeof(LookupsAsColumns));

        Assert.Equal("Map it with [DataverseLookup(\"primarycontactid\", \"contacts\")].",
            Single(report, SchemaCodes.LookupMappedAsColumn, nameof(LookupsAsColumns.PrimaryContactId)).Suggestion);
        Assert.Equal("Use '_primarycontactid_value@OData.Community.Display.V1.FormattedValue'.",
            Single(report, SchemaCodes.LookupMappedAsColumn, nameof(LookupsAsColumns.PrimaryContactName)).Suggestion);

        var contact = Single(report, SchemaCodes.LookupValueWritten, nameof(LookupsAsColumns.PrimaryContactValue)).Fix!;
        Assert.Equal((SchemaFixKind.MapAsLookup, "primarycontactid", "contacts", "primarycontactid"),
            (contact.Kind, contact.LookupLogicalName, contact.EntitySet, contact.NavigationProperty));
        Assert.Equal("map it with [DataverseLookup(\"new_regionid\", \"new_regions\", NavigationProperty = \"new_RegionId\")]",
            Single(report, SchemaCodes.LookupValueWritten, nameof(LookupsAsColumns.RegionValue)).Fix!.Description);

        // A lookup Dataverse sets itself cannot be bound either, so the fix stops the write instead.
        var createdBy = Single(report, SchemaCodes.LookupValueWritten, nameof(LookupsAsColumns.CreatedByValue)).Fix!;
        Assert.Equal((SchemaFixKind.SetReadOnly, DataverseOperation.CreateAndUpdate), (createdBy.Kind, createdBy.ReadOnlyFor));

        // A lookup that can only be set on create is mapped and kept out of updates by the one fix.
        var origin = Single(report, SchemaCodes.LookupValueWritten, nameof(LookupsAsColumns.OriginValue)).Fix!;
        Assert.Equal((SchemaFixKind.MapAsLookup, DataverseOperation.Update), (origin.Kind, origin.ReadOnlyFor));
        Assert.Equal("map it with [DataverseLookup(\"originid\", \"incidents\")] and [DataverseReadOnly(DataverseOperation.Update)]", origin.Description);
        Assert.Equal(DataverseOperation.None, contact.ReadOnlyFor);

        // A polymorphic lookup has no single bind to switch to.
        Assert.Null(Single(report, SchemaCodes.LookupValueWritten, nameof(LookupsAsColumns.CustomerValue)).Fix);
        Assert.DoesNotContain(report.Findings, f => f.Property?.Name == nameof(LookupsAsColumns.CustomerValueReadOnly));
    }

    // ───────── Navigation properties ─────────

    [DataverseEntity("incident", "incidents")]
    public class NavigationMistakes
    {
        [JsonPropertyName("incident_tasks")]
        public List<TaskActivity>? Tasks { get; set; }

        [JsonPropertyName("primarycontact")]
        public Person? PrimaryContact { get; set; }

        [JsonPropertyName("customerid_contact")]
        public Company? CustomerAsCompany { get; set; }

        [JsonPropertyName("parentaccount@odata.bind")]
        public string? ParentBind { get; set; }

        [JsonPropertyName("customerid_account@odata.bind")]
        public string? CustomerBind { get; set; }
    }

    [Fact]
    public async Task NavigationMistakes_AreReported()
    {
        var report = await Environment().ValidateAsync(new SchemaValidationOptions { CheckSelect = false }, typeof(NavigationMistakes));

        Assert.Equal("Did you mean 'Incident_Tasks'?", Single(report, SchemaCodes.CollectionNavigationNotFound, nameof(NavigationMistakes.Tasks)).Suggestion);
        Assert.Equal("Did you mean 'primarycontactid'?", Single(report, SchemaCodes.NavigationNotFound, nameof(NavigationMistakes.PrimaryContact)).Suggestion);
        Assert.Equal("'customerid_contact' leads to contact, but Company maps account.",
            Single(report, SchemaCodes.RelatedTypeMismatch, nameof(NavigationMistakes.CustomerAsCompany)).Message);
        Single(report, SchemaCodes.NavigationNotFound, nameof(NavigationMistakes.ParentBind));
        Assert.DoesNotContain(report.Findings, f => f.Property?.Name == nameof(NavigationMistakes.CustomerBind));
    }

    // ───────── Writes ─────────

    [DataverseEntity("incident", "incidents")]
    public class UnmarkedSystemColumns
    {
        [JsonPropertyName("incidentid")]
        public Guid? IncidentId { get; set; }

        [JsonPropertyName("ticketnumber")]
        public string? TicketNumber { get; set; }

        [JsonPropertyName("createdon")]
        public DateTime? CreatedOn { get; set; }

        [DataverseReadOnly(DataverseOperation.Update)]
        [JsonPropertyName("versionnumber")]
        public long? VersionNumber { get; set; }

        [DataverseLookup("createdby", "systemusers")]
        public Guid? CreatedBy { get; set; }

        [JsonPropertyName("createdon@OData.Community.Display.V1.FormattedValue")]
        public string? CreatedOnText { get; set; }

        [JsonPropertyName("@odata.etag")]
        public string? ETag { get; set; }
    }

    [Fact]
    public async Task ColumnsDataverseIgnores_AreReported_WithTheMarkerToAdd()
    {
        var report = await Environment().ValidateAsync(new SchemaValidationOptions { CheckSelect = false }, typeof(UnmarkedSystemColumns));

        Assert.Equal(DataverseOperation.Update, Single(report, SchemaCodes.SentOnUpdate, nameof(UnmarkedSystemColumns.IncidentId)).Fix!.ReadOnlyFor);
        Assert.Equal(DataverseOperation.Update, Single(report, SchemaCodes.SentOnUpdate, nameof(UnmarkedSystemColumns.TicketNumber)).Fix!.ReadOnlyFor);
        Assert.Equal(DataverseOperation.CreateAndUpdate, Single(report, SchemaCodes.NotWritable, nameof(UnmarkedSystemColumns.CreatedOn)).Fix!.ReadOnlyFor);
        Assert.Contains("on create", Single(report, SchemaCodes.NotWritable, nameof(UnmarkedSystemColumns.VersionNumber)).Message);
        Assert.Equal(DataverseOperation.CreateAndUpdate, Single(report, SchemaCodes.NotWritable, nameof(UnmarkedSystemColumns.CreatedBy)).Fix!.ReadOnlyFor);

        var annotation = Single(report, SchemaCodes.AnnotationWritten, nameof(UnmarkedSystemColumns.CreatedOnText));
        Assert.Equal(SchemaSeverity.Info, annotation.Severity);
        Assert.Equal(SchemaSeverity.Info, Single(report, SchemaCodes.AnnotationWritten, nameof(UnmarkedSystemColumns.ETag)).Severity);
    }

    // ───────── The live $select check ─────────

    [DataverseEntity("incident", "incidents")]
    public class Selected
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("quantity")]
        public int? Quantity { get; set; }

        [JsonPropertyName("score")]
        public double? Score { get; set; }

        [DataverseLookup("primarycontactid", "contacts")]
        public Guid? PrimaryContactId { get; set; }

        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("title@OData.Community.Display.V1.FormattedValue")]
        public string? TitleText { get; set; }
    }

    [Fact]
    public async Task SelectCheck_SelectsEveryMappedColumn_ForARandomId()
    {
        var environment = Environment();

        None(await environment.ValidateAsync(typeof(Selected)));

        var retrieve = Assert.Single(environment.Transport.Requests, r => r.Path.StartsWith("incidents(", StringComparison.Ordinal));
        Assert.Equal("title,quantity,score,_primarycontactid_value", retrieve.GetQueryOption("$select"));
    }

    [Fact]
    public async Task SelectCheck_ReportsEachColumnDataverseRejects()
    {
        var environment = Environment();
        environment["incident"].RejectedInSelect.UnionWith(["quantity", "score"]);

        var report = await environment.ValidateAsync(typeof(Selected));

        Assert.Equal("Dataverse rejects 'quantity' in a $select: Could not find a property named 'quantity' on type 'Microsoft.Dynamics.CRM.incident'.",
            Single(report, SchemaCodes.RejectedBySelect, nameof(Selected.Quantity)).Message);
        Single(report, SchemaCodes.RejectedBySelect, nameof(Selected.Score));
        Assert.Equal(3, environment.Transport.Requests.Count(r => r.Path.StartsWith("incidents(", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SelectCheck_NarrowsDownARejectionThatNamesNoColumn()
    {
        var environment = Environment();
        environment.RetrieveOverride = r => throw (r.GetQueryOption("$select")!.Split(',').Contains("score")
            ? FakeEnvironment.Error(HttpStatusCode.BadRequest, "Term 'score' is not valid in a $select or $expand expression.")
            : FakeEnvironment.Error(HttpStatusCode.NotFound, "Does Not Exist"));

        var report = await environment.ValidateAsync(typeof(Selected));

        Assert.Equal(nameof(Selected.Score), Single(report, SchemaCodes.RejectedBySelect).Property!.Name);
    }

    [Fact]
    public async Task SelectCheck_ThatCannotRun_IsReportedAsInfo()
    {
        var environment = Environment();
        environment.RetrieveOverride = _ => throw FakeEnvironment.Error(HttpStatusCode.Forbidden, "Principal user is missing prvReadIncident privilege.", "0x80040220");

        var finding = Single(await environment.ValidateAsync(typeof(Selected)), SchemaCodes.SelectCheckSkipped);

        Assert.Equal(SchemaSeverity.Info, finding.Severity);
        Assert.Contains("prvReadIncident", finding.Message);
    }

    [Fact]
    public async Task SelectCheck_CanBeTurnedOff()
    {
        var environment = Environment();

        await environment.ValidateAsync(new SchemaValidationOptions { CheckSelect = false }, typeof(Selected));

        Assert.DoesNotContain(environment.Transport.Requests, r => r.Path.StartsWith("incidents(", StringComparison.Ordinal));
    }

    // ───────── Options and inheritance ─────────

    [Fact]
    public async Task IgnoredCodes_AreLeftOut()
    {
        var options = new SchemaValidationOptions { CheckSelect = false };
        options.IgnoreCodes.Add("dvs010");

        None(await Environment().ValidateAsync(options, typeof(ColumnMistakes)));
    }

    [DataverseEntity("incident", "incidents")]
    public class BaseCase
    {
        [JsonPropertyName("titel")]
        public string? Title { get; set; }
    }

    public class DerivedCaseA : BaseCase
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    public class DerivedCaseB : BaseCase
    {
        [JsonPropertyName("nosuch")]
        public string? Extra { get; set; }
    }

    [Fact]
    public async Task AnInheritedProperty_IsReportedOnce_AgainstTheModelThatDeclaresIt()
    {
        var report = await Environment().ValidateAsync(new SchemaValidationOptions { CheckSelect = false }, typeof(DerivedCaseA), typeof(DerivedCaseB));

        var inherited = Single(report, SchemaCodes.ColumnNotFound, nameof(BaseCase.Title));
        Assert.Equal(typeof(BaseCase), inherited.Model);
        Assert.Equal("BaseCase.Title", inherited.Location);
        Assert.Equal(typeof(DerivedCaseB), Single(report, SchemaCodes.ColumnNotFound, nameof(DerivedCaseB.Extra)).Model);
    }
}
