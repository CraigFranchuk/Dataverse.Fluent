namespace Dataverse.Fluent;

/// <summary>
/// Maps a <see cref="Guid"/> or <see cref="Nullable{Guid}"/> property to a Dataverse lookup column. The library
/// then reads, filters and selects the lookup as <c>_{column}_value</c> and writes it as
/// <c>{navigation}@odata.bind: "/{targetEntitySet}({id})"</c>, so a retrieved record can be written back
/// with <c>SetFrom</c>.
/// </summary>
/// <remarks>
/// <para>
/// The lookup must point at a single table. A polymorphic lookup (customer, owner, regarding) has one
/// navigation property per target table; set it with <c>SetLookup("parentcustomerid_account", ...)</c>.
/// </para>
/// <para>
/// Setting the property to <see langword="null"/> with <c>Set(x =&gt; x.Lookup, null)</c> clears the lookup.
/// <c>SetFrom</c> skips lookups that are <see langword="null"/> or <see cref="Guid.Empty"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DataverseLookup("primarycontactid", "contacts")]
/// public Guid? PrimaryContactId { get; set; }
///
/// // A custom lookup whose navigation property is the schema name:
/// [DataverseLookup("new_regionid", "new_regions", NavigationProperty = "new_RegionId")]
/// public Guid? RegionId { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DataverseLookupAttribute : Attribute
{
    private string? _navigationProperty;

    /// <summary>
    /// Initializes a new instance of <see cref="DataverseLookupAttribute"/>.
    /// </summary>
    /// <param name="logicalName">The lookup column's logical name (e.g., "primarycontactid").</param>
    /// <param name="targetEntitySet">The entity set of the table the lookup points at (e.g., "contacts").</param>
    public DataverseLookupAttribute(string logicalName, string targetEntitySet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetEntitySet);
        LogicalName = logicalName;
        TargetEntitySet = targetEntitySet;
    }

    /// <summary>The lookup column's logical name (e.g., "primarycontactid").</summary>
    public string LogicalName { get; }

    /// <summary>The entity set of the table the lookup points at (e.g., "contacts").</summary>
    public string TargetEntitySet { get; }

    /// <summary>
    /// The single-valued navigation property used to write the lookup. Defaults to <see cref="LogicalName"/>,
    /// which matches system lookups; for a custom lookup it is usually the column's schema name
    /// (e.g., "new_RegionId").
    /// </summary>
    public string NavigationProperty
    {
        get => _navigationProperty ?? LogicalName;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _navigationProperty = value;
        }
    }

    /// <summary>The name the lookup is read, selected and filtered by: <c>_{logicalname}_value</c>.</summary>
    internal string ValueName => $"_{LogicalName}_value";
}
