namespace Dataverse.Fluent.Schema;

/// <summary>Options for <see cref="DataverseSchemaValidator"/>.</summary>
public sealed class SchemaValidationOptions
{
    /// <summary>
    /// Whether each model's columns are also checked with a live request: one <c>GET</c> per model that selects every
    /// mapped column for a random ID. Dataverse rejects a column it cannot select (HTTP 400) before it looks for the
    /// record, so a 404 proves every column can be selected. The request reads no data. Default: <see langword="true"/>.
    /// </summary>
    public bool CheckSelect { get; set; } = true;

    /// <summary>
    /// Whether a choice column mapped to an enum has its options compared with the enum's members, which costs one
    /// metadata request per such column. Default: <see langword="true"/>.
    /// </summary>
    public bool CheckChoiceEnums { get; set; } = true;

    /// <summary>
    /// Codes of findings to leave out of the report, for example <c>SchemaCodes.OptionWithoutEnumMember</c>.
    /// Comparison ignores case.
    /// </summary>
    public ISet<string> IgnoreCodes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How many tables are read from the environment at the same time. Default: 4.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;
}
