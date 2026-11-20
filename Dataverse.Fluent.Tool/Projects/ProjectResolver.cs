using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace Dataverse.Fluent.Tool.Projects;

/// <summary>A built project: the assembly that holds its models, and the source files they are declared in.</summary>
internal sealed record ProjectOutput(string AssemblyPath, IReadOnlyList<string> SourceFiles);

/// <summary>A project's own source files, read without building it, and whether it has nullable reference types on.</summary>
internal sealed record ProjectSources(IReadOnlyList<string> SourceFiles, bool NullableEnabled);

/// <summary>Builds a project and finds its output, or takes a built assembly as it is.</summary>
internal interface IProjectResolver
{
    Task<ProjectOutput> ResolveAsync(ToolSettings settings, bool build, CancellationToken ct);

    /// <summary>The project's source files, without a build: scaffolding works on models that do not compile yet.</summary>
    Task<ProjectSources> ReadSourcesAsync(ToolSettings settings, CancellationToken ct);
}

/// <summary>Runs <c>dotnet build</c> and asks MSBuild for <c>TargetPath</c> and the <c>Compile</c> items.</summary>
internal sealed class DotnetProjectResolver(TextWriter log) : IProjectResolver
{
    public async Task<ProjectOutput> ResolveAsync(ToolSettings settings, bool build, CancellationToken ct)
    {
        var target = Path.GetFullPath(settings.Target);
        if (target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(target))
                throw new ToolException($"'{settings.Target}' does not exist.");
            return new ProjectOutput(target, []);
        }

        var project = FindProject(target, settings.Target);

        if (build)
        {
            log.WriteLine($"Building {Path.GetFileName(project)}...");
            var args = new List<string> { "build", project, "-c", settings.Configuration, "-nologo", "-v:q" };
            if (settings.Framework is { } tfm)
                args.AddRange(["-f", tfm]);

            var (exit, output) = await RunDotnetAsync(args, ct).ConfigureAwait(false);
            if (exit != 0)
                throw new ToolException($"The build failed:{Environment.NewLine}{Tail(output, 40)}");
        }

        var properties = await QueryAsync(project, settings.Configuration, settings.Framework, ct).ConfigureAwait(false);
        var targetPath = (string?)properties["Properties"]?["TargetPath"];
        if (string.IsNullOrEmpty(targetPath))
        {
            // A multi-targeted project has no TargetPath of its own: use its first framework.
            var first = ((string?)properties["Properties"]?["TargetFrameworks"])?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
                ?? throw new ToolException($"MSBuild did not report an output for {Path.GetFileName(project)}.");
            properties = await QueryAsync(project, settings.Configuration, first, ct).ConfigureAwait(false);
            targetPath = (string?)properties["Properties"]?["TargetPath"];
        }

        if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
        {
            throw new ToolException(build
                ? $"The build did not produce {targetPath}."
                : $"{targetPath} does not exist. Build the project, or leave out --no-build.");
        }

        return new ProjectOutput(targetPath, Sources(project, properties));
    }

    public async Task<ProjectSources> ReadSourcesAsync(ToolSettings settings, CancellationToken ct)
    {
        var target = Path.GetFullPath(settings.Target);
        if (target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new ToolException("scaffold edits source, so it needs the project (.csproj), not a built assembly.");

        var project = FindProject(target, settings.Target);
        var properties = await QueryAsync(project, settings.Configuration, settings.Framework, ct).ConfigureAwait(false);
        var nullable = (string?)properties["Properties"]?["Nullable"];
        return new ProjectSources(Sources(project, properties), string.Equals(nullable, "enable", StringComparison.OrdinalIgnoreCase));
    }

    // The project's own source only: generated files under obj are left out.
    private static List<string> Sources(string project, JsonObject properties)
    {
        var objDirectory = Path.Combine(Path.GetDirectoryName(project)!, "obj") + Path.DirectorySeparatorChar;
        return properties["Items"]?["Compile"]?.AsArray()
            .Select(i => (string?)i?["FullPath"])
            .OfType<string>()
            .Where(p => !p.StartsWith(objDirectory, StringComparison.OrdinalIgnoreCase) && File.Exists(p))
            .ToList() ?? [];
    }

    private static string FindProject(string target, string given)
    {
        if (Directory.Exists(target))
        {
            var projects = Directory.GetFiles(target, "*.csproj");
            return projects.Length switch
            {
                1 => projects[0],
                0 => throw new ToolException($"'{given}' holds no .csproj file."),
                _ => throw new ToolException($"'{given}' holds more than one .csproj file; name the one to use.")
            };
        }

        if (!File.Exists(target))
            throw new ToolException($"'{given}' does not exist.");
        if (!target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            throw new ToolException($"'{given}' is not a .csproj or .dll file.");
        return target;
    }

    private static async Task<JsonObject> QueryAsync(string project, string configuration, string? framework, CancellationToken ct)
    {
        var args = new List<string> { "msbuild", project, "-getProperty:TargetPath", "-getProperty:TargetFrameworks", "-getProperty:Nullable", "-getItem:Compile", $"-p:Configuration={configuration}", "-nologo" };
        if (framework is not null)
            args.Add($"-p:TargetFramework={framework}");

        var (exit, output) = await RunDotnetAsync(args, ct).ConfigureAwait(false);
        var start = output.IndexOf('{', StringComparison.Ordinal);
        if (exit != 0 || start < 0)
            throw new ToolException($"MSBuild could not evaluate {Path.GetFileName(project)}:{Environment.NewLine}{Tail(output, 20)}");

        return JsonNode.Parse(output[start..])?.AsObject() ?? throw new ToolException("MSBuild returned no properties.");
    }

    private static async Task<(int ExitCode, string Output)> RunDotnetAsync(IEnumerable<string> args, CancellationToken ct)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new ToolException("Could not start 'dotnet'.");
        var output = new StringBuilder();
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        output.Append(await stdout.ConfigureAwait(false)).Append(await stderr.ConfigureAwait(false));
        return (process.ExitCode, output.ToString());
    }

    private static string Tail(string text, int lines)
        => string.Join(Environment.NewLine, text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(lines));
}
