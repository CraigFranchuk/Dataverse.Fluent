namespace Dataverse.Fluent.Models;

/// <summary>
/// The outcome of an upsert: the record's ID, whether it was created or updated (when Dataverse says), and,
/// when requested via <c>ReturnRepresentation()</c>, the record as returned by Dataverse.
/// </summary>
/// <typeparam name="T">The POCO type of the entity.</typeparam>
public sealed class DataverseUpsertResult<T>
{
    /// <summary>
    /// Initializes a new <see cref="DataverseUpsertResult{T}"/>.
    /// </summary>
    public DataverseUpsertResult(Guid? id, T? entity, string? entityUrl, bool? created)
    {
        Id = id;
        Entity = entity;
        EntityUrl = entityUrl;
        Created = created;
    }

    /// <summary>
    /// The primary key of the record, parsed from the <c>OData-EntityId</c> response header.
    /// </summary>
    /// <remarks>
    /// Dataverse omits the header when a representation is returned; read the key from <see cref="Entity"/>
    /// in that case. For a record addressed by alternate key this is the only way to learn its ID without
    /// another request.
    /// </remarks>
    public Guid? Id { get; }

    /// <summary>
    /// The record, populated only when <c>ReturnRepresentation()</c> was requested.
    /// </summary>
    public T? Entity { get; }

    /// <summary>
    /// The raw <c>OData-EntityId</c> URL of the record, when returned.
    /// </summary>
    public string? EntityUrl { get; }

    /// <summary>
    /// <see langword="true"/> when the upsert created the record, <see langword="false"/> when it updated an
    /// existing one, and <see langword="null"/> when the response does not say.
    /// </summary>
    /// <remarks>
    /// Dataverse distinguishes the two only when a representation is returned (201 Created or 200 OK);
    /// without one, both outcomes are 204 No Content.
    /// </remarks>
    public bool? Created { get; }
}
