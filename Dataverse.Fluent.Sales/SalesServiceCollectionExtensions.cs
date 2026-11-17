using Azure.Core;
using Dataverse.Fluent;
using Microsoft.Extensions.DependencyInjection;

namespace Dataverse.Fluent.Sales;

/// <summary>
/// Extension methods for registering <see cref="SalesClient"/> with dependency injection.
/// </summary>
/// <remarks>
/// Each overload that takes connection settings registers the underlying Dataverse client with the matching
/// <c>AddDataverseClient</c> / <c>AddKeyedDataverseClient</c> overload and returns its <see cref="IHttpClientBuilder"/>.
/// </remarks>
public static class SalesServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SalesClient"/> as a scoped service that wraps an already-registered <see cref="IDataverseClient"/>.
    /// Call <c>AddDataverseClient()</c> first to register the underlying Dataverse client.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddDataverseClient(
    ///     "https://yourorg.api.crm.dynamics.com/api/data/v9.2",
    ///     () => GetTokenAsync());
    ///
    /// services.AddDynamicsSalesClient();
    /// </code>
    /// </example>
    public static IServiceCollection AddDynamicsSalesClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped(sp => new SalesClient(sp.GetRequiredService<IDataverseClient>()));
        return services;
    }

    /// <summary>
    /// Registers both the underlying <see cref="DataverseClient"/> / <see cref="IDataverseClient"/>
    /// and the <see cref="SalesClient"/> in a single call.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The Dataverse environment URL (e.g., https://yourorg.api.crm.dynamics.com/api/data/v9.2).</param>
    /// <param name="tokenProvider">Async function to provide a valid Bearer token.</param>
    /// <param name="configureClient">Optional delegate to further configure the <see cref="HttpClient"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> to allow adding handlers.</returns>
    public static IHttpClientBuilder AddDynamicsSalesClient(
        this IServiceCollection services,
        string baseUrl,
        Func<Task<string>> tokenProvider,
        Action<HttpClient>? configureClient = null)
    {
        var builder = services.AddDataverseClient(baseUrl, tokenProvider, configureClient);
        services.AddDynamicsSalesClient();
        return builder;
    }

    /// <summary>
    /// Registers both the underlying Dataverse client and the <see cref="SalesClient"/>, using a
    /// <paramref name="tokenProviderFactory"/> that resolves from the service provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The Dataverse environment URL.</param>
    /// <param name="tokenProviderFactory">Factory that receives <see cref="IServiceProvider"/> and returns the token provider.</param>
    /// <param name="configureClient">Optional delegate to further configure the <see cref="HttpClient"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> to allow adding handlers.</returns>
    /// <example>
    /// <code>
    /// services.AddDynamicsSalesClient(
    ///     "https://yourorg.api.crm.dynamics.com/api/data/v9.2",
    ///     sp => async () =>
    ///     {
    ///         var app = sp.GetRequiredService&lt;IConfidentialClientApplication&gt;();
    ///         var result = await app.AcquireTokenForClient(scopes).ExecuteAsync();
    ///         return result.AccessToken;
    ///     });
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddDynamicsSalesClient(
        this IServiceCollection services,
        string baseUrl,
        Func<IServiceProvider, Func<Task<string>>> tokenProviderFactory,
        Action<HttpClient>? configureClient = null)
    {
        var builder = services.AddDataverseClient(baseUrl, tokenProviderFactory, configureClient);
        services.AddDynamicsSalesClient();
        return builder;
    }

    /// <summary>
    /// Registers both the underlying Dataverse client, authenticated with an Azure <see cref="TokenCredential"/>
    /// (e.g., <c>DefaultAzureCredential</c> or <c>ManagedIdentityCredential</c>), and the <see cref="SalesClient"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The Dataverse environment URL.</param>
    /// <param name="credential">The credential used to acquire tokens.</param>
    /// <param name="configureClient">Optional delegate to further configure the <see cref="HttpClient"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> to allow adding handlers.</returns>
    public static IHttpClientBuilder AddDynamicsSalesClient(
        this IServiceCollection services,
        string baseUrl,
        TokenCredential credential,
        Action<HttpClient>? configureClient = null)
    {
        var builder = services.AddDataverseClient(baseUrl, credential, configureClient);
        services.AddDynamicsSalesClient();
        return builder;
    }

    /// <summary>
    /// Registers both the underlying Dataverse client, configured from <see cref="DataverseClientOptions"/>
    /// (URL, credential, resilience), and the <see cref="SalesClient"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the client options.</param>
    /// <param name="configureClient">Optional delegate to further configure the <see cref="HttpClient"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> to allow adding handlers.</returns>
    public static IHttpClientBuilder AddDynamicsSalesClient(
        this IServiceCollection services,
        Action<DataverseClientOptions> configure,
        Action<HttpClient>? configureClient = null)
    {
        var builder = services.AddDataverseClient(configure, configureClient);
        services.AddDynamicsSalesClient();
        return builder;
    }

    /// <summary>
    /// Registers a keyed <see cref="SalesClient"/> that wraps the already-registered keyed <see cref="IDataverseClient"/>
    /// of the same name (see <c>AddKeyedDataverseClient</c>). Resolve it with <c>[FromKeyedServices(name)] SalesClient</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The registration name of the Dataverse client.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddKeyedDynamicsSalesClient(this IServiceCollection services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        services.AddKeyedScoped(name, (sp, _) => new SalesClient(sp.GetRequiredKeyedService<IDataverseClient>(name)));
        return services;
    }

    /// <summary>
    /// Registers an additional, named Dataverse client and a keyed <see cref="SalesClient"/> over it (e.g. for a
    /// second environment). Resolve it with <c>[FromKeyedServices(name)] SalesClient</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The registration name, used as the DI service key.</param>
    /// <param name="configure">Configures the client options (URL, credential, resilience).</param>
    /// <param name="configureClient">Optional delegate to further configure the <see cref="HttpClient"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> to allow adding handlers.</returns>
    public static IHttpClientBuilder AddKeyedDynamicsSalesClient(
        this IServiceCollection services,
        string name,
        Action<DataverseClientOptions> configure,
        Action<HttpClient>? configureClient = null)
    {
        var builder = services.AddKeyedDataverseClient(name, configure, configureClient);
        services.AddKeyedDynamicsSalesClient(name);
        return builder;
    }
}
