using Azure.Core;
using Azure.Identity;
using Dataverse.Fluent.Models;
using Dataverse.Fluent.Schema;
using Dataverse.Fluent.Tool.Fixing;
using Dataverse.Fluent.Tool.Projects;
using Dataverse.Fluent.Tool.Scaffolding;

namespace Dataverse.Fluent.Tool;

/// <summary>Runs the <c>validate</c> and <c>fix</c> commands.</summary>
internal sealed class ToolRunner(TextWriter output, IProjectResolver projects, Func<ToolSettings, IDataverseTransport> connect)
{
    /// <summary>Connects with <c>DefaultAzureCredential</c>, or a browser sign-in with <c>--interactive</c>.</summary>
    public static IDataverseTransport Connect(ToolSettings settings)
    {
        TokenCredential credential = settings.Interactive
            ? new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions { TenantId = settings.TenantId })
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = settings.TenantId });

        return new DataverseHttpTransport(new DataverseClientOptions { BaseUrl = settings.WebApiUrl, Credential = credential });
    }

    public Task<int> ValidateAsync(ToolSettings settings, CancellationToken ct)
        => GuardAsync(settings, async () =>
        {
            var project = await projects.ResolveAsync(settings, build: !settings.NoBuild, ct).ConfigureAwait(false);
            var (report, models) = await ValidateProjectAsync(settings, project, ct).ConfigureAwait(false);

            output.WriteLine();
            ReportWriter.WriteFindings(output, report.Findings);
            if (report.Findings.Count > 0)
                output.WriteLine();
            ReportWriter.WriteSummary(output, models, report.Findings, mentionFix: project.SourceFiles.Count > 0);
            return ExitCode(report.Findings, settings);
        });

    public Task<int> FixAsync(ToolSettings settings, CancellationToken ct)
        => GuardAsync(settings, async () =>
        {
            if (settings.Target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new ToolException("fix edits source, so it needs the project (.csproj), not a built assembly.");

            var project = await projects.ResolveAsync(settings, build: !settings.NoBuild, ct).ConfigureAwait(false);
            var (report, models) = await ValidateProjectAsync(settings, project, ct).ConfigureAwait(false);

            var fixer = SourceFixer.Load(project.SourceFiles);
            var plan = fixer.Plan(report.Findings);
            var applied = plan.Outcomes.Where(o => o.Applied).Select(o => o.Finding).ToHashSet();
            var baseDirectory = BaseDirectory(settings);
            output.WriteLine();

            if (settings.DryRun)
            {
                foreach (var file in plan.Changed)
                    output.Write(UnifiedDiff.Create(ReportWriter.Relative(file.Path, baseDirectory).Replace('\\', '/'), file.Text.ToString(), file.Edited().ToString()));
                if (plan.Changed.Any())
                    output.WriteLine();
            }
            else
            {
                SourceFixer.Write(plan);
            }

            ReportWriter.WriteFixes(output, plan, baseDirectory);

            var manual = report.Findings.Where(f => !applied.Contains(f)).ToList();
            var verb = settings.DryRun ? "can be fixed" : "fixed";
            output.WriteLine();
            output.WriteLine($"{applied.Count} finding{(applied.Count == 1 ? "" : "s")} {verb} in {plan.Changed.Count()} file{(plan.Changed.Count() == 1 ? "" : "s")}.{(settings.DryRun ? " Nothing was written (--dry-run)." : "")}");

            if (!settings.DryRun && applied.Count > 0 && !settings.NoBuild)
            {
                // Check the result: rebuild and validate again.
                output.WriteLine();
                project = await projects.ResolveAsync(settings, build: true, ct).ConfigureAwait(false);
                (report, models) = await ValidateProjectAsync(settings, project, ct).ConfigureAwait(false);
                manual = [.. report.Findings];
            }

            if (manual.Count > 0)
            {
                output.WriteLine();
                output.WriteLine(settings.DryRun || applied.Count == 0 || settings.NoBuild ? "Needs a manual change:" : "Still to change by hand:");
                ReportWriter.WriteFindings(output, manual);
            }

            output.WriteLine();
            ReportWriter.WriteSummary(output, models, manual, mentionFix: false);
            return ExitCode(manual, settings);
        });

    /// <summary>
    /// Fills in the project's models from the environment, or with <c>--table</c> writes a new model for a table.
    /// Exits with 1 when a model or property could not be matched.
    /// </summary>
    public Task<int> ScaffoldAsync(ToolSettings settings, CancellationToken ct)
        => GuardAsync(settings, () => settings.Table is null ? ScaffoldProjectAsync(settings, ct) : ScaffoldTableAsync(settings, ct));

    private async Task<int> ScaffoldProjectAsync(ToolSettings settings, CancellationToken ct)
    {
        var sources = await projects.ReadSourcesAsync(settings, ct).ConfigureAwait(false);
        var files = sources.SourceFiles.Select(SourceFile.Read).ToList();
        output.WriteLine($"Scaffolding from {settings.WebApiUrl}...");

        ScaffoldResult result;
        var transport = connect(settings);
        try
        {
            result = await new ModelScaffolder(transport, sources.NullableEnabled, settings.Enums).ScaffoldAsync(files, settings.Types, ct).ConfigureAwait(false);
        }
        finally
        {
            (transport as IDisposable)?.Dispose();
        }

        if (result.Models == 0)
            throw new ToolException("The project has no class with [DataverseEntity] or [EntitySet] to fill in. Use --table to write a new model.");

        var baseDirectory = BaseDirectory(settings);
        output.WriteLine();
        if (settings.DryRun)
        {
            foreach (var file in result.Changed)
                output.Write(UnifiedDiff.Create(ReportWriter.Relative(file.Path, baseDirectory).Replace('\\', '/'), file.Text.ToString(), file.Edited().ToString()));
            if (result.Changed.Any())
                output.WriteLine();
        }
        else
        {
            foreach (var file in result.Changed)
                file.Write();
        }

        foreach (var group in result.Changes.GroupBy(c => c.File))
        {
            output.WriteLine(ReportWriter.Relative(group.Key, baseDirectory));
            foreach (var change in group)
                output.WriteLine($"  {change.Location}  {change.Description}");
        }

        WriteMisses(result.Misses);

        var types = result.Changes.Count(c => c.TypeChanged);
        var changed = result.Changes.Count;
        output.WriteLine();
        output.WriteLine($"{result.Models} model{Plural(result.Models)} read: {changed} change{Plural(changed)}{(settings.DryRun ? " to make" : "")}, " +
            $"{types} type{Plural(types)} changed, {result.Misses.Count} not matched.{(settings.DryRun ? " Nothing was written (--dry-run)." : "")}");
        if (types > 0 && !settings.DryRun)
            output.WriteLine("A changed type can break code that uses the property: build the project to find it.");

        return result.Misses.Count > 0 ? ExitCodes.Findings : ExitCodes.Success;
    }

    private async Task<int> ScaffoldTableAsync(ToolSettings settings, CancellationToken ct)
    {
        if (settings.Columns.Count == 0)
            throw new ToolException("--table needs --columns: the logical names of the columns the model gets, e.g. --columns title,customerid.");
        if (string.IsNullOrWhiteSpace(settings.Output))
            throw new ToolException("--table needs --output: the file to write.");

        var path = Path.GetFullPath(settings.Output);
        if (!settings.DryRun && File.Exists(path))
            throw new ToolException($"'{settings.Output}' exists. To add to that model, add the properties to its class and run scaffold on the project.");

        output.WriteLine($"Scaffolding {settings.Table} from {settings.WebApiUrl}...");

        ScaffoldedModel model;
        var transport = connect(settings);
        try
        {
            model = await DataverseModelScaffolder.ScaffoldAsync(transport, new ScaffoldRequest
            {
                LogicalName = settings.Table,
                Properties = [.. settings.Columns.Distinct(StringComparer.OrdinalIgnoreCase).Select(c => new ScaffoldProperty(c))],
                IncludeChoiceOptions = settings.Enums
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            (transport as IDisposable)?.Dispose();
        }

        if (!model.TableFound)
            throw new ToolException(model.Message + (model.Suggestion is null ? "" : " " + model.Suggestion));

        var className = settings.ClassName ?? ModelText.Identifier(model.SchemaName ?? model.LogicalName) ?? "Model";
        var names = new HashSet<string>(StringComparer.Ordinal) { className };
        var properties = new List<(ScaffoldedProperty, string)>();
        foreach (var property in model.Properties.Where(p => p.Kind != ScaffoldedPropertyKind.NotFound))
        {
            // A property cannot have its class's name, and two columns can give the same name.
            var name = ModelText.Identifier(property.SchemaName ?? property.LogicalName) ?? "Column";
            while (!names.Add(name))
                name += "Value";
            properties.Add((property, name));
        }

        var misses = model.Properties.Where(p => p.Kind == ScaffoldedPropertyKind.NotFound)
            .Select(p => new ScaffoldMiss(p.Name, p.Message!, p.Suggestion)).ToList();
        if (properties.Count == 0)
        {
            WriteMisses(misses);
            throw new ToolException($"None of the columns exist on {model.LogicalName}, so no model was written.");
        }

        var text = ModelText.NewModel(model, className, settings.Namespace, properties, settings.Enums);
        output.WriteLine();
        if (settings.DryRun)
        {
            output.Write(text);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, text.ReplaceLineEndings(), ct).ConfigureAwait(false);
        }

        WriteMisses(misses);
        var count = $"{properties.Count} propert{(properties.Count == 1 ? "y" : "ies")}, {misses.Count} not matched";
        output.WriteLine();
        output.WriteLine(settings.DryRun
            ? $"{className}: {count}. Nothing was written (--dry-run)."
            : $"Wrote {className} to {settings.Output}: {count}.");

        return misses.Count > 0 ? ExitCodes.Findings : ExitCodes.Success;
    }

    private void WriteMisses(IReadOnlyCollection<ScaffoldMiss> misses)
    {
        if (misses.Count == 0)
            return;

        output.WriteLine();
        output.WriteLine("Not matched:");
        foreach (var miss in misses)
        {
            output.WriteLine($"  {miss.Location}  {miss.Message}");
            if (miss.Suggestion is not null)
                output.WriteLine($"        {miss.Suggestion}");
        }
    }

    private static string Plural(int count) => count == 1 ? "" : "s";

    // The project's directory, which the report gives file paths relative to.
    private static string? BaseDirectory(ToolSettings settings)
        => Path.GetDirectoryName(Path.GetFullPath(settings.Target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? settings.Target : Path.Combine(settings.Target, "x")));

    private async Task<(SchemaReport Report, int Models)> ValidateProjectAsync(ToolSettings settings, ProjectOutput project, CancellationToken ct)
    {
        var assembly = ModelLoader.Load(project.AssemblyPath);
        if (settings.Resolver is { } resolver)
            ModelLoader.RegisterResolver(assembly, resolver);

        var models = ModelLoader.FindModels(assembly, settings.Types);
        if (models.Count == 0)
            throw new ToolException($"{assembly.GetName().Name} has no models: no class is mapped with [DataverseEntity] or [EntitySet]{(settings.Resolver is null ? " (use --resolver for models mapped by a resolver)" : "")}.");

        var options = new SchemaValidationOptions { CheckSelect = !settings.NoSelectCheck };
        foreach (var code in settings.Ignore)
            options.IgnoreCodes.Add(code);

        output.WriteLine($"Checking {models.Count} model{(models.Count == 1 ? "" : "s")} against {settings.WebApiUrl}...");

        var transport = connect(settings);
        try
        {
            return (await DataverseSchemaValidator.ValidateAsync(transport, models, options, ct).ConfigureAwait(false), models.Count);
        }
        finally
        {
            (transport as IDisposable)?.Dispose();
        }
    }

    private static int ExitCode(IReadOnlyCollection<SchemaFinding> findings, ToolSettings settings)
        => findings.Any(f => f.Severity == SchemaSeverity.Error || (settings.WarningsAsErrors && f.Severity == SchemaSeverity.Warning))
            ? ExitCodes.Findings
            : ExitCodes.Success;

    // The finding codes, read from SchemaCodes so a new code needs no change here.
    private static readonly string[] KnownCodes = [.. typeof(SchemaCodes)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .Order(StringComparer.Ordinal)];

    // A mistyped code would be ignored itself, and the findings it was meant to leave out would fail the run.
    private static void CheckIgnoredCodes(ToolSettings settings)
    {
        var unknown = settings.Ignore.Where(c => !KnownCodes.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
        {
            throw new ToolException(
                $"--ignore: {string.Join(", ", unknown.Select(c => $"'{c}'"))} {(unknown.Count == 1 ? "is not a finding code" : "are not finding codes")}. " +
                $"The codes are {string.Join(", ", KnownCodes)}.");
        }
    }

    private async Task<int> GuardAsync(ToolSettings settings, Func<Task<int>> run)
    {
        try
        {
            CheckIgnoredCodes(settings);
            return await run().ConfigureAwait(false);
        }
        catch (ToolException ex)
        {
            output.WriteLine($"error: {ex.Message}");
        }
        catch (AuthenticationFailedException ex)
        {
            output.WriteLine($"error: could not sign in to Dataverse: {FirstLine(ex.Message)}");
            output.WriteLine("Sign in with 'az login', set AZURE_CLIENT_ID, AZURE_CLIENT_SECRET and AZURE_TENANT_ID, or pass --interactive.");
        }
        catch (DataverseException ex)
        {
            output.WriteLine($"error: Dataverse answered {(int)ex.StatusCode}: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            output.WriteLine($"error: could not reach the environment: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything else is the tool failing, not the models: exit code 1 is kept for findings.
            output.WriteLine($"error: {ex.GetType().Name}: {ex.Message}");
            output.WriteLine(settings.Verbose ? ex.ToString() : "Run again with --verbose for the stack trace.");
        }

        return ExitCodes.Failure;
    }

    private static string FirstLine(string text) => text.ReplaceLineEndings("\n").Split('\n')[0];
}
