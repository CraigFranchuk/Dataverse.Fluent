using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dataverse.Fluent.Tests;

public class MultiEnvironmentTests
{
    private const string SourceUrl = "https://source.crm.dynamics.com/api/data/v9.2";
    private const string TargetUrl = "https://target.crm.dynamics.com/api/data/v9.2";

    [Fact]
    public async Task KeyedClients_TargetTheirOwnEnvironment_WithTheirOwnCredentials()
    {
        var sourcePrimary = new StubHandler((_, _) => StubHandler.Json());
        var targetPrimary = new StubHandler((_, _) => StubHandler.Json());

        var services = new ServiceCollection();
        services.AddKeyedDataverseClient("source", o => { o.BaseUrl = SourceUrl; o.TokenProvider = () => Task.FromResult("source-token"); })
            .ConfigurePrimaryHttpMessageHandler(() => sourcePrimary);
        services.AddKeyedDataverseClient("target", o => { o.BaseUrl = TargetUrl; o.TokenProvider = () => Task.FromResult("target-token"); })
            .ConfigurePrimaryHttpMessageHandler(() => targetPrimary);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var source = scope.ServiceProvider.GetRequiredKeyedService<IDataverseClient>("source");
        var target = scope.ServiceProvider.GetRequiredKeyedService<IDataverseClient>("target");
        await source.Query<TestAccount>().GetListAsync();
        await target.Query<TestAccount>().GetListAsync();

        Assert.Equal(SourceUrl, source.Transport.BaseUrl);
        Assert.Equal(TargetUrl, target.Transport.BaseUrl);
        Assert.StartsWith(SourceUrl, sourcePrimary.Requests.Single().RequestUri!.ToString());
        Assert.StartsWith(TargetUrl, targetPrimary.Requests.Single().RequestUri!.ToString());
        Assert.Equal(["source-token"], sourcePrimary.AuthorizationTokens);
        Assert.Equal(["target-token"], targetPrimary.AuthorizationTokens);
    }

    [Fact]
    public void DefaultAndKeyedClients_CanBeRegisteredTogether()
    {
        var services = new ServiceCollection();
        services.AddDataverseClient(SourceUrl, () => Task.FromResult("t"));
        services.AddKeyedDataverseClient("target", o => { o.BaseUrl = TargetUrl; o.TokenProvider = () => Task.FromResult("t"); });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Equal(SourceUrl, scope.ServiceProvider.GetRequiredService<IDataverseClient>().Transport.BaseUrl);
        Assert.Equal(TargetUrl, scope.ServiceProvider.GetRequiredKeyedService<IDataverseClient>("target").Transport.BaseUrl);
    }

