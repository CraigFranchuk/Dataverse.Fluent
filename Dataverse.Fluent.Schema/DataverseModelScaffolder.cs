using Dataverse.Fluent.Schema.Metadata;
using Dataverse.Fluent.Schema.Validation;

namespace Dataverse.Fluent.Schema;

/// <summary>
/// Works out, from an environment's metadata, how the named properties of a model should be mapped: the column each
/// one matches, the mapping attribute it needs, the type that holds the column, and whether it must be read-only.
/// The <c>dataverse-fluent scaffold</c> command writes the result into a model's source.
/// </summary>
/// <remarks>
/// Only the properties asked for are described; a model needs only the columns it uses. Scaffolding reads metadata
/// with <c>GET</c> requests only and changes nothing.
/// </remarks>
/// <example>
/// <code>
/// var model = await DataverseModelScaffolder.ScaffoldAsync(client.Transport, new ScaffoldRequest
/// {
///     LogicalName = "incident",
///     Properties = [new ScaffoldProperty("Title"), new ScaffoldProperty("CustomerId", typeof(Guid))]
/// });
/// </code>
/// </example>
public static class DataverseModelScaffolder
{
    /// <summary>Describes the mapping of the requested properties.</summary>
    /// <param name="transport">The environment to read, for example <c>client.Transport</c>.</param>
    /// <param name="request">The table and the properties to describe.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The table and one <see cref="ScaffoldedProperty"/> per requested property, in the order asked.</returns>
    /// <exception cref="ArgumentException">The request names neither a logical name nor an entity set.</exception>
    /// <exception cref="Models.DataverseException">A metadata request failed, for example because the caller cannot read metadata.</exception>
    public static async Task<ScaffoldedModel> ScaffoldAsync(IDataverseTransport transport, ScaffoldRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.LogicalName) && string.IsNullOrWhiteSpace(request.EntitySet))
            throw new ArgumentException("The request needs the table's logical name or its entity set.", nameof(request));

        using var reader = new SchemaReader(transport, maxDegreeOfParallelism: 4);

        var logicalName = string.IsNullOrWhiteSpace(request.LogicalName)
            ? await reader.FindLogicalNameAsync(request.EntitySet!, ct).ConfigureAwait(false)
            : request.LogicalName;
        var table = logicalName is null ? null : await reader.GetTableAsync(logicalName, ct).ConfigureAwait(false);
        if (table is null)
        {
            var all = await reader.GetAllTablesAsync(ct).ConfigureAwait(false);
            var (message, suggestion) = string.IsNullOrWhiteSpace(request.LogicalName)
                ? ($"No table has the entity set '{request.EntitySet}'.", NameSuggestions.DidYouMean(request.EntitySet!, all.Select(t => t.EntitySetName)))
                : ($"Table '{request.LogicalName}' does not exist.", NameSuggestions.DidYouMean(request.LogicalName, all.Select(t => t.LogicalName)));
            return new ScaffoldedModel(null, null, null, [], message, suggestion);
        }

        var properties = new List<ScaffoldedProperty>();
        foreach (var property in request.Properties)
            properties.Add(await DescribeAsync(reader, table, property, request.IncludeChoiceOptions, ct).ConfigureAwait(false));

        return new ScaffoldedModel(table.LogicalName, table.EntitySetName, table.SchemaName, properties, null, null);
    }

    private static async Task<ScaffoldedProperty> DescribeAsync(SchemaReader reader, TableSchema table, ScaffoldProperty property, bool includeOptions, CancellationToken ct)
    {
        var column = table.Columns.Values.FirstOrDefault(c => string.Equals(c.LogicalName, property.Name, StringComparison.OrdinalIgnoreCase));
        if (column is null)
        {
            return ScaffoldedProperty.NotFound(property.Name, $"No column '{property.Name.ToLowerInvariant()}' on {table.LogicalName}.",
                NameSuggestions.DidYouMean(property.Name.ToLowerInvariant(), table.Columns.Values.Where(c => !table.IsCompanion(c)).Select(c => c.LogicalName)));
        }

        if (table.IsCompanion(column))
        {
            var owner = table.Column(column.AttributeOf!)!;
            return ScaffoldedProperty.NotFound(property.Name,
                $"'{column.LogicalName}' is the display value of '{owner.LogicalName}', which the Web API returns as an annotation, not as a column.",
                $"Map '{owner.LogicalName}' instead.");
        }

        var type = property.Type is null ? null : Nullable.GetUnderlyingType(property.Type) ?? property.Type;
        var readOnlyFor = !column.IsValidForCreate && !column.IsValidForUpdate ? DataverseOperation.CreateAndUpdate
            : !column.IsValidForUpdate ? DataverseOperation.Update
            : DataverseOperation.None;

        if (column.IsLookup)
            return await DescribeLookupAsync(reader, table, property, column, type, readOnlyFor, ct).ConfigureAwait(false);

        var recommended = RecommendedType(column);
        IReadOnlyList<ScaffoldedOption> options = [];
        if (includeOptions && column.IsChoice && await reader.GetOptionsAsync(table.LogicalName, column, ct).ConfigureAwait(false) is { } read)
            options = [.. read.Select(o => new ScaffoldedOption(o.Value, o.Label))];

        return new ScaffoldedProperty(property.Name, ScaffoldedPropertyKind.Column, column.LogicalName)
        {
            SchemaName = column.SchemaName,
            ClrType = recommended?.Name,
            ClrTypeIsValueType = recommended?.IsValueType ?? false,
            // A type the rules do not know, or a column they do not know, is left as the model has it.
            TypeFits = type is null ? null : recommended is null || ClrTypeRules.Check(column, type) is null,
            ReadOnlyFor = readOnlyFor,
            IsChoice = column.IsChoice,
            Options = options
        };
    }

    private static async Task<ScaffoldedProperty> DescribeLookupAsync(
        SchemaReader reader, TableSchema table, ScaffoldProperty property, ColumnSchema column, Type? type, DataverseOperation readOnlyFor, CancellationToken ct)
    {
        var sets = await reader.GetEntitySetsAsync(column.Targets, ct).ConfigureAwait(false);
        var targets = column.Targets
            .Select(t => (Table: t, Set: sets[t], Navigation: table.NavigationPropertyFor(column.LogicalName, t)))
            .Where(t => t.Set is not null && t.Navigation is not null)
            .Select(t => new ScaffoldedLookupTarget(t.Table, t.Set!, t.Navigation!))
            .ToList();
        if (targets.Count == 0)
        {
            return ScaffoldedProperty.NotFound(property.Name,
                $"'{column.LogicalName}' is a lookup, but the metadata names no table it can be bound to.", null);
        }

        // One table: a Guid. Several, or a property that is already a DataverseReference: a reference with its targets.
        var polymorphic = column.Targets.Count > 1 || type == typeof(DataverseReference);
        return new ScaffoldedProperty(property.Name, polymorphic ? ScaffoldedPropertyKind.PolymorphicLookup : ScaffoldedPropertyKind.Lookup, column.LogicalName)
        {
            SchemaName = column.SchemaName,
            ClrType = polymorphic ? nameof(DataverseReference) : nameof(Guid),
            ClrTypeIsValueType = !polymorphic,
            TypeFits = type is null ? null : type == (polymorphic ? typeof(DataverseReference) : typeof(Guid)),
            ReadOnlyFor = readOnlyFor,
            Targets = targets
        };
    }

    // The type that holds every value of the column, or null for a column type the rules do not know.
    private static (string Name, bool IsValueType)? RecommendedType(ColumnSchema column)
    {
        if (column.IsMultiSelectChoice)
            return ("string", false);

        return column.AttributeType switch
        {
            "String" or "Memo" or "EntityName" => ("string", false),
            "Boolean" => ("bool", true),
            "Uniqueidentifier" => ("Guid", true),
            "Integer" or "Picklist" or "State" or "Status" => ("int", true),
            "BigInt" => ("long", true),
            "Decimal" or "Money" => ("decimal", true),
            "Double" => ("double", true),
            "DateTime" => (column.DateTimeBehavior == "DateOnly" ? "DateOnly" : "DateTime", true),
            "Virtual" when column.AttributeTypeName == "ImageType" => ("byte[]", false),
            _ => null
        };
    }
}

