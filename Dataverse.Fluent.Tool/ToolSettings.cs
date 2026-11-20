namespace Dataverse.Fluent.Tool;

/// <summary>What a <c>validate</c> or <c>fix</c> run was asked to do, from the command line.</summary>
internal sealed record ToolSettings
{
    /// <summary>A .csproj, a directory holding one, or a built .dll.</summary>
    public required string Target { get; init; }

    /// <summary>The environment URL (https://org.crm.dynamics.com) or Web API URL (…/api/data/v9.2).</summary>
    public required string Url { get; init; }

    public string ApiVersion { get; init; } = "v9.2";

    public bool Interactive { get; init; }

    public string? TenantId { get; init; }

    /// <summary>Full or simple names of the models to check; all models when empty.</summary>
    public IReadOnlyList<string> Types { get; init; } = [];

    /// <summary>The full name of an <see cref="IDataverseNameResolver"/> in the project to register first.</summary>
    public string? Resolver { get; init; }

    public IReadOnlyList<string> Ignore { get; init; } = [];

    public bool WarningsAsErrors { get; init; }

    public bool NoSelectCheck { get; init; }

    public string Configuration { get; init; } = "Debug";

    public string? Framework { get; init; }

    public bool NoBuild { get; init; }

    /// <summary>For <c>fix</c>: print the changes as a diff and write nothing.</summary>
    public bool DryRun { get; init; }

    /// <summary>Print the stack trace of an unexpected error.</summary>
    public bool Verbose { get; init; }

    /// <summary>For <c>scaffold</c>: the logical name of a table to write a new model for, instead of editing a project's models.</summary>
    public string? Table { get; init; }

    /// <summary>For <c>scaffold --table</c>: the logical names of the columns the model gets.</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>For <c>scaffold --table</c>: the file to write.</summary>
    public string? Output { get; init; }

    /// <summary>For <c>scaffold --table</c>: the namespace of the new model; none when <see langword="null"/>.</summary>
    public string? Namespace { get; init; }

    /// <summary>For <c>scaffold --table</c>: the class name; from the table's schema name when <see langword="null"/>.</summary>
    public string? ClassName { get; init; }

    /// <summary>For <c>scaffold</c>: write an enum for each choice column that is an <c>int</c>.</summary>
    public bool Enums { get; init; }

    /// <summary>The Web API URL: <see cref="Url"/> with <c>/api/data/{version}</c> added unless it already has it.</summary>
    public string WebApiUrl
    {
        get
        {
            var url = Url.TrimEnd('/');
            return url.Contains("/api/data/", StringComparison.OrdinalIgnoreCase) ? url : $"{url}/api/data/{ApiVersion}";
        }
    }
}

/// <summary>A failure the tool reports as a message and exit code 2, rather than a stack trace.</summary>
internal sealed class ToolException : Exception
{
    public ToolException()
    {
    }

    public ToolException(string message)
        : base(message)
    {
    }

    public ToolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The tool's exit codes.</summary>
internal static class ExitCodes
{
    /// <summary>No errors (and no warnings with <c>--warnings-as-errors</c>).</summary>
    public const int Success = 0;

    /// <summary>The models have findings that fail the run.</summary>
    public const int Findings = 1;

    /// <summary>The tool could not run: bad arguments, a failed build, or no access to the environment.</summary>
    public const int Failure = 2;
}
