namespace Dataverse.Fluent.Schema.Validation;

/// <summary>Finds the names closest to one that does not exist, for "did you mean" suggestions.</summary>
internal static class NameSuggestions
{
    /// <summary>
    /// "Did you mean 'a'?" or "Did you mean 'a' or 'b'?" for the closest candidates, or <see langword="null"/> when none
    /// is close. A name differing only in case always comes first.
    /// </summary>
    public static string? DidYouMean(string name, IEnumerable<string> candidates)
    {
        var closest = Closest(name, candidates);
        return closest.Count switch
        {
            0 => null,
            1 => $"Did you mean '{closest[0]}'?",
            _ => $"Did you mean {string.Join(", ", closest.Take(closest.Count - 1).Select(c => $"'{c}'"))} or '{closest[^1]}'?"
        };
    }

    /// <summary>Up to three candidates close to <paramref name="name"/>, closest first.</summary>
    public static List<string> Closest(string name, IEnumerable<string> candidates)
    {
        var lower = name.ToLowerInvariant();
        var threshold = Math.Max(2, lower.Length / 3);

        return candidates
            .Distinct(StringComparer.Ordinal)
            .Where(c => !string.Equals(c, name, StringComparison.Ordinal))
            .Select(c => (Name: c, Score: Score(lower, c.ToLowerInvariant(), threshold)))
            .Where(c => c.Score is not null)
            .OrderBy(c => c.Score)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Take(3)
            .Select(c => c.Name)
            .ToList();
    }

    // Lower is closer; null is not close. A name that contains the other (satisfactioncode in
    // customersatisfactioncode) counts as close whatever the edit distance.
    private static int? Score(string name, string candidate, int threshold)
    {
        if (name == candidate)
            return 0;

        var distance = Distance(name, candidate);
        if (distance <= threshold)
            return distance;

        var shorter = Math.Min(name.Length, candidate.Length);
        if (shorter >= 5 && (candidate.Contains(name, StringComparison.Ordinal) || name.Contains(candidate, StringComparison.Ordinal)))
            return threshold + 1 + Math.Abs(name.Length - candidate.Length);

        return null;
    }

    // Levenshtein distance.
    internal static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
