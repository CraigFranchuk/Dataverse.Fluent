using Dataverse.Fluent.IntegrationTests.Models;

namespace Dataverse.Fluent.IntegrationTests;

[Collection("Dataverse Integration Tests")]
public class ConnectionTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public ConnectionTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanConnect_ToDataverse()
    {
        var client = _fixture.Client;
        
        Assert.NotNull(client);
        
        var accounts = await client.Query<Account>()
            .Top(1)
            .GetListAsync();
        
        Assert.NotNull(accounts);
    }

    [Fact]
    public async Task CanExecute_WhoAmIRequest()
    {
        var client = _fixture.Client;
        
        var whoAmIUrl = $"{_fixture.Configuration.BaseUrl}/WhoAmI";
        var request = new HttpRequestMessage(HttpMethod.Get, whoAmIUrl);
        var response = await client.Transport.SendRequestAsync(request);
        
        Assert.True(response.IsSuccessStatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);
        Assert.Contains("UserId", content);
    }

    [Fact]
    public async Task CanRetrieve_SystemUsers()
    {
        var client = _fixture.Client;
        
        var systemUsersUrl = $"{_fixture.Configuration.BaseUrl}/systemusers?$select=systemuserid,fullname&$top=5";
        var request = new HttpRequestMessage(HttpMethod.Get, systemUsersUrl);
        var response = await client.Transport.SendRequestAsync(request);
        
        Assert.True(response.IsSuccessStatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("systemuserid", content);
    }
}
