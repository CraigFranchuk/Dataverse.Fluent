using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Dataverse.Fluent.Tests;

/// <summary>
/// Dataverse sets an ARRAffinity cookie on every response, which would send all of a client's requests to one web
/// server. The pipelines the library builds do not store cookies; a handler the caller supplies is left alone.
/// </summary>
public class AffinityCookieTests
{
    private const string BaseUrl = "https://org.crm.dynamics.com/api/data/v9.2";

    [Fact]
    public void OwnedPipeline_DoesNotStoreCookies()
    {
        var handler = DataverseHttpTransport.CreatePrimaryHandler();

        Assert.False(handler.UseCookies);
        Assert.Equal(DecompressionMethods.All, handler.AutomaticDecompression);
    }

    [Theory]
    [InlineData("")]
    [InlineData("target")]
    public void DependencyInjection_DefaultPrimaryHandler_DoesNotStoreCookies(string name)
    {
        var services = new ServiceCollection();
        Action<DataverseClientOptions> configure = o => { o.BaseUrl = BaseUrl; o.TokenProvider = () => Task.FromResult("t"); };
        if (name.Length == 0)
            services.AddDataverseClient(configure);
        else
            services.AddKeyedDataverseClient(name, configure);
        using var provider = services.BuildServiceProvider();

        var primary = PrimaryHandler(provider, name);

        Assert.False(primary switch
        {
            SocketsHttpHandler s => s.UseCookies,
            HttpClientHandler h => h.UseCookies,
            _ => throw new InvalidOperationException($"Unexpected primary handler {primary.GetType()}.")
        });
    }

    [Fact]
    public void DependencyInjection_CallersPrimaryHandler_IsNotChanged()
    {
        var own = new SocketsHttpHandler { UseCookies = true };
        var services = new ServiceCollection();
        services.AddDataverseClient(o => { o.BaseUrl = BaseUrl; o.TokenProvider = () => Task.FromResult("t"); })
            .ConfigurePrimaryHttpMessageHandler(() => own);
        using var provider = services.BuildServiceProvider();

        Assert.Same(own, PrimaryHandler(provider, ""));
        Assert.True(own.UseCookies);
    }

    private static HttpMessageHandler PrimaryHandler(IServiceProvider provider, string name)
    {
        HttpMessageHandler handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(ServiceCollectionExtensions.GetHttpClientName(name));
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;
        return handler;
    }
}