    [Fact]
    public void Factory_CreatesNamedAndDefaultClients_AndRejectsUnknownNames()
    {
        var services = new ServiceCollection();
        services.AddDataverseClient(o => { o.BaseUrl = SourceUrl; o.TokenProvider = () => Task.FromResult("t"); });
        services.AddKeyedDataverseClient("target", o => { o.BaseUrl = TargetUrl; o.TokenProvider = () => Task.FromResult("t"); });
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDataverseClientFactory>();

        Assert.Equal(SourceUrl, factory.CreateClient().Transport.BaseUrl);
        Assert.Equal(TargetUrl, factory.CreateClient("target").Transport.BaseUrl);
        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient("missing"));
        Assert.Contains("'missing'", ex.Message);
    }

    [Fact]
    public void RegisteringTheSameNameTwice_Throws()
    {
        var services = new ServiceCollection();
        services.AddKeyedDataverseClient("dup", o => o.BaseUrl = SourceUrl);

        Assert.Throws<InvalidOperationException>(() => services.AddKeyedDataverseClient("dup", o => o.BaseUrl = TargetUrl));
    }

    [Fact]
    public void RegisteringTheDefaultClientTwice_Throws()
    {
        var services = new ServiceCollection();
        services.AddDataverseClient(SourceUrl, () => Task.FromResult("t"));

        Assert.Throws<InvalidOperationException>(() => services.AddDataverseClient(TargetUrl, () => Task.FromResult("t")));
    }

    [Fact]
    public void Options_AreBoundFromConfiguration_IncludingResilience()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:Target:BaseUrl"] = TargetUrl,
            ["Dataverse:Target:Resilience:MaxRetryAttempts"] = "7",
            ["Dataverse:Target:Resilience:AttemptTimeout"] = "00:01:00",
            ["Dataverse:Target:Resilience:TotalRequestTimeout"] = "00:05:00"
        }).Build();

        var services = new ServiceCollection();
        services.AddKeyedDataverseClient("target", configuration.GetSection("Dataverse:Target"),
            o => o.TokenProvider = () => Task.FromResult("t"));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<DataverseClientOptions>>().Get("target");
        Assert.Equal(TargetUrl, options.BaseUrl);
        Assert.Equal(7, options.Resilience.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromMinutes(1), options.Resilience.AttemptTimeout);
        Assert.Equal(TargetUrl, provider.GetRequiredService<IDataverseClientFactory>().CreateClient("target").Transport.BaseUrl);
    }

    [Fact]
    public void MissingCredential_FailsValidation_NamingTheClient()
    {
        var services = new ServiceCollection();
        services.AddKeyedDataverseClient("target", o => o.BaseUrl = TargetUrl);
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IDataverseClientFactory>().CreateClient("target"));

        Assert.Contains("Dataverse client 'target'", ex.Message);
        Assert.Contains("Credential or TokenProvider", ex.Message);
    }

    [Fact]
    public void HttpBaseUrl_FailsValidation()
    {
        var services = new ServiceCollection();
        services.AddKeyedDataverseClient("target", o => { o.BaseUrl = "http://insecure.crm.dynamics.com/api/data/v9.2"; o.TokenProvider = () => Task.FromResult("t"); });
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IDataverseClientFactory>().CreateClient("target"));
        Assert.Contains("HTTPS", ex.Message);
    }

    [Fact]
    public void RegisteredHttpClient_HasNoHttpClientTimeout_WhenResilienceIsEnabled()
    {
        var services = new ServiceCollection();
        services.AddKeyedDataverseClient("target", o => { o.BaseUrl = TargetUrl; o.TokenProvider = () => Task.FromResult("t"); });
        using var provider = services.BuildServiceProvider();

        var httpClient = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ServiceCollectionExtensions.GetHttpClientName("target"));

        Assert.Equal(Timeout.InfiniteTimeSpan, httpClient.Timeout);
    }

    [Fact]
    public void ChainedAddDataverseResilienceHandler_ConfiguresTheRegisteredClient()
    {
        var services = new ServiceCollection();
        services.AddDataverseClient(SourceUrl, () => Task.FromResult("t"))
            .AddDataverseResilienceHandler(o => o.MaxRetryAttempts = 9);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<DataverseClientOptions>>().Get(Options.DefaultName);
        Assert.Equal(9, options.Resilience.MaxRetryAttempts);
    }

    [Fact]
    public async Task TokenCredentialRegistration_AuthenticatesWithTheCredential()
    {
        var credential = new FakeTokenCredential(TimeProvider.System, TimeSpan.FromHours(1));
        var primary = new StubHandler((_, _) => StubHandler.Json());
        var services = new ServiceCollection();
        services.AddDataverseClient(SourceUrl, credential).ConfigurePrimaryHttpMessageHandler(() => primary);
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<DataverseClient>().Query<TestAccount>().GetListAsync();
        await provider.GetRequiredService<DataverseClient>().Query<TestAccount>().GetListAsync();

        // One token for both requests: the cache is shared across client instances of the registration.
        Assert.Equal(["credential-token-1", "credential-token-1"], primary.AuthorizationTokens);
        Assert.Equal(["https://source.crm.dynamics.com/.default"], credential.RequestedScopes);
    }
}
