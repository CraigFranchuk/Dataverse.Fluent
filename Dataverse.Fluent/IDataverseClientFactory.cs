using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dataverse.Fluent;

/// <summary>
/// Creates <see cref="IDataverseClient"/> instances for the Dataverse environments registered with
/// <see cref="ServiceCollectionExtensions.AddDataverseClient(IServiceCollection, Action{DataverseClientOptions}, Action{HttpClient}?)"/>
/// and <see cref="ServiceCollectionExtensions.AddKeyedDataverseClient(IServiceCollection, string, Action{DataverseClientOptions}, Action{HttpClient}?)"/>.
/// </summary>
/// <remarks>
/// Use the factory when the environment is chosen at runtime (e.g. per tenant). When it is known at
/// compile time, inject <c>[FromKeyedServices("name")] IDataverseClient</c> instead.
/// </remarks>
public interface IDataverseClientFactory
{
    /// <summary>Creates a client for the default (unnamed) registration.</summary>
    IDataverseClient CreateClient();

    /// <summary>Creates a client for the registration with the given name.</summary>
    /// <exception cref="InvalidOperationException">No Dataverse client with that name is registered.</exception>
    IDataverseClient CreateClient(string name);
}

/// <summary>
/// Marks a Dataverse client registered in DI under <see cref="Name"/>, using the named
/// <see cref="HttpClient"/> <see cref="HttpClientName"/>.
/// </summary>
internal sealed record DataverseClientRegistration(string Name, string HttpClientName);

internal sealed class DataverseClientFactory : IDataverseClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<DataverseClientOptions> _options;
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, DataverseClientRegistration> _registrations;

    public DataverseClientFactory(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<DataverseClientOptions> options,
        IEnumerable<DataverseClientRegistration> registrations,
        IServiceProvider services)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _services = services;
        _registrations = registrations.ToDictionary(r => r.Name, StringComparer.Ordinal);
    }

    public IDataverseClient CreateClient() => CreateDataverseClient(Options.DefaultName);

    public IDataverseClient CreateClient(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return CreateDataverseClient(name);
    }

    internal DataverseClient CreateDataverseClient(string name)
    {
        if (!_registrations.TryGetValue(name, out var registration))
        {
            var label = name.Length == 0 ? "No default Dataverse client is registered" : $"No Dataverse client named '{name}' is registered";
            throw new InvalidOperationException($"{label}. Register it with AddDataverseClient or AddKeyedDataverseClient.");
        }

        // The options validator runs here, so misconfiguration surfaces with the client name.
        var options = _options.Get(name);
        var httpClient = _httpClientFactory.CreateClient(registration.HttpClientName);
        return new DataverseClient(options.BaseUrl, httpClient, _services.GetService<ILogger<DataverseClient>>(), options.IncludeQueryInTelemetry);
    }
}

/// <summary>
/// Holds one <see cref="DataverseTokenProvider"/> per registered client name so that the token cache
/// survives <see cref="IHttpClientFactory"/> handler rotation.
/// </summary>
internal sealed class DataverseTokenProviderRegistry
{
    private readonly IOptionsMonitor<DataverseClientOptions> _options;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<DataverseTokenProvider>> _providers = new(StringComparer.Ordinal);

    public DataverseTokenProviderRegistry(IOptionsMonitor<DataverseClientOptions> options)
    {
        _options = options;
    }

    public DataverseTokenProvider Get(string name)
        => _providers.GetOrAdd(name, n => new Lazy<DataverseTokenProvider>(() => DataverseTokenProvider.FromOptions(_options.Get(n)))).Value;
}