/// <summary>What <see cref="DataverseModelScaffolder.ScaffoldAsync"/> is asked to describe.</summary>
public sealed class ScaffoldRequest
{
    /// <summary>The table's logical name (e.g. "incident"). Either this or <see cref="EntitySet"/> is needed.</summary>
    public string? LogicalName { get; init; }

    /// <summary>The table's entity set (e.g. "incidents"), used when <see cref="LogicalName"/> is not given.</summary>
    public string? EntitySet { get; init; }

    /// <summary>The properties to describe. Each is matched to a column by name, ignoring case.</summary>
    public IReadOnlyList<ScaffoldProperty> Properties { get; init; } = [];

    /// <summary>Whether to read the options of choice columns into <see cref="ScaffoldedProperty.Options"/>, to write an enum from.</summary>
    public bool IncludeChoiceOptions { get; init; }
}

/// <summary>A property to scaffold: the name to match to a column, and the type the model gives it now.</summary>
public sealed class ScaffoldProperty
{
    /// <summary>Initializes a new instance of <see cref="ScaffoldProperty"/>.</summary>
    /// <param name="name">The property's name, or the column's logical name. It is matched ignoring case.</param>
    /// <param name="type">The property's current type, or <see langword="null"/> when there is none or it is not known.</param>
    public ScaffoldProperty(string name, Type? type = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Type = type;
    }

