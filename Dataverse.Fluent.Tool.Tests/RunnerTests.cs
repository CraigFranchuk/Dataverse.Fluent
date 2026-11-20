using System.Net;
using System.Text.Json.Serialization;
using Dataverse.Fluent.Schema.Tests;
using Dataverse.Fluent.Tool.Projects;

namespace Dataverse.Fluent.Tool.Tests;

// A model in this assembly: the runner tests load the assembly the way the tool loads a project's.
[DataverseEntity("incident", "incidents")]
public class RunnerCase
{
    [JsonPropertyName("ticketnumber")]
    public string? TicketNumber { get; set; }

    [JsonPropertyName("titel")]
    public string? Title { get; set; }
}

/// <summary>The validate and fix commands, against a fake environment and this assembly as the project.</summary>
public class RunnerTests : IDisposable
{
    private const string Source = """
        using System.Text.Json.Serialization;

        namespace Dataverse.Fluent.Tool.Tests;

        [DataverseEntity("incident", "incidents")]
        public class RunnerCase
        {
            [JsonPropertyName("ticketnumber")]
            public string? TicketNumber { get; set; }

            [JsonPropertyName("titel")]
            public string? Title { get; set; }
        }
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"runner-{Guid.NewGuid():N}");
    private readonly string _source;
    private readonly FakeEnvironment _environment = new();
    private readonly StringWriter _output = new();

    public RunnerTests()
    {
        Directory.CreateDirectory(_directory);
        _source = Path.Combine(_directory, "RunnerCase.cs");
        File.WriteAllText(_source, Source.ReplaceLineEndings("\n"));
        _environment.Table("incident", "incidents")
            .Column("incidentid", "Uniqueidentifier", update: false)
            .Column("title", "String")
            .Column("ticketnumber", "String", update: false);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        _output.Dispose();
    }

    private sealed class ThisAssembly(string source) : IProjectResolver
    {
        public int Builds { get; private set; }

        public Task<ProjectOutput> ResolveAsync(ToolSettings settings, bool build, CancellationToken ct)
        {
            if (build)
                Builds++;
            return Task.FromResult(new ProjectOutput(typeof(RunnerCase).Assembly.Location, [source]));
        }
    }

    private ToolSettings Settings(bool dryRun = false, params string[] ignore) => new()
    {
        Target = Path.Combine(_directory, "App.csproj"),
        Url = "https://org.crm.dynamics.com",
        Types = [nameof(RunnerCase)],
        Ignore = ignore,
        NoBuild = true,
        DryRun = dryRun
    };

    private ToolRunner Runner(Func<ToolSettings, IDataverseTransport>? connect = null)
        => new(_output, new ThisAssembly(_source), connect ?? (_ => _environment.Transport));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Validate_PrintsTheFindings_AndFailsOnAnError()
    {
        var exit = await Runner().ValidateAsync(Settings(), Ct);

        var output = _output.ToString();
        Assert.Equal(ExitCodes.Findings, exit);
        Assert.Contains("Checking 1 model against https://org.crm.dynamics.com/api/data/v9.2...", output);
        Assert.Contains("error   RunnerCase.Title  DVS010  Column 'titel' does not exist on incident.", output);
        Assert.Contains("        Did you mean 'title'?", output);
        Assert.Contains("warning RunnerCase.TicketNumber  DVS040", output);
        Assert.Contains("        fix: mark it [DataverseReadOnly(DataverseOperation.Update)]", output);
        Assert.Contains("1 model checked: 1 error, 1 warning, 0 info. 1 can be fixed with 'dataverse-fluent fix'.", output);
    }

    [Fact]
    public async Task Validate_PassesWithWarnings_UnlessWarningsAreErrors()
    {
        Assert.Equal(ExitCodes.Success, await Runner().ValidateAsync(Settings(ignore: "DVS010"), Ct));
        Assert.Equal(ExitCodes.Findings, await Runner().ValidateAsync(Settings(ignore: "DVS010") with { WarningsAsErrors = true }, Ct));
    }

    [Fact]
    public async Task Fix_EditsTheSource_AndListsWhatIsLeft()
    {
        var exit = await Runner().FixAsync(Settings(), Ct);

        var output = _output.ToString();
        Assert.Contains("[DataverseReadOnly(DataverseOperation.Update)]\n    [JsonPropertyName(\"ticketnumber\")]", await File.ReadAllTextAsync(_source, Ct));
        Assert.Contains("RunnerCase.cs\n  RunnerCase.TicketNumber  DVS040  mark it [DataverseReadOnly(DataverseOperation.Update)]".ReplaceLineEndings(), output);
        Assert.Contains("1 finding fixed in 1 file.", output);
        Assert.Contains("Needs a manual change:", output);
        Assert.Contains("error   RunnerCase.Title  DVS010", output);
        Assert.Equal(ExitCodes.Findings, exit);
    }

    [Fact]
    public async Task Fix_DryRun_PrintsADiff_AndWritesNothing()
    {
        await Runner().FixAsync(Settings(dryRun: true), Ct);

        var output = _output.ToString();
        Assert.Equal(Source.ReplaceLineEndings("\n"), await File.ReadAllTextAsync(_source, Ct));
        Assert.Contains("--- RunnerCase.cs", output);
        Assert.Contains("+    [DataverseReadOnly(DataverseOperation.Update)]", output);
        Assert.Contains("1 finding can be fixed in 1 file. Nothing was written (--dry-run).", output);
    }

    [Fact]
    public async Task Fix_RebuildsAndChecksAgain_AfterWriting()
    {
        var projects = new ThisAssembly(_source);
        var runner = new ToolRunner(_output, projects, _ => _environment.Transport);

        await runner.FixAsync(Settings() with { NoBuild = false }, Ct);

        // The first build, and the one after writing the fix.
        Assert.Equal(2, projects.Builds);
        Assert.Contains("Still to change by hand:", _output.ToString());
    }

    [Fact]
    public async Task Fix_NeedsAProject()
    {
        var exit = await Runner().FixAsync(Settings() with { Target = "bin/App.dll" }, Ct);

        Assert.Equal(ExitCodes.Failure, exit);
        Assert.Contains("error: fix edits source, so it needs the project (.csproj), not a built assembly.", _output.ToString());
    }

    [Fact]
    public async Task ADataverseError_IsReported_WithExitCode2()
    {
        var transport = new Testing.FakeDataverseTransport();
        transport.On(_ => true).Respond(_ => throw FakeEnvironment.Error(HttpStatusCode.Unauthorized, "The user is not a member of the organization.", "0x80072560"));

        var exit = await Runner(_ => transport).ValidateAsync(Settings(), Ct);

        Assert.Equal(ExitCodes.Failure, exit);
        Assert.Contains("error: Dataverse answered 401: The user is not a member of the organization.", _output.ToString());
    }

    [Fact]
    public async Task AnUnexpectedError_IsReported_WithExitCode2_NotAsFindings()
    {
        var exit = await Runner(_ => throw new InvalidDataException("The stream is not text.")).ValidateAsync(Settings(), Ct);

        Assert.Equal(ExitCodes.Failure, exit);
        Assert.Contains("error: InvalidDataException: The stream is not text.", _output.ToString());
        Assert.Contains("Run again with --verbose for the stack trace.", _output.ToString());
        Assert.DoesNotContain("   at ", _output.ToString());
    }

    [Fact]
    public async Task AnUnexpectedError_PrintsItsStackTrace_WhenVerbose()
    {
        await Runner(_ => throw new InvalidDataException("The stream is not text.")).ValidateAsync(Settings() with { Verbose = true }, Ct);

        Assert.Contains("   at ", _output.ToString());
    }

    [Fact]
    public async Task AnUnknownIgnoreCode_IsRejected_BeforeAnythingRuns()
    {
        var projects = new ThisAssembly(_source);
        var runner = new ToolRunner(_output, projects, _ => _environment.Transport);

        var exit = await runner.ValidateAsync(Settings(ignore: ["dvs040", "DVS51"]) with { NoBuild = false }, Ct);

        Assert.Equal(ExitCodes.Failure, exit);
        Assert.Contains("error: --ignore: 'DVS51' is not a finding code. The codes are DVS001, DVS002,", _output.ToString());
        Assert.Equal(0, projects.Builds);
        Assert.Empty(_environment.Transport.Requests);
    }

    [Fact]
    public async Task Cancellation_IsNotReportedAsAnError()
        => await Assert.ThrowsAsync<OperationCanceledException>(
            () => Runner(_ => throw new OperationCanceledException()).ValidateAsync(Settings(), Ct));

    [Fact]
    public async Task AnUnknownModel_IsReported()
    {
        var exit = await Runner().ValidateAsync(Settings() with { Types = ["Nope"] }, Ct);

        Assert.Equal(ExitCodes.Failure, exit);
        Assert.Contains("error: No model named 'Nope'", _output.ToString());
    }
}

/// <summary>The loader gives a project's models the tool's Dataverse.Fluent, so the validator can read them.</summary>
public class ModelLoaderTests
{
    [Fact]
    public void Load_ReadsTheAssemblyIntoItsOwnContext_SharingDataverseFluent()
    {
        var assembly = ModelLoader.Load(typeof(RunnerCase).Assembly.Location);

        Assert.NotSame(typeof(RunnerCase).Assembly, assembly);
        var model = Assert.Single(ModelLoader.FindModels(assembly, [nameof(RunnerCase)]));
        Assert.NotEqual(typeof(RunnerCase), model);
        // The attributes are the tool's own types, so the model's mapping can be read.
        Assert.Equal("incidents", DataverseEntityMapping.For(model).EntitySet);
    }

    [Fact]
    public void FindModels_AcceptsTheFullName()
    {
        var assembly = ModelLoader.Load(typeof(RunnerCase).Assembly.Location);

        Assert.Single(ModelLoader.FindModels(assembly, [typeof(RunnerCase).FullName!]));
    }

    [Fact]
    public void RegisterResolver_ExplainsWhatIsWrong()
    {
        var assembly = ModelLoader.Load(typeof(RunnerCase).Assembly.Location);

        Assert.Contains("No type named", Assert.Throws<ToolException>(() => ModelLoader.RegisterResolver(assembly, "Missing.Resolver")).Message);
        Assert.Contains("does not implement IDataverseNameResolver",
            Assert.Throws<ToolException>(() => ModelLoader.RegisterResolver(assembly, typeof(RunnerCase).FullName!)).Message);
    }
}

/// <summary>A real build of a small project, as the tool runs one.</summary>
public class ProjectResolverTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"resolver-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // The build server can hold a file for a moment; the temp folder is cleaned up eventually.
        }
    }

    [Fact]
    public async Task BuildsTheProject_AndFindsItsAssemblyAndSources()
    {
        Directory.CreateDirectory(_directory);
        var core = typeof(DataverseClient).Assembly.Location;
        await File.WriteAllTextAsync(Path.Combine(_directory, "Models.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="Dataverse.Fluent" HintPath="{core}" />
              </ItemGroup>
            </Project>
            """, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_directory, "Account.cs"), """
            namespace Models;

            [Dataverse.Fluent.EntitySet("accounts")]
            public class Account
            {
                public string? Name { get; set; }
            }
            """, TestContext.Current.CancellationToken);

        var log = new StringWriter();
        var output = await new DotnetProjectResolver(log).ResolveAsync(
            new ToolSettings { Target = _directory, Url = "https://org.crm.dynamics.com" }, build: true, TestContext.Current.CancellationToken);

        Assert.Equal("Models.dll", Path.GetFileName(output.AssemblyPath));
        Assert.True(File.Exists(output.AssemblyPath));
        // The project's own source only: generated files under obj are left out.
        Assert.Equal("Account.cs", Path.GetFileName(Assert.Single(output.SourceFiles)));
        Assert.Contains("Building Models.csproj...", log.ToString());

        var model = Assert.Single(ModelLoader.FindModels(ModelLoader.Load(output.AssemblyPath), []));
        Assert.Equal("Models.Account", model.FullName);
    }

    [Fact]
    public async Task ADirectoryWithoutAProject_IsReported()
    {
        Directory.CreateDirectory(_directory);

        var ex = await Assert.ThrowsAsync<ToolException>(() => new DotnetProjectResolver(TextWriter.Null).ResolveAsync(
            new ToolSettings { Target = _directory, Url = "https://org.crm.dynamics.com" }, build: false, TestContext.Current.CancellationToken));

        Assert.Contains("holds no .csproj file", ex.Message);
    }
}
