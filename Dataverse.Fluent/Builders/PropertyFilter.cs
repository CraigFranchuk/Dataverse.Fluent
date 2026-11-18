using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Shared helper for resolving and filtering read-only properties based on
/// <see cref="DataverseReadOnlyAttribute"/>. Results are cached per (Type, Operation) pair
/// to avoid repeated reflection on hot paths (e.g., batch operations).
/// </summary>
/// <remarks>
/// Names are the ones a payload is written with (<see cref="EntityMember.WriteNames"/>), so a read-only
/// lookup is matched by its <c>@odata.bind</c> names.
/// </remarks>
internal static class PropertyFilter
{
    private static readonly ConcurrentDictionary<(Type, DataverseOperation), HashSet<string>> _cache = new();

    static PropertyFilter()
    {
        DataverseTypeMapping.ResolversChanged += _cache.Clear;
    }

    /// <summary>
    /// Returns the set of payload property names that are marked as read-only for the given operation.
    /// </summary>
    internal static HashSet<string> GetReadOnlyProperties<TEntity>(DataverseOperation operation)
        => GetReadOnlyProperties(typeof(TEntity), operation);

    /// <summary>
    /// Returns the set of payload property names on <paramref name="entityType"/> that are marked as
    /// read-only for the given operation.
    /// </summary>
    internal static HashSet<string> GetReadOnlyProperties(Type entityType, DataverseOperation operation)
    {
        return _cache.GetOrAdd((entityType, operation), static key =>
        {
            var (type, op) = key;
            var readOnlyProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var member in EntityTypeMetadata.For(type).Members)
            {
                var readOnlyAttr = member.Property.GetCustomAttribute<DataverseReadOnlyAttribute>();
                if (readOnlyAttr != null && readOnlyAttr.ExcludeFrom.HasFlag(op))
                    readOnlyProps.UnionWith(member.WriteNames);
            }

            return readOnlyProps;
        });
    }

    /// <summary>
    /// Removes properties marked as read-only for the given operation from a <see cref="JsonObject"/>.
    /// </summary>
    internal static void RemoveReadOnlyProperties<TEntity>(JsonObject jsonObject, DataverseOperation operation)
        => RemoveReadOnlyProperties(typeof(TEntity), jsonObject, operation);

    /// <summary>
    /// Removes properties of <paramref name="entityType"/> marked as read-only for the given operation
    /// from a <see cref="JsonObject"/>.
    /// </summary>
    internal static void RemoveReadOnlyProperties(Type entityType, JsonObject jsonObject, DataverseOperation operation)
    {
        var readOnlyProps = GetReadOnlyProperties(entityType, operation);

        foreach (var propName in readOnlyProps)
        {
            jsonObject.Remove(propName);
        }
    }
}
