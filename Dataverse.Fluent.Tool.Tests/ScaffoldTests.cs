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

    // Raw strings take the line endings of this file, which depend on the checkout; the tests use LF.
    private static string N(string text) => text.ReplaceLineEndings("\n");

    private async Task<(string Text, ScaffoldResult Result)> ScaffoldAsync(string source, bool nullable = true, bool enums = false, params string[] types)
    {
        var file = SourceFile.Parse("Incident.cs", N(source));
        var result = await new ModelScaffolder(_environment.Transport, nullable, enums).ScaffoldAsync([file], types, Ct);
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
        public Task<ProjectOutput> ResolveAsync(ToolSettings settings, bool build, CancellationToken ct)
            => throw new InvalidOperationException("scaffold does not build the project.");

        public Task<ProjectSources> ReadSourcesAsync(ToolSettings settings, CancellationToken ct)
            => Task.FromResult(new ProjectSources(files, NullableEnabled: true));
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
        Assert.Contains("1 model read: 11 changes, 4 types changed, 0 not matched.", output);
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
