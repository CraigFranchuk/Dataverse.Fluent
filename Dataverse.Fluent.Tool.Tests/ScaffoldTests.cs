using Dataverse.Fluent.Schema.Tests;
using Dataverse.Fluent.Tool.Fixing;
using Dataverse.Fluent.Tool.Projects;
using Dataverse.Fluent.Tool.Scaffolding;

namespace Dataverse.Fluent.Tool.Tests;

/// <summary>The scaffold command: filling in a project's models, and writing a new one, from a fake environment.</summary>
public class ScaffoldTests : IDisposable
{
    private const string Stub = """
        using System.Text.Json.Serialization;
        using Dataverse.Fluent;

        namespace App.Models;

        [DataverseEntity("incident")]
        public partial class Incident
        {
            public Guid IncidentId { get; set; }

            /// <summary>The title.</summary>
            public string Title { get; set; }

            public DateTime DueDate { get; set; }

            public double Revenue { get; set; }

            public int PriorityCode { get; set; }

            public Guid CustomerId { get; set; }

            public Guid? PrimaryContactId { get; set; }

            public Guid New_RegionId { get; set; }

            public DateTime? CreatedOn { get; set; }

            [JsonPropertyName("followupby")]
            public DateOnly? FollowUpBy { get; set; }

            [JsonIgnore]
            public string Scratch { get; set; }

            public string Summary => Title;
        }
        """;

    private const string Scaffolded = """
        using System.Text.Json.Serialization;
        using Dataverse.Fluent;

        namespace App.Models;

        [DataverseEntity("incident", "incidents")]
        public partial class Incident
        {
            [DataverseReadOnly(DataverseOperation.Update)]
            [DataverseColumn("incidentid")]
            public Guid IncidentId { get; set; }

            /// <summary>The title.</summary>
            [DataverseColumn("title")]
            public string Title { get; set; }

            [DataverseColumn("duedate")]
            public DateOnly? DueDate { get; set; }

            [DataverseColumn("revenue")]
            public decimal? Revenue { get; set; }

            [DataverseColumn("prioritycode")]
            public int PriorityCode { get; set; }

            [DataverseLookup("customerid")]
            [DataverseLookupTarget("account", "accounts", "customerid_account")]
            [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
            public DataverseReference? CustomerId { get; set; }

            [DataverseLookup("primarycontactid", "contacts")]
            public Guid? PrimaryContactId { get; set; }

            [DataverseLookup("new_regionid", "new_regions", NavigationProperty = "new_RegionId")]
            public Guid New_RegionId { get; set; }

            [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
            [DataverseColumn("createdon")]
            public DateTime? CreatedOn { get; set; }

            [JsonPropertyName("followupby")]
            public DateTime? FollowUpBy { get; set; }

            [JsonIgnore]
            public string Scratch { get; set; }

            public string Summary => Title;
        }
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"scaffold-{Guid.NewGuid():N}");
    private readonly FakeEnvironment _environment = FakeEnvironment.WithStandardTables();
    private readonly StringWriter _output = new();

    public ScaffoldTests()
    {
        Directory.CreateDirectory(_directory);
        var incident = _environment.Table("incident", "incidents")
            .Column("incidentid", "Uniqueidentifier", update: false)
            .Column("title", "String")
            .Column("revenue", "Money")
            .Column("entityimage", "Virtual", typeName: "ImageType")
            .Column("somethingnew", "Mystery")
            .DateTime("followupby", "UserLocal")
            .DateTime("duedate", "DateOnly")
            .DateTime("createdon", "UserLocal", create: false, update: false)
            .Choice("prioritycode", "Picklist", 1, 2, 3)
            .Labels("prioritycode", "High", "Normal", "Low priority")
            .Lookup("customerid", "Customer", ("account", "customerid_account"), ("contact", "customerid_contact"))
            .Lookup("primarycontactid", "Lookup", ("contact", "primarycontactid"))
            .Lookup("new_regionid", "Lookup", ("new_region", "new_RegionId"))
            .Schema("followupby", "FollowupBy")
            .Schema("customerid", "CustomerId")
            .Schema("new_regionid", "new_RegionId");
        incident.SchemaName = "Incident";
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        _output.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The properties the next scaffold leaves alone, as --skip names them.
    private string[] Skip { get; set; } = [];

    // Raw strings take the line endings of this file, which depend on the checkout; the tests use LF.
    private static string N(string text) => text.ReplaceLineEndings("\n");

    private Task<(string Text, ScaffoldResult Result)> ScaffoldAsync(string source, bool nullable = true, bool enums = false, params string[] types)
        => ScaffoldAsync(source, nullable, enums, implicitUsings: true, types);

    private async Task<(string Text, ScaffoldResult Result)> ScaffoldAsync(string source, bool nullable, bool enums, bool implicitUsings, params string[] types)
    {
        var file = SourceFile.Parse("Incident.cs", N(source));
        var result = await new ModelScaffolder(_environment.Transport, nullable, enums, implicitUsings ? ModelSemantics.ImplicitUsings : [], Skip).ScaffoldAsync([file], types, Ct);
        Assert.Null(file.Verify());
        return (file.Edited().ToString(), result);
    }

    // ───────── Filling in a model ─────────

    [Fact]
    public async Task AStub_GetsItsEntitySet_Mappings_ReadOnlyMarkers_AndCorrectedTypes()
    {
        var (text, result) = await ScaffoldAsync(Stub);

        Assert.Equal(N(Scaffolded), text);
        Assert.Empty(result.Misses);
        Assert.Equal(1, result.Models);
        Assert.Equal(new ScaffoldSkip("Incident.Summary", "it is computed, and the client still sends it as a column unless it has [JsonIgnore]"), Assert.Single(result.Skipped));
        // DueDate, Revenue, CustomerId and FollowUpBy; the types that already hold their column are kept as written.
        Assert.Equal(4, result.Changes.Count(c => c.TypeChanged));
        Assert.Contains(result.Changes, c => c is { Location: "Incident.CustomerId", Description: "mapped to the lookup 'customerid' (account, contact); type Guid -> DataverseReference?" });
        Assert.Contains(result.Changes, c => c is { Location: "Incident.IncidentId", Description: "mapped to 'incidentid'; read-only on update" });
        Assert.Contains(result.Changes, c => c is { Location: "Incident.FollowUpBy", Description: "type DateOnly? -> DateTime?" });
        Assert.Contains(result.Changes, c => c is { Location: "Incident", Description: "entity set \"incidents\"" });
    }

    [Fact]
    public async Task AScaffoldedModel_IsLeftAsItIs_WhenScaffoldedAgain()
    {
        var (text, result) = await ScaffoldAsync(Scaffolded);

        Assert.Equal(N(Scaffolded), text);
        Assert.Empty(result.Changes);
        Assert.Empty(result.Misses);
    }

    [Fact]
    public async Task APropertyThatMatchesNoColumn_IsLeftAlone_AndReported()
    {
        var source = """
            [Dataverse.Fluent.DataverseEntity("incident", "incidents")]
            public class Incident
            {
                public string Titel { get; set; }

