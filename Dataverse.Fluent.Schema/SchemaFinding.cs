using System.Reflection;

namespace Dataverse.Fluent.Schema;

/// <summary>How serious a <see cref="SchemaFinding"/> is.</summary>
public enum SchemaSeverity
{
    /// <summary>Worth knowing; nothing fails.</summary>
    Info = 0,

    /// <summary>The model works, but loses data or sends values Dataverse ignores.</summary>
    Warning = 1,

    /// <summary>A read returns nothing or a write fails.</summary>
    Error = 2
}

/// <summary>
/// One mismatch between a model and the environment's metadata. <see cref="Code"/> identifies the kind of finding
/// (see <see cref="SchemaCodes"/>).
/// </summary>
public sealed class SchemaFinding
{
    internal SchemaFinding(string code, SchemaSeverity severity, Type model, PropertyInfo? property, string message,
        string? suggestion = null, SchemaFix? fix = null)
    {
        Code = code;
        Severity = severity;
        Model = model;
        Property = property;
        Message = message;
        Suggestion = suggestion;
        Fix = fix;
    }

    /// <summary>The finding's code, for example <c>DVS010</c>. See <see cref="SchemaCodes"/>.</summary>
    public string Code { get; }

    /// <summary>How serious the finding is.</summary>
    public SchemaSeverity Severity { get; }

    /// <summary>The model that was validated. For a property inherited from a base model, the base model.</summary>
    public Type Model { get; }

    /// <summary>The property the finding is about, or <see langword="null"/> for a finding about the whole model.</summary>
    public PropertyInfo? Property { get; }

    /// <summary>What is wrong.</summary>
    public string Message { get; }

    /// <summary>What to change, when the validator can tell, for example the name of a similar column.</summary>
    public string? Suggestion { get; }

    /// <summary>
    /// The change that corrects the finding mechanically, or <see langword="null"/> when it needs a decision. The
    /// <c>dataverse-fluent fix</c> command applies it to the model's source.
    /// </summary>
    public SchemaFix? Fix { get; }

    /// <summary>The model and property, for example <c>Incident.CustomerId</c>.</summary>
    public string Location => Property is null ? Model.Name : $"{Model.Name}.{Property.Name}";

    /// <summary>The finding as one line: location, code, message, and the suggestion or fix.</summary>
    public override string ToString()
        => $"{Location}: {Code} {Message}" + (Suggestion is not null ? " " + Suggestion : Fix is not null ? $" Fix: {Fix.Description}." : string.Empty);
}

/// <summary>The kind of change a <see cref="SchemaFix"/> makes to a model's attributes.</summary>
public enum SchemaFixKind
{
    /// <summary>Set the entity set of <see cref="DataverseEntityAttribute"/> or <see cref="EntitySetAttribute"/> to <see cref="SchemaFix.EntitySet"/>.</summary>
    SetEntitySet,

    /// <summary>Set the target entity set of a single-table <see cref="DataverseLookupAttribute"/> to <see cref="SchemaFix.EntitySet"/>.</summary>
    SetLookupTargetEntitySet,

    /// <summary>Set <see cref="DataverseLookupAttribute.NavigationProperty"/> to <see cref="SchemaFix.NavigationProperty"/>.</summary>
    SetLookupNavigationProperty,

    /// <summary>Add a <see cref="DataverseLookupTargetAttribute"/> for <see cref="SchemaFix.TargetLogicalName"/>.</summary>
    AddLookupTarget,

    /// <summary>Replace the entity set and navigation property of the <see cref="DataverseLookupTargetAttribute"/> for
    /// <see cref="SchemaFix.TargetLogicalName"/>.</summary>
    SetLookupTarget,

    /// <summary>Set the property's <see cref="DataverseReadOnlyAttribute"/> to <see cref="SchemaFix.ReadOnlyFor"/>, adding it when absent.</summary>
    SetReadOnly,

    /// <summary>
    /// Replace the property's <c>[JsonPropertyName("_{column}_value")]</c> or <see cref="DataverseColumnAttribute"/>
    /// with <see cref="DataverseLookupAttribute"/> for <see cref="SchemaFix.LookupLogicalName"/>,
    /// <see cref="SchemaFix.EntitySet"/> and <see cref="SchemaFix.NavigationProperty"/>. When
    /// <see cref="SchemaFix.ReadOnlyFor"/> is not <see cref="DataverseOperation.None"/>, also add that
    /// <see cref="DataverseReadOnlyAttribute"/>: the lookup cannot be written by those operations.
    /// </summary>
    MapAsLookup
}

/// <summary>
/// A mechanical correction for a <see cref="SchemaFinding"/>, described as data. The validator never changes source;
/// the <c>dataverse-fluent fix</c> command turns a fix into an edit of the model's attributes.
/// </summary>
public sealed class SchemaFix
{
    private SchemaFix(SchemaFixKind kind, string description)
    {
        Kind = kind;
        Description = description;
    }

    /// <summary>What the fix changes.</summary>
    public SchemaFixKind Kind { get; }

