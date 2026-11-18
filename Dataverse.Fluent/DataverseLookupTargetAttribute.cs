namespace Dataverse.Fluent;

/// <summary>
/// Declares one table a polymorphic lookup can point at, and the navigation property that binds it. Apply one per
/// target to a <see cref="DataverseReference"/> property marked with <see cref="DataverseLookupAttribute(string)"/>.
/// </summary>
/// <remarks>
/// Dataverse names the navigation property per target, and the names do not follow one rule, so each target states
/// its own: <c>customerid_account</c> for a customer, <c>ownerid</c> for both owner targets, and
/// <c>regardingobjectid_account_task</c> for the regarding lookup of a task. The names are in each table's
/// <c>$metadata</c> or the Dataverse Web API reference.
/// </remarks>
/// <example>
/// <code>
/// [DataverseLookup("ownerid")]
/// [DataverseLookupTarget("systemuser", "systemusers", "ownerid")]
/// [DataverseLookupTarget("team", "teams", "ownerid")]
/// public DataverseReference? OwnerId { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class DataverseLookupTargetAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of <see cref="DataverseLookupTargetAttribute"/>.
    /// </summary>
    /// <param name="logicalName">The target table's logical name (e.g., "account").</param>
    /// <param name="entitySet">The target table's entity set (e.g., "accounts").</param>
    /// <param name="navigationProperty">The single-valued navigation property that binds this target
    /// (e.g., "customerid_account").</param>
    public DataverseLookupTargetAttribute(string logicalName, string entitySet, string navigationProperty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entitySet);
        ArgumentException.ThrowIfNullOrWhiteSpace(navigationProperty);
        LogicalName = logicalName.ToLowerInvariant();
        EntitySet = entitySet;
        NavigationProperty = navigationProperty;
    }

    /// <summary>The target table's logical name (e.g., "account").</summary>
    public string LogicalName { get; }

    /// <summary>The target table's entity set (e.g., "accounts").</summary>
    public string EntitySet { get; }

    /// <summary>The single-valued navigation property that binds this target (e.g., "customerid_account").</summary>
    public string NavigationProperty { get; }
}