                public string CustomerIdName { get; set; }

                [System.Text.Json.Serialization.JsonPropertyName("Incident_Tasks")]
                public List<Task> Tasks { get; set; }
            }
            """;

        var (text, result) = await ScaffoldAsync(source);

        Assert.Equal(N(source), text);
        Assert.Equal(2, result.Misses.Count);
        Assert.Equal(("Incident.Titel", "No column 'titel' on incident.", "Did you mean 'title'?"), (result.Misses[0].Location, result.Misses[0].Message, result.Misses[0].Suggestion));
        Assert.Equal("Map 'customerid' instead.", result.Misses[1].Suggestion);
    }

    [Fact]
    public async Task APropertyIgnoredOnlyConditionally_IsStillAColumn()
    {
        var (text, result) = await ScaffoldAsync("""
            [DataverseEntity("incident", "incidents")]
            public class Incident
            {
                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                public string Title { get; set; }

                [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
                public double Revenue { get; set; }
            }
            """);

        Assert.Contains(N("""
                [DataverseColumn("title")]
                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                public string Title { get; set; }
            """), text);
        Assert.Contains("public double Revenue", text);
        Assert.DoesNotContain("revenue", text);
        Assert.Single(result.Changes);
        Assert.Empty(result.Misses);
    }

    [Fact]
    public async Task APropertyMappedToALookupsName_IsLeftToValidate()
    {
        var source = """
            [DataverseEntity("incident", "incidents")]
            public class Incident
            {
                [JsonPropertyName("primarycontactid")]
                public Contact PrimaryContact { get; set; }

