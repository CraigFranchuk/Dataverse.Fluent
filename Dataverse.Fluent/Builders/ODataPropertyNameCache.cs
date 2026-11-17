using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Thread-safe cache for OData property name resolution from <see cref="MemberInfo"/>,
/// entity set name inference from <see cref="Type"/>, and SelectAll property lists.
/// Eliminates repeated reflection on hot paths. Name resolution is delegated to
/// <see cref="DataverseTypeMapping"/> so custom <see cref="IDataverseNameResolver"/> strategies
/// are honored; caches are cleared whenever the registered resolvers change.
/// </summary>
internal static class ODataPropertyNameCache
{
    private static readonly ConcurrentDictionary<MemberInfo, string> _propertyNameCache = new();
    private static readonly ConcurrentDictionary<Type, string> _entitySetCache = new();
    private static readonly ConcurrentDictionary<Type, List<string>> _selectAllCache = new();

    static ODataPropertyNameCache()
    {
        DataverseTypeMapping.ResolversChanged += ClearCaches;
    }

    private static void ClearCaches()
    {
        _propertyNameCache.Clear();
        _entitySetCache.Clear();
        _selectAllCache.Clear();
    }

    /// <summary>
    /// Returns the OData property name for a member.
    /// Resolution order: registered <see cref="IDataverseNameResolver"/> strategies →
    /// <see cref="DataverseColumnAttribute"/> → <see cref="JsonPropertyNameAttribute"/> →
    /// lowercase member name (Dataverse logical-name convention).
    /// Results are cached per <see cref="MemberInfo"/>.
    /// </summary>
    internal static string GetODataPropertyName(MemberInfo member)
    {
        return _propertyNameCache.GetOrAdd(member, static m =>
            DataverseTypeMapping.ResolvePropertyName(m) ?? m.Name.ToLowerInvariant());
    }

    /// <summary>
    /// Returns the entity set name for a type.
    /// Resolution order: registered <see cref="IDataverseNameResolver"/> strategies →
    /// <see cref="EntitySetAttribute"/>. Results are cached per <see cref="Type"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no resolver or <see cref="EntitySetAttribute"/> can resolve the type.</exception>
    internal static string GetEntitySetName<T>()
    {
        return _entitySetCache.GetOrAdd(typeof(T), static t =>
        {
            var name = DataverseTypeMapping.ResolveEntitySetName(t);
            if (name != null)
            {
                return name;
            }

            throw new InvalidOperationException(
                $"Type '{t.Name}' does not have an [EntitySet] attribute and no registered " +
                $"IDataverseNameResolver could resolve it. Apply [EntitySet(\"entitysetname\")] to the class, " +
                $"register a resolver via DataverseTypeMapping.RegisterResolver, or pass the entity set name explicitly.");
        });
    }

    /// <summary>
    /// Returns the list of all OData property names for a type's public instance properties.
    /// Resolution order: registered <see cref="IDataverseNameResolver"/> strategies →
    /// <see cref="DataverseColumnAttribute"/> → <see cref="JsonPropertyNameAttribute"/> →
    /// lowercase property name. Results are cached per <see cref="Type"/>.
    /// </summary>
    internal static List<string> GetSelectAllProperties<T>()
    {
        // Columns and lookups only: navigation properties and [JsonIgnore] members cannot be selected.
        return _selectAllCache.GetOrAdd(typeof(T), static t => EntityTypeMetadata.For(t).SelectableNames());
    }

    /// <summary>
    /// Resolves the OData/JSON property name for a <see cref="PropertyInfo"/>.
    /// Resolution order: registered <see cref="IDataverseNameResolver"/> strategies →
    /// <see cref="DataverseColumnAttribute"/> → <see cref="JsonPropertyNameAttribute"/> →
    /// lowercase property name.
    /// </summary>
    internal static string ResolvePropertyName(PropertyInfo prop)
        => DataverseTypeMapping.ResolvePropertyName(prop) ?? prop.Name.ToLowerInvariant();
}
