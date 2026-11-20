using Dataverse.Fluent.Schema;
using Dataverse.Fluent.Tool.Fixing;

namespace Dataverse.Fluent.Tool;

/// <summary>Writes findings and fix results as plain text, one finding per line with its hint indented below.</summary>
internal static class ReportWriter
{
    private const string Indent = "        ";

    public static void WriteFindings(TextWriter output, IEnumerable<SchemaFinding> findings)
    {
        foreach (var finding in findings)
        {
            output.WriteLine($"{Label(finding.Severity),-8}{finding.Location}  {finding.Code}  {finding.Message}");
            if (finding.Suggestion is not null)
                output.WriteLine(Indent + finding.Suggestion);
            else if (finding.Fix is not null)
                output.WriteLine($"{Indent}fix: {finding.Fix.Description}");
        }
    }

    public static void WriteSummary(TextWriter output, int models, IReadOnlyCollection<SchemaFinding> findings, bool mentionFix)
    {
        var errors = findings.Count(f => f.Severity == SchemaSeverity.Error);
        var warnings = findings.Count(f => f.Severity == SchemaSeverity.Warning);
        var infos = findings.Count - errors - warnings;
        var line = $"{models} model{Plural(models)} checked: {errors} error{Plural(errors)}, {warnings} warning{Plural(warnings)}, {infos} info.";

        var fixable = findings.Count(f => f.Fix is not null);
        if (mentionFix && fixable > 0)
            line += $" {fixable} can be fixed with 'dataverse-fluent fix'.";

        output.WriteLine(line);
    }

    public static void WriteFixes(TextWriter output, FixPlan plan, string? baseDirectory)
    {
        foreach (var group in plan.Outcomes.Where(o => o.Applied).GroupBy(o => o.File!))
        {
            output.WriteLine(Relative(group.Key, baseDirectory));
            foreach (var outcome in group)
                output.WriteLine($"  {outcome.Finding.Location}  {outcome.Finding.Code}  {outcome.Finding.Fix!.Description}");
        }

        var skipped = plan.Outcomes.Where(o => !o.Applied).ToList();
        if (skipped.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Not fixed:");
            foreach (var outcome in skipped)
                output.WriteLine($"  {outcome.Finding.Location}  {outcome.Finding.Code}  {outcome.Finding.Fix!.Description}: {outcome.NotAppliedBecause}");
        }
    }

    public static string Relative(string path, string? baseDirectory)
        => baseDirectory is null ? path : Path.GetRelativePath(baseDirectory, path);

    private static string Label(SchemaSeverity severity) => severity switch
    {
        SchemaSeverity.Error => "error",
        SchemaSeverity.Warning => "warning",
        _ => "info"
    };

    private static string Plural(int count) => count == 1 ? "" : "s";
}
