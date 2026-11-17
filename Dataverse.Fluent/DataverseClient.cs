using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Dataverse.Fluent.Builders;
using Dataverse.Fluent.Models;
using Microsoft.Extensions.Logging;

namespace Dataverse.Fluent;

/// <summary>
/// The main client for interacting with the Dataverse Web API in a fluent, lightweight way.
/// </summary>
/// <remarks>
/// <para>
/// The client is the fluent layer: it creates the builders and runs them over an <see cref="IDataverseTransport"/>.
/// The URL, token and options constructors create a <see cref="DataverseHttpTransport"/> (see its remarks for the
/// resilience and authentication pipeline) and dispose it with the client.
/// </para>
/// <para>
/// <see cref="DataverseClient(IDataverseTransport)"/> runs the same builders over any transport, such as a test
/// fake or a decorator around <see cref="DataverseHttpTransport"/>. The caller keeps ownership of that transport.
/// </para>
/// </remarks>
public class DataverseClient : IDataverseClient, IDisposable
{
    private readonly IDataverseTransport _transport;
    private readonly bool _ownsTransport;
    private bool _disposed;

    /// <inheritdoc />
    public IDataverseTransport Transport => _transport;

    /// <summary>
    /// Initializes a new instance of the DataverseClient with a token delegate.
    /// </summary>
    /// <param name="baseUrl">The Dataverse environment URL (e.g., https://yourorg.api.crm.dynamics.com/api/data/v9.2).</param>
    /// <param name="tokenProvider">Async function to provide a valid Bearer token. Tokens are cached until shortly before their JWT <c>exp</c> claim.</param>
    /// <param name="httpClient">
    /// Optional custom HttpClient. When provided, the caller retains ownership and its pipeline is used
    /// as-is (no resilience is added). When omitted, the client creates a resilient pipeline with default
    /// <see cref="DataverseResilienceOptions"/>.
    /// </param>
    /// <param name="logger">Optional logger for request/response diagnostics.</param>
    public DataverseClient(string baseUrl, Func<Task<string>> tokenProvider, HttpClient? httpClient = null, ILogger<DataverseClient>? logger = null)
        : this(CreateOptions(baseUrl, tokenProvider), httpClient, primaryHandler: null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the DataverseClient authenticated with an Azure <see cref="TokenCredential"/>
    /// (e.g., <c>DefaultAzureCredential</c> or <c>ManagedIdentityCredential</c>).
    /// </summary>
    /// <param name="baseUrl">The Dataverse environment URL.</param>
    /// <param name="credential">The credential; tokens are requested for <c>https://{host}/.default</c> and cached until shortly before they expire.</param>
    /// <param name="httpClient">Optional custom HttpClient (see the token-delegate constructor for ownership and pipeline semantics).</param>
    /// <param name="logger">Optional logger for request/response diagnostics.</param>
    public DataverseClient(string baseUrl, TokenCredential credential, HttpClient? httpClient = null, ILogger<DataverseClient>? logger = null)
        : this(CreateOptions(baseUrl, credential), httpClient, primaryHandler: null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the DataverseClient from <see cref="DataverseClientOptions"/>, including
    /// custom resilience settings.
    /// </summary>
    /// <param name="options">The client options. Exactly one of <see cref="DataverseClientOptions.Credential"/> and <see cref="DataverseClientOptions.TokenProvider"/> must be set.</param>
    /// <param name="httpClient">Optional custom HttpClient (see the token-delegate constructor for ownership and pipeline semantics).</param>
    /// <param name="logger">Optional logger for request/response diagnostics.</param>
    public DataverseClient(DataverseClientOptions options, HttpClient? httpClient = null, ILogger<DataverseClient>? logger = null)
        : this(options, httpClient, primaryHandler: null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the DataverseClient over an existing transport. The fluent builders send
    /// every request through <paramref name="transport"/>.
    /// </summary>
    /// <param name="transport">The transport. The caller retains ownership; disposing the client does not dispose it.</param>
    /// <example>
    /// <code>
    /// // Unit test: run the real builders against a fake transport.
    /// var client = new DataverseClient(new FakeTransport());
    ///
    /// // Decorate the HTTP transport.
    /// using var http = new DataverseHttpTransport(options);
    /// var audited = new DataverseClient(new AuditingTransport(http));
    /// </code>
    /// </example>
    public DataverseClient(IDataverseTransport transport)
        : this(transport ?? throw new ArgumentNullException(nameof(transport)), ownsTransport: false)
    {
    }

    /// <summary>
    /// Creates a client with an owned pipeline over <paramref name="primaryHandler"/> (used by tests), or over
    /// <paramref name="httpClient"/> when supplied.
    /// </summary>
    internal DataverseClient(DataverseClientOptions options, HttpClient? httpClient, HttpMessageHandler? primaryHandler, ILogger? logger)
        : this(new DataverseHttpTransport(options, httpClient, primaryHandler, logger), ownsTransport: true)
    {
    }

    /// <summary>
    /// Internal constructor for the DI path where authentication and resilience are provided by the
    /// <see cref="IHttpClientFactory"/> pipeline.
    /// </summary>
    internal DataverseClient(string baseUrl, HttpClient httpClient, ILogger<DataverseClient>? logger = null, bool includeQueryInTelemetry = false)
        : this(new DataverseHttpTransport(baseUrl, httpClient, logger, includeQueryInTelemetry), ownsTransport: true)
    {
    }

    private DataverseClient(IDataverseTransport transport, bool ownsTransport)
    {
        _transport = transport;
        _ownsTransport = ownsTransport;
    }

    private static DataverseClientOptions CreateOptions(string baseUrl, Func<Task<string>> tokenProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        return new DataverseClientOptions { BaseUrl = baseUrl, TokenProvider = tokenProvider };
    }

    private static DataverseClientOptions CreateOptions(string baseUrl, TokenCredential credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(credential);
        return new DataverseClientOptions { BaseUrl = baseUrl, Credential = credential };
    }

    /// <inheritdoc />
    public QueryBuilder<T> Query<T>(string? entitySet = null) => new(_transport, entitySet);
    /// <inheritdoc />
    public AggregateBuilder<T> Aggregate<T>(string? entitySet = null) => new(_transport, entitySet);
    /// <inheritdoc />
    public EntityBuilder<T> Entity<T>(Guid? id = null, string? entitySet = null) => new(_transport, id, entitySet);
    /// <inheritdoc />
    public BatchBuilder Batch(Guid? batchId = null) => new(_transport, batchId ?? Guid.NewGuid());
    /// <inheritdoc />
    public AnnotationBuilder Annotation(Guid? annotationId = null) => new(_transport, annotationId);

    /// <summary>
    /// Creates a fluent builder for delta (change tracking) queries.
    /// </summary>
    /// <typeparam name="T">The POCO type for the entities.</typeparam>
    /// <param name="entitySet">Optional entity set name (inferred from type name if not provided).</param>
    /// <returns>A delta builder for chaining query options and execution.</returns>
    public DeltaBuilder<T> Delta<T>(string? entitySet = null) => new DeltaBuilder<T>(_transport, entitySet);

    /// <summary>
    /// Creates a fluent builder for executing an unbound action.
    /// </summary>
    /// <typeparam name="TRequest">The request POCO type containing action parameters.</typeparam>
    /// <typeparam name="TResponse">The response POCO type for the action result.</typeparam>
    /// <param name="actionName">The logical name of the action.</param>
    /// <returns>An action builder for chaining parameters and execution.</returns>
    public ActionBuilder<TRequest, TResponse> Action<TRequest, TResponse>(string actionName)
        where TRequest : class
        => new ActionBuilder<TRequest, TResponse>(_transport, actionName);

    /// <summary>
    /// Creates a fluent builder for executing an unbound function.
    /// </summary>
    /// <typeparam name="TResponse">The response POCO type for the function result.</typeparam>
    /// <param name="functionName">The logical name of the function.</param>
    /// <returns>A function builder for chaining parameters and execution.</returns>
    public FunctionBuilder<TResponse> Function<TResponse>(string functionName)
        => new FunctionBuilder<TResponse>(_transport, functionName);

    /// <summary>
    /// Creates a fluent builder for executing bound actions and functions on a specific entity.
    /// </summary>
    /// <typeparam name="T">The POCO type for the entity.</typeparam>
    /// <param name="entityId">The ID of the entity to bind the operation to.</param>
    /// <param name="entitySet">Optional entity set name (inferred from type name if not provided).</param>
    /// <returns>An entity fluent builder for chaining bound operations.</returns>
    public EntityFluentBuilder<T> ForEntity<T>(Guid entityId, string? entitySet = null)
        => new EntityFluentBuilder<T>(_transport, entitySet ?? Builders.ODataPropertyNameCache.GetEntitySetName<T>(), entityId);

    /// <summary>
    /// Creates a fluent FetchXML builder for constructing strongly-typed FetchXML queries.
    /// </summary>
    /// <typeparam name="T">The POCO type to deserialize results into.</typeparam>
    /// <param name="entityLogicalName">Optional entity logical name (inferred from type name if not provided).</param>
    /// <param name="options">Optional FetchXML execution options.</param>
    public FetchBuilder<T> FetchBuilder<T>(string? entityLogicalName = null, FetchXmlOptions? options = null)
        => new FetchBuilder<T>(_transport, entityLogicalName, options);

    /// <summary>
    /// Executes a raw FetchXML query and returns strongly-typed results.
    /// </summary>
    /// <typeparam name="T">The POCO type to deserialize results into.</typeparam>
    /// <param name="fetchXml">The FetchXML query string.</param>
    /// <param name="options">Optional execution options.</param>
    public FetchQueryExecutor<T> Fetch<T>(string fetchXml, FetchXmlOptions? options = null)
        => new(_transport, fetchXml, options ?? FetchXmlOptions.Default);

    /// <summary>
    /// Centralized deserialization of a <see cref="JsonObject"/> to <typeparamref name="T"/>.
    /// Returns <c>default</c> for empty response bodies.
    /// </summary>
    internal static T? DeserializeEntity<T>(JsonObject json)
    {
        if (json.Count == 0)
            return default;

        return json.Deserialize<T>(Builders.JsonDefaults.CaseInsensitive);
    }

    /// <summary>
    /// The OData annotation name used by Dataverse to surface display/formatted values.
    /// </summary>
    private const string FormattedValueAnnotation = "OData.Community.Display.V1.FormattedValue";

    /// <summary>
    /// Deserializes a <see cref="JsonObject"/> into a metadata-aware envelope, surfacing the
    /// <c>@odata.etag</c> annotation and any formatted values without requiring the POCO to
    /// model OData annotations.
    /// </summary>
    internal static DataverseEntityResult<T> DeserializeEntityResult<T>(JsonObject json)
    {
        var entity = DeserializeEntity<T>(json);

        string? etag = null;
        Dictionary<string, string>? formattedValues = null;

        foreach (var property in json)
        {
            if (property.Key == "@odata.etag")
            {
                etag = property.Value?.GetValue<string>();
                continue;
            }

            // Formatted values arrive as "<logicalname>@OData.Community.Display.V1.FormattedValue".
            var annotationIndex = property.Key.IndexOf('@');
            if (annotationIndex > 0 &&
                property.Key.AsSpan(annotationIndex + 1).Equals(FormattedValueAnnotation, StringComparison.Ordinal))
            {
                var logicalName = property.Key[..annotationIndex];
                var value = property.Value?.GetValue<string>();
                if (value != null)
                {
                    formattedValues ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    formattedValues[logicalName] = value;
                }
            }
        }

        IReadOnlyDictionary<string, string> formatted = formattedValues
            ?? (IReadOnlyDictionary<string, string>)EmptyFormattedValues;

        return new DataverseEntityResult<T>(entity, etag, formatted);
    }

    private static readonly Dictionary<string, string> EmptyFormattedValues = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a fluent builder for deep insert operations (creating a parent entity with nested child entities in a single request).
    /// </summary>
    /// <typeparam name="T">The POCO type for the parent entity.</typeparam>
    /// <param name="parentEntity">The parent entity instance to create with nested related entities.</param>
    /// <param name="entitySet">Optional entity set name (inferred from type name if not provided).</param>
    /// <returns>A deep insert builder for chaining related entities and execution.</returns>
    /// <remarks>
    /// Deep insert leverages the Dataverse Web API's ability to create related entities in a single POST operation.
    /// Use <see cref="DeepInsertBuilder{T}.WithRelated{TRelated}(IEnumerable{TRelated}, string)"/> to add related child entities.
    /// </remarks>
    public DeepInsertBuilder<T> DeepInsert<T>(T parentEntity, string? entitySet = null)
        => new DeepInsertBuilder<T>(_transport, parentEntity, entitySet);

    /// <summary>
    /// Creates a fluent builder for associating or disassociating entities via navigation properties.
    /// Supports both collection-valued (N:N, 1:N) and single-valued navigation properties,
    /// with primary key and alternate key addressing for both source and target entities.
    /// </summary>
    /// <returns>An association builder for chaining source, navigation, target, and execution.</returns>
    /// <example>
    /// <code>
    /// // Associate by primary key (N:N)
    /// await client.Associate()
    ///     .From&lt;Account&gt;(accountId)
    ///     .Via("contact_customer_accounts")
    ///     .To&lt;Contact&gt;(contactId)
    ///     .AssociateAsync();
    ///
    /// // Disassociate
    /// await client.Associate()
    ///     .From&lt;Account&gt;(accountId)
    ///     .Via("contact_customer_accounts")
    ///     .To&lt;Contact&gt;(contactId)
    ///     .DisassociateAsync();
    ///
    /// // Associate using alternate key
    /// await client.Associate()
    ///     .From&lt;Account&gt;("accountnumber", "ACC-001")
    ///     .Via("contact_customer_accounts")
    ///     .To&lt;Contact&gt;(contactId)
    ///     .AssociateAsync();
    /// </code>
    /// </example>
    public AssociationBuilder Associate() => new(_transport);


    /// <summary>
    /// Disposes the transport when the client created it.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_ownsTransport && _transport is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }
}
