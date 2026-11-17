namespace Dataverse.Fluent.Builders;

/// <summary>
/// The <c>$select</c>, <c>$filter</c>, <c>$orderby</c> and <c>$expand</c> options of one query. Each option
/// accumulates across calls and is written once: repeated filters are combined with <c>and</c>.
/// </summary>
internal sealed class ODataQueryOptions
{
    public List<string> Select { get; } = [];

    public List<string> Filter { get; } = [];

    public List<string> OrderBy { get; } = [];

    public List<string> Expand { get; } = [];

    /// <summary>
    /// Combines filter clauses with <c>and</c>, parenthesizing each when there is more than one.
    /// Returns <see langword="null"/> when there are none.
    /// </summary>
    public static string? CombineFilters(IReadOnlyList<string> clauses) => clauses.Count switch
    {
        0 => null,
        1 => clauses[0],
        _ => string.Join(" and ", clauses.Select(f => $"({f})"))
    };

    /// <summary>
    /// Appends the options that are set to <paramref name="parts"/> as percent-encoded
    /// <c>name=value</c> pairs.
    /// </summary>
    /// <remarks>
    /// Values are percent-encoded whole: filter literals and nested <c>$expand</c> options can contain
    /// '&amp;', '#' or '+', which would otherwise split or truncate the URL.
    /// </remarks>
    public void AppendTo(List<string> parts)
    {
        if (Select.Count > 0)
            parts.Add("$select=" + Uri.EscapeDataString(string.Join(",", Select)));

        if (CombineFilters(Filter) is { } filter)
            parts.Add("$filter=" + Uri.EscapeDataString(filter));

        if (OrderBy.Count > 0)
            parts.Add("$orderby=" + Uri.EscapeDataString(string.Join(",", OrderBy)));

        if (Expand.Count > 0)
            parts.Add("$expand=" + Uri.EscapeDataString(string.Join(",", Expand)));
    }
}
