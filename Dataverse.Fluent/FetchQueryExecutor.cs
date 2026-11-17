using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent;

/// <summary>
/// Executor for raw FetchXML queries with options for execution and streaming.
/// </summary>
/// <typeparam name="T">The POCO type to deserialize results into.</typeparam>
public sealed class FetchQueryExecutor<T>
{
    private readonly IDataverseTransport _client;
    private readonly string _fetchXml;
    private readonly FetchXmlOptions _options;
    private readonly FetchXmlExecutor<T> _executor;

    internal FetchQueryExecutor(IDataverseTransport client, string fetchXml, FetchXmlOptions options)
        : this(client, fetchXml, options, new FetchXmlExecutor<T>(client))
    {
    }

    private FetchQueryExecutor(IDataverseTransport client, string fetchXml, FetchXmlOptions options, FetchXmlExecutor<T> executor)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _fetchXml = fetchXml ?? throw new ArgumentNullException(nameof(fetchXml));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _executor = executor;
    }

    /// <summary>
    /// Configures execution options for the FetchXML query.
    /// </summary>
    public FetchQueryExecutor<T> WithOptions(FetchXmlOptions options)
    {
        return new FetchQueryExecutor<T>(_client, _fetchXml, options, _executor);
    }

    /// <summary>
    /// Configures execution options using a configuration delegate, starting from the current options.
    /// </summary>
    public FetchQueryExecutor<T> WithOptions(Action<FetchXmlOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new FetchXmlOptions
        {
            NoLock = _options.NoLock,
            IncludeFormattedValues = _options.IncludeFormattedValues,
            MaxPageSize = _options.MaxPageSize
        };
        configure(options);
        
        return new FetchQueryExecutor<T>(_client, _fetchXml, options, _executor);
    }

    /// <summary>
    /// Executes the FetchXML query and returns all results as a list.
    /// </summary>
    public async Task<List<T>> ExecuteAsync(CancellationToken ct = default)
    {
        return await _executor.ExecuteAsync(_fetchXml, _options, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Streams results with automatic pagination for large datasets.
    /// </summary>
    public IAsyncEnumerable<T> StreamAsync(CancellationToken ct = default)
    {
        return _executor.StreamAsync(_fetchXml, _options, ct);
    }
}
