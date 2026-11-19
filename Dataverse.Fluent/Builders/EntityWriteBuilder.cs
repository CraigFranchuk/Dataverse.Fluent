using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Fluent builder for writing a single entity (Create, Update, Upsert). Returned by the write methods of
/// <see cref="EntityBuilder{T}"/> (<see cref="EntityBuilder{T}.Set(string, object?)"/>,
/// <see cref="EntityBuilder{T}.SetFrom(T)"/>, <see cref="EntityBuilder{T}.WithETag(string)"/>, ...); it offers
/// only payload, response and concurrency options and the write terminals, so a read option such as
/// <c>Select</c> cannot be chained onto a write.
/// </summary>
/// <typeparam name="T">The POCO type for the entity. Use [JsonPropertyName("logicalname")] attributes for mapping.</typeparam>
/// <remarks>
/// This type is not thread-safe. The terminal methods mark the operation as consumed; subsequent calls throw
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class EntityWriteBuilder<T>
{
    private readonly EntityRequest _request;
    private readonly JsonObject _payload = new();

    // Payload properties copied by SetFrom that are subject to [DataverseReadOnly] filtering. They are
    // filtered when the operation is known, because the attribute's rules depend on it.
    private readonly HashSet<string> _setFromProperties = new(StringComparer.Ordinal);

    // ETag for optimistic concurrency control
    private string? _etag;

    // Properties of the returned representation ($select), set by ReturnRepresentation(selector)
    private readonly List<string> _representationSelect = [];

    internal EntityWriteBuilder(EntityRequest request)
    {
        _request = request;
    }

    /// <summary>
    /// Specifies the ID of the entity to update or upsert.
    /// </summary>
    public EntityWriteBuilder<T> WithId(Guid id)
    {
        _request.EntityId = id;
        return this;
    }

    /// <summary>
    /// Updates or upserts the entity by an alternate key. Call once per key attribute for a composite key.
    /// </summary>
    public EntityWriteBuilder<T> UsingAlternateKey(string keyName, object keyValue)
    {
        _request.AddAlternateKey(keyName, keyValue);
        return this;
    }

    /// <summary>
    /// Updates or upserts the entity by an alternate key using a strongly-typed expression.
    /// </summary>
    /// <typeparam name="TProperty">The property type</typeparam>
    /// <param name="propertyExpression">Expression pointing to the property (e.g., x => x.AccountNumber)</param>
    /// <param name="keyValue">The alternate key value</param>
    /// <returns>Current EntityWriteBuilder instance for chaining</returns>
    public EntityWriteBuilder<T> UsingAlternateKey<TProperty>(Expression<Func<T, TProperty>> propertyExpression, TProperty keyValue)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);

        _request.AddAlternateKey(EntityRequest.GetJsonPropertyName(propertyExpression), keyValue!);
        return this;
    }

    /// <summary>
    /// Specifies an ETag value for optimistic concurrency control.
    /// <see cref="UpdateAsync"/> sends it as <c>If-Match</c>; use it with
    /// UpsertAsync(UpsertConflictResolution.IfUnmodifiedSince) to ensure the record
    /// hasn't been modified since it was retrieved.
    /// </summary>
    /// <param name="etag">The ETag value from a previous retrieve operation (from @odata.etag or ETag header)</param>
    /// <returns>Current EntityWriteBuilder instance for chaining</returns>
    public EntityWriteBuilder<T> WithETag(string etag)
    {
        _etag = etag;
        return this;
    }

    /// <summary>
    /// Sets a property value in the payload.
    /// </summary>
    public EntityWriteBuilder<T> Set(string logicalName, object? value)
    {
        // An explicitly set value is always sent, even for a [DataverseReadOnly] property.
        _setFromProperties.Remove(logicalName);

        if (value != null)
        {
            var serialized = JsonSerializer.SerializeToNode(value, value.GetType(), JsonDefaults.PayloadSerialization);
            _payload[logicalName] = serialized;
        }
        else
        {
            _payload[logicalName] = null; // Explicit null for clearing fields
        }
        return this;
    }

    /// <summary>
    /// Sets a property value in the payload using a strongly-typed expression.
    /// </summary>
    /// <typeparam name="TProperty">The property type</typeparam>
    /// <param name="propertyExpression">Expression pointing to the property (e.g., x => x.Name)</param>
    /// <param name="value">The value to set (use null to clear a field)</param>
    /// <returns>Current EntityWriteBuilder instance for chaining</returns>
    /// <remarks>
    /// A <see cref="DataverseLookupAttribute"/> property is written as <c>{navigation}@odata.bind</c>; a
    /// <see langword="null"/> or <see cref="Guid.Empty"/> value clears the lookup.
    /// </remarks>
    /// <exception cref="ArgumentException">The property holds related records (a navigation property).</exception>
    public EntityWriteBuilder<T> Set<TProperty>(Expression<Func<T, TProperty>> propertyExpression, TProperty? value)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);

        var property = Expressions.MemberPathExtractor.GetProperty(propertyExpression);
        var member = EntityTypeMetadata.For(typeof(T)).Find(property);

        switch (member?.Kind)
        {
            case EntityMemberKind.Lookup:
                var target = value is Guid id && id != Guid.Empty
                    ? DataverseColumnResolver.FormatBind(member.Lookup!.TargetEntitySet, id)
                    : null;
                return Set(member.WriteName, target);

            case EntityMemberKind.Navigation:
                throw new ArgumentException(
                    $"{typeof(T).Name}.{property.Name} holds related records, which cannot be set as a value. " +
                    "Use SetLookup to point a lookup at an existing record, or Associate() to link records.",
                    nameof(propertyExpression));

            default:
                return Set(EntityRequest.GetJsonPropertyName(propertyExpression), value);
        }
    }

    /// <summary>
    /// Sets a lookup field to reference an entity by its ID.
    /// </summary>
    /// <param name="lookupField">The lookup field name (e.g., "parentcustomerid_account")</param>
    /// <param name="relatedEntitySet">The related entity set name (e.g., "accounts")</param>
    /// <param name="relatedEntityId">The ID of the related entity</param>
    /// <returns>Current EntityWriteBuilder instance for chaining</returns>
    /// <example>
    /// <code>
    /// // Create contact linked to account
    /// await client.Entity&lt;Contact&gt;()
    ///     .Set(c => c.FirstName, "John")
    ///     .Set(c => c.LastName, "Doe")
    ///     .SetLookup("parentcustomerid_account", "accounts", parentAccountId)
    ///     .CreateAsync();
    /// </code>
    /// </example>
    public EntityWriteBuilder<T> SetLookup(string lookupField, string relatedEntitySet, Guid relatedEntityId)
    {
        if (string.IsNullOrWhiteSpace(lookupField))
            throw new ArgumentNullException(nameof(lookupField));
        if (string.IsNullOrWhiteSpace(relatedEntitySet))
            throw new ArgumentNullException(nameof(relatedEntitySet));

        var bindingValue = $"/{relatedEntitySet}({relatedEntityId})";
        return Set($"{lookupField}@odata.bind", bindingValue);
    }

    /// <summary>
    /// Sets a lookup field to reference an entity by its alternate key.
    /// </summary>
    /// <param name="lookupField">The lookup field name (e.g., "parentcustomerid_account")</param>
    /// <param name="relatedEntitySet">The related entity set name (e.g., "accounts")</param>
    /// <param name="alternateKeyName">The alternate key attribute name</param>
    /// <param name="alternateKeyValue">The alternate key value</param>
    /// <returns>Current EntityWriteBuilder instance for chaining</returns>
    /// <example>
    /// <code>
    /// // Create contact linked to account by account number
    /// await client.Entity&lt;Contact&gt;()
    ///     .Set(c => c.FirstName, "John")
    ///     .Set(c => c.LastName, "Doe")
    ///     .SetLookupByAlternateKey("parentcustomerid_account", "accounts",
    ///         "accountnumber", "ACC-001")
    ///     .CreateAsync();
    /// </code>
    /// </example>
    public EntityWriteBuilder<T> SetLookupByAlternateKey(string lookupField, string relatedEntitySet,
        string alternateKeyName, object alternateKeyValue)
    {
        if (string.IsNullOrWhiteSpace(lookupField))
            throw new ArgumentNullException(nameof(lookupField));
        if (string.IsNullOrWhiteSpace(relatedEntitySet))
            throw new ArgumentNullException(nameof(relatedEntitySet));
        if (string.IsNullOrWhiteSpace(alternateKeyName))
            throw new ArgumentNullException(nameof(alternateKeyName));

        var bindingValue = $"/{relatedEntitySet}({AlternateKeyFormatter.FormatKeyValue(alternateKeyName, alternateKeyValue, KeyPlacement.JsonBody)})";
        return Set($"{lookupField}@odata.bind", bindingValue);
    }

    /// <summary>
    /// Sets a lookup field to reference an entity by its alternate key using strongly-typed expressions.
    /// </summary>
    /// <typeparam name="TRelated">The type of the related entity</typeparam>
    /// <typeparam name="TKey">The type of the alternate key property</typeparam>
    /// <param name="lookupField">The lookup field name (e.g., "parentcustomerid_account")</param>
    /// <param name="relatedEntitySet">The related entity set name (e.g., "accounts")</param>
    /// <param name="keyExpression">Expression pointing to the alternate key property</param>
    /// <param name="keyValue">The alternate key value</param>
    /// <returns>Current EntityWriteBuilder instance for chaining</returns>
    /// <example>
    /// <code>
    /// // Create contact linked to account by account number (type-safe)
    /// await client.Entity&lt;Contact&gt;()
    ///     .Set(c => c.FirstName, "John")
    ///     .Set(c => c.LastName, "Doe")
    ///     .SetLookupByAlternateKey&lt;Account, string&gt;("parentcustomerid_account", "accounts",
    ///         a => a.AccountNumber, "ACC-001")
    ///     .CreateAsync();
    /// </code>
    /// </example>
    public EntityWriteBuilder<T> SetLookupByAlternateKey<TRelated, TKey>(string lookupField, string relatedEntitySet,
        Expression<Func<TRelated, TKey>> keyExpression, TKey keyValue)
    {
        if (string.IsNullOrWhiteSpace(lookupField))
            throw new ArgumentNullException(nameof(lookupField));
        if (string.IsNullOrWhiteSpace(relatedEntitySet))
            throw new ArgumentNullException(nameof(relatedEntitySet));
        ArgumentNullException.ThrowIfNull(keyExpression);

        var keyName = EntityRequest.GetJsonPropertyName(keyExpression);
        return SetLookupByAlternateKey(lookupField, relatedEntitySet, keyName, keyValue!);
    }

    /// <summary>
    /// Sets a lookup field to reference an entity by multiple alternate keys (composite key).
    /// </summary>
    /// <param name="lookupField">The lookup field name (e.g., "parentcustomerid_account")</param>
    /// <param name="relatedEntitySet">The related entity set name (e.g., "accounts")</param>
    /// <param name="alternateKeys">Dictionary of alternate key names and values</param>
    /// <returns>Current EntityWriteBuilder instance for chaining</returns>
    /// <example>
    /// <code>
    /// // Create record linked to entity with composite key
    /// await client.Entity&lt;OrderLine&gt;()
    ///     .Set(ol => ol.Quantity, 5)
    ///     .SetLookupByAlternateKeys("productid", "products",
    ///         new Dictionary&lt;string, object&gt;
    ///         {
    ///             { "dataareaid", "USMF" },
    ///             { "itemnumber", "A0001" }
    ///         })
    ///     .CreateAsync();
    /// </code>
    /// </example>
    public EntityWriteBuilder<T> SetLookupByAlternateKeys(string lookupField, string relatedEntitySet,
        Dictionary<string, object> alternateKeys)
    {
        if (string.IsNullOrWhiteSpace(lookupField))
            throw new ArgumentNullException(nameof(lookupField));
        if (string.IsNullOrWhiteSpace(relatedEntitySet))
            throw new ArgumentNullException(nameof(relatedEntitySet));
        if (alternateKeys == null || alternateKeys.Count == 0)
            throw new ArgumentException("At least one alternate key must be specified.", nameof(alternateKeys));

        var bindingValue = $"/{relatedEntitySet}({AlternateKeyFormatter.FormatCompositeKey(alternateKeys, KeyPlacement.JsonBody)})";
        return Set($"{lookupField}@odata.bind", bindingValue);
    }

    /// <summary>
    /// Sets all properties from an entity instance.
    /// Uses JsonSerializer to respect [JsonPropertyName] attributes and ignore conditions.
    /// Properties marked with [DataverseReadOnly] are excluded according to the operation that is executed:
    /// <see cref="CreateAsync"/> and a create-only upsert (<see cref="UpsertConflictResolution.PreventUpdate"/>)
    /// leave out properties marked for Create. Every operation that can update a record
    /// (<see cref="UpdateAsync"/> and the other upserts) leaves out properties marked for Create or Update, so
    /// a create-only marker such as the one on <c>statecode</c> never sends a default value over an existing record.
    /// Values set explicitly with <see cref="Set(string, object?)"/> are always sent.
    /// </summary>
    public EntityWriteBuilder<T> SetFrom(T entity)
    {
        return SetFrom(entity, excludeReadOnly: true);
    }

    /// <summary>
    /// Sets all properties from an entity instance with control over read-only property handling.
    /// </summary>
    /// <param name="entity">The entity instance to copy properties from</param>
    /// <param name="excludeReadOnly">If true, excludes properties marked with [DataverseReadOnly] attribute
    /// for the operation that is executed (see <see cref="SetFrom(T)"/>)</param>
    /// <returns>Current EntityWriteBuilder instance for chaining</returns>
    public EntityWriteBuilder<T> SetFrom(T entity, bool excludeReadOnly)
    {
        return CopyFrom(entity, excludeReadOnly, exclude: null);
    }

    /// <summary>
    /// Sets properties from an entity instance, excluding specific properties by name. Properties marked
    /// with [DataverseReadOnly] are also excluded (see <see cref="SetFrom(T)"/>).
    /// </summary>
    /// <param name="entity">The entity instance to copy properties from</param>
    /// <param name="exclude">Property names (logical names) to exclude</param>
    /// <returns>Current EntityWriteBuilder instance for chaining</returns>
    public EntityWriteBuilder<T> SetFrom(T entity, params string[] exclude)
    {
        ArgumentNullException.ThrowIfNull(exclude);

        // A lookup is written as a bind, so its logical or read name excludes its bind name too.
        var excluded = new HashSet<string>(exclude, StringComparer.OrdinalIgnoreCase);
        foreach (var member in EntityTypeMetadata.For(typeof(T)).Members)
        {
            if (excluded.Contains(member.LogicalName) || excluded.Contains(member.Name))
                excluded.Add(member.WriteName);
        }

        return CopyFrom(entity, excludeReadOnly: true, excluded);
    }

    /// <summary>
    /// Sets properties from an entity instance, excluding specific properties by expression. Properties
    /// marked with [DataverseReadOnly] are also excluded (see <see cref="SetFrom(T)"/>).
    /// </summary>
    /// <param name="entity">The entity instance to copy properties from</param>
    /// <param name="exclude">Expressions pointing to properties to exclude</param>
    /// <returns>Current EntityWriteBuilder instance for chaining</returns>
    public EntityWriteBuilder<T> SetFromExcept(T entity, params Expression<Func<T, object>>[] exclude)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var metadata = EntityTypeMetadata.For(typeof(T));
        var excludedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var expr in exclude)
        {
            // A lookup is written under its bind name, not the _x_value name it is read by.
            excludedNames.Add(metadata.Find(Expressions.MemberPathExtractor.GetProperty(expr)) is { } member
                ? member.WriteName
                : EntityRequest.GetJsonPropertyName(expr));
        }

        return CopyFrom(entity, excludeReadOnly: true, excludedNames);
    }

    private EntityWriteBuilder<T> CopyFrom(T entity, bool excludeReadOnly, HashSet<string>? exclude)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var serialized = JsonSerializer.SerializeToNode(entity, JsonDefaults.EntityPayload);
        if (serialized is JsonObject obj)
        {
            foreach (var prop in obj)
            {
                if (exclude?.Contains(prop.Key) == true)
                    continue;

                _payload[prop.Key] = prop.Value?.DeepClone();

                if (excludeReadOnly)
                    _setFromProperties.Add(prop.Key);
                else
                    _setFromProperties.Remove(prop.Key);
            }
        }

        return this;
    }

    // Removes the properties copied by SetFrom that are marked [DataverseReadOnly] for any of the operations.
    // Operations that can update a record pass both Create and Update (see SetFrom(T)).
    private void RemoveReadOnlyProperties(params DataverseOperation[] operations)
    {
        if (_setFromProperties.Count == 0)
            return;

        foreach (var operation in operations)
        {
            var readOnly = PropertyFilter.GetReadOnlyProperties<T>(operation);
            foreach (var name in _setFromProperties)
            {
                if (readOnly.Contains(name))
                    _payload.Remove(name);
            }
        }
    }

    /// <summary>
    /// Requests the full entity representation in the response.
    /// Mutually exclusive with <see cref="ReturnMinimal"/>; last call wins.
    /// </summary>
    public EntityWriteBuilder<T> ReturnRepresentation(bool returnIt = true)
    {
        _representationSelect.Clear();
        if (returnIt)
        {
            _request.Prefer.RemoveAll(p => p.StartsWith("return=", StringComparison.Ordinal));
            _request.Prefer.Add("return=representation");
        }
        else
        {
            _request.Prefer.Remove("return=representation");
        }
        return this;
    }

    /// <summary>
    /// Requests the entity representation in the response, limited to the selected properties ($select).
    /// Mutually exclusive with <see cref="ReturnMinimal"/>; last call wins.
    /// </summary>
    /// <typeparam name="TProjection">The projection type (typically anonymous type)</typeparam>
    /// <param name="selector">Lambda expression selecting properties (e.g., x => new { x.Name, x.Revenue })</param>
    /// <returns>Current EntityWriteBuilder instance for chaining</returns>
    /// <example>
    /// <code>
    /// var updated = await client.Entity&lt;Account&gt;()
    ///     .WithId(accountId)
    ///     .Set(a => a.Name, "Contoso")
    ///     .ReturnRepresentation(a => new { a.Name, a.ModifiedOn })
    ///     .UpdateAsync();
    /// </code>
    /// </example>
    public EntityWriteBuilder<T> ReturnRepresentation<TProjection>(Expression<Func<T, TProjection>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        ReturnRepresentation();
        _representationSelect.AddRange(EntityRequest.ExtractPropertyNames(selector));
        return this;
    }

    /// <summary>
    /// Requests a minimal response (HTTP 204 No Content) for write operations, reducing response payload.
    /// Mutually exclusive with <see cref="ReturnRepresentation(bool)"/>; last call wins.
    /// </summary>
    public EntityWriteBuilder<T> ReturnMinimal()
    {
        _representationSelect.Clear();
        _request.Prefer.RemoveAll(p => p.StartsWith("return=", StringComparison.Ordinal));
        _request.Prefer.Add("return=minimal");
        return this;
    }

    /// <summary>
    /// Adds a custom Prefer header value (e.g., "odata.continue-on-error").
    /// </summary>
    public EntityWriteBuilder<T> WithPrefer(string preferValue)
    {
        _request.Prefer.Add(preferValue);
        return this;
    }

    /// <summary>
    /// Creates a new entity.
    /// </summary>
    /// <returns>
    /// The new record's <see cref="Models.DataverseCreateResult{T}.Id"/> (from the <c>OData-EntityId</c>
    /// response header) and, when <see cref="ReturnRepresentation(bool)"/> was requested, the created
    /// <see cref="Models.DataverseCreateResult{T}.Entity"/>. Dataverse omits the <c>OData-EntityId</c>
    /// header when a representation is returned, so read the key from the entity in that case.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when an ID or alternate key was specified.</exception>
    public async Task<Models.DataverseCreateResult<T>> CreateAsync(CancellationToken ct = default)
    {
        _request.ThrowIfAlreadyExecuted();
        var request = BuildCreateRequest();

        using var response = await KeyedRequestSender.SendAsync(_request.Client, request, ct).ConfigureAwait(false);

        // Dataverse returns the new record's URL in OData-EntityId, and the record itself only when a
        // representation was requested.
        var entity = await response.ReadAsync<T>(ct).ConfigureAwait(false);
        return new Models.DataverseCreateResult<T>(response.EntityId, entity, response.EntityUrl);
    }

    /// <summary>
    /// Updates an existing entity (PATCH). The update never creates a record: it fails with a
    /// <see cref="Models.DataverseException"/> (404) when the record does not exist.
    /// </summary>
    /// <remarks>
    /// A Dataverse PATCH without an <c>If-Match</c> header is an upsert, so this method always sends
    /// <c>If-Match</c>: the ETag supplied via <see cref="WithETag"/> (optimistic concurrency, 412 on
    /// mismatch) or <c>*</c> otherwise. Use <see cref="UpsertAsync"/> to create-or-update.
    /// </remarks>
    /// <returns>The updated entity when <see cref="ReturnRepresentation(bool)"/> was requested, otherwise <see langword="null"/>.</returns>
    public async Task<T?> UpdateAsync(CancellationToken ct = default)
    {
        _request.ThrowIfAlreadyExecuted();
        var request = BuildUpdateRequest();

        using var response = await KeyedRequestSender.SendAsync(_request.Client, request, ct).ConfigureAwait(false);
        return await response.ReadEntityAsync<T>(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Upserts an entity with configurable conflict resolution strategy.
    /// </summary>
    /// <param name="conflictResolution">Strategy for handling create/update conflicts. Default is standard upsert behavior.</param>
    /// <param name="ct">Optional cancellation token</param>
    /// <returns>
    /// The record's ID (from the <c>OData-EntityId</c> header), the entity when
    /// <see cref="ReturnRepresentation(bool)"/> was requested, and whether the upsert created the record.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when IfUnmodifiedSince is used without calling WithETag() first</exception>
    /// <exception cref="Models.DataverseException">Thrown when the operation fails (e.g., 412 Precondition Failed for conflicts)</exception>
    /// <remarks>
    /// <para><b>Conflict Resolution Strategies:</b></para>
    /// <list type="bullet">
    /// <item><b>Upsert (default):</b> Creates if doesn't exist, updates if exists</item>
    /// <item><b>PreventUpdate:</b> Only creates; fails with 412 if record exists</item>
    /// <item><b>PreventCreate:</b> Only updates; fails with 404 if record doesn't exist</item>
    /// <item><b>IfUnmodifiedSince:</b> Updates only if ETag matches; requires WithETag()</item>
    /// </list>
    /// <para><b>Example Usage:</b></para>
    /// <code>
    /// // Standard upsert by alternate key; the result carries the record's ID.
    /// var result = await client.Entity&lt;Account&gt;()
    ///     .UsingAlternateKey(x => x.AccountNumber, "ACC-001")
    ///     .SetFrom(account)
    ///     .UpsertAsync();
    /// Guid? id = result.Id;
    ///
    /// // Prevent overwriting existing records
    /// await client.Entity&lt;Account&gt;()
    ///     .WithId(accountId)
    ///     .SetFrom(account)
    ///     .UpsertAsync(UpsertConflictResolution.PreventUpdate);
    ///
    /// // Optimistic concurrency
    /// var existing = await client.Entity&lt;Account&gt;().WithId(id).RetrieveWithMetadataAsync();
    /// await client.Entity&lt;Account&gt;()
    ///     .WithId(accountId)
    ///     .WithETag(existing.ETag!)
    ///     .Set(x => x.Revenue, 5000000)
    ///     .UpsertAsync(UpsertConflictResolution.IfUnmodifiedSince);
    /// </code>
    /// </remarks>
    public async Task<Models.DataverseUpsertResult<T>> UpsertAsync(
        UpsertConflictResolution conflictResolution = UpsertConflictResolution.Upsert,
        CancellationToken ct = default)
    {
        _request.ThrowIfAlreadyExecuted();
        var request = BuildUpsertRequest(conflictResolution);

        using var response = await KeyedRequestSender.SendAsync(_request.Client, request, ct).ConfigureAwait(false);
        var entity = await response.ReadEntityAsync<T>(ct).ConfigureAwait(false);

        // With a representation Dataverse answers 201 for a create and 200 for an update; without one,
        // both are 204 and the outcome is unknown.
        bool? created = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Created => true,
            System.Net.HttpStatusCode.OK => false,
            _ => null
        };

        return new Models.DataverseUpsertResult<T>(response.EntityId, entity, response.EntityUrl, created);
    }

    // ────────── Requests for batches ──────────

    /// <summary>
    /// Builds the request <see cref="CreateAsync"/> would send, without sending it, so it can be queued in a
    /// batch with <see cref="BatchBuilder.Add(DataverseRequest)"/>. The builder is consumed.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when an ID or alternate key was specified.</exception>
    public DataverseRequest ToCreateRequest()
    {
        _request.ThrowIfAlreadyExecuted();
        return BuildCreateRequest();
    }

    /// <summary>
    /// Builds the request <see cref="UpdateAsync"/> would send, without sending it, so it can be queued in a
    /// batch with <see cref="BatchBuilder.Add(DataverseRequest)"/>. The builder is consumed.
    /// </summary>
    public DataverseRequest ToUpdateRequest()
    {
        _request.ThrowIfAlreadyExecuted();
        return BuildUpdateRequest();
    }

    /// <summary>
    /// Builds the request <see cref="UpsertAsync"/> would send, without sending it, so it can be queued in a
    /// batch with <see cref="BatchBuilder.Add(DataverseRequest)"/>. The builder is consumed.
    /// </summary>
    /// <param name="conflictResolution">Strategy for handling create/update conflicts.</param>
    public DataverseRequest ToUpsertRequest(UpsertConflictResolution conflictResolution = UpsertConflictResolution.Upsert)
    {
        _request.ThrowIfAlreadyExecuted();
        return BuildUpsertRequest(conflictResolution);
    }

    private DataverseRequest BuildCreateRequest()
    {
        if (_request.EntityId.HasValue)
            throw new InvalidOperationException("ID should not be set for Create.");
        if (_request.HasAlternateKey)
            throw new InvalidOperationException("Alternate key should not be set for Create. Use UpsertAsync to create a record by alternate key.");

        RemoveReadOnlyProperties(DataverseOperation.Create);
        return CreateRequest(HttpMethod.Post, BuildUrl(), ifMatch: null, ifNoneMatch: null);
    }

    private DataverseRequest BuildUpdateRequest()
    {
        var url = BuildUrl(requireKey: true);
        var ifMatch = string.IsNullOrWhiteSpace(_etag) ? "*" : _etag;
        RemoveReadOnlyProperties(DataverseOperation.Create, DataverseOperation.Update);
        return CreateRequest(HttpMethod.Patch, url, ifMatch, ifNoneMatch: null);
    }

    private DataverseRequest BuildUpsertRequest(UpsertConflictResolution conflictResolution)
    {
        var url = BuildUrl(requireKey: true);

        // Apply conflict resolution strategy via HTTP headers
        string? ifMatch = null;
        string? ifNoneMatch = null;
        switch (conflictResolution)
        {
            case UpsertConflictResolution.Upsert:
                // Standard upsert: create or update, no If-Match header needed
                RemoveReadOnlyProperties(DataverseOperation.Create, DataverseOperation.Update);
                break;

            case UpsertConflictResolution.PreventUpdate:
                // Create only: fail if exists (412 Precondition Failed)
                ifNoneMatch = "*";
                RemoveReadOnlyProperties(DataverseOperation.Create);
                break;

            case UpsertConflictResolution.PreventCreate:
                // Update only: fail if doesn't exist (404 Not Found)
                ifMatch = "*";
                RemoveReadOnlyProperties(DataverseOperation.Create, DataverseOperation.Update);
                break;

            case UpsertConflictResolution.IfUnmodifiedSince:
                // Optimistic concurrency: update only if ETag matches
                if (string.IsNullOrWhiteSpace(_etag))
                {
                    throw new InvalidOperationException(
                        "ETag is required when using UpsertConflictResolution.IfUnmodifiedSince. " +
                        "Call WithETag() with the ETag value from a previous retrieve operation.");
                }
                ifMatch = _etag;
                RemoveReadOnlyProperties(DataverseOperation.Create, DataverseOperation.Update);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(conflictResolution),
                    conflictResolution,
                    "Invalid conflict resolution strategy");
        }

        return CreateRequest(HttpMethod.Patch, url, ifMatch, ifNoneMatch);
    }

    private DataverseRequest CreateRequest(HttpMethod method, string url, string? ifMatch, string? ifNoneMatch)
    {
        // The request gets its own copy of the payload, so it is unaffected by later use of the builder.
        var request = new DataverseRequest(method, url) { Payload = _payload.DeepClone().AsObject() };

        var headers = _request.BuildHeaders(ifMatch: ifMatch, ifNoneMatch: ifNoneMatch);
        if (headers != null)
        {
            foreach (var header in headers)
                request.Headers[header.Key] = header.Value;
        }

        return request;
    }

    private string BuildUrl(bool requireKey = false) =>
        EntityRequest.AppendQueryOptions(_request.BuildPath(requireKey), _representationSelect);
}
