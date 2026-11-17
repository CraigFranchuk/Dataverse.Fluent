namespace Dataverse.Fluent;

/// <summary>
/// Indicates that a property is read-only or system-managed and should be excluded from the payloads
/// built from an entity instance (<c>SetFrom()</c>, deep insert children, batch operations).
/// </summary>
/// <remarks>
/// With <c>EntityBuilder.SetFrom()</c>, a create leaves out properties marked for
/// <see cref="DataverseOperation.Create"/>; an update or an upsert that can update leaves out properties
/// marked for either operation, so a create-only marker never sends a default value over an existing record.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class DataverseReadOnlyAttribute : Attribute
{
    /// <summary>
    /// Specifies the operations where this property should be excluded.
    /// </summary>
    public DataverseOperation ExcludeFrom { get; set; } = DataverseOperation.Create;

    /// <summary>
    /// Marks the property as excluded from Create payloads.
    /// </summary>
    public DataverseReadOnlyAttribute()
    {
    }

    /// <summary>
    /// Marks the property as excluded from the payloads of the given operations.
    /// </summary>
    /// <param name="excludeFrom">The operations whose payloads omit the property.</param>
    public DataverseReadOnlyAttribute(DataverseOperation excludeFrom)
    {
        ExcludeFrom = excludeFrom;
    }
}

/// <summary>
/// Specifies which Dataverse operations should exclude a property.
/// </summary>
[Flags]
public enum DataverseOperation
{
    /// <summary>No operations</summary>
    None = 0,
    
    /// <summary>Create operation (POST)</summary>
    Create = 1,
    
    /// <summary>Update operation (PATCH)</summary>
    Update = 2,
    
    /// <summary>Create and Update operations</summary>
    CreateAndUpdate = Create | Update,
    
    /// <summary>All operations</summary>
    All = CreateAndUpdate
}