                [DataverseColumn("customerid")]
                public string CustomerId { get; set; }
            }
            """;

        var (text, result) = await ScaffoldAsync(source);

        Assert.Equal(N(source), text);
        Assert.Empty(result.Changes);
        Assert.Empty(result.Misses);
        Assert.Equal(
            [
                new ScaffoldSkip("Incident.PrimaryContact", "it is mapped to 'primarycontactid' as a column, and that is a lookup on the table"),
                new ScaffoldSkip("Incident.CustomerId", "it is mapped to 'customerid' as a column, and that is a lookup on the table")
            ],
            result.Skipped);
    }

    [Fact]
    public async Task AttributesGoRightBeforeTheProperty_WhenItSharesItsLine()
    {
        var (text, result) = await ScaffoldAsync("""
            [DataverseEntity("incident", "incidents")]
            public class Incident { public Guid IncidentId { get; set; } public int Title { get; set; } }
            """);

        Assert.Equal(N("""
            [DataverseEntity("incident", "incidents")]
            public class Incident { [DataverseReadOnly(DataverseOperation.Update)] [DataverseColumn("incidentid")] public Guid IncidentId { get; set; } [DataverseColumn("title")] public string? Title { get; set; } }
            """), text);
        Assert.Equal(2, result.Changes.Count);
    }

    [Fact]
    public async Task AnEntitySetGivenAsAConstant_IsRead_AndNotRewritten()
    {
        const string source = """
            using Dataverse.Fluent;

            [DataverseEntity(Tables.Incident, Tables.Incidents)]
            public class Incident
            {
                [DataverseColumn(Tables.Title)]
                public int Title { get; set; }
            }

            public static class Tables
            {
                public const string Incident = "incident";
                public const string Incidents = "incid" + "ents";
                public const string Title = nameof(Title);
            }
            """;

        // The constants hold the right names: the table is found by one, and the entity set needs no change.
        var (text, result) = await ScaffoldAsync(source.Replace("nameof(Title)", "\"title\""));
        Assert.Contains("[DataverseEntity(Tables.Incident, Tables.Incidents)]", text);
        Assert.Equal(("Incident.Title", "type int -> string?"), (Assert.Single(result.Changes).Location, result.Changes[0].Description));
        Assert.Empty(result.Misses);
        Assert.Empty(result.Skipped);

        // A wrong one is reported, since scaffolding does not rewrite a constant.
        (text, result) = await ScaffoldAsync(source.Replace("\"incid\" + \"ents\"", "\"incident\""));
        Assert.Contains("public const string Incidents = \"incident\";", text);
        var miss = Assert.Single(result.Misses);
        Assert.Equal(("Incident", "The entity set is \"incidents\", but Tables.Incidents is \"incident\".", "Change the constant, or write the entity set as a string."),
            (miss.Location, miss.Message, miss.Suggestion));
    }

    [Fact]
    public async Task AnEntitySetThatCannotBeRead_IsLeftToValidate()
    {
        var (text, result) = await ScaffoldAsync("""
            [DataverseEntity("incident", EntitySets.Incidents)]
            public class Incident
            {
                public string Title { get; set; }
            }
            """);

        Assert.Contains("[DataverseEntity(\"incident\", EntitySets.Incidents)]", text);
        Assert.Equal("Incident.Title", Assert.Single(result.Changes).Location);
        Assert.Empty(result.Misses);
        Assert.Equal(new ScaffoldSkip("Incident", "its entity set, EntitySets.Incidents, is not a constant the tool can read"), Assert.Single(result.Skipped));
    }

    [Fact]
    public async Task ANavigationProperty_IsNotMatchedToAColumn()
    {
        var source = """
            using System.Collections.Generic;
            using Dataverse.Fluent;

            [DataverseEntity("incident", "incidents")]
            public class Incident
            {
                public Contact PrimaryContact { get; set; }

                public List<Contact> Contacts { get; set; }

                public Contact[] More { get; set; }
            }

            [EntitySet("contacts")]
            public class Contact
            {
            }
            """;

        var (text, result) = await ScaffoldAsync(source);

        Assert.Equal(N(source), text);
        Assert.Empty(result.Misses);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public async Task AnAttributeIsKnownByItsType_NotOnlyItsName()
    {
        var (text, result) = await ScaffoldAsync("""
            using Dataverse.Fluent;
            using Column = Dataverse.Fluent.DataverseColumnAttribute;

            [DataverseEntity("incident", "incidents")]
            public class Incident
            {
                [Column("title")]
                public string Title { get; set; }

                [JsonIgnore]
                public double Revenue { get; set; }
            }

