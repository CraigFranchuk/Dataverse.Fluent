namespace Dataverse.Fluent.IntegrationTests.Contracts;

/// <summary>
/// Contract tests pin the Dataverse behaviours the library depends on. They send raw requests and assert what Dataverse
/// itself returns, so a change on Microsoft's side fails here before it breaks a feature. They are opt-in: set
/// <c>DATAVERSE_RUN_CONTRACT_TESTS=true</c>, before a release and after Dataverse updates.
/// </summary>
internal static class ContractTest
{
    public const string Switch = "DATAVERSE_RUN_CONTRACT_TESTS";

    /// <summary>Skips the test unless contract tests were asked for.</summary>
    public static void RequireOptIn() => Assert.SkipUnless(
        string.Equals(Environment.GetEnvironmentVariable(Switch), "true", StringComparison.OrdinalIgnoreCase),
        $"Contract test: set {Switch}=true to run it.");
}
