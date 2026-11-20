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

    /// <summary>
    /// A polymorphic lookup: a <see cref="DataverseReference"/> read from <c>_x_value</c> and its
    /// <c>lookuplogicalname</c> annotation, and written as the bind of the target it points at.
    /// </summary>
    PolymorphicLookup,

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
/// <param name="Lookup">The lookup mapping, for <see cref="EntityMemberKind.Lookup"/> and
/// <see cref="EntityMemberKind.PolymorphicLookup"/>.</param>
/// <param name="Targets">The tables a <see cref="EntityMemberKind.PolymorphicLookup"/> can point at; empty otherwise.</param>
internal sealed record EntityMember(
    PropertyInfo Property,
    EntityMemberKind Kind,
    string Name,
    string LogicalName,
    DataverseLookupAttribute? Lookup,
    IReadOnlyList<DataverseLookupTargetAttribute> Targets)
{
    /// <summary>
    /// The names the property can be written under in a create or update payload: the column name, the bind of a
    /// lookup, or the bind of each distinct navigation property of a polymorphic lookup.
    /// </summary>
    public IReadOnlyList<string> WriteNames => Kind switch
    {
        EntityMemberKind.Lookup => [Bind(Lookup!.NavigationProperty)],
        EntityMemberKind.PolymorphicLookup => [.. Targets.Select(t => t.NavigationProperty).Distinct(StringComparer.Ordinal).Select(Bind)],
        _ => [Name]
    };

    /// <summary>The name a single-table lookup or a column is written under.</summary>
    public string WriteName => WriteNames[0];

    /// <summary>The target of a polymorphic lookup with the given logical name, or <see langword="null"/>.</summary>
    public DataverseLookupTargetAttribute? FindTarget(string? logicalName)
        => logicalName is null ? null : Targets.FirstOrDefault(t => string.Equals(t.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The bind value for a polymorphic lookup: the target it points at and <c>/{entityset}({id})</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The reference's table is unknown or not a target of the lookup.</exception>
    public (DataverseLookupTargetAttribute Target, string Bind) ResolveBind(DataverseReference reference)
    {
        var target = FindTarget(reference.LogicalName) ?? throw new InvalidOperationException(
            reference.LogicalName is null
                ? $"{Property.DeclaringType?.Name}.{Property.Name} has no table, so it cannot be written. A response read without the " +
                  "Microsoft.Dynamics.CRM.lookuplogicalname annotation does not say which table the lookup points at."
                : $"{Property.DeclaringType?.Name}.{Property.Name} points at '{reference.LogicalName}', which is not one of its targets " +
                  $"({string.Join(", ", Targets.Select(t => t.LogicalName))}). Add a [DataverseLookupTarget] for it.");

        return (target, DataverseColumnResolver.FormatBind(target.EntitySet, reference.Id));
    }

    private static string Bind(string navigationProperty) => $"{navigationProperty}@odata.bind";
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
        var properties = new List<PropertyInfo>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is null)
                continue;

            // A property hidden with 'new' is returned along with the one that hides it. The most derived one is the
            // type's property, as it is for System.Text.Json.
            var hidden = properties.FindIndex(p => p.Name == property.Name);
            if (hidden < 0)
                properties.Add(property);
            else if (properties[hidden].DeclaringType!.IsAssignableFrom(property.DeclaringType))
                properties[hidden] = property;
        }

        var members = properties.ConvertAll(p => Classify(type, p));
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
    /// Whether reads of this type, or of the related records it holds, must request the
    /// <c>Microsoft.Dynamics.CRM.lookuplogicalname</c> annotation: true when it or any type reachable through its
    /// navigation properties has a polymorphic lookup.
    /// </summary>
    public bool ReadsLookupLogicalNames => _readsLookupLogicalNames ??= ComputeReadsLookupLogicalNames(this, []);

    private bool? _readsLookupLogicalNames;

    private static bool ComputeReadsLookupLogicalNames(EntityTypeMetadata metadata, HashSet<Type> visited)
    {
        if (!visited.Add(metadata.Type))
            return false;

        foreach (var member in metadata.Members)
        {
            if (member.Kind == EntityMemberKind.PolymorphicLookup)
                return true;

            if (member.Kind == EntityMemberKind.Navigation && ElementType(member.Property.PropertyType) is { } related
                && ComputeReadsLookupLogicalNames(For(related), visited))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The names <c>SelectAll()</c> requests: columns and lookups. Navigation properties, ignored members and
    /// write-only annotations (names containing '@') cannot be selected.
    /// </summary>
    public List<string> SelectableNames()
        => Members.Where(m => m.Kind is EntityMemberKind.Column or EntityMemberKind.Lookup or EntityMemberKind.PolymorphicLookup
                && !m.Name.Contains('@', StringComparison.Ordinal))
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
            return new EntityMember(property, EntityMemberKind.Ignored, name, name, null, []);

        var targets = property.GetCustomAttributes<DataverseLookupTargetAttribute>().ToList();
        var isReference = property.PropertyType == typeof(DataverseReference);

        if (property.GetCustomAttribute<DataverseLookupAttribute>() is { } lookup)
        {
            // A custom resolver can still rename the read name; the logical name comes from the attribute.
            if (isReference)
                return new EntityMember(property, EntityMemberKind.PolymorphicLookup, name, lookup.LogicalName, lookup, ValidateTargets(owner, property, lookup, targets));

            if (property.PropertyType != typeof(Guid) && property.PropertyType != typeof(Guid?))
            {
                throw new InvalidOperationException(
                    $"[DataverseLookup] on {owner.Name}.{property.Name} requires a Guid, Guid? or DataverseReference property, not {property.PropertyType.Name}.");
            }

            if (lookup.TargetEntitySet is null || targets.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{owner.Name}.{property.Name} is a single-table lookup: use [DataverseLookup(\"{lookup.LogicalName}\", \"entityset\")] " +
                    "without [DataverseLookupTarget]. A lookup that can point at more than one table needs a DataverseReference property.");
            }

            return new EntityMember(property, EntityMemberKind.Lookup, name, lookup.LogicalName, lookup, []);
        }

        if (isReference || targets.Count > 0)
        {
            throw new InvalidOperationException(
                $"{owner.Name}.{property.Name} needs [DataverseLookup(\"logicalname\")] to map a DataverseReference or [DataverseLookupTarget].");
        }

        var kind = IsNavigation(property.PropertyType) ? EntityMemberKind.Navigation : EntityMemberKind.Column;
        return new EntityMember(property, kind, name, ToLogicalName(name), null, []);
    }

    private static List<DataverseLookupTargetAttribute> ValidateTargets(
        Type owner, PropertyInfo property, DataverseLookupAttribute lookup, List<DataverseLookupTargetAttribute> targets)
    {
        if (lookup.TargetEntitySet is not null)
        {
            throw new InvalidOperationException(
                $"{owner.Name}.{property.Name} is a DataverseReference: use [DataverseLookup(\"{lookup.LogicalName}\")] with one " +
                "[DataverseLookupTarget] per table, not a single target entity set.");
        }

        if (targets.Count == 0)
        {
            throw new InvalidOperationException(
                $"{owner.Name}.{property.Name} needs at least one [DataverseLookupTarget(\"logicalname\", \"entityset\", \"navigationproperty\")].");
        }

        var duplicate = targets.GroupBy(t => t.LogicalName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"{owner.Name}.{property.Name} declares the target '{duplicate.Key}' more than once.");

        return targets;
    }

    // A property whose type, or element type, is itself a mapped entity holds related records.
    private static bool IsNavigation(Type type)
        => ElementType(type) is { } element && DataverseTypeMapping.ResolveEntitySetName(element) != null;

    /// <summary>The related record type a navigation property holds, and whether it holds a collection of them.</summary>
    internal static (Type? Type, bool IsCollection) RelatedRecordType(Type propertyType)
        => (ElementType(propertyType), propertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(propertyType));

    // The record type a property holds: the type itself, or the element type of a collection. Null for scalars.
    private static Type? ElementType(Type type)
    {
        if (type == typeof(string) || type.IsPrimitive || type.IsEnum || Nullable.GetUnderlyingType(type) != null)
            return null;

        var element = type;
        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            element = type.IsArray
                ? type.GetElementType()
                : type.GetInterfaces().Append(type)
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    ?.GetGenericArguments()[0];
        }

        return element is { IsClass: true } && element != typeof(string) ? element : null;
    }

    // "_primarycontactid_value" → "primarycontactid": a lookup mapped by its read name without [DataverseLookup].
    private static string ToLogicalName(string name)
        => name.Length > "__value".Length && name.StartsWith('_') && name.EndsWith("_value", StringComparison.Ordinal)
            ? name[1..^"_value".Length]
            : name;
}
