using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dataverse.Fluent.Tool.Projects;

/// <summary>A built project: the assembly that holds its models, and the source files they are declared in.</summary>
internal sealed record ProjectOutput(string AssemblyPath, IReadOnlyList<string> SourceFiles);

/// <summary>A project's own source, read without building it.</summary>
/// <param name="SourceFiles">The project's source files.</param>
/// <param name="NullableEnabled">Whether nullable reference types are on.</param>
/// <param name="GlobalUsings">The global usings the build adds (the project's <c>Using</c> items, which hold its implicit
/// usings), each as it follows <c>global using</c>: <c>System</c>, <c>static System.Math</c>, <c>Json = System.Text.Json</c>.</param>
/// <param name="Framework">The target framework the project was read for when it has several and none was asked for;
/// otherwise <see langword="null"/>.</param>
internal sealed record ProjectSources(IReadOnlyList<string> SourceFiles, bool NullableEnabled, IReadOnlyList<string> GlobalUsings, string? Framework = null);

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

            var (exit, stdout, stderr) = await RunDotnetAsync(args, ct).ConfigureAwait(false);
            if (exit != 0)
                throw new ToolException($"The build failed:{Environment.NewLine}{Tail(stdout + stderr, 40)}");
        }

        var properties = await QueryAsync(project, settings.Configuration, settings.Framework, ct).ConfigureAwait(false);
        var targetPath = (string?)properties["Properties"]?["TargetPath"];
        if (string.IsNullOrEmpty(targetPath))
        {
            // A multi-targeted project has no TargetPath of its own: use its first framework.
            var first = FirstFramework(properties)
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

        // A multi-targeted project can set these per framework: read them for its first, like the assembly.
        var first = settings.Framework is null ? FirstFramework(properties) : null;
        if (first is not null)
            properties = await QueryAsync(project, settings.Configuration, first, ct).ConfigureAwait(false);

        var nullable = (string?)properties["Properties"]?["Nullable"];
        return new ProjectSources(Sources(project, properties), string.Equals(nullable, "enable", StringComparison.OrdinalIgnoreCase), GlobalUsings(properties), first);
    }

    // The Using items: the implicit usings the SDK adds, and the project's own, less the ones it removes.
    private static List<string> GlobalUsings(JsonObject properties)
    {
        var usings = new List<string>();
        foreach (var item in properties["Items"]?["Using"]?.AsArray() ?? [])
        {
            if ((string?)item?["Identity"] is not { Length: > 0 } name)
                continue;

            var isStatic = string.Equals((string?)item["Static"], "true", StringComparison.OrdinalIgnoreCase);
            var alias = (string?)item["Alias"];
            usings.Add($"{(isStatic ? "static " : "")}{(string.IsNullOrEmpty(alias) ? "" : alias + " = ")}{name}");
        }

        return usings;
    }

    private static string? FirstFramework(JsonObject properties)
        => ((string?)properties["Properties"]?["TargetFrameworks"])?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

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
        var args = new List<string> { "msbuild", project, "-getProperty:TargetPath", "-getProperty:TargetFrameworks", "-getProperty:Nullable", "-getItem:Compile", "-getItem:Using", $"-p:Configuration={configuration}", "-nologo" };
        if (framework is not null)
            args.Add($"-p:TargetFramework={framework}");

        // The answer is on standard output; warnings and notices on standard error are not part of it.
        var (exit, stdout, stderr) = await RunDotnetAsync(args, ct).ConfigureAwait(false);
        return (exit == 0 ? FirstJsonObject(stdout) : null)
            ?? throw new ToolException($"MSBuild could not evaluate {Path.GetFileName(project)}:{Environment.NewLine}{Tail(stdout + stderr, 20)}");
    }

    /// <summary>The first JSON object in <paramref name="text"/>, whatever is printed before or after it.</summary>
    internal static JsonObject? FirstJsonObject(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        for (var start = Array.IndexOf(bytes, (byte)'{'); start >= 0; start = Array.IndexOf(bytes, (byte)'{', start + 1))
        {
            try
            {
                var reader = new Utf8JsonReader(bytes.AsSpan(start));
                if (JsonNode.Parse(ref reader) is JsonObject found)
                    return found;
            }
            catch (JsonException)
            {
                // Not the start of the answer: a brace in a message printed before it.
            }
        }

        return null;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunDotnetAsync(IEnumerable<string> args, CancellationToken ct)
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
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return (process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    private static string Tail(string text, int lines)
        => string.Join(Environment.NewLine, text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(lines));
}
