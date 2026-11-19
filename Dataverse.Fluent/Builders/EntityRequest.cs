using System.Linq.Expressions;
using Dataverse.Fluent.Builders.Expressions;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// State shared by the stages of one single-entity operation: the target record, the Prefer header values and
/// the single-use guard. <see cref="EntityBuilder{T}"/>, <see cref="EntityReadBuilder{T}"/> and
/// <see cref="EntityWriteBuilder{T}"/> all wrap the same instance, so the target can be set at any stage and
/// the operation runs once.
/// </summary>
internal sealed class EntityRequest
{
    private Dictionary<string, object>? _alternateKey;
    private SingleUseGuard _guard;

    public EntityRequest(IDataverseTransport client, Type entityType, string entitySet, Guid? id)
    {
        Client = client;
        EntityType = entityType;
        EntitySet = entitySet;
        EntityId = id;
    }

    /// <summary>The POCO type the record is read into.</summary>
    public Type EntityType { get; }

    public IDataverseTransport Client { get; }

    public string EntitySet { get; }

    public Guid? EntityId { get; set; }

    public bool HasAlternateKey => _alternateKey != null;

    // Explicit Prefer values (WithPrefer, ReturnRepresentation, ReturnMinimal)
    public List<string> Prefer { get; } = [];

    public void AddAlternateKey(string keyName, object keyValue)
    {
        // Rejected here, when the key is set, rather than when the request is sent.
        AlternateKeyFormatter.ThrowIfUnusable(keyValue, KeyPlacement.UrlPath);

        _alternateKey ??= new Dictionary<string, object>();
        _alternateKey[keyName] = keyValue;
    }

    public void ThrowIfAlreadyExecuted() => _guard.Use();

    // The record path: entityset(alternate key), entityset(id) or the bare entity set. An alternate key wins over an ID.
    public string BuildPath(bool requireKey = false)
    {
        if (requireKey && !EntityId.HasValue && _alternateKey == null)
            throw new InvalidOperationException("Entity ID or alternate key required for this operation.");

        if (_alternateKey != null)
        {
            if (_alternateKey.Count == 0)
                throw new InvalidOperationException("Alternate key must be specified.");

            return $"{EntitySet}({AlternateKeyFormatter.FormatCompositeKey(_alternateKey)})";
        }

        return EntityId.HasValue ? $"{EntitySet}({EntityId.Value})" : EntitySet;
    }

    public static string AppendQueryOptions(string path, IReadOnlyCollection<string> select, IReadOnlyCollection<string>? expand = null)
    {
        // Query option values are percent-encoded: nested $expand options can carry string literals
        // containing '&', '#', or '+' that would otherwise split or truncate the URL.
        var options = new List<string>(2);

        if (select.Count > 0)
            options.Add("$select=" + Uri.EscapeDataString(string.Join(",", select)));

        if (expand?.Count > 0)
            options.Add("$expand=" + Uri.EscapeDataString(string.Join(",", expand)));

        return options.Count == 0 ? path : path + "?" + string.Join("&", options);
    }

    /// <param name="includeFormattedValues">Whether to request formatted values.</param>
    /// <param name="ifMatch">The If-Match header, if any.</param>
    /// <param name="ifNoneMatch">The If-None-Match header, if any.</param>
    /// <param name="readsRecord">Whether the response is a record read into <see cref="EntityType"/>: a retrieve, or a
    /// write with <c>return=representation</c>. Annotations a polymorphic lookup needs are then requested.</param>
    public Dictionary<string, string>? BuildHeaders(bool includeFormattedValues = false, string? ifMatch = null, string? ifNoneMatch = null, bool readsRecord = false)
    {
        var headers = new Dictionary<string, string>();

        // Combine explicit Prefer options with annotation requests
        var preferValues = new List<string>(Prefer);
        readsRecord |= Prefer.Contains("return=representation");
        if ((includeFormattedValues || readsRecord)
            && ODataPaging.IncludeAnnotationsPreference(readsRecord ? EntityType : null, includeFormattedValues) is { } annotations)
        {
            preferValues.Add(annotations);
        }

        if (preferValues.Count > 0)
        {
            headers["Prefer"] = string.Join(",", preferValues);
        }

        // If-Match and If-None-Match carry optimistic concurrency and upsert conflict resolution
        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            headers["If-Match"] = ifMatch;
        }

        if (!string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            headers["If-None-Match"] = ifNoneMatch;
        }

        return headers.Count > 0 ? headers : null;
    }

    // Extracts the OData property names from a projection: e => e.Name, e => new { e.Name, e.Revenue }
    // or e => new Dto { Name = e.Name }.
    public static List<string> ExtractPropertyNames(LambdaExpression selector)
        => MemberPathExtractor.GetPropertyNames(selector);

    // Extracts the JSON property name from a simple property accessor on any type
    public static string GetJsonPropertyName(LambdaExpression propertyExpression)
        => MemberPathExtractor.GetPropertyName(propertyExpression);
}