            // Not System.Text.Json's: the property is still a column.
            public sealed class JsonIgnoreAttribute : System.Attribute
            {
            }
            """);

        Assert.DoesNotContain("[DataverseColumn(\"title\")]", text);
        Assert.Contains(N("""
                [DataverseColumn("revenue")]
                [JsonIgnore]
                public decimal? Revenue { get; set; }
            """), text);
        Assert.Equal("Incident.Revenue", Assert.Single(result.Changes).Location);
    }

    [Fact]
    public async Task ATypeIsKnownByWhatItIs_NotOnlyItsName()
    {
        var (text, result) = await ScaffoldAsync("""
            using Dataverse.Fluent;
            using Id = System.Guid;

            namespace App;

            [DataverseEntity("incident", "incidents")]
            public class Incident
            {
                public Id IncidentId { get; set; }

                public System.Nullable<System.DateOnly> DueDate { get; set; }

                public Guid PrimaryContactId { get; set; }
            }

            // The model's own type, which happens to be called Guid: it is not replaced.
            public readonly record struct Guid(string Value);
            """);

        // The alias and the long form already hold their columns.
        Assert.Contains("public Id IncidentId", text);
        Assert.Contains("public System.Nullable<System.DateOnly> DueDate", text);
        Assert.Contains("public Guid PrimaryContactId", text);
        Assert.DoesNotContain(result.Changes, c => c.TypeChanged);
    }

    [Fact]
    public async Task ASkippedProperty_IsLeftAlone_AndNotReported()
    {
        const string source = """
            namespace App;

            [DataverseEntity("incident", "incidents")]
            public class Incident
            {
                public string Titel { get; set; }
                public string Scratch { get; set; }
                public string Title { get; set; }
            }
            """;

        Skip = ["Incident.Titel", "App.Incident.Scratch"];
        var (text, result) = await ScaffoldAsync(source);

        Assert.Empty(result.Misses);
        Assert.Empty(result.Skipped);
        Assert.Equal("Incident.Title", Assert.Single(result.Changes).Location);
        Assert.Contains("    public string Titel { get; set; }\n    public string Scratch { get; set; }\n    [DataverseColumn", text);

        // A name that matches no property would skip nothing.
        Skip = ["Incident.Tittle"];
        var ex = await Assert.ThrowsAsync<ToolException>(() => ScaffoldAsync(source));
        Assert.Equal("--skip: 'Incident.Tittle' is not a property of a model that was read. Name a property as Model.Property.", ex.Message);
    }

    [Fact]
    public async Task AComputedProperty_IsNotAColumn_ButOneWithABackingFieldIs()
    {
        var (text, result) = await ScaffoldAsync("""
            [DataverseEntity("incident", "incidents")]
            public class Incident
            {
                private string _title;

                public string Title
                {
                    get => _title;
                    set => _title = value;
                }

                public string DisplayName { get { return Title + "!"; } }

                public string Shout { get => Title.ToUpperInvariant(); }
            }
            """);

        Assert.Contains("    [DataverseColumn(\"title\")]\n    public string Title\n", text);
        Assert.Equal("Incident.Title", Assert.Single(result.Changes).Location);
        Assert.Empty(result.Misses);
        Assert.Equal(["Incident.DisplayName", "Incident.Shout"], result.Skipped.Select(s => s.Location));
    }

    [Theory]
    [InlineData("", "System.DateOnly?", "System.Guid?")]
    [InlineData("using System;", "DateOnly?", "Guid?")]
    [InlineData("global using System;", "DateOnly?", "Guid?")]
    public async Task ACorrectedType_IsQualified_WhenSystemIsNotImported(string @using, string date, string id)
    {
        var (text, _) = await ScaffoldAsync($$"""
            {{@using}}
            namespace App;