    /// <summary>The name matched to a column.</summary>
    public string Name { get; }

    /// <summary>The property's current type, if known. It decides <see cref="ScaffoldedProperty.TypeFits"/>.</summary>
    public Type? Type { get; }
}

/// <summary>A table and the mapping of the properties asked for.</summary>
public sealed class ScaffoldedModel
{
    internal ScaffoldedModel(string? logicalName, string? entitySet, string? schemaName, IReadOnlyList<ScaffoldedProperty> properties, string? message, string? suggestion)
    {
        LogicalName = logicalName;
        EntitySet = entitySet;
        SchemaName = schemaName;
        Properties = properties;
        Message = message;
        Suggestion = suggestion;
    }

    /// <summary>Whether the table exists. When it does not, see <see cref="Message"/> and <see cref="Suggestion"/>.</summary>
    public bool TableFound => LogicalName is not null;

    /// <summary>The table's logical name, or <see langword="null"/> when it was not found.</summary>
    public string? LogicalName { get; }

    /// <summary>The table's entity set, or <see langword="null"/> when it was not found.</summary>
    public string? EntitySet { get; }

    /// <summary>The table's schema name (e.g. "Incident"), a starting point for a class name.</summary>
    public string? SchemaName { get; }

    /// <summary>One entry per requested property, in the order asked.</summary>
    public IReadOnlyList<ScaffoldedProperty> Properties { get; }

    /// <summary>Why the table was not found; otherwise <see langword="null"/>.</summary>
    public string? Message { get; }

    /// <summary>The closest table names when the table was not found, if any are close.</summary>
    public string? Suggestion { get; }
}

/// <summary>What a scaffolded property maps to.</summary>
public enum ScaffoldedPropertyKind
{
    /// <summary>No column matches the property. See <see cref="ScaffoldedProperty.Message"/>.</summary>
    NotFound,

    /// <summary>A column: <c>[DataverseColumn("logicalname")]</c>.</summary>
    Column,

