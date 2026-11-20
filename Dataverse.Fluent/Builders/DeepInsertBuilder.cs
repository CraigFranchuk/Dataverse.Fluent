using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dataverse.Fluent.Builders.Expressions;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Fluent builder for deep insert operations (creating parent entity with nested child entities).
/// Leverages Dataverse Web API deep insert capabilities to create related entities in a single request.
/// </summary>
/// <typeparam name="T">The POCO type for the parent entity.</typeparam>
/// <remarks>
/// This type is not thread-safe. Each instance should be used for a single operation and not shared across threads.
/// Terminal methods (e.g., <see cref="ExecuteAsync"/>) mark the builder as consumed; subsequent calls will throw
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class DeepInsertBuilder<T>
{
    private readonly IDataverseTransport _client;
    private readonly string _entitySet;
    private readonly T _parentEntity;
    private readonly JsonObject _payload = new();
    private readonly Dictionary<string, JsonArray> _relatedCollections = [];
    private readonly Dictionary<string, JsonObject> _relatedSingle = [];
    private readonly HashSet<string> _prefer = new(StringComparer.OrdinalIgnoreCase);
    private SingleUseGuard _guard;

    internal DeepInsertBuilder(IDataverseTransport client, T parentEntity, string? entitySet = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _parentEntity = parentEntity ?? throw new ArgumentNullException(nameof(parentEntity));
        
        _entitySet = entitySet ?? ODataPropertyNameCache.GetEntitySetName<T>();
        
        // Serialize parent entity into payload
        SerializeParentEntity();
    }

    /// <summary>
    /// Adds a collection of related entities for a 1:N relationship.
    /// </summary>
    public DeepInsertBuilder<T> WithRelated<TRelated>(IEnumerable<TRelated> relatedEntities, string relationshipName) 
        where TRelated : class
    {
        ArgumentNullException.ThrowIfNull(relatedEntities);
        if (string.IsNullOrWhiteSpace(relationshipName))
            throw new ArgumentException("Relationship name cannot be null or whitespace.", nameof(relationshipName));

        AddRelatedCollection(relatedEntities, relationshipName);
        return this;
    }

    /// <summary>
    /// Adds a single related entity for a 1:1 or single-valued navigation property.
    /// </summary>
    public DeepInsertBuilder<T> WithRelated<TRelated>(TRelated relatedEntity, string relationshipName) 
        where TRelated : class
    {
        ArgumentNullException.ThrowIfNull(relatedEntity);
        if (string.IsNullOrWhiteSpace(relationshipName))
            throw new ArgumentException("Relationship name cannot be null or whitespace.", nameof(relationshipName));

        var serialized = JsonSerializer.SerializeToNode(relatedEntity, JsonDefaults.EntityPayload);

        // A List<T> or T[] binds to this overload (an exact type match beats the conversion to
        // IEnumerable<T>), so a value that serializes to an array is treated as a collection.
        if (serialized is JsonArray && relatedEntity is System.Collections.IEnumerable collection)
        {
            AddRelatedCollection(collection, relationshipName);
        }
        else if (serialized is JsonObject obj)
        {
            // Filter out read-only properties for Create operation
            PropertyFilter.RemoveReadOnlyProperties<TRelated>(obj, DataverseOperation.Create);
            _relatedSingle[relationshipName] = obj;
        }

        return this;
    }

    /// <summary>
    /// Adds related entities for a collection-valued navigation property of the model, such as
    /// <c>x =&gt; x.Contacts</c>. The name of the relationship is the one the property is mapped to.
    /// </summary>
    /// <exception cref="ArgumentException">The property is not a navigation property of <typeparamref name="T"/>.</exception>
    public DeepInsertBuilder<T> WithRelated<TRelated>(Expression<Func<T, IEnumerable<TRelated>?>> navigation, IEnumerable<TRelated> relatedEntities)
        where TRelated : class
        => WithRelated(relatedEntities, NavigationPropertyReader.Read<T>(navigation).Name);

    /// <summary>
    /// Adds a related entity for a single-valued navigation property of the model, such as
    /// <c>x =&gt; x.PrimaryContact</c>. The name of the relationship is the one the property is mapped to.
    /// </summary>
    /// <exception cref="ArgumentException">The property is not a navigation property of <typeparamref name="T"/>.</exception>
    public DeepInsertBuilder<T> WithRelated<TRelated>(Expression<Func<T, TRelated?>> navigation, TRelated relatedEntity)
        where TRelated : class
        => WithRelated(relatedEntity, NavigationPropertyReader.Read<T>(navigation).Name);

    // Serializes each entity by its runtime type so that read-only filtering applies to the element
    // type. An empty collection is omitted: it creates no related records.
    private void AddRelatedCollection(System.Collections.IEnumerable relatedEntities, string relationshipName)
    {
        var relatedArray = new JsonArray();

        foreach (var entity in relatedEntities)
        {
            if (entity is null)
                continue;

            var serialized = JsonSerializer.SerializeToNode(entity, entity.GetType(), JsonDefaults.EntityPayload);
            if (serialized is JsonObject obj)
            {
                // Filter out read-only properties for Create operation
                PropertyFilter.RemoveReadOnlyProperties(entity.GetType(), obj, DataverseOperation.Create);
                relatedArray.Add(obj);
            }
        }

        if (relatedArray.Count > 0)
            _relatedCollections[relationshipName] = relatedArray;
        else
            _relatedCollections.Remove(relationshipName);
    }

    /// <summary>
    /// Requests the full entity representation in the response.
    /// </summary>
    public DeepInsertBuilder<T> ReturnRepresentation(bool returnIt = true)
    {
        if (returnIt)
        {
            _prefer.Add("return=representation");
        }
        else
        {
            _prefer.Remove("return=representation");
        }
        return this;
    }

    /// <summary>
    /// Adds a custom Prefer header.
    /// </summary>
    public DeepInsertBuilder<T> WithPrefer(string preferValue)
    {
        if (!string.IsNullOrWhiteSpace(preferValue))
        {
            _prefer.Add(preferValue);
        }
        return this;
    }

    /// <summary>
    /// Executes the deep insert operation.
    /// </summary>
    /// <returns>
    /// The new parent record's ID (from the <c>OData-EntityId</c> header) and, when
    /// <see cref="ReturnRepresentation(bool)"/> was requested, the created parent. Dataverse omits the header
    /// when a representation is returned, so read the key from the entity in that case.
    /// </returns>
    public async Task<Models.DataverseCreateResult<T>> ExecuteAsync(CancellationToken ct = default)
    {
        _guard.Use();

        // Merge related entities into the payload
        MergeRelatedEntities();

        var request = new DataverseRequest(HttpMethod.Post, _entitySet) { Payload = _payload };
        if (BuildPreferHeaders() is { } headers)
        {
            foreach (var header in headers)
                request.Headers[header.Key] = header.Value;
        }

        using var response = await _client.SendAsync(request, ct).ConfigureAwait(false);
        var entity = await response.ReadEntityAsync<T>(ct).ConfigureAwait(false);
        return new Models.DataverseCreateResult<T>(response.EntityId, entity, response.EntityUrl);
    }

    private void SerializeParentEntity()
    {
        var serialized = JsonSerializer.SerializeToNode(_parentEntity, JsonDefaults.EntityPayload);
        if (serialized is JsonObject obj)
        {
            // Get read-only properties for Create operation
            var readOnlyProps = PropertyFilter.GetReadOnlyProperties<T>(DataverseOperation.Create);

            foreach (var prop in obj)
            {
                // Only add properties that are not read-only for Create
                if (!readOnlyProps.Contains(prop.Key))
                {
                    _payload[prop.Key] = prop.Value?.DeepClone();
                }
            }
        }
    }

    private void MergeRelatedEntities()
    {
        // Add 1:N relationships as arrays
        foreach (var kvp in _relatedCollections)
        {
            _payload[kvp.Key] = kvp.Value.DeepClone();
        }

        // Add 1:1 relationships as single objects
        foreach (var kvp in _relatedSingle)
        {
            _payload[kvp.Key] = kvp.Value.DeepClone();
        }
    }

    private Dictionary<string, string>? BuildPreferHeaders()
    {
        if (_prefer.Count == 0)
            return null;

        // A returned representation is read into T, whose polymorphic lookups need the table annotation.
        var prefer = new List<string>(_prefer);
        if (_prefer.Contains("return=representation") && ODataPaging.IncludeAnnotationsPreference(typeof(T), formattedValues: false) is { } annotations)
            prefer.Add(annotations);

        return new Dictionary<string, string>
        {
            ["Prefer"] = string.Join(",", prefer)
        };
    }

    }
