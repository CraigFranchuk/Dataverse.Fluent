using System.Linq.Expressions;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Entry point for single-entity operations in Dataverse, returned by <see cref="IDataverseClient.Entity{T}"/>.
/// Targets the record (<see cref="WithId"/>, <see cref="UsingAlternateKey(string, object)"/>) and deletes it;
/// the first read or write method moves the chain to a typed stage:
/// <list type="bullet">
/// <item>Read methods (<c>Select</c>, <c>SelectAll</c>, <c>Expand</c>, <c>IncludeFormattedValues</c>) return an
/// <see cref="EntityReadBuilder{T}"/>, which ends in <c>RetrieveAsync</c> or <c>RetrieveWithMetadataAsync</c>.</item>
/// <item>Write methods (<c>Set</c>, <c>SetLookup*</c>, <c>SetFrom*</c>, <c>WithETag</c>, <c>ReturnRepresentation</c>,
/// <c>ReturnMinimal</c>) return an <see cref="EntityWriteBuilder{T}"/>, which ends in <c>CreateAsync</c>,
/// <c>UpdateAsync</c> or <c>UpsertAsync</c>.</item>
/// </list>
/// Each stage only exposes the options that apply to it, so chains such as <c>Select(...).CreateAsync()</c> do not compile.
/// </summary>
/// <typeparam name="T">The POCO type for the entity. Use [JsonPropertyName("logicalname")] attributes for mapping.</typeparam>
/// <remarks>
/// This type is not thread-safe. Each instance should be used for a single operation and not shared across threads.
/// Terminal methods (e.g., <see cref="RetrieveAsync"/>, <see cref="DeleteAsync"/>,
/// <see cref="EntityWriteBuilder{T}.CreateAsync"/>) mark the operation as consumed; subsequent calls on any stage
/// throw <see cref="InvalidOperationException"/>. Repeated read (or write) calls on the same
/// <see cref="EntityBuilder{T}"/> configure the same read (or write) stage.
/// </remarks>
public sealed class EntityBuilder<T>
{
    private readonly EntityRequest _request;
    private EntityReadBuilder<T>? _read;
    private EntityWriteBuilder<T>? _write;

    internal EntityBuilder(IDataverseTransport client, Guid? id = null, string? entitySet = null)
    {
        _request = new EntityRequest(client, entitySet ?? ODataPropertyNameCache.GetEntitySetName<T>(), id);
    }

    private EntityReadBuilder<T> Read => _read ??= new EntityReadBuilder<T>(_request);

    private EntityWriteBuilder<T> Write => _write ??= new EntityWriteBuilder<T>(_request);

    // ────────── Target ──────────

    /// <summary>
    /// Specifies the entity ID (for Retrieve/Update/Upsert/Delete).
    /// </summary>
    public EntityBuilder<T> WithId(Guid id)
    {
        _request.EntityId = id;
        return this;
    }

    /// <summary>
    /// Uses an alternate key for Retrieve/Update/Upsert/Delete operations. Call once per key attribute for a composite key.
    /// </summary>
    public EntityBuilder<T> UsingAlternateKey(string keyName, object keyValue)
    {
        _request.AddAlternateKey(keyName, keyValue);
        return this;
    }

