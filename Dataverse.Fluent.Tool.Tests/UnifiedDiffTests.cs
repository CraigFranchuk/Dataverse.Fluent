using Dataverse.Fluent.Tool.Fixing;

namespace Dataverse.Fluent.Tool.Tests;

public class UnifiedDiffTests
{
    [Fact]
    public void AnInsertedLine_IsOneHunkWithContext()
    {
        var before = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"line {i}")) + "\n";
        var after = before.Replace("line 5\n", "line 5\ninserted\n", StringComparison.Ordinal);

        var diff = UnifiedDiff.Create("Models/A.cs", before, after);

        Assert.Equal("""
            --- Models/A.cs
            +++ Models/A.cs
            @@ -3,6 +3,7 @@
             line 3
             line 4
             line 5
            +inserted
             line 6
             line 7
             line 8

            """.ReplaceLineEndings("\n"), diff);
    }

    [Fact]
    public void DistantChanges_AreSeparateHunks_AndRemovalsComeFirst()
    {
        var lines = Enumerable.Range(1, 20).Select(i => $"line {i}.").ToArray();
        var before = string.Join("\n", lines);
        lines[1] = "changed 2.";
        lines[18] = "changed 19.";
        var after = string.Join("\n", lines);

        var diff = UnifiedDiff.Create("a", before, after);

        Assert.Equal(2, diff.Split('\n').Count(l => l.StartsWith("@@", StringComparison.Ordinal)));
        Assert.Contains("-line 2.\n+changed 2.\n", diff);
        // Line 19 with three lines of context before it and the one line after it.
        Assert.Contains("@@ -16,5 +16,5 @@", diff);
    }

    [Fact]
    public void LineEndings_DoNotCountAsChanges()
        => Assert.Equal("--- a\n+++ a\n", UnifiedDiff.Create("a", "x\r\ny\r\n", "x\ny\n"));
}
