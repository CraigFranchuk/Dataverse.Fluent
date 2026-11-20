using PublicApiGenerator;

namespace Dataverse.Fluent.Testing.Tests;

/// <summary>
/// Pins the public API of <c>Dataverse.Fluent.Testing</c>. A change to the public surface fails this test until
/// <c>PublicApi/Dataverse.Fluent.Testing.approved.txt</c> is updated, so that it is deliberate and visible in review.
/// </summary>
public class PublicApiTests
{
    [Fact]
    public void PublicApi_MatchesTheApprovedBaseline()
    {
        var actual = Normalize(typeof(FakeDataverseTransport).Assembly.GeneratePublicApi(new ApiGeneratorOptions
        {
            IncludeAssemblyAttributes = false
        }));

        var directory = Path.Combine(AppContext.BaseDirectory, "PublicApi");
        var approvedPath = Path.Combine(directory, "Dataverse.Fluent.Testing.approved.txt");
        var receivedPath = Path.Combine(directory, "Dataverse.Fluent.Testing.received.txt");
        var approved = File.Exists(approvedPath) ? Normalize(File.ReadAllText(approvedPath)) : string.Empty;

        if (actual == approved)
            return;

        Directory.CreateDirectory(directory);
        File.WriteAllText(receivedPath, actual);

        Assert.Fail(
            "The public API of Dataverse.Fluent.Testing has changed. If the change is intended, copy" + Environment.NewLine +
            "  " + receivedPath + Environment.NewLine +
            "over Dataverse.Fluent.Testing.Tests/PublicApi/Dataverse.Fluent.Testing.approved.txt and record the change in CHANGELOG.md.");
    }

    private static string Normalize(string text) => text.ReplaceLineEndings("\n").Trim() + "\n";
}
