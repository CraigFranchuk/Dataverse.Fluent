using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Models;

/// <summary>
/// Represents the result of a delta (change tracking) query.
/// Contains changed entities, deleted entity references, and the next delta token.
/// </summary>
/// <typeparam name="T">The POCO type for the entities.</typeparam>
public class DeltaResults<T>
{
    /// <summary>
    /// Entities that have been created or modified since the last delta query.
    /// </summary>
    [JsonPropertyName("value")]
    public List<T> Value { get; set; } = [];

    /// <summary>
    /// References to entities that have been deleted since the last delta query.
    /// </summary>
    [JsonPropertyName("@odata.deltaLink")]
    public string? DeltaToken { get; set; }

    /// <summary>
    /// List of entity references that have been deleted.
    /// </summary>
    public List<EntityReference> Deleted { get; set; } = [];

    /// <summary>
    /// Total count of records (if $count=true was requested).
    /// </summary>
    [JsonPropertyName("@odata.count")]
    public long? Count { get; set; }

    /// <summary>
    /// Indicates whether there are more records available in the current page.
    /// </summary>
    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; set; }
}

/// <summary>
/// Represents a reference to a deleted entity.
/// </summary>
public class EntityReference
{
    /// <summary>
    /// The ID of the deleted entity.
    /// </summary>
    [JsonPropertyName("@removed")]
    public DeletedReference? Removed { get; set; }

    /// <summary>
    /// The ID property from the deleted entity context.
    /// </summary>
    [JsonPropertyName("@id")]
    public string? Id { get; set; }

    /// <summary>
    /// Extracts the GUID from the entity reference ID.
    /// </summary>
    public Guid? GetEntityId()
    {
        if (string.IsNullOrWhiteSpace(Id))
            return null;

        // Try to extract GUID from formats like:
        // "accounts(12345678-1234-1234-1234-123456789012)"
        // "https://test.crm.dynamics.com/api/data/v9.2/accounts(12345678-1234-1234-1234-123456789012)"
        
        var idPart = Id;

        // If there's a slash, get everything after the last slash
        var lastSlash = Id.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < Id.Length - 1)
        {
            idPart = Id[(lastSlash + 1)..];
        }
        
        // Extract GUID from parentheses: "accounts(guid)" -> "guid"
        var openParen = idPart.IndexOf('(');
        var closeParen = idPart.IndexOf(')');
        
        if (openParen >= 0 && closeParen > openParen)
        {
            idPart = idPart[(openParen + 1)..closeParen];
        }
        
        // Try to parse as GUID
        if (Guid.TryParse(idPart, out var guid))
        {
            return guid;
        }

        return null;
    }
}

/// <summary>
/// Contains the reason for entity removal.
/// </summary>
public class DeletedReference
{
    /// <summary>
    /// The reason the entity was removed (e.g., "deleted", "changed").
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}
