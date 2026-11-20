using System.Reflection;
using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent;

/// <summary>
/// How a POCO type maps to Dataverse, as the library reads and writes it: the table, its entity set, and what each
/// public property maps to. The same mapping drives URL building, payloads, <c>SelectAll()</c> and read-only
/// filtering, so a tool that checks a model against an environment sees exactly what the client sends.
/// </summary>
/// <remarks>
/// <para>
/// The mapping comes from the POCO alone (its attributes and any registered <see cref="IDataverseNameResolver"/>);
/// nothing is read from the environment. <c>Dataverse.Fluent.Schema</c> compares it with the environment's metadata.
/// </para>
/// <para>
/// Each call builds a new description; the classification underneath is cached per type.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var mapping = DataverseEntityMapping.For&lt;Incident&gt;();
/// foreach (var member in mapping.Members)
///     Console.WriteLine($"{member.Property.Name}: {member.Kind} {member.LogicalName}");
/// </code>
/// </example>
public sealed class DataverseEntityMapping
{
    private DataverseEntityMapping(Type type, string? entitySet, string? logicalName, IReadOnlyList<DataverseMemberMapping> members)
    {
        Type = type;
        EntitySet = entitySet;
        LogicalName = logicalName;
        Members = members;
    }

    /// <summary>The POCO type.</summary>
    public Type Type { get; }

    /// <summary>
    /// The entity set the type is read and written through (e.g. "accounts"), or <see langword="null"/> when the type
    /// is not mapped to a table (no <see cref="DataverseEntityAttribute"/>, <see cref="EntitySetAttribute"/> or resolver).
    /// </summary>
    public string? EntitySet { get; }

    /// <summary>
    /// The table's logical name (e.g. "account"), or <see langword="null"/> when only the entity set is known, as with
    /// <see cref="EntitySetAttribute"/>.
    /// </summary>
    public string? LogicalName { get; }

    /// <summary>Whether the type is mapped to a table: <see cref="EntitySet"/> is known.</summary>
    public bool IsMapped => EntitySet is not null;

    /// <summary>The type's public instance properties with a getter, in declaration order, and what each maps to.</summary>
    public IReadOnlyList<DataverseMemberMapping> Members { get; }

    /// <summary>Returns the mapping of <typeparamref name="T"/>.</summary>
    /// <exception cref="InvalidOperationException">A property's mapping attributes are invalid, for example
    /// <see cref="DataverseLookupAttribute"/> on a property that is not a <see cref="Guid"/> or
    /// <see cref="DataverseReference"/>. The same error is thrown when the client first uses the type.</exception>
    public static DataverseEntityMapping For<T>() => For(typeof(T));

    /// <summary>Returns the mapping of <paramref name="type"/>.</summary>
    /// <param name="type">The POCO type.</param>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A property's mapping attributes are invalid, for example
    /// <see cref="DataverseLookupAttribute"/> on a property that is not a <see cref="Guid"/> or
    /// <see cref="DataverseReference"/>. The same error is thrown when the client first uses the type.</exception>
    public static DataverseEntityMapping For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var metadata = EntityTypeMetadata.For(type);
        var members = metadata.Members.Select(m => new DataverseMemberMapping(m)).ToList();
        return new DataverseEntityMapping(
            type,
            DataverseTypeMapping.ResolveEntitySetName(type),
            DataverseTypeMapping.ResolveEntityLogicalName(type),
            members);
    }
}

/// <summary>What a POCO property maps to in Dataverse. See <see cref="DataverseMemberMapping.Kind"/>.</summary>
public enum DataverseMemberKind
{
    /// <summary>A column, read, selected and written under the same name.</summary>
    Column,

    /// <summary>
    /// A single-table lookup (<see cref="DataverseLookupAttribute"/> on a <see cref="Guid"/>): read as
    /// <c>_{column}_value</c> and written as <c>{navigation}@odata.bind</c>.
    /// </summary>
    Lookup,

    /// <summary>
    /// A polymorphic lookup (<see cref="DataverseLookupAttribute"/> on a <see cref="DataverseReference"/>): read as
    /// <c>_{column}_value</c> with its table, and written as the bind of the target it points at.
    /// </summary>
    PolymorphicLookup,

