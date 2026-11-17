using System.Reflection;
using System.Text.Json.Serialization;

namespace Dataverse.Fluent;

/// <summary>
/// The built-in <see cref="IDataverseNameResolver"/> that maps members and types using the
/// library's own attributes. This is always consulted after any custom resolvers registered
/// via <see cref="DataverseTypeMapping"/>, preserving the default behavior.
/// </summary>
/// <remarks>
/// <para>
/// Property name resolution order: <see cref="DataverseLookupAttribute"/> (as <c>_{column}_value</c>) →
/// <see cref="DataverseColumnAttribute"/> → <see cref="JsonPropertyNameAttribute"/>. When neither attribute is present this resolver
/// returns <see langword="null"/>, allowing the caller to apply the lowercase logical-name
/// convention.
/// </para>
/// <para>
/// Entity set resolution uses <see cref="DataverseEntityAttribute"/> or <see cref="EntitySetAttribute"/>, and
/// logical name resolution uses <see cref="DataverseEntityAttribute"/>; both return <see langword="null"/> when
/// the attributes are absent.
/// </para>
/// </remarks>
public sealed class AttributeNameResolver : IDataverseNameResolver
{
    /// <summary>
    /// A shared, stateless instance of the default attribute-based resolver.
    /// </summary>
    public static readonly AttributeNameResolver Instance = new();

    /// <inheritdoc />
    public string? ResolvePropertyName(MemberInfo member)
    {
        var lookup = member.GetCustomAttribute<DataverseLookupAttribute>();
        if (lookup != null)
        {
            return lookup.ValueName;
        }

        var dvAttr = member.GetCustomAttribute<DataverseColumnAttribute>();
        if (dvAttr != null)
        {
            return dvAttr.LogicalName;
        }

        var jsonAttr = member.GetCustomAttribute<JsonPropertyNameAttribute>();
        return jsonAttr?.Name;
    }

    /// <inheritdoc />
    public string? ResolveEntitySetName(Type type)
        => type.GetCustomAttribute<DataverseEntityAttribute>()?.EntitySet
           ?? type.GetCustomAttribute<EntitySetAttribute>()?.Name;

    /// <inheritdoc />
    public string? ResolveEntityLogicalName(Type type)
        => type.GetCustomAttribute<DataverseEntityAttribute>()?.LogicalName;
}
