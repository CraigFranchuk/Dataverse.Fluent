using System.Text;

namespace Dataverse.Fluent.Schema;

/// <summary>The result of <see cref="DataverseSchemaValidator.ValidateAsync(IDataverseTransport, IEnumerable{Type}, SchemaValidationOptions?, CancellationToken)"/>.</summary>
public sealed class SchemaReport
{
    internal SchemaReport(IReadOnlyList<Type> models, IReadOnlyList<SchemaFinding> findings)
    {
        Models = models;
        Findings = findings;
    }

    /// <summary>The models that were validated, in the order they were checked.</summary>
    public IReadOnlyList<Type> Models { get; }

    /// <summary>The findings, grouped by model in the order of <see cref="Models"/>.</summary>
    public IReadOnlyList<SchemaFinding> Findings { get; }

    /// <summary>Whether any finding is an <see cref="SchemaSeverity.Error"/>.</summary>
    public bool HasErrors => Findings.Any(f => f.Severity == SchemaSeverity.Error);

    /// <summary>The findings at or above <paramref name="minimum"/>.</summary>
    public IEnumerable<SchemaFinding> AtLeast(SchemaSeverity minimum) => Findings.Where(f => f.Severity >= minimum);

    /// <summary>
    /// Throws <see cref="SchemaValidationException"/> when a finding is at or above <paramref name="minimum"/>, so a test
    /// fails with every finding in its message.
    /// </summary>
    /// <param name="minimum">The lowest severity that fails. Defaults to <see cref="SchemaSeverity.Error"/>.</param>
    public void ThrowIfAny(SchemaSeverity minimum = SchemaSeverity.Error)
    {
        var failing = AtLeast(minimum).ToList();
        if (failing.Count > 0)
            throw new SchemaValidationException(failing);
    }

    /// <summary>The findings, one per line, followed by a count.</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var finding in Findings)
            text.Append(Format(finding)).AppendLine();

        text.Append(Summary(Models.Count, Findings));
        return text.ToString();
    }

    internal static string Format(SchemaFinding finding)
        => $"{finding.Severity.ToString().ToUpperInvariant()} {finding}";

    internal static string Summary(int models, IReadOnlyCollection<SchemaFinding> findings)
    {
        var errors = findings.Count(f => f.Severity == SchemaSeverity.Error);
        var warnings = findings.Count(f => f.Severity == SchemaSeverity.Warning);
        var infos = findings.Count - errors - warnings;
        return $"{models} model{(models == 1 ? "" : "s")} checked: {errors} error{(errors == 1 ? "" : "s")}, " +
               $"{warnings} warning{(warnings == 1 ? "" : "s")}, {infos} info.";
    }
}

/// <summary>Thrown by <see cref="SchemaReport.ThrowIfAny"/>: the models do not match the environment.</summary>
public sealed class SchemaValidationException : Exception
{
    /// <summary>Initializes a new instance with no findings.</summary>
    public SchemaValidationException()
        : this([])
    {
    }

    /// <summary>Initializes a new instance with a message and no findings.</summary>
    /// <param name="message">The message.</param>
    public SchemaValidationException(string message)
        : base(message)
    {
        Findings = [];
    }

    /// <summary>Initializes a new instance with a message, an inner exception and no findings.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public SchemaValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Findings = [];
    }

    /// <summary>Initializes a new instance for the given findings.</summary>
    /// <param name="findings">The findings that failed validation.</param>
    public SchemaValidationException(IReadOnlyList<SchemaFinding> findings)
        : base(BuildMessage(findings))
    {
        Findings = findings;
    }

    /// <summary>The findings that failed validation.</summary>
    public IReadOnlyList<SchemaFinding> Findings { get; }

    private static string BuildMessage(IReadOnlyList<SchemaFinding> findings)
    {
        var text = new StringBuilder($"The models do not match the environment ({findings.Count} finding{(findings.Count == 1 ? "" : "s")}):");
        foreach (var finding in findings)
            text.AppendLine().Append("  ").Append(SchemaReport.Format(finding));
        return text.ToString();
    }
}
