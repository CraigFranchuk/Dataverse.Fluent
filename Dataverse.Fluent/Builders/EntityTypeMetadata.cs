using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Builders;

/// <summary>What a POCO property maps to in Dataverse.</summary>
internal enum EntityMemberKind
{
    /// <summary>A column, read and written under the same name.</summary>
    Column,

    /// <summary>A lookup (<see cref="DataverseLookupAttribute"/>): read as <c>_x_value</c>, written as a bind.</summary>
    Lookup,

    /// <summary>A related record or collection: read from <c>$expand</c>, never written or selected.</summary>
    Navigation,

    /// <summary>Excluded with <c>[JsonIgnore]</c>.</summary>
    Ignored
}

/// <summary>A POCO property and how it maps to Dataverse.</summary>
/// <param name="Property">The property.</param>
/// <param name="Kind">What it maps to.</param>
/// <param name="Name">The name it is read, selected and filtered by.</param>
/// <param name="LogicalName">The column's logical name, as FetchXML uses it.</param>
/// <param name="Lookup">The lookup mapping, for <see cref="EntityMemberKind.Lookup"/>.</param>
internal sealed record EntityMember(PropertyInfo Property, EntityMemberKind Kind, string Name, string LogicalName, DataverseLookupAttribute? Lookup)
{
    /// <summary>The name the property is written under in a create or update payload.</summary>
    public string WriteName => Lookup is null ? Name : $"{Lookup.NavigationProperty}@odata.bind";
}

/// <summary>
/// The mapping of one POCO type, computed once: each public property classified as a column, lookup,
/// navigation property or ignored member. URL building, payload serialization, <c>SelectAll()</c> and
/// read-only filtering all read from this, so they agree on names.
/// </summary>
internal sealed class EntityTypeMetadata
{
    private static readonly ConcurrentDictionary<Type, EntityTypeMetadata> Cache = new();

    private readonly Dictionary<string, EntityMember> _byPropertyName;

    static EntityTypeMetadata()
    {
        DataverseTypeMapping.ResolversChanged += Cache.Clear;
    }

    private EntityTypeMetadata(Type type)
    {
        Type = type;
        var members = new List<EntityMember>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is null)
                continue;

            members.Add(Classify(type, property));
        }

        Members = members;
        _byPropertyName = members.ToDictionary(m => m.Property.Name, StringComparer.Ordinal);
    }

    /// <summary>The POCO type.</summary>
    public Type Type { get; }

    /// <summary>The type's public instance properties, in declaration order.</summary>
    public IReadOnlyList<EntityMember> Members { get; }

    /// <summary>Returns the mapping of <paramref name="type"/>.</summary>
    public static EntityTypeMetadata For(Type type) => Cache.GetOrAdd(type, static t => new EntityTypeMetadata(t));

    /// <summary>Returns the mapping of the property named like <paramref name="member"/>, or <see langword="null"/>.</summary>
    public EntityMember? Find(MemberInfo member)
        => _byPropertyName.TryGetValue(member.Name, out var found) ? found : null;

    /// <summary>
    /// The names <c>SelectAll()</c> requests: columns and lookups. Navigation properties, ignored members and
    /// write-only annotations (names containing '@') cannot be selected.
    /// </summary>
    public List<string> SelectableNames()
        => Members.Where(m => m.Kind is EntityMemberKind.Column or EntityMemberKind.Lookup && !m.Name.Contains('@', StringComparison.Ordinal))
            .Select(m => m.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The FetchXML attribute name of a member: the logical name of a lookup rather than its
    /// <c>_x_value</c> read name.
    /// </summary>
    public static string GetLogicalName(MemberInfo member)
    {
        if (member.DeclaringType is { } declaring && member is PropertyInfo
            && For(declaring).Find(member) is { } mapped)
        {
            return mapped.LogicalName;
        }

        return ToLogicalName(ODataPropertyNameCache.GetODataPropertyName(member));
    }

    private static EntityMember Classify(Type owner, PropertyInfo property)
    {
        var name = ODataPropertyNameCache.ResolvePropertyName(property);

        if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
            return new EntityMember(property, EntityMemberKind.Ignored, name, name, null);

        if (property.GetCustomAttribute<DataverseLookupAttribute>() is { } lookup)
        {
            if (property.PropertyType != typeof(Guid) && property.PropertyType != typeof(Guid?))
            {
                throw new InvalidOperationException(
                    $"[DataverseLookup] on {owner.Name}.{property.Name} requires a Guid or Guid? property, not {property.PropertyType.Name}.");
            }

            // A custom resolver can still rename the read name; the logical name comes from the attribute.
            return new EntityMember(property, EntityMemberKind.Lookup, name, lookup.LogicalName, lookup);
        }

        var kind = IsNavigation(property.PropertyType) ? EntityMemberKind.Navigation : EntityMemberKind.Column;
        return new EntityMember(property, kind, name, ToLogicalName(name), null);
    }

    // A property whose type, or element type, is itself a mapped entity holds related records.
    private static bool IsNavigation(Type type)
    {
        if (type == typeof(string) || type.IsPrimitive || type.IsEnum || Nullable.GetUnderlyingType(type) != null)
            return false;

        var element = type;
        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            element = type.IsArray
                ? type.GetElementType()!
                : type.GetInterfaces().Append(type)
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    ?.GetGenericArguments()[0];

            if (element is null)
                return false;
        }

        return element.IsClass && element != typeof(string) && DataverseTypeMapping.ResolveEntitySetName(element) != null;
    }

    // "_primarycontactid_value" → "primarycontactid": a lookup mapped by its read name without [DataverseLookup].
    private static string ToLogicalName(string name)
        => name.Length > "__value".Length && name.StartsWith('_') && name.EndsWith("_value", StringComparison.Ordinal)
            ? name[1..^"_value".Length]
            : name;
}
