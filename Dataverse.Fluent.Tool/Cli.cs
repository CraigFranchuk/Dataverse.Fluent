using System.CommandLine;

namespace Dataverse.Fluent.Tool;

/// <summary>The command line: <c>validate</c> and <c>fix</c>, with the options they share.</summary>
internal static class Cli
{
    public const string UrlVariable = "DATAVERSE_URL";

    /// <summary>Builds the commands. <paramref name="run"/> receives the settings and whether to fix.</summary>
    public static RootCommand Build(Func<ToolSettings, bool, CancellationToken, Task<int>> run)
    {
        var root = new RootCommand("Checks Dataverse.Fluent models against a Dataverse environment's metadata, and fixes the mechanical mistakes.");
        root.Add(Command("validate", "Checks every model in a project (or built assembly) against the environment.", fix: false, run));
        root.Add(Command("fix", "Checks the models, then applies the fixes to the source and checks again.", fix: true, run));
        return root;
    }

    private static Command Command(string name, string description, bool fix, Func<ToolSettings, bool, CancellationToken, Task<int>> run)
    {
        var target = new Argument<string>("project")
        {
            Description = fix ? "The .csproj, or a directory holding one." : "The .csproj, a directory holding one, or a built .dll.",
            DefaultValueFactory = _ => "."
        };
        var url = new Option<string?>("--url", "-u") { Description = $"The environment URL, e.g. https://yourorg.crm.dynamics.com. Default: the {UrlVariable} environment variable." };
        var interactive = new Option<bool>("--interactive") { Description = "Sign in with a browser instead of DefaultAzureCredential (az login, Visual Studio, or AZURE_* variables)." };
        var tenant = new Option<string?>("--tenant-id") { Description = "The Microsoft Entra tenant to sign in to." };
        var apiVersion = new Option<string>("--api-version") { Description = "The Web API version.", DefaultValueFactory = _ => "v9.2" };
        var types = new Option<string[]>("--type", "-t") { Description = "Check only this model (full or simple name). Repeat for several." };
        var resolver = new Option<string?>("--resolver") { Description = "The full name of an IDataverseNameResolver in the project to register before checking, for models mapped by a resolver." };
        var ignore = new Option<string[]>("--ignore") { Description = "Leave out findings with this code, e.g. DVS015. Repeat for several." };
        var warningsAsErrors = new Option<bool>("--warnings-as-errors") { Description = "Exit with code 1 when a warning is found, not only an error." };
        var noSelect = new Option<bool>("--no-select-check") { Description = "Skip the live $select check (one retrieve of a random ID per model)." };
        var configuration = new Option<string>("--configuration", "-c") { Description = "The build configuration.", DefaultValueFactory = _ => "Debug" };
        var framework = new Option<string?>("--framework", "-f") { Description = "The target framework of a multi-targeted project. Default: its first." };
        var noBuild = new Option<bool>("--no-build") { Description = "Use the existing build output." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Print the changes as a diff and write nothing." };
        var verbose = new Option<bool>("--verbose") { Description = "Print the stack trace of an unexpected error." };

        var command = new Command(name, description) { target, url, interactive, tenant, apiVersion, types, resolver, ignore, warningsAsErrors, noSelect, configuration, framework, noBuild, verbose };
        if (fix)
            command.Add(dryRun);

        command.SetAction(async (parse, ct) =>
        {
            var environmentUrl = parse.GetValue(url) ?? Environment.GetEnvironmentVariable(UrlVariable);
            if (string.IsNullOrWhiteSpace(environmentUrl))
            {
                await parse.InvocationConfiguration.Error.WriteLineAsync($"error: pass --url, or set {UrlVariable}.").ConfigureAwait(false);
                return ExitCodes.Failure;
            }

            if (!Uri.TryCreate(environmentUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                await parse.InvocationConfiguration.Error.WriteLineAsync($"error: '{environmentUrl}' is not an https URL.").ConfigureAwait(false);
                return ExitCodes.Failure;
            }

            var settings = new ToolSettings
            {
                Target = parse.GetValue(target) ?? ".",
                Url = environmentUrl,
                Interactive = parse.GetValue(interactive),
                TenantId = parse.GetValue(tenant),
                ApiVersion = parse.GetValue(apiVersion) ?? "v9.2",
                Types = parse.GetValue(types) ?? [],
                Resolver = parse.GetValue(resolver),
                Ignore = parse.GetValue(ignore) ?? [],
                WarningsAsErrors = parse.GetValue(warningsAsErrors),
                NoSelectCheck = parse.GetValue(noSelect),
                Configuration = parse.GetValue(configuration) ?? "Debug",
                Framework = parse.GetValue(framework),
                NoBuild = parse.GetValue(noBuild),
                DryRun = fix && parse.GetValue(dryRun),
                Verbose = parse.GetValue(verbose)
            };

            return await run(settings, fix, ct).ConfigureAwait(false);
        });

        return command;
    }
}