            [Dataverse.Fluent.DataverseEntity("incident", "incidents")]
            public class Incident
            {
                public string DueDate { get; set; }
                public string PrimaryContactId { get; set; }
                public int Title { get; set; }
            }
            """, nullable: true, enums: false, implicitUsings: false);

        Assert.Contains($"public {date} DueDate", text);
        Assert.Contains($"public {id} PrimaryContactId", text);
        Assert.Contains("public string? Title", text);
    }

    [Fact]
    public async Task ReferenceTypes_AreNotMarkedNullable_WhenTheProjectHasNullableOff()
    {
        var (text, _) = await ScaffoldAsync("""
            [DataverseEntity("incident", "incidents")]
            public class Incident
            {
                public int Title { get; set; }
                public string CustomerId { get; set; }
            }
            """, nullable: false);

        Assert.Contains("public string Title { get; set; }", text);
        Assert.Contains("public DataverseReference CustomerId { get; set; }", text);
    }

    [Fact]
    public async Task AttributesAreWritten_TheWayTheModelWritesItsOwn()
    {
        var (text, _) = await ScaffoldAsync("""
            [Dataverse.Fluent.DataverseEntity("incident", "incidents")]
            public class Incident
            {
                public string CustomerId { get; set; }
                public Guid IncidentId { get; set; }
            }
            """);

        Assert.Contains("[Dataverse.Fluent.DataverseLookupTarget(\"account\", \"accounts\", \"customerid_account\")]", text);
        Assert.Contains("public Dataverse.Fluent.DataverseReference? CustomerId", text);
        Assert.Contains("[Dataverse.Fluent.DataverseReadOnly(Dataverse.Fluent.DataverseOperation.Update)]", text);
    }

    [Fact]
    public async Task AModelWithOnlyAnEntitySet_IsFoundByIt()
    {
        var (text, result) = await ScaffoldAsync("""
            [EntitySet("incidents")]
            public class Incident
            {
                public string Title { get; set; }
            }
            """);

        Assert.Contains("[DataverseColumn(\"title\")]", text);
        Assert.Contains("[EntitySet(\"incidents\")]", text);
        Assert.Single(result.Changes);
    }

    [Fact]
    public async Task ATableThatDoesNotExist_IsReported()
    {
        var (_, result) = await ScaffoldAsync("""
            [DataverseEntity("incidnet")]
            public class Incident
            {
                public string Title { get; set; }
            }
            """);

        var miss = Assert.Single(result.Misses);
        Assert.Equal(("Incident", "Table 'incidnet' does not exist.", "Did you mean 'incident'?"), (miss.Location, miss.Message, miss.Suggestion));
        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task Enums_AreWrittenForChoiceColumns_WhenAskedFor()
    {
        var source = """
            namespace App;

            [DataverseEntity("incident", "incidents")]
            public class Incident
            {
                public int PriorityCode { get; set; }
            }
            """;

        var (text, _) = await ScaffoldAsync(source, enums: true);

        Assert.Equal(N("""
            namespace App;

            [DataverseEntity("incident", "incidents")]
            public class Incident
            {
                [DataverseColumn("prioritycode")]
                public IncidentPriorityCode? PriorityCode { get; set; }
            }

            public enum IncidentPriorityCode
            {
                High = 1,
                Normal = 2,
                LowPriority = 3
            }
            """), text);

        // The enum is the model's type now, so a second run changes nothing.
        var (again, result) = await ScaffoldAsync(text, enums: true);
        Assert.Equal(text, again);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task AnEnum_IsWrittenPerNamespace_AndDoesNotTakeAnotherTypesName()
    {
        var (text, result) = await ScaffoldAsync("""
            namespace A
            {
                [DataverseEntity("incident", "incidents")]
                public class Incident
                {
                    public int PriorityCode { get; set; }
                }
            }

            namespace B
            {
                [DataverseEntity("incident", "incidents")]
                public class Incident
                {
                    public int PriorityCode { get; set; }
                }
            }

            namespace C
            {
                [DataverseEntity("incident", "incidents")]
                public class Incident
                {
                    public int PriorityCode { get; set; }
                }

                public class IncidentPriorityCode { }
            }
            """, enums: true);

        // A and B each get their own enum; in C the name is a class, so the property keeps its int.
        Assert.Null(SourceFile.Parse("Incident.cs", text).Verify());
        Assert.Equal(2, text.Split("public enum IncidentPriorityCode").Length - 1);
        Assert.Equal(2, text.Split("public IncidentPriorityCode? PriorityCode").Length - 1);
        Assert.Contains("public int PriorityCode", text);
        Assert.Equal(2, result.Changes.Count(c => c.TypeChanged));
    }

    [Fact]
    public async Task TypeFilter_NarrowsTheModels_AndRejectsAnUnknownName()
    {
        var source = """
            [DataverseEntity("incident", "incidents")]
            public class Incident { public string Title { get; set; } }

