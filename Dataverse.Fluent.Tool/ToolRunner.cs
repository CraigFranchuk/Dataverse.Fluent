using Azure.Core;
using Azure.Identity;
using Dataverse.Fluent.Models;
using Dataverse.Fluent.Schema;
using Dataverse.Fluent.Tool.Fixing;
using Dataverse.Fluent.Tool.Projects;

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
        => GuardAsync(async () =>
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
        => GuardAsync(async () =>
        {
            if (settings.Target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new ToolException("fix edits source, so it needs the project (.csproj), not a built assembly.");

            var project = await projects.ResolveAsync(settings, build: !settings.NoBuild, ct).ConfigureAwait(false);
            var (report, models) = await ValidateProjectAsync(settings, project, ct).ConfigureAwait(false);

            var fixer = SourceFixer.Load(project.SourceFiles);
            var plan = fixer.Plan(report.Findings);
            var applied = plan.Outcomes.Where(o => o.Applied).Select(o => o.Finding).ToHashSet();
            var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(settings.Target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? settings.Target : Path.Combine(settings.Target, "x")));
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

    private async Task<int> GuardAsync(Func<Task<int>> run)
    {
        try
        {
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

        return ExitCodes.Failure;
    }

    private static string FirstLine(string text) => text.ReplaceLineEndings("\n").Split('\n')[0];
}
