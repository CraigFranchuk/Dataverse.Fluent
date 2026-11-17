namespace Dataverse.Fluent.Models;

/// <summary>
/// The outcome of a create operation: the new record's ID and, when requested via
/// <c>ReturnRepresentation()</c>, the created record as returned by Dataverse.
/// </summary>
/// <typeparam name="T">The POCO type of the created entity.</typeparam>
public sealed class DataverseCreateResult<T>
{
    /// <summary>
    /// The primary key of the created record, parsed from the <c>OData-EntityId</c> response header.
    /// </summary>
    /// <remarks>
    /// Dataverse does not return the <c>OData-EntityId</c> header when <c>Prefer: return=representation</c>
    /// is used, so this is <see langword="null"/> in that case; read the key from <see cref="Entity"/> instead.
    /// </remarks>
    public Guid? Id { get; }

    /// <summary>
    /// The created record, populated only when <c>ReturnRepresentation()</c> was requested.
    /// </summary>
    public T? Entity { get; }

    /// <summary>
    /// The raw <c>OData-EntityId</c> URL of the created record, when returned.
    /// </summary>
    public string? EntityUrl { get; }

    /// <summary>
    /// Initializes a new <see cref="DataverseCreateResult{T}"/>.
    /// </summary>
    public DataverseCreateResult(Guid? id, T? entity, string? entityUrl)
    {
        Id = id;
        Entity = entity;
        EntityUrl = entityUrl;
    }
}
