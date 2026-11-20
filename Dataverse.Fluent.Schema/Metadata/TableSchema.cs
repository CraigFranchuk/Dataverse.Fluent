namespace Dataverse.Fluent.Schema.Metadata;

/// <summary>
/// What the validator knows about one table, read from <c>EntityDefinitions</c>. Plain data, so a saved snapshot can
/// stand in for a live environment later.
/// </summary>
internal sealed record TableSchema(
    string LogicalName,
    string EntitySetName,
    IReadOnlyDictionary<string, ColumnSchema> Columns,
    IReadOnlyList<ManyToOneSchema> ManyToOne,
    IReadOnlyList<OneToManySchema> OneToMany,
    IReadOnlyList<ManyToManySchema> ManyToMany)
{
    /// <summary>The column with this logical name, or <see langword="null"/>.</summary>
    public ColumnSchema? Column(string logicalName) => Columns.GetValueOrDefault(logicalName);

    /// <summary>
    /// Whether the column is the display name or label of another column (<c>createdbyname</c>, <c>statecodename</c>,
    /// <c>customeridtype</c>). The Web API does not return these as properties: a <c>$select</c> naming one fails, and
    /// the value comes back as an annotation of the column it belongs to.
    /// </summary>
    /// <remarks>
    /// A 2026-11-20 probe of ten tables found that every column <c>$select</c> rejected had <c>AttributeOf</c> set, and
    /// that the only <c>AttributeOf</c> columns it accepted belonged to <c>entityimageid</c>, a uniqueidentifier.
    /// </remarks>
    public bool IsCompanion(ColumnSchema column)
        => column.AttributeOf is { } owner && Column(owner) is { } parent && parent.HasCompanions;

    /// <summary>The many-to-one relationships of a lookup column.</summary>
    public IEnumerable<ManyToOneSchema> RelationshipsOf(string lookupLogicalName)
        => ManyToOne.Where(r => string.Equals(r.ReferencingAttribute, lookupLogicalName, StringComparison.Ordinal));

    /// <summary>
    /// The navigation property that binds <paramref name="lookupLogicalName"/> to <paramref name="target"/>. A customer
    /// lookup has one relationship per table; the owner lookup has one, to the abstract <c>owner</c> table, shared by
    /// both its targets.
    /// </summary>
    public string? NavigationPropertyFor(string lookupLogicalName, string target)
    {
        var relationships = RelationshipsOf(lookupLogicalName).ToList();
        var relationship = relationships.FirstOrDefault(r => string.Equals(r.ReferencedEntity, target, StringComparison.OrdinalIgnoreCase))
            ?? (relationships.Count == 1 ? relationships[0] : null);
        return relationship?.NavigationProperty;
    }
}

/// <summary>A column: its type and whether it can be read, created and updated.</summary>
internal sealed record ColumnSchema(
    string LogicalName,
    string AttributeType,
    string? AttributeTypeName,
    string? AttributeOf,
    bool IsValidForRead,
    bool IsValidForCreate,
    bool IsValidForUpdate)
{
    /// <summary>For a date and time column, its behaviour: <c>UserLocal</c>, <c>DateOnly</c> or <c>TimeZoneIndependent</c>.</summary>
    public string? DateTimeBehavior { get; init; }

    /// <summary>For a lookup column, the tables it can point at.</summary>
    public IReadOnlyList<string> Targets { get; init; } = [];

    /// <summary>Whether the column is a lookup: read as <c>_{column}_value</c> and written as a bind.</summary>
    public bool IsLookup => AttributeType is "Lookup" or "Customer" or "Owner";

    /// <summary>Whether the column is a choice: read and written as its integer value.</summary>
    public bool IsChoice => AttributeType is "Picklist" or "State" or "Status";

    /// <summary>Whether the column is a multi-select choice, which the Web API reads and writes as text such as "1,3".</summary>
    public bool IsMultiSelectChoice => AttributeTypeName == "MultiSelectPicklistType";

    /// <summary>Whether Dataverse adds name or label columns for this one (<c>{column}name</c>).</summary>
    public bool HasCompanions => IsLookup || IsChoice || IsMultiSelectChoice || AttributeType == "Boolean";

    /// <summary>The column's type as the message names it, e.g. "Money" or "DateTime (DateOnly)".</summary>
    public string Description => DateTimeBehavior is { } behavior ? $"{AttributeType} ({behavior})"
        : AttributeType == "Virtual" && AttributeTypeName is { } name ? name.Replace("Type", "", StringComparison.Ordinal)
        : AttributeType;
}

/// <summary>A lookup of this table: <see cref="ReferencingAttribute"/> points at <see cref="ReferencedEntity"/>.</summary>
internal sealed record ManyToOneSchema(string ReferencingAttribute, string ReferencedEntity, string NavigationProperty);

/// <summary>Records of <see cref="ReferencingEntity"/> that point at this table, read through <see cref="NavigationProperty"/>.</summary>
internal sealed record OneToManySchema(string ReferencingEntity, string NavigationProperty);

/// <summary>A many-to-many relationship, read from this table through <see cref="NavigationProperty"/>.</summary>
internal sealed record ManyToManySchema(string OtherEntity, string NavigationProperty);

/// <summary>A table's logical name and entity set.</summary>
internal sealed record TableName(string LogicalName, string EntitySetName);
