namespace Dataverse.Fluent.IntegrationTests;

/// <summary>
/// Collection definition for Dataverse integration tests.
/// This allows multiple test classes to share the same DataverseTestFixture instance,
/// improving test performance by reusing the authenticated client.
/// </summary>
[CollectionDefinition("Dataverse Integration Tests")]
public class DataverseIntegrationTestCollection : ICollectionFixture<DataverseTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