    /// <summary>A related record or collection, read from <c>$expand</c> and never written or selected.</summary>
    Navigation,

    /// <summary>Excluded with <c>[JsonIgnore]</c>.</summary>
    Ignored
}

/// <summary>A POCO property and what it maps to in Dataverse. Part of a <see cref="DataverseEntityMapping"/>.</summary>
public sealed class DataverseMemberMapping
{
    internal DataverseMemberMapping(EntityMember member)
    {
        Property = member.Property;
        Kind = member.Kind switch
        {
            EntityMemberKind.Column => DataverseMemberKind.Column,
            EntityMemberKind.Lookup => DataverseMemberKind.Lookup,
            EntityMemberKind.PolymorphicLookup => DataverseMemberKind.PolymorphicLookup,
            EntityMemberKind.Navigation => DataverseMemberKind.Navigation,
            _ => DataverseMemberKind.Ignored
        };
        Name = member.Name;
        LogicalName = member.LogicalName;
        Lookup = member.Lookup;
        Targets = member.Targets;
        ReadOnlyFor = member.Property.GetCustomAttribute<DataverseReadOnlyAttribute>()?.ExcludeFrom ?? DataverseOperation.None;
        (RelatedType, IsCollection) = Kind == DataverseMemberKind.Navigation
            ? EntityTypeMetadata.RelatedRecordType(member.Property.PropertyType)
            : (null, false);
    }

    /// <summary>The property.</summary>
    public PropertyInfo Property { get; }

    /// <summary>What the property maps to.</summary>
    public DataverseMemberKind Kind { get; }

    /// <summary>
    /// The name the property is read, selected and filtered by: the column's logical name, <c>_{column}_value</c> for a
    /// lookup, the navigation property for a related record, or an annotated name such as
    /// <c>createdon@OData.Community.Display.V1.FormattedValue</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The column's logical name, as FetchXML uses it: <see cref="Name"/> without the <c>_{column}_value</c> wrapping of
    /// a lookup. For a navigation property it is the navigation property's name.
    /// </summary>
    public string LogicalName { get; }

    /// <summary>The lookup mapping, for <see cref="DataverseMemberKind.Lookup"/> and
    /// <see cref="DataverseMemberKind.PolymorphicLookup"/>; otherwise <see langword="null"/>.</summary>
    public DataverseLookupAttribute? Lookup { get; }

    /// <summary>The tables a <see cref="DataverseMemberKind.PolymorphicLookup"/> can point at; empty otherwise.</summary>
    public IReadOnlyList<DataverseLookupTargetAttribute> Targets { get; }

    /// <summary>The operations whose payloads leave the property out, from <see cref="DataverseReadOnlyAttribute"/>.</summary>
    public DataverseOperation ReadOnlyFor { get; }

    /// <summary>
    /// For <see cref="DataverseMemberKind.Navigation"/>, the type of the related record (the element type of a
    /// collection); otherwise <see langword="null"/>.
    /// </summary>
    public Type? RelatedType { get; }

    /// <summary>Whether a <see cref="DataverseMemberKind.Navigation"/> property holds a collection of related records.</summary>
    public bool IsCollection { get; }

    /// <summary>
    /// Whether <c>SetFrom</c>, batches, deep inserts and bulk operations send the property when they create a record.
    /// A column or lookup is sent unless it is marked read-only for <see cref="DataverseOperation.Create"/>.
    /// </summary>
    public bool IsWrittenOnCreate => IsWritable && !ReadOnlyFor.HasFlag(DataverseOperation.Create);

    /// <summary>
    /// Whether <c>SetFrom</c>, batches and bulk operations send the property when they update a record (including an
    /// upsert). A column or lookup is sent unless it carries any read-only marker: a create-only marker also keeps it
    /// out of updates, so its default value is never written over an existing record.
    /// </summary>
    public bool IsWrittenOnUpdate => IsWritable && ReadOnlyFor == DataverseOperation.None;

    private bool IsWritable => Kind is DataverseMemberKind.Column or DataverseMemberKind.Lookup or DataverseMemberKind.PolymorphicLookup;
}