    /// <summary>The change in words, for example <c>set NavigationProperty = "new_RegionId"</c>.</summary>
    public string Description { get; }

    /// <summary>The entity set to set, for <see cref="SchemaFixKind.SetEntitySet"/>,
    /// <see cref="SchemaFixKind.SetLookupTargetEntitySet"/>, <see cref="SchemaFixKind.AddLookupTarget"/>,
    /// <see cref="SchemaFixKind.SetLookupTarget"/> and <see cref="SchemaFixKind.MapAsLookup"/>.</summary>
    public string? EntitySet { get; private init; }

    /// <summary>The navigation property to set, for <see cref="SchemaFixKind.SetLookupNavigationProperty"/>,
    /// <see cref="SchemaFixKind.AddLookupTarget"/>, <see cref="SchemaFixKind.SetLookupTarget"/> and
    /// <see cref="SchemaFixKind.MapAsLookup"/>.</summary>
    public string? NavigationProperty { get; private init; }

    /// <summary>The target table, for <see cref="SchemaFixKind.AddLookupTarget"/> and <see cref="SchemaFixKind.SetLookupTarget"/>.</summary>
    public string? TargetLogicalName { get; private init; }

    /// <summary>The lookup column, for <see cref="SchemaFixKind.MapAsLookup"/>.</summary>
    public string? LookupLogicalName { get; private init; }

    /// <summary>The operations to mark read-only, for <see cref="SchemaFixKind.SetReadOnly"/>, and for a
    /// <see cref="SchemaFixKind.MapAsLookup"/> of a lookup that cannot be updated.</summary>
    public DataverseOperation ReadOnlyFor { get; private init; }

    /// <inheritdoc />
    public override string ToString() => Description;

    internal static SchemaFix SetEntitySet(string entitySet)
        => new(SchemaFixKind.SetEntitySet, $"set the entity set to \"{entitySet}\"") { EntitySet = entitySet };

    internal static SchemaFix SetLookupTargetEntitySet(string entitySet)
        => new(SchemaFixKind.SetLookupTargetEntitySet, $"set the target entity set to \"{entitySet}\"") { EntitySet = entitySet };

    internal static SchemaFix SetLookupNavigationProperty(string navigationProperty)
        => new(SchemaFixKind.SetLookupNavigationProperty, $"set NavigationProperty = \"{navigationProperty}\"") { NavigationProperty = navigationProperty };

    internal static SchemaFix AddLookupTarget(string logicalName, string entitySet, string navigationProperty)
        => new(SchemaFixKind.AddLookupTarget, $"add [DataverseLookupTarget(\"{logicalName}\", \"{entitySet}\", \"{navigationProperty}\")]")
        {
            TargetLogicalName = logicalName,
            EntitySet = entitySet,
            NavigationProperty = navigationProperty
        };

    internal static SchemaFix SetLookupTarget(string logicalName, string entitySet, string navigationProperty)
        => new(SchemaFixKind.SetLookupTarget, $"use [DataverseLookupTarget(\"{logicalName}\", \"{entitySet}\", \"{navigationProperty}\")]")
        {
            TargetLogicalName = logicalName,
            EntitySet = entitySet,
            NavigationProperty = navigationProperty
        };

    internal static SchemaFix SetReadOnly(DataverseOperation readOnlyFor)
        => new(SchemaFixKind.SetReadOnly, $"mark it [DataverseReadOnly(DataverseOperation.{OperationName(readOnlyFor)})]") { ReadOnlyFor = readOnlyFor };

    /// <summary>
    /// The member name to write for <paramref name="operations"/>. <c>CreateAndUpdate</c> and <c>All</c> share a value,
    /// so <see cref="Enum.ToString()"/> may return either; the code base writes <c>CreateAndUpdate</c>.
    /// </summary>
    internal static string OperationName(DataverseOperation operations) => operations switch
    {
        DataverseOperation.Create => nameof(DataverseOperation.Create),
        DataverseOperation.Update => nameof(DataverseOperation.Update),
        DataverseOperation.CreateAndUpdate => nameof(DataverseOperation.CreateAndUpdate),
        _ => nameof(DataverseOperation.None)
    };

    internal static SchemaFix MapAsLookup(string logicalName, string entitySet, string navigationProperty, DataverseOperation readOnlyFor = DataverseOperation.None)
        => new(SchemaFixKind.MapAsLookup, (logicalName == navigationProperty
            ? $"map it with [DataverseLookup(\"{logicalName}\", \"{entitySet}\")]"
            : $"map it with [DataverseLookup(\"{logicalName}\", \"{entitySet}\", NavigationProperty = \"{navigationProperty}\")]")
            + (readOnlyFor == DataverseOperation.None ? "" : $" and [DataverseReadOnly(DataverseOperation.{OperationName(readOnlyFor)})]"))
        {
            LookupLogicalName = logicalName,
            EntitySet = entitySet,
            NavigationProperty = navigationProperty,
            ReadOnlyFor = readOnlyFor
        };
}
