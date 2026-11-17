using Dataverse.Fluent.IntegrationTests;

namespace Dataverse.Fluent.Sales.IntegrationTests;

/// <summary>
/// Collection definition for Sales integration tests.
/// Reuses <see cref="DataverseTestFixture"/> from the Dataverse integration test project
/// so all tests share a single authenticated client instance.
/// </summary>
[CollectionDefinition("Dataverse Integration Tests")]
public class SalesIntegrationTestCollection : ICollectionFixture<DataverseTestFixture>
{
}
