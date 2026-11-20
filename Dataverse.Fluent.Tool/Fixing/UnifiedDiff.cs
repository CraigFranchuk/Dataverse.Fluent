using System.Globalization;
using System.Text;

namespace Dataverse.Fluent.Tool.Fixing;

/// <summary>A unified diff of two versions of a file, with three lines of context, as <c>git diff</c> prints it.</summary>
internal static class UnifiedDiff
{
    private const int Context = 3;

    public static string Create(string path, string before, string after)
    {
        var edits = Diff(Lines(before), Lines(after));

        // Changes closer than twice the context share a hunk.
        var hunks = new List<(int First, int Last)>();
        for (var i = 0; i < edits.Count; i++)
        {
            if (edits[i].Kind == ' ')
                continue;
            if (hunks.Count > 0 && i - hunks[^1].Last <= 2 * Context + 1)
                hunks[^1] = (hunks[^1].First, i);
            else
                hunks.Add((i, i));
        }

        var text = new StringBuilder();
        text.Append("--- ").Append(path).Append('\n');
        text.Append("+++ ").Append(path).Append('\n');
        foreach (var (first, last) in hunks)
        {
            var start = Math.Max(0, first - Context);
            var end = Math.Min(edits.Count, last + Context + 1);
            var hunk = edits.GetRange(start, end - start);
            var preceding = edits.Take(start).ToList();
            var oldStart = preceding.Count(e => e.Kind != '+') + 1;
            var newStart = preceding.Count(e => e.Kind != '-') + 1;

            text.Append(CultureInfo.InvariantCulture, $"@@ -{oldStart},{hunk.Count(e => e.Kind != '+')} +{newStart},{hunk.Count(e => e.Kind != '-')} @@\n");
            foreach (var (kind, line) in hunk)
                text.Append(kind).Append(line).Append('\n');
        }

        return text.ToString();
    }

    private static string[] Lines(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        return lines.Length > 0 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    // The longest common subsequence of lines, walked into ' ', '-' and '+' edits (removals before additions).
    private static List<(char Kind, string Line)> Diff(string[] a, string[] b)
    {
        // Common lines at either end need no table.
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
            prefix++;
        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[^(suffix + 1)] == b[^(suffix + 1)])
            suffix++;

        var x = a[prefix..(a.Length - suffix)];
        var y = b[prefix..(b.Length - suffix)];
        var lengths = new int[x.Length + 1, y.Length + 1];
        for (var i = x.Length - 1; i >= 0; i--)
        {
            for (var j = y.Length - 1; j >= 0; j--)
                lengths[i, j] = x[i] == y[j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        }

        var edits = new List<(char, string)>(a.Length + y.Length);
        edits.AddRange(a[..prefix].Select(l => (' ', l)));
        int p = 0, q = 0;
        while (p < x.Length || q < y.Length)
        {
            if (p < x.Length && q < y.Length && x[p] == y[q])
            {
                edits.Add((' ', x[p++]));
                q++;
            }
            else if (p < x.Length && (q == y.Length || lengths[p + 1, q] >= lengths[p, q + 1]))
            {
                edits.Add(('-', x[p++]));
            }
            else
            {
                edits.Add(('+', y[q++]));
            }
        }

        edits.AddRange(a[(a.Length - suffix)..].Select(l => (' ', l)));
        return edits;
    }
}
