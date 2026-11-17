namespace Dataverse.Fluent;

/// <summary>
/// Maps a POCO type to a Dataverse table: its logical name (used by FetchXML) and its OData entity set name
/// (used in Web API URLs).
/// </summary>
/// <remarks>
/// <see cref="EntitySetAttribute"/> still works for types used only with the Web API; this attribute adds the
/// logical name, which FetchXML queries need and which cannot be derived from the entity set name.
/// </remarks>
/// <example>
/// <code>
/// [DataverseEntity("opportunityproduct", "opportunityproducts")]
/// public class OpportunityProduct { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class DataverseEntityAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of <see cref="DataverseEntityAttribute"/>.
    /// </summary>
    /// <param name="logicalName">The table's logical name (e.g., "account").</param>
    /// <param name="entitySet">The table's OData entity set name (e.g., "accounts").</param>
    public DataverseEntityAttribute(string logicalName, string entitySet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entitySet);
        LogicalName = logicalName;
        EntitySet = entitySet;
    }

    /// <summary>The table's logical name (e.g., "account").</summary>
    public string LogicalName { get; }

    /// <summary>The table's OData entity set name (e.g., "accounts").</summary>
    public string EntitySet { get; }
}
