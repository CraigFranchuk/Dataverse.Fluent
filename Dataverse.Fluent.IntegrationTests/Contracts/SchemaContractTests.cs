using System.Net;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.IntegrationTests.Contracts;

// Dataverse's $select behaviour that the schema validator depends on, as established on 2026-11-20:
// - SelectCheck: the $select is checked before the record is looked up, so a random ID answers 404 when every name
//   can be selected and 400 naming the first one that cannot.
// - ModelChecker (DVS027): a lookup's logical name is not a column of the Web API. A $select accepts it only when a
//   navigation property has the same name (primarycontactid, ownerid, createdby), and then returns nothing for it; it
//   rejects it otherwise (customerid, whose navigation properties are customerid_account and customerid_contact).
//   So the live check cannot find this mistake reliably: the validator reports it from the metadata and leaves the
//   name out of the live check, which would otherwise report some of them a second time as DVS013.
// Every request is a read of a random ID.
[Collection("Dataverse Integration Tests")]
[Trait("Category", "Contract")]
public class SchemaContractTests(DataverseTestFixture fixture)
{
    private async Task<DataverseException> SelectAsync(string entitySet, string select)
        => await Assert.ThrowsAsync<DataverseException>(() => fixture.Client.Transport.ExecuteRequestAsync(
            HttpMethod.Get, $"{entitySet}({Guid.NewGuid()})?$select={select}", ct: TestContext.Current.CancellationToken));

    [Fact]
    public async Task ASelectOfColumnsThatExist_Answers404_ForARandomId()
    {
        ContractTest.RequireOptIn();

        var ex = await SelectAsync("accounts", "name,_primarycontactid_value");

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Theory]
    [InlineData("nosuchcolumn")]
    [InlineData("createdbyname")]
    public async Task ASelectOfANameThatIsNotAColumn_Answers400_NamingIt(string name)
    {
        ContractTest.RequireOptIn();

        var ex = await SelectAsync("accounts", $"name,{name}");

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains($"property named '{name}'", ex.Message);
    }

    [Theory]
    [InlineData("primarycontactid")]
    [InlineData("ownerid")]
    [InlineData("createdby")]
    public async Task ALookupsLogicalName_IsAccepted_WhenANavigationPropertyHasThatName(string name)
    {
        ContractTest.RequireOptIn();

        var ex = await SelectAsync("accounts", name);

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Theory]
    [InlineData("incidents", "customerid")]
    [InlineData("contacts", "parentcustomerid")]
    public async Task ALookupsLogicalName_IsRejected_WhenNoNavigationPropertyHasThatName(string entitySet, string name)
    {
        ContractTest.RequireOptIn();

        var ex = await SelectAsync(entitySet, name);

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains($"property named '{name}'", ex.Message);
    }
}