    /// <summary>A lookup to one table, on a <see cref="Guid"/>: <c>[DataverseLookup("logicalname", "entityset")]</c>.</summary>
    Lookup,

    /// <summary>A lookup on a <see cref="DataverseReference"/>: <c>[DataverseLookup("logicalname")]</c> and one
    /// <c>[DataverseLookupTarget]</c> per table.</summary>
    PolymorphicLookup
}

/// <summary>How one property should be mapped.</summary>
public sealed class ScaffoldedProperty
{
    internal ScaffoldedProperty(string name, ScaffoldedPropertyKind kind, string? logicalName)
    {
        Name = name;
        Kind = kind;
        LogicalName = logicalName;
    }

    /// <summary>The name that was asked for.</summary>
    public string Name { get; }

    /// <summary>What the property maps to.</summary>
    public ScaffoldedPropertyKind Kind { get; }

    /// <summary>The column's logical name, or <see langword="null"/> when no column matches.</summary>
    public string? LogicalName { get; }

    /// <summary>The column's schema name (e.g. "FollowupBy"), a starting point for a property name.</summary>
    public string? SchemaName { get; internal init; }

    /// <summary>
    /// The C# type that holds the column, without a nullable marker: <c>string</c>, <c>int</c>, <c>decimal</c>,
    /// <c>DateOnly</c>, <c>Guid</c>, <c>DataverseReference</c>. <see langword="null"/> for a column type the validator
    /// has no rule for.
    /// </summary>
    public string? ClrType { get; internal init; }

    /// <summary>Whether <see cref="ClrType"/> is a value type, which a model declares as nullable (<c>int?</c>).</summary>
    public bool ClrTypeIsValueType { get; internal init; }

    /// <summary>
    /// Whether the type given in the request holds the column: <see langword="false"/> when it must change to
    /// <see cref="ClrType"/>, and <see langword="null"/> when the request gave no type.
    /// </summary>
    public bool? TypeFits { get; internal init; }

    /// <summary>The operations the column cannot be written by, for <c>[DataverseReadOnly]</c>.</summary>
    public DataverseOperation ReadOnlyFor { get; internal init; }

    /// <summary>
    /// The tables a lookup can point at, each with its entity set and navigation property. One entry for
    /// <see cref="ScaffoldedPropertyKind.Lookup"/>; empty for a column.
    /// </summary>
    public IReadOnlyList<ScaffoldedLookupTarget> Targets { get; internal init; } = [];

    /// <summary>Whether the column is a choice, which an enum can hold in place of <c>int</c>.</summary>
    public bool IsChoice { get; internal init; }

    /// <summary>The options of a choice column, when <see cref="ScaffoldRequest.IncludeChoiceOptions"/> was set.</summary>
    public IReadOnlyList<ScaffoldedOption> Options { get; internal init; } = [];

    /// <summary>Why the property was not matched; otherwise <see langword="null"/>.</summary>
    public string? Message { get; private init; }

    /// <summary>What to use instead when the property was not matched, if the scaffolder can tell.</summary>
    public string? Suggestion { get; private init; }

    internal static ScaffoldedProperty NotFound(string name, string message, string? suggestion)
        => new(name, ScaffoldedPropertyKind.NotFound, null) { Message = message, Suggestion = suggestion };
}

/// <summary>A table a lookup can point at.</summary>
/// <param name="LogicalName">The table's logical name.</param>
/// <param name="EntitySet">The table's entity set.</param>
/// <param name="NavigationProperty">The navigation property the lookup is bound through for this table.</param>
public sealed record ScaffoldedLookupTarget(string LogicalName, string EntitySet, string NavigationProperty);

/// <summary>An option of a choice column.</summary>
/// <param name="Value">The option's value.</param>
/// <param name="Label">The option's label in the caller's language, when the metadata gives one.</param>
public sealed record ScaffoldedOption(int Value, string? Label);
