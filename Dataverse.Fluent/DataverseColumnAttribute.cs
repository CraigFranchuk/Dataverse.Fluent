namespace Dataverse.Fluent;

/// <summary>
/// Maps a POCO property to a Dataverse column by logical name.
/// When present, the library uses this as the JSON property name for serialization,
/// deserialization, query building, and expression translation — eliminating the
/// need for a separate <c>[JsonPropertyName]</c> attribute.
/// </summary>
/// <example>
/// <code>
/// [EntitySet("accounts")]
/// public class Account
/// {
///     [DataverseColumn("accountid")]
///     public Guid Id { get; set; }
///
///     [DataverseColumn("name")]
///     public string Name { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DataverseColumnAttribute : Attribute
{
    /// <summary>
    /// The Dataverse logical name of the column (e.g., "name", "revenue").
    /// </summary>
    public string LogicalName { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="DataverseColumnAttribute"/> with the specified logical name.
    /// </summary>
    /// <param name="logicalName">The Dataverse column logical name.</param>
    public DataverseColumnAttribute(string logicalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
        LogicalName = logicalName;
    }
}
