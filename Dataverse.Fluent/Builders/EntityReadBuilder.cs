using System.Linq.Expressions;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Fluent builder for retrieving a single entity. Returned by the read methods of <see cref="EntityBuilder{T}"/>
/// (<see cref="EntityBuilder{T}.Select(string[])"/>, <see cref="EntityBuilder{T}.Expand(string)"/>,
/// <see cref="EntityBuilder{T}.IncludeFormattedValues(bool)"/>); it offers only retrieve options and terminals,
/// so a write cannot be chained onto a read.
/// </summary>
/// <typeparam name="T">The POCO type for the entity. Use [JsonPropertyName("logicalname")] attributes for mapping.</typeparam>
/// <remarks>
/// This type is not thread-safe. The terminal methods mark the operation as consumed; subsequent calls throw
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class EntityReadBuilder<T>
{
    private readonly EntityRequest _request;

    private readonly List<string> _selectProperties = [];
    private readonly List<string> _expandClauses = [];

    // Requests formatted (display) values via the Prefer header
    private bool _includeFormattedValues;

    internal EntityReadBuilder(EntityRequest request)
    {
        _request = request;
    }

    /// <summary>
    /// Specifies the ID of the entity to retrieve.
    /// </summary>
    public EntityReadBuilder<T> WithId(Guid id)
    {
        _request.EntityId = id;
        return this;
    }

    /// <summary>
    /// Retrieves the entity by an alternate key. Call once per key attribute for a composite key.
    /// </summary>
    public EntityReadBuilder<T> UsingAlternateKey(string keyName, object keyValue)
    {
        _request.AddAlternateKey(keyName, keyValue);
        return this;
    }

    /// <summary>
    /// Retrieves the entity by an alternate key using a strongly-typed expression.
    /// </summary>
    /// <typeparam name="TProperty">The property type</typeparam>
    /// <param name="propertyExpression">Expression pointing to the property (e.g., x => x.AccountNumber)</param>
    /// <param name="keyValue">The alternate key value</param>
    /// <returns>Current EntityReadBuilder instance for chaining</returns>
    public EntityReadBuilder<T> UsingAlternateKey<TProperty>(Expression<Func<T, TProperty>> propertyExpression, TProperty keyValue)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);

        _request.AddAlternateKey(EntityRequest.GetJsonPropertyName(propertyExpression), keyValue!);
        return this;
    }

    /// <summary>
    /// Adds a custom Prefer header value.
    /// </summary>
    public EntityReadBuilder<T> WithPrefer(string preferValue)
    {
        _request.Prefer.Add(preferValue);
        return this;
    }

    /// <summary>
    /// Specifies which properties to retrieve in the response.
    /// Call with no arguments to retrieve all entity properties (same as not calling Select at all).
    /// </summary>
    /// <param name="properties">Property names (logical names) to select. If empty, no $select clause is added.</param>
    /// <returns>Current EntityReadBuilder instance for chaining</returns>
    /// <example>
    /// <code>
    /// // Select specific properties by logical name
    /// var account = await client.Entity&lt;Account&gt;()
    ///     .WithId(accountId)
    ///     .Select("name", "revenue", "industrycode")
    ///     .RetrieveAsync();
    /// </code>
    /// </example>
    public EntityReadBuilder<T> Select(params string[] properties)
    {
        _selectProperties.Clear();
        if (properties?.Length > 0)
        {
            _selectProperties.AddRange(properties);
        }
        return this;
    }

    /// <summary>
    /// Specifies which properties to retrieve using a strongly-typed expression.
    /// </summary>
    /// <typeparam name="TProjection">The projection type (typically anonymous type)</typeparam>
    /// <param name="selector">Lambda expression selecting properties (e.g., x => new { x.Name, x.Revenue })</param>
    /// <returns>Current EntityReadBuilder instance for chaining</returns>
    /// <example>
    /// <code>
    /// // Select properties using lambda expression
    /// var account = await client.Entity&lt;Account&gt;()
    ///     .WithId(accountId)
    ///     .Select(a => new { a.Name, a.Revenue, a.IndustryCode })
    ///     .RetrieveAsync();
    ///
    /// // Single property
    /// var account = await client.Entity&lt;Account&gt;()
    ///     .WithId(accountId)
    ///     .Select(a => a.Name)
    ///     .RetrieveAsync();
    /// </code>
    /// </example>
    public EntityReadBuilder<T> Select<TProjection>(Expression<Func<T, TProjection>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var propertyNames = EntityRequest.ExtractPropertyNames(selector);
        _selectProperties.Clear();
        _selectProperties.AddRange(propertyNames);

        return this;
    }

    /// <summary>
    /// Selects all properties defined on the generic type T with [JsonPropertyName] attributes.
    /// This is a network optimization to explicitly request only the properties your POCO has,
    /// avoiding retrieval of all 100+ Dataverse system columns.
    /// </summary>
    /// <returns>Current EntityReadBuilder instance for chaining</returns>
    /// <example>
    /// <code>
    /// // Select all properties from Account POCO (not all Dataverse columns)
    /// var account = await client.Entity&lt;Account&gt;()
    ///     .WithId(accountId)
    ///     .SelectAll()
    ///     .RetrieveAsync();
    /// </code>
    /// </example>
    public EntityReadBuilder<T> SelectAll()
    {
        _selectProperties.Clear();
        _selectProperties.AddRange(ODataPropertyNameCache.GetSelectAllProperties<T>());

        return this;
    }

    /// <summary>
    /// Adds a raw $expand clause to the retrieve URL.
    /// </summary>
    /// <param name="expand">Raw OData expand string (e.g., "contact_customer_accounts($select=firstname,lastname)").</param>
    /// <returns>Current EntityReadBuilder instance for chaining</returns>
    public EntityReadBuilder<T> Expand(string expand)
    {
        if (!string.IsNullOrWhiteSpace(expand))
        {
            _expandClauses.Add(expand);
        }
        return this;
    }

    /// <summary>
    /// Adds an $expand clause for a navigation property with nested query options configured via a fluent builder.
    /// </summary>
    /// <typeparam name="TRelated">The related entity type being expanded.</typeparam>
    /// <param name="navigationProperty">The navigation property name (e.g., "contact_customer_accounts").</param>
    /// <param name="configure">Optional action to configure nested $select, $filter, $orderby, $top on the expansion.</param>
    /// <returns>Current EntityReadBuilder instance for chaining</returns>
    /// <example>
    /// <code>
    /// // Expand contacts with nested select
    /// var account = await client.Entity&lt;Account&gt;()
    ///     .WithId(accountId)
    ///     .Expand&lt;Contact&gt;("contact_customer_accounts", e => e
    ///         .Select(c => new { c.FirstName, c.LastName })
    ///         .Top(5))
    ///     .RetrieveAsync();
    /// </code>
    /// </example>
    public EntityReadBuilder<T> Expand<TRelated>(string navigationProperty, Action<ExpandBuilder<T, TRelated>>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(navigationProperty))
            throw new ArgumentException("Navigation property cannot be null or whitespace.", nameof(navigationProperty));

        var builder = new ExpandBuilder<T, TRelated>(navigationProperty);
        configure?.Invoke(builder);
        _expandClauses.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Requests formatted (display) values for the retrieved entity, surfaced via
    /// <see cref="Models.DataverseEntityResult{T}.FormattedValues"/> when using
    /// <see cref="RetrieveWithMetadataAsync"/>.
    /// </summary>
    /// <param name="include">Whether to request formatted values. Defaults to <c>true</c>.</param>
    /// <returns>Current EntityReadBuilder instance for chaining</returns>
    public EntityReadBuilder<T> IncludeFormattedValues(bool include = true)
    {
        _includeFormattedValues = include;
        return this;
    }

    /// <summary>
    /// Retrieves a single entity.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when neither an ID nor an alternate key was specified.</exception>
    public async Task<T?> RetrieveAsync(CancellationToken ct = default)
    {
        _request.ThrowIfAlreadyExecuted();
        var url = BuildUrl();

        // Deserialized straight from the response stream (no intermediate JSON DOM).
        return await RequestSender.ExecuteAsync<T>(_request.Client, HttpMethod.Get, url, BuildHeaders(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves a single entity together with its OData metadata (ETag and, when requested via
    /// <see cref="IncludeFormattedValues(bool)"/>, formatted values).
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// An envelope exposing the deserialized entity, the <c>@odata.etag</c> value for optimistic
    /// concurrency, and any formatted values, without requiring the POCO to model OData annotations.
    /// </returns>
    /// <example>
    /// <code>
    /// // Capture the ETag for optimistic concurrency without leaking annotations into the POCO
    /// var result = await client.Entity&lt;Account&gt;()
    ///     .WithId(accountId)
    ///     .RetrieveWithMetadataAsync();
    ///
    /// await client.Entity&lt;Account&gt;()
    ///     .WithId(accountId)
    ///     .WithETag(result.ETag!)
    ///     .Set(x => x.Revenue, 5000000)
    ///     .UpsertAsync(UpsertConflictResolution.IfUnmodifiedSince);
    /// </code>
    /// </example>
    public async Task<Models.DataverseEntityResult<T>> RetrieveWithMetadataAsync(CancellationToken ct = default)
    {
        _request.ThrowIfAlreadyExecuted();
        var url = BuildUrl();

        var json = await RequestSender.ExecuteJsonAsync(_request.Client, HttpMethod.Get, url, null, BuildHeaders(), ct).ConfigureAwait(false);
        return DataverseClient.DeserializeEntityResult<T>(json);
    }

    private string BuildUrl() => EntityRequest.AppendQueryOptions(_request.BuildPath(requireKey: true), _selectProperties, _expandClauses);

    private Dictionary<string, string>? BuildHeaders() => _request.BuildHeaders(_includeFormattedValues);
}