            [DataverseEntity("account", "accounts")]
            public class Account { public string Name { get; set; } }
            """;

        var (text, result) = await ScaffoldAsync(source, types: "Account");

        Assert.Equal(1, result.Models);
        Assert.Contains("[DataverseColumn(\"name\")]", text);
        Assert.DoesNotContain("[DataverseColumn(\"title\")]", text);
        await Assert.ThrowsAsync<ToolException>(() => ScaffoldAsync(source, types: "Nope"));
    }

    [Theory]
    [InlineData("In Progress", "InProgress")]
    [InlineData("new_RegionId", "NewRegionId")]
    [InlineData("3rd party", "_3rdParty")]
    [InlineData("--", null)]
    public void Identifiers_AreMadeFromLabelsAndSchemaNames(string text, string? expected)
        => Assert.Equal(expected, ModelText.Identifier(text));

    // ───────── The command ─────────

    private sealed class Sources(params string[] files) : IProjectResolver
    {
        public string? Framework { get; init; }

        public Task<ProjectOutput> ResolveAsync(ToolSettings settings, bool build, CancellationToken ct)
            => throw new InvalidOperationException("scaffold does not build the project.");

        public Task<ProjectSources> ReadSourcesAsync(ToolSettings settings, CancellationToken ct)
            => Task.FromResult(new ProjectSources(files, NullableEnabled: true, ModelSemantics.ImplicitUsings, Framework));
    }

    private ToolSettings Settings() => new() { Target = Path.Combine(_directory, "App.csproj"), Url = "https://org.crm.dynamics.com" };

    private ToolRunner Runner(params string[] files) => new(_output, new Sources(files), _ => _environment.Transport);

    private string Write(string name, string source)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, N(source));
        return path;
    }

    [Fact]
    public async Task Scaffold_WritesTheModels_AndSaysWhatChanged()
    {
        var path = Write("Incident.cs", Stub);

        var exit = await Runner(path).ScaffoldAsync(Settings(), Ct);

        var output = _output.ToString();
        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(N(Scaffolded), await File.ReadAllTextAsync(path, Ct));
        Assert.Contains("Incident.cs", output);
        Assert.Contains("  Incident.Revenue  mapped to 'revenue'; type double -> decimal?", output);
        Assert.Contains("1 model read: 11 changes, 4 types changed, 0 not matched, 1 left as it is.", output);
        Assert.Contains("A changed type can break code that uses the property", output);
        // Metadata reads only.
        Assert.All(_environment.Transport.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task Scaffold_DryRun_PrintsADiff_AndWritesNothing()
    {
        var path = Write("Incident.cs", Stub);

        await Runner(path).ScaffoldAsync(Settings() with { DryRun = true }, Ct);

        var output = _output.ToString();
        Assert.Equal(N(Stub), await File.ReadAllTextAsync(path, Ct));
        Assert.Contains("--- Incident.cs", output);
        Assert.Contains("+    [DataverseColumn(\"title\")]", output);
        Assert.Contains("Nothing was written (--dry-run).", output);
    }

    [Fact]
    public async Task Scaffold_ExitsWith1_WhenAPropertyIsNotMatched()
    {
        var path = Write("Incident.cs", """
            [DataverseEntity("incident", "incidents")]
            public class Incident { public string Titel { get; set; } }
            """);

        var exit = await Runner(path).ScaffoldAsync(Settings(), Ct);

        Assert.Equal(ExitCodes.Findings, exit);
        Assert.Contains("Not matched:", _output.ToString());
        Assert.Contains("  Incident.Titel  No column 'titel' on incident.", _output.ToString());
        Assert.Contains("        Did you mean 'title'?", _output.ToString());
        Assert.Contains("A property that is not a column: mark it [JsonIgnore], or leave it out with --skip Model.Property.", _output.ToString());
    }

    [Fact]
    public async Task Scaffold_ListsWhatItLeftForValidate_AndStillSucceeds()
    {
        var path = Write("Incident.cs", """
            [DataverseEntity("incident", "incidents")]
            public class Incident
            {
                [DataverseColumn("customerid")]
                public string CustomerId { get; set; }

                public string Summary => CustomerId;
            }
            """);

        var exit = await Runner(path).ScaffoldAsync(Settings(), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(N("""
            Left as they are (validate checks them):
              Incident.Summary  it is computed, and the client still sends it as a column unless it has [JsonIgnore]
              Incident.CustomerId  it is mapped to 'customerid' as a column, and that is a lookup on the table

