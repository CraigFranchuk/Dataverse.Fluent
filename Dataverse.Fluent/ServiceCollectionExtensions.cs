using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Dataverse.Fluent;

/// <summary>
/// Extension methods for registering Dataverse clients with dependency injection via
/// <see cref="IHttpClientFactory"/> for proper handler lifetime management.
/// </summary>
/// <remarks>
/// <para>
/// Each registration gets its own named <see cref="HttpClient"/> whose pipeline is, outermost first:
/// the Dataverse resilience pipeline (see <see cref="DataverseResilienceOptions"/>), then authentication
/// (token caching with refresh on 401), then any handlers you add to the returned builder.
/// </para>
/// <para>
/// The default registration (<c>AddDataverseClient</c>) is resolved as <see cref="IDataverseClient"/> /
/// <see cref="DataverseClient"/>. Additional environments are registered with <c>AddKeyedDataverseClient</c>
/// and resolved with <c>[FromKeyedServices("name")] IDataverseClient</c> or <see cref="IDataverseClientFactory"/>.
/// Options are validated on first use, or at startup when running under a host.
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    internal const string HttpClientNamePrefix = "Dataverse.Fluent";

    internal static string GetHttpClientName(string name)
        => name.Length == 0 ? HttpClientNamePrefix : $"{HttpClientNamePrefix}:{name}";

    /// <summary>
    /// Registers the default Dataverse client with a token delegate.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The Dataverse environment URL (e.g., https://yourorg.api.crm.dynamics.com/api/data/v9.2).</param>
    /// <param name="tokenProvider">Async function to provide a valid Bearer token.</param>
    /// <param name="configureClient">Optional delegate to further configure the <see cref="HttpClient"/> (e.g., default headers).</param>
    /// <param name="configureResilience">
    /// Optional delegate to customize the Dataverse resilience pipeline, which is enabled by default.
    /// Set <see cref="DataverseResilienceOptions.Enabled"/> to <c>false</c> to opt out.
    /// </param>
    /// <returns>An <see cref="IHttpClientBuilder"/> to allow adding handlers.</returns>
    /// <example>
    /// <code>
    /// services.AddDataverseClient(
    ///     "https://yourorg.api.crm.dynamics.com/api/data/v9.2",
    ///     () => GetTokenAsync(),
    ///     configureResilience: options => options.MaxRetryAttempts = 5);
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddDataverseClient(
        this IServiceCollection services,
        string baseUrl,
        Func<Task<string>> tokenProvider,
        Action<HttpClient>? configureClient = null,
        Action<DataverseResilienceOptions>? configureResilience = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(tokenProvider);

        return AddDataverseClientCore(services, Options.DefaultName, options => options.Configure(o =>
        {
            o.BaseUrl = baseUrl;
            o.TokenProvider = tokenProvider;
            configureResilience?.Invoke(o.Resilience);
        }), configureClient);
    }

    /// <summary>
    /// Registers the default Dataverse client with a token delegate resolved from the service provider.
    /// Use this overload when the token provider depends on DI services (e.g., MSAL's IConfidentialClientApplication).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The Dataverse environment URL.</param>
    /// <param name="tokenProviderFactory">Factory that receives <see cref="IServiceProvider"/> and returns the token provider.</param>
    /// <param name="configureClient">Optional delegate to further configure the <see cref="HttpClient"/>.</param>
    /// <param name="configureResilience">Optional delegate to customize the Dataverse resilience pipeline.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> to allow adding handlers.</returns>
    public static IHttpClientBuilder AddDataverseClient(
        this IServiceCollection services,
        string baseUrl,
        Func<IServiceProvider, Func<Task<string>>> tokenProviderFactory,
        Action<HttpClient>? configureClient = null,
        Action<DataverseResilienceOptions>? configureResilience = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(tokenProviderFactory);

        return AddDataverseClientCore(services, Options.DefaultName, options => options.Configure<IServiceProvider>((o, sp) =>
        {
            o.BaseUrl = baseUrl;
            o.TokenProvider = tokenProviderFactory(sp);
            configureResilience?.Invoke(o.Resilience);
        }), configureClient);
    }

    /// <summary>
    /// Registers the default Dataverse client authenticated with an Azure <see cref="TokenCredential"/>
    /// (e.g., <c>DefaultAzureCredential</c> or <c>ManagedIdentityCredential</c>). Tokens are requested for
    /// <c>https://{host}/.default</c> and cached until shortly before they expire.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The Dataverse environment URL.</param>
    /// <param name="credential">The credential used to acquire tokens.</param>
    /// <param name="configureClient">Optional delegate to further configure the <see cref="HttpClient"/>.</param>
    /// <param name="configureResilience">Optional delegate to customize the Dataverse resilience pipeline.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> to allow adding handlers.</returns>
    public static IHttpClientBuilder AddDataverseClient(
        this IServiceCollection services,
        string baseUrl,
        TokenCredential credential,
        Action<HttpClient>? configureClient = null,
        Action<DataverseResilienceOptions>? configureResilience = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(credential);

        return AddDataverseClientCore(services, Options.DefaultName, options => options.Configure(o =>
        {
            o.BaseUrl = baseUrl;
            o.Credential = credential;
            configureResilience?.Invoke(o.Resilience);
        }), configureClient);
    }

    /// <summary>
    /// Registers the default Dataverse client from <see cref="DataverseClientOptions"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the client options (URL, credential, resilience).</param>
    /// <param name="configureClient">Optional delegate to further configure the <see cref="HttpClient"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> to allow adding handlers.</returns>
    public static IHttpClientBuilder AddDataverseClient(
        this IServiceCollection services,
        Action<DataverseClientOptions> configure,
        Action<HttpClient>? configureClient = null)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return AddDataverseClientCore(services, Options.DefaultName, options => options.Configure(configure), configureClient);
    }

    /// <summary>
    /// Registers the default Dataverse client with options bound from configuration
    /// (<c>BaseUrl</c>, <c>Scope</c>, <c>Resilience:*</c>). The credential or token provider, which cannot be
    /// expressed in configuration, is set in <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section to bind, e.g. <c>configuration.GetSection("Dataverse")</c>.</param>
    /// <param name="configure">Optional additional configuration applied after binding (typically setting <see cref="DataverseClientOptions.Credential"/>).</param>
    /// <param name="configureClient">Optional delegate to further configure the <see cref="HttpClient"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> to allow adding handlers.</returns>
    public static IHttpClientBuilder AddDataverseClient(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DataverseClientOptions>? configure = null,
        Action<HttpClient>? configureClient = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return AddDataverseClientCore(services, Options.DefaultName, options => BindAndConfigure(options, configuration, configure), configureClient);
    }

    /// <summary>
    /// Registers an additional, named Dataverse client (e.g. a second environment). Resolve it with
    /// <c>[FromKeyedServices(name)] IDataverseClient</c> or <see cref="IDataverseClientFactory.CreateClient(string)"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The registration name, used as the DI service key. Must not be empty.</param>
    /// <param name="configure">Configures the client options (URL, credential, resilience).</param>
    /// <param name="configureClient">Optional delegate to further configure the <see cref="HttpClient"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> to allow adding handlers.</returns>
    /// <example>
    /// <code>
    /// var credential = new DefaultAzureCredential();
    /// services.AddKeyedDataverseClient("source", o => { o.BaseUrl = sourceUrl; o.Credential = credential; });
    /// services.AddKeyedDataverseClient("target", o => { o.BaseUrl = targetUrl; o.Credential = credential; });
    ///
    /// public class Migrator([FromKeyedServices("source")] IDataverseClient source,
    ///                       [FromKeyedServices("target")] IDataverseClient target) { }
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddKeyedDataverseClient(
        this IServiceCollection services,
        string name,
        Action<DataverseClientOptions> configure,
        Action<HttpClient>? configureClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        return AddDataverseClientCore(services, name, options => options.Configure(configure), configureClient);
    }

    /// <summary>
    /// Registers an additional, named Dataverse client with options bound from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The registration name, used as the DI service key. Must not be empty.</param>
    /// <param name="configuration">The configuration section to bind, e.g. <c>configuration.GetSection("Dataverse:Target")</c>.</param>
    /// <param name="configure">Optional additional configuration applied after binding (typically setting <see cref="DataverseClientOptions.Credential"/>).</param>
    /// <param name="configureClient">Optional delegate to further configure the <see cref="HttpClient"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> to allow adding handlers.</returns>
    public static IHttpClientBuilder AddKeyedDataverseClient(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<DataverseClientOptions>? configure = null,
        Action<HttpClient>? configureClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configuration);
        return AddDataverseClientCore(services, name, options => BindAndConfigure(options, configuration, configure), configureClient);
    }

    private static void BindAndConfigure(
        OptionsBuilder<DataverseClientOptions> options,
        IConfiguration configuration,
        Action<DataverseClientOptions>? configure)
    {
        options.Bind(configuration);
        if (configure != null)
            options.Configure(configure);
    }

    private static IHttpClientBuilder AddDataverseClientCore(
        IServiceCollection services,
        string name,
        Action<OptionsBuilder<DataverseClientOptions>> configureOptions,
        Action<HttpClient>? configureClient)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(d => d.ImplementationInstance is DataverseClientRegistration r && r.Name == name))
        {
            throw new InvalidOperationException(name.Length == 0
                ? "A default Dataverse client is already registered. Use AddKeyedDataverseClient to register additional environments."
                : $"A Dataverse client named '{name}' is already registered.");
        }

        var httpClientName = GetHttpClientName(name);
        services.AddSingleton(new DataverseClientRegistration(name, httpClientName));

        var optionsBuilder = services.AddOptions<DataverseClientOptions>(name);
        configureOptions(optionsBuilder);
        optionsBuilder.ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DataverseClientOptions>, DataverseClientOptionsValidator>());
        services.TryAddSingleton<DataverseTokenProviderRegistry>();
        services.TryAddSingleton<DataverseClientFactory>();
        services.TryAddSingleton<IDataverseClientFactory>(sp => sp.GetRequiredService<DataverseClientFactory>());

        var builder = services.AddHttpClient(httpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                // With resilience enabled, the pipeline's TotalRequestTimeout governs; the default
                // 100-second HttpClient.Timeout would otherwise cut off retries and Retry-After waits.
                if (GetOptions(sp, name).Resilience.Enabled)
                    client.Timeout = Timeout.InfiniteTimeSpan;

                configureClient?.Invoke(client);
            });

        // Resilience is added first so it is the outer handler; authentication is inside it, so every
        // retry attempt is sent with a currently valid token.
        builder.AddResilienceHandler(DataverseResiliencePipeline.PipelineName, (pipeline, context) =>
            DataverseResiliencePipeline.Configure(pipeline, GetOptions(context.ServiceProvider, name).Resilience));

        builder.AddHttpMessageHandler(sp =>
            new DataverseAuthenticationHandler(sp.GetRequiredService<DataverseTokenProviderRegistry>().Get(name)));

        if (name.Length == 0)
        {
            services.AddTransient(sp => sp.GetRequiredService<DataverseClientFactory>().CreateDataverseClient(name));
            services.AddScoped<IDataverseClient>(sp => sp.GetRequiredService<DataverseClient>());
            services.AddScoped(sp => sp.GetRequiredService<IDataverseClient>().Transport);
        }
        else
        {
            services.AddKeyedTransient(name, (sp, _) => sp.GetRequiredService<DataverseClientFactory>().CreateDataverseClient(name));
            services.AddKeyedScoped<IDataverseClient>(name, (sp, _) => sp.GetRequiredKeyedService<DataverseClient>(name));
            services.AddKeyedScoped(name, (sp, _) => sp.GetRequiredKeyedService<IDataverseClient>(name).Transport);
        }

        return builder;
    }

    private static DataverseClientOptions GetOptions(IServiceProvider services, string name)
        => services.GetRequiredService<IOptionsMonitor<DataverseClientOptions>>().Get(name);
}
