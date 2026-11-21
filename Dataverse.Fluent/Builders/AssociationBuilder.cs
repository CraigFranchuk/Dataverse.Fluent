using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Nodes;
using Dataverse.Fluent.Builders.Expressions;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Fluent builder for associating and disassociating entities via Dataverse Web API $ref operations.
/// Supports both collection-valued (N:N, 1:N) and single-valued navigation properties,
/// with primary key and alternate key addressing for both source and target entities.
/// </summary>
/// <remarks>
/// This type is not thread-safe. Each instance should be used for a single operation and not shared across threads.
/// Terminal methods (e.g., <see cref="AssociateAsync"/>, <see cref="DisassociateAsync"/>) mark the builder as consumed;
/// subsequent calls will throw <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class AssociationBuilder
{
    private readonly IDataverseTransport _client;
    private string? _sourceEntitySet;
    private string? _sourceKeySegment;
    private string? _navigationProperty;
    private string? _targetEntitySet;
    private string? _targetKeySegment;
    private bool _isSingleValued;
    private SingleUseGuard _guard;

    internal AssociationBuilder(IDataverseTransport client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    // ───────────────────────── Source entity ─────────────────────────

    /// <summary>
    /// Specifies the source entity by type and primary key ID.
    /// </summary>
    /// <typeparam name="TSource">The source entity POCO type (used to infer entity set name).</typeparam>
    /// <param name="id">The primary key ID of the source entity.</param>
    public AssociationBuilder From<TSource>(Guid id)
    {
        _sourceEntitySet = InferEntitySet<TSource>();
        _sourceKeySegment = id.ToString();
        return this;
    }

    /// <summary>
    /// Specifies the source entity by explicit entity set name and primary key ID.
    /// </summary>
    /// <param name="entitySet">The entity set name (e.g., "accounts").</param>
    /// <param name="id">The primary key ID of the source entity.</param>
    public AssociationBuilder From(string entitySet, Guid id)
    {
        _sourceEntitySet = ValidateEntitySet(entitySet, nameof(entitySet));
        _sourceKeySegment = id.ToString();
        return this;
    }

    /// <summary>
    /// Specifies the source entity by type and a single alternate key.
    /// </summary>
    /// <typeparam name="TSource">The source entity POCO type.</typeparam>
    /// <param name="keyName">The alternate key attribute name.</param>
    /// <param name="keyValue">The alternate key value.</param>
    public AssociationBuilder From<TSource>(string keyName, object keyValue)
    {
        _sourceEntitySet = InferEntitySet<TSource>();
        _sourceKeySegment = FormatAlternateKey(keyName, keyValue);
        return this;
    }

    /// <summary>
    /// Specifies the source entity by type and a strongly-typed alternate key expression.
    /// </summary>
    /// <typeparam name="TSource">The source entity POCO type.</typeparam>
    /// <typeparam name="TKey">The alternate key property type.</typeparam>
    /// <param name="keyExpression">Expression pointing to the alternate key property.</param>
    /// <param name="keyValue">The alternate key value.</param>
    public AssociationBuilder From<TSource, TKey>(Expression<Func<TSource, TKey>> keyExpression, TKey keyValue)
    {
        ArgumentNullException.ThrowIfNull(keyExpression);

        _sourceEntitySet = InferEntitySet<TSource>();
        var keyName = GetJsonPropertyName(keyExpression);
        _sourceKeySegment = FormatAlternateKey(keyName, keyValue!);
        return this;
    }

    /// <summary>
    /// Specifies the source entity by type and composite alternate keys.
    /// </summary>
    /// <typeparam name="TSource">The source entity POCO type.</typeparam>
    /// <param name="alternateKeys">Dictionary of alternate key names and values.</param>
    public AssociationBuilder From<TSource>(Dictionary<string, object> alternateKeys)
    {
        if (alternateKeys == null || alternateKeys.Count == 0)
            throw new ArgumentException("At least one alternate key must be specified.", nameof(alternateKeys));

        _sourceEntitySet = InferEntitySet<TSource>();
        _sourceKeySegment = FormatCompositeKey(alternateKeys);
        return this;
    }

    /// <summary>
    /// Specifies the source entity by explicit entity set name and a single alternate key.
    /// </summary>
    /// <param name="entitySet">The entity set name.</param>
    /// <param name="keyName">The alternate key attribute name.</param>
    /// <param name="keyValue">The alternate key value.</param>
    public AssociationBuilder From(string entitySet, string keyName, object keyValue)
    {
        _sourceEntitySet = ValidateEntitySet(entitySet, nameof(entitySet));
        _sourceKeySegment = FormatAlternateKey(keyName, keyValue);
        return this;
    }

    /// <summary>
    /// Specifies the source entity by explicit entity set name and composite alternate keys.
    /// </summary>
    /// <param name="entitySet">The entity set name.</param>
    /// <param name="alternateKeys">Dictionary of alternate key names and values.</param>
    public AssociationBuilder From(string entitySet, Dictionary<string, object> alternateKeys)
    {
        if (alternateKeys == null || alternateKeys.Count == 0)
            throw new ArgumentException("At least one alternate key must be specified.", nameof(alternateKeys));

        _sourceEntitySet = ValidateEntitySet(entitySet, nameof(entitySet));
        _sourceKeySegment = FormatCompositeKey(alternateKeys);
        return this;
    }

    // ───────────────────────── Navigation property ─────────────────────────

    /// <summary>
    /// Specifies the collection-valued navigation property for the relationship (N:N or 1:N).
    /// </summary>
    /// <param name="navigationProperty">The schema name of the navigation property.</param>
    public AssociationBuilder Via(string navigationProperty)
    {
        if (string.IsNullOrWhiteSpace(navigationProperty))
            throw new ArgumentException("Navigation property cannot be null or whitespace.", nameof(navigationProperty));

        _navigationProperty = navigationProperty;
        _isSingleValued = false;
        return this;
    }

    /// <summary>
    /// Specifies a single-valued navigation property for the relationship.
    /// Use this for single-valued lookups (e.g., setting or clearing a lookup field).
    /// </summary>
    /// <param name="navigationProperty">The schema name of the single-valued navigation property.</param>
    public AssociationBuilder ViaSingleValued(string navigationProperty)
    {
        if (string.IsNullOrWhiteSpace(navigationProperty))
            throw new ArgumentException("Navigation property cannot be null or whitespace.", nameof(navigationProperty));

        _navigationProperty = navigationProperty;
        _isSingleValued = true;
        return this;
    }

    /// <summary>
    /// Specifies the relationship by a navigation property of the source model, such as <c>a =&gt; a.Contacts</c>.
    /// The name of the relationship is the one the property is mapped to. A property that holds a single record is
    /// treated as single-valued, as with <see cref="ViaSingleValued(string)"/>.
    /// </summary>
    /// <typeparam name="TSource">The source entity POCO type that declares the navigation property.</typeparam>
    /// <param name="navigation">The navigation property.</param>
    /// <exception cref="ArgumentException">The property is not a navigation property of <typeparamref name="TSource"/>.</exception>
    public AssociationBuilder Via<TSource>(Expression<Func<TSource, object?>> navigation)
    {
        var (name, isCollection) = NavigationPropertyReader.Read<TSource>(navigation);
        return isCollection ? Via(name) : ViaSingleValued(name);
    }

    // ───────────────────────── Target entity ─────────────────────────

    /// <summary>
    /// Specifies the target entity by type and primary key ID.
    /// </summary>
    /// <typeparam name="TTarget">The target entity POCO type (used to infer entity set name).</typeparam>
    /// <param name="id">The primary key ID of the target entity.</param>
    public AssociationBuilder To<TTarget>(Guid id)
    {
        _targetEntitySet = InferEntitySet<TTarget>();
        _targetKeySegment = id.ToString();
        return this;
    }

    /// <summary>
    /// Specifies the target entity by explicit entity set name and primary key ID.
    /// </summary>
    /// <param name="entitySet">The entity set name (e.g., "contacts").</param>
    /// <param name="id">The primary key ID of the target entity.</param>
    public AssociationBuilder To(string entitySet, Guid id)
    {
        _targetEntitySet = ValidateEntitySet(entitySet, nameof(entitySet));
        _targetKeySegment = id.ToString();
        return this;
    }

    /// <summary>
    /// Specifies the target entity by type and a single alternate key.
    /// </summary>
    /// <typeparam name="TTarget">The target entity POCO type.</typeparam>
    /// <param name="keyName">The alternate key attribute name.</param>
    /// <param name="keyValue">The alternate key value.</param>
    public AssociationBuilder To<TTarget>(string keyName, object keyValue)
    {
        _targetEntitySet = InferEntitySet<TTarget>();
        _targetKeySegment = FormatAlternateKey(keyName, keyValue);
        return this;
    }

    /// <summary>
    /// Specifies the target entity by type and a strongly-typed alternate key expression.
    /// </summary>
    /// <typeparam name="TTarget">The target entity POCO type.</typeparam>
    /// <typeparam name="TKey">The alternate key property type.</typeparam>
    /// <param name="keyExpression">Expression pointing to the alternate key property.</param>
    /// <param name="keyValue">The alternate key value.</param>
    public AssociationBuilder To<TTarget, TKey>(Expression<Func<TTarget, TKey>> keyExpression, TKey keyValue)
    {
        ArgumentNullException.ThrowIfNull(keyExpression);

        _targetEntitySet = InferEntitySet<TTarget>();
        var keyName = GetJsonPropertyName(keyExpression);
        _targetKeySegment = FormatAlternateKey(keyName, keyValue!);
        return this;
    }

    /// <summary>
    /// Specifies the target entity by type and composite alternate keys.
    /// </summary>
    /// <typeparam name="TTarget">The target entity POCO type.</typeparam>
    /// <param name="alternateKeys">Dictionary of alternate key names and values.</param>
    public AssociationBuilder To<TTarget>(Dictionary<string, object> alternateKeys)
    {
        if (alternateKeys == null || alternateKeys.Count == 0)
            throw new ArgumentException("At least one alternate key must be specified.", nameof(alternateKeys));

        _targetEntitySet = InferEntitySet<TTarget>();
        _targetKeySegment = FormatCompositeKey(alternateKeys);
        return this;
    }

    /// <summary>
    /// Specifies the target entity by explicit entity set name and a single alternate key.
    /// </summary>
    /// <param name="entitySet">The entity set name.</param>
    /// <param name="keyName">The alternate key attribute name.</param>
    /// <param name="keyValue">The alternate key value.</param>
    public AssociationBuilder To(string entitySet, string keyName, object keyValue)
    {
        _targetEntitySet = ValidateEntitySet(entitySet, nameof(entitySet));
        _targetKeySegment = FormatAlternateKey(keyName, keyValue);
        return this;
    }

    /// <summary>
    /// Specifies the target entity by explicit entity set name and composite alternate keys.
    /// </summary>
    /// <param name="entitySet">The entity set name.</param>
    /// <param name="alternateKeys">Dictionary of alternate key names and values.</param>
    public AssociationBuilder To(string entitySet, Dictionary<string, object> alternateKeys)
    {
        if (alternateKeys == null || alternateKeys.Count == 0)
            throw new ArgumentException("At least one alternate key must be specified.", nameof(alternateKeys));

        _targetEntitySet = ValidateEntitySet(entitySet, nameof(entitySet));
        _targetKeySegment = FormatCompositeKey(alternateKeys);
        return this;
    }

    // ───────────────────────── Execute ─────────────────────────

    /// <summary>
    /// Executes the associate operation.
    /// POST {sourceEntitySet}({sourceKey})/{navigationProperty}/$ref
    /// with body {"@odata.id": "{baseUrl}/{targetEntitySet}({targetKey})"}
    /// </summary>
    public async Task AssociateAsync(CancellationToken ct = default)
    {
        _guard.Use();
        ValidateState();

        var url = BuildRefUrl();
        var payload = new JsonObject
        {
            ["@odata.id"] = $"{_client.BaseUrl}/{_targetEntitySet}({_targetKeySegment})"
        };

        await RequestSender.ExecuteJsonAsync(_client, HttpMethod.Post, url, payload, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the disassociate operation for a collection-valued navigation property.
    /// DELETE {sourceEntitySet}({sourceKey})/{navigationProperty}({targetKey})/$ref
    /// </summary>
    public async Task DisassociateAsync(CancellationToken ct = default)
    {
        _guard.Use();
        ValidateState();

        string url;
        if (_isSingleValued)
        {
            // Single-valued: DELETE {source}/{navProp}/$ref
            url = $"{_sourceEntitySet}({_sourceKeySegment})/{_navigationProperty}/$ref";
        }
        else
        {
            // Collection-valued: DELETE {source}/{navProp}({targetKey})/$ref
            url = $"{_sourceEntitySet}({_sourceKeySegment})/{_navigationProperty}({_targetKeySegment})/$ref";
        }

        await RequestSender.ExecuteJsonAsync(_client, HttpMethod.Delete, url, null, null, ct).ConfigureAwait(false);
    }

    // ───────────────────────── Private helpers ─────────────────────────

    private string BuildRefUrl()
    {
        return $"{_sourceEntitySet}({_sourceKeySegment})/{_navigationProperty}/$ref";
    }

    private void ValidateState()
    {
        if (string.IsNullOrWhiteSpace(_sourceEntitySet) || string.IsNullOrWhiteSpace(_sourceKeySegment))
            throw new InvalidOperationException("Source entity must be specified. Call From() before executing.");
        if (string.IsNullOrWhiteSpace(_navigationProperty))
            throw new InvalidOperationException("Navigation property must be specified. Call Via() or ViaSingleValued() before executing.");
        if (string.IsNullOrWhiteSpace(_targetEntitySet) || string.IsNullOrWhiteSpace(_targetKeySegment))
            throw new InvalidOperationException("Target entity must be specified. Call To() before executing.");
    }

    private static string InferEntitySet<T>()
    {
        return ODataPropertyNameCache.GetEntitySetName<T>();
    }

    private static string ValidateEntitySet(string entitySet, string paramName)
    {
        if (string.IsNullOrWhiteSpace(entitySet))
            throw new ArgumentException("Entity set name cannot be null or whitespace.", paramName);
        return entitySet;
    }

    private static string FormatAlternateKey(string keyName, object keyValue)
    {
        if (string.IsNullOrWhiteSpace(keyName))
            throw new ArgumentException("Key name cannot be null or whitespace.", nameof(keyName));

        return AlternateKeyFormatter.FormatKeyValue(keyName, keyValue);
    }

    private static string FormatCompositeKey(Dictionary<string, object> alternateKeys)
    {
        return AlternateKeyFormatter.FormatCompositeKey(alternateKeys);
    }

    private static string GetJsonPropertyName<TSource, TProperty>(Expression<Func<TSource, TProperty>> propertyExpression)
    {
        MemberExpression? memberExpression = null;

        if (propertyExpression.Body is MemberExpression directMember)
        {
            memberExpression = directMember;
        }
        else if (propertyExpression.Body is UnaryExpression unaryExpression
                 && unaryExpression.Operand is MemberExpression convertedMember)
        {
            memberExpression = convertedMember;
        }

        if (memberExpression == null)
        {
            throw new ArgumentException(
                "Expression must be a simple property accessor (e.g., x => x.PropertyName)",
                nameof(propertyExpression));
        }

        if (memberExpression.Member is not PropertyInfo)
        {
            throw new ArgumentException("Expression must reference a property", nameof(propertyExpression));
        }

        return ODataPropertyNameCache.GetODataPropertyName(memberExpression.Member);
    }
}