            1 model read: 0 changes, 0 types changed, 0 not matched, 2 left as they are.
            """), N(_output.ToString()));
    }

    [Fact]
    public async Task Scaffold_SkipsThePropertiesNamed()
    {
        var path = Write("Incident.cs", """
            [DataverseEntity("incident", "incidents")]
            public class Incident { public string Titel { get; set; } }
            """);

        var exit = await Runner(path).ScaffoldAsync(Settings() with { Skip = ["Incident.Titel"] }, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("1 model read: 0 changes, 0 types changed, 0 not matched, 0 left as they are.", _output.ToString());
    }

    [Fact]
    public async Task Scaffold_SaysWhichFrameworkItRead_WhenTheProjectHasSeveral()
    {
        var path = Write("Incident.cs", Stub);
        var runner = new ToolRunner(_output, new Sources(path) { Framework = "net10.0" }, _ => _environment.Transport);

        await runner.ScaffoldAsync(Settings() with { DryRun = true }, Ct);

        Assert.StartsWith("Reading the project for net10.0, its first target framework.", _output.ToString());
    }

    private const string OneModel = """
        [DataverseEntity("{0}", "{0}s")]
        public class {1}
        {{
            public string {2} {{ get; set; }}
        }}
        """;

    // A runner whose check of the planned edits fails for Incident.cs, as it would for an edit the tool got wrong.
    private (ToolRunner Runner, string Incident, string Account) RunnerThatRejectsIncident()
    {
        var incident = Write("Incident.cs", string.Format(System.Globalization.CultureInfo.InvariantCulture, OneModel, "incident", "Incident", "Title"));
        var account = Write("Account.cs", string.Format(System.Globalization.CultureInfo.InvariantCulture, OneModel, "account", "Account", "Name"));
        var runner = new ToolRunner(_output, new Sources(incident, account), _ => _environment.Transport, f => f.Path == incident ? "it would not parse" : null);
        return (runner, incident, account);
    }

    [Fact]
    public async Task Scaffold_HoldsBackAFileWhoseEditIsWrong_AndWritesTheOthers()
    {
        var (runner, incident, account) = RunnerThatRejectsIncident();
        var before = await File.ReadAllTextAsync(incident, Ct);

        var exit = await runner.ScaffoldAsync(Settings(), Ct);

        var output = N(_output.ToString());
        Assert.Equal(ExitCodes.Failure, exit);
        Assert.Equal(before, await File.ReadAllTextAsync(incident, Ct));
        Assert.Contains("[DataverseColumn(\"name\")]", await File.ReadAllTextAsync(account, Ct));

        // The edit that was held back is shown, so that it can be reported or made by hand.
        Assert.Contains("error: the edit planned for Incident.cs is wrong: it would not parse. The file is left as it is:\n--- Incident.cs", output);
        Assert.Contains("+    [DataverseColumn(\"title\")]", output);
        Assert.Contains("--force writes the file anyway.", output);

        // Only what was written is listed and counted.
        Assert.Contains("  Account.Name  mapped to 'name'", output);
        Assert.DoesNotContain("  Incident.Title  mapped to 'title'", output);
        Assert.Contains("2 models read: 1 change, 0 types changed, 0 not matched, 0 left as they are. 1 file held back.", output);
    }

    [Fact]
    public async Task Scaffold_Force_WritesAFileWhoseEditLooksWrong()
    {
        var (runner, incident, account) = RunnerThatRejectsIncident();

        var exit = await runner.ScaffoldAsync(Settings() with { Force = true }, Ct);

        var output = _output.ToString();
        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("[DataverseColumn(\"title\")]", await File.ReadAllTextAsync(incident, Ct));
        Assert.Contains("[DataverseColumn(\"name\")]", await File.ReadAllTextAsync(account, Ct));
        Assert.Contains("warning: the edit planned for Incident.cs looks wrong: it would not parse. It is kept (--force).", output);
        Assert.Contains("2 models read: 2 changes, 0 types changed, 0 not matched, 0 left as they are.", output);
        Assert.DoesNotContain("held back", output);
    }

    [Fact]
    public async Task Scaffold_NeedsAModel_OrATable()
    {
        var path = Write("Plain.cs", "public class Plain { public string Name { get; set; } }");

        var exit = await Runner(path).ScaffoldAsync(Settings(), Ct);

        Assert.Equal(ExitCodes.Failure, exit);
        Assert.Contains("error: The project has no class with [DataverseEntity] or [EntitySet] to fill in. Use --table to write a new model.", _output.ToString());
    }

    // ───────── A new model ─────────

    private ToolSettings TableSettings(string output, params string[] columns) => Settings() with
    {
        Table = "incident",
        Columns = columns,
        Output = Path.Combine(_directory, output),
        Namespace = "App.Models"
    };

    [Fact]
    public async Task ScaffoldTable_WritesANewModel_WithTheColumnsAskedFor()
    {
        var settings = TableSettings("Models/Incident.cs", "incidentid", "title", "followupby", "duedate", "prioritycode", "customerid", "new_regionid", "entityimage", "somethingnew", "nosuch");

        var exit = await Runner().ScaffoldAsync(settings, Ct);

        Assert.Equal(N("""
            #nullable enable
            using System;
            using Dataverse.Fluent;