    /// <summary>
    /// Uses an alternate key for Retrieve/Update/Upsert/Delete operations using a strongly-typed expression.
    /// </summary>
    /// <typeparam name="TProperty">The property type</typeparam>
    /// <param name="propertyExpression">Expression pointing to the property (e.g., x => x.AccountNumber)</param>
    /// <param name="keyValue">The alternate key value</param>
    /// <returns>Current EntityBuilder instance for chaining</returns>
    public EntityBuilder<T> UsingAlternateKey<TProperty>(Expression<Func<T, TProperty>> propertyExpression, TProperty keyValue)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);

        _request.AddAlternateKey(EntityRequest.GetJsonPropertyName(propertyExpression), keyValue!);
        return this;
    }

    /// <summary>
    /// Adds a custom Prefer header value (e.g., "odata.continue-on-error").
    /// </summary>
    public EntityBuilder<T> WithPrefer(string preferValue)
    {
        _request.Prefer.Add(preferValue);
        return this;
    }

    // ────────── Read stage ──────────

    /// <inheritdoc cref="EntityReadBuilder{T}.Select(string[])"/>
    public EntityReadBuilder<T> Select(params string[] properties) => Read.Select(properties);

    /// <inheritdoc cref="EntityReadBuilder{T}.Select{TProjection}(Expression{Func{T, TProjection}})"/>
    public EntityReadBuilder<T> Select<TProjection>(Expression<Func<T, TProjection>> selector) => Read.Select(selector);

    /// <inheritdoc cref="EntityReadBuilder{T}.SelectAll"/>
    public EntityReadBuilder<T> SelectAll() => Read.SelectAll();

    /// <inheritdoc cref="EntityReadBuilder{T}.Expand(string)"/>
    public EntityReadBuilder<T> Expand(string expand) => Read.Expand(expand);

    /// <inheritdoc cref="EntityReadBuilder{T}.Expand{TRelated}(string, Action{ExpandBuilder{T, TRelated}}?)"/>
    public EntityReadBuilder<T> Expand<TRelated>(string navigationProperty, Action<ExpandBuilder<T, TRelated>>? configure = null)
        => Read.Expand(navigationProperty, configure);

    /// <inheritdoc cref="EntityReadBuilder{T}.IncludeFormattedValues(bool)"/>
    public EntityReadBuilder<T> IncludeFormattedValues(bool include = true) => Read.IncludeFormattedValues(include);

    /// <inheritdoc cref="EntityReadBuilder{T}.RetrieveAsync(CancellationToken)"/>
    public Task<T?> RetrieveAsync(CancellationToken ct = default) => Read.RetrieveAsync(ct);

    /// <inheritdoc cref="EntityReadBuilder{T}.RetrieveWithMetadataAsync(CancellationToken)"/>
    public Task<Models.DataverseEntityResult<T>> RetrieveWithMetadataAsync(CancellationToken ct = default)
        => Read.RetrieveWithMetadataAsync(ct);

    // ────────── Write stage ──────────

    /// <inheritdoc cref="EntityWriteBuilder{T}.WithETag(string)"/>
    public EntityWriteBuilder<T> WithETag(string etag) => Write.WithETag(etag);

    /// <inheritdoc cref="EntityWriteBuilder{T}.Set(string, object?)"/>
    public EntityWriteBuilder<T> Set(string logicalName, object? value) => Write.Set(logicalName, value);

    /// <inheritdoc cref="EntityWriteBuilder{T}.Set{TProperty}(Expression{Func{T, TProperty}}, TProperty)"/>
    public EntityWriteBuilder<T> Set<TProperty>(Expression<Func<T, TProperty>> propertyExpression, TProperty? value)
        => Write.Set(propertyExpression, value);

    /// <inheritdoc cref="EntityWriteBuilder{T}.SetLookup(string, string, Guid)"/>
    public EntityWriteBuilder<T> SetLookup(string lookupField, string relatedEntitySet, Guid relatedEntityId)
        => Write.SetLookup(lookupField, relatedEntitySet, relatedEntityId);

    /// <inheritdoc cref="EntityWriteBuilder{T}.SetLookupByAlternateKey(string, string, string, object)"/>
    public EntityWriteBuilder<T> SetLookupByAlternateKey(string lookupField, string relatedEntitySet,
        string alternateKeyName, object alternateKeyValue)
        => Write.SetLookupByAlternateKey(lookupField, relatedEntitySet, alternateKeyName, alternateKeyValue);

    /// <inheritdoc cref="EntityWriteBuilder{T}.SetLookupByAlternateKey{TRelated, TKey}(string, string, Expression{Func{TRelated, TKey}}, TKey)"/>
    public EntityWriteBuilder<T> SetLookupByAlternateKey<TRelated, TKey>(string lookupField, string relatedEntitySet,
        Expression<Func<TRelated, TKey>> keyExpression, TKey keyValue)
        => Write.SetLookupByAlternateKey(lookupField, relatedEntitySet, keyExpression, keyValue);

    /// <inheritdoc cref="EntityWriteBuilder{T}.SetLookupByAlternateKeys(string, string, Dictionary{string, object})"/>
    public EntityWriteBuilder<T> SetLookupByAlternateKeys(string lookupField, string relatedEntitySet,
        Dictionary<string, object> alternateKeys)
        => Write.SetLookupByAlternateKeys(lookupField, relatedEntitySet, alternateKeys);

    /// <inheritdoc cref="EntityWriteBuilder{T}.SetFrom(T)"/>
    public EntityWriteBuilder<T> SetFrom(T entity) => Write.SetFrom(entity);

    /// <inheritdoc cref="EntityWriteBuilder{T}.SetFrom(T, bool)"/>
    public EntityWriteBuilder<T> SetFrom(T entity, bool excludeReadOnly) => Write.SetFrom(entity, excludeReadOnly);

    /// <inheritdoc cref="EntityWriteBuilder{T}.SetFrom(T, string[])"/>
    public EntityWriteBuilder<T> SetFrom(T entity, params string[] exclude) => Write.SetFrom(entity, exclude);

    /// <inheritdoc cref="EntityWriteBuilder{T}.SetFromExcept(T, Expression{Func{T, object}}[])"/>
    public EntityWriteBuilder<T> SetFromExcept(T entity, params Expression<Func<T, object>>[] exclude)
        => Write.SetFromExcept(entity, exclude);

    /// <inheritdoc cref="EntityWriteBuilder{T}.ReturnRepresentation(bool)"/>
    public EntityWriteBuilder<T> ReturnRepresentation(bool returnIt = true) => Write.ReturnRepresentation(returnIt);

    /// <inheritdoc cref="EntityWriteBuilder{T}.ReturnRepresentation{TProjection}(Expression{Func{T, TProjection}})"/>
    public EntityWriteBuilder<T> ReturnRepresentation<TProjection>(Expression<Func<T, TProjection>> selector)
        => Write.ReturnRepresentation(selector);

    /// <inheritdoc cref="EntityWriteBuilder{T}.ReturnMinimal"/>
    public EntityWriteBuilder<T> ReturnMinimal() => Write.ReturnMinimal();

    // ────────── Delete ──────────

    /// <summary>
    /// Deletes the entity identified by <see cref="WithId"/> or <see cref="UsingAlternateKey(string, object)"/>.
    /// </summary>
    public async Task DeleteAsync(CancellationToken ct = default)
    {
        _request.ThrowIfAlreadyExecuted();
        using var response = await KeyedRequestSender.SendAsync(_request.Client, BuildDeleteRequest(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the request <see cref="DeleteAsync"/> would send, without sending it, so it can be queued in a
    /// batch with <see cref="BatchBuilder.Add(DataverseRequest)"/>. The builder is consumed.
    /// </summary>
    public DataverseRequest ToDeleteRequest()
    {
        _request.ThrowIfAlreadyExecuted();
        return BuildDeleteRequest();
    }

    private DataverseRequest BuildDeleteRequest()
    {
        var request = new DataverseRequest(HttpMethod.Delete, _request.BuildPath(requireKey: true));

        // The If-Match/Prefer values set on this stage (WithPrefer) apply to the delete as well.
        var headers = _request.BuildHeaders();
        if (headers != null)
        {
            foreach (var header in headers)
                request.Headers[header.Key] = header.Value;
        }

        return request;
    }
}