            namespace App.Models;

            [DataverseEntity("incident", "incidents")]
            public partial class Incident
            {
                [DataverseReadOnly(DataverseOperation.Update)]
                [DataverseColumn("incidentid")]
                public Guid? Incidentid { get; set; }

                [DataverseColumn("title")]
                public string? Title { get; set; }

                [DataverseColumn("followupby")]
                public DateTime? FollowupBy { get; set; }

                [DataverseColumn("duedate")]
                public DateOnly? Duedate { get; set; }

                [DataverseColumn("prioritycode")]
                public int? Prioritycode { get; set; }

                [DataverseLookup("customerid")]
                [DataverseLookupTarget("account", "accounts", "customerid_account")]
                [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
                public DataverseReference? CustomerId { get; set; }

                [DataverseLookup("new_regionid", "new_regions", NavigationProperty = "new_RegionId")]
                public Guid? NewRegionId { get; set; }

                [DataverseColumn("entityimage")]
                public byte[]? Entityimage { get; set; }

                [DataverseColumn("somethingnew")]
                public object? Somethingnew { get; set; }
            }

            """), N(await File.ReadAllTextAsync(settings.Output!, Ct)));

        // A column that does not exist is left out and reported.
        Assert.Equal(ExitCodes.Findings, exit);
        Assert.Contains("  nosuch  No column 'nosuch' on incident.", _output.ToString());
        Assert.Contains("9 properties, 1 not matched.", _output.ToString());
    }

    [Fact]
    public async Task ScaffoldTable_WritesEnums_AndTakesAClassName()
    {
        var settings = TableSettings("Case.cs", "prioritycode") with { Enums = true, ClassName = "Case", Namespace = null, DryRun = true };

        var exit = await Runner().ScaffoldAsync(settings, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(File.Exists(settings.Output));
        Assert.Contains(N("""
            [DataverseEntity("incident", "incidents")]
            public partial class Case
            {
                [DataverseColumn("prioritycode")]
                public CasePrioritycode? Prioritycode { get; set; }
            }

            public enum CasePrioritycode
            {
                High = 1,
                Normal = 2,
                LowPriority = 3
            }
            """), N(_output.ToString()));
        Assert.DoesNotContain("namespace", _output.ToString());
    }

    [Fact]
    public async Task ScaffoldTable_DoesNotOverwriteAFile()
    {
        var existing = Write("Incident.cs", "// mine");

        var exit = await Runner().ScaffoldAsync(TableSettings("Incident.cs", "title"), Ct);

        Assert.Equal(ExitCodes.Failure, exit);
        Assert.Equal("// mine", await File.ReadAllTextAsync(existing, Ct));
        Assert.Contains("exists. To add to that model, add the properties to its class and run scaffold on the project.", _output.ToString());
    }

    [Fact]
    public async Task ScaffoldTable_ReportsWhatIsMissing()
    {
        Assert.Equal(ExitCodes.Failure, await Runner().ScaffoldAsync(TableSettings("A.cs"), Ct));
        Assert.Equal(ExitCodes.Failure, await Runner().ScaffoldAsync(TableSettings("A.cs", "title") with { Output = null }, Ct));
        Assert.Equal(ExitCodes.Failure, await Runner().ScaffoldAsync(TableSettings("A.cs", "title") with { Table = "incidnet" }, Ct));
        Assert.Equal(ExitCodes.Failure, await Runner().ScaffoldAsync(TableSettings("A.cs", "nosuch"), Ct));

        var output = _output.ToString();
        Assert.Contains("error: --table needs --columns", output);
        Assert.Contains("error: --table needs --output", output);
        Assert.Contains("error: Table 'incidnet' does not exist. Did you mean 'incident'?", output);
        Assert.Contains("error: None of the columns exist on incident, so no model was written.", output);
        Assert.False(File.Exists(Path.Combine(_directory, "A.cs")));
    }
}
