namespace Dataverse.Fluent;

/// <summary>
/// Specifies how to handle conflicts during upsert operations in Dataverse.
/// Controls the HTTP headers (If-Match, If-None-Match) sent with PATCH requests.
/// </summary>
public enum UpsertConflictResolution
{
    /// <summary>
    /// Default upsert behavior: Create if the record doesn't exist, update if it does.
    /// Uses If-Match:* header to enable standard upsert semantics.
    /// </summary>
    Upsert,

    /// <summary>
    /// Only create the record if it doesn't exist. Prevents updating existing records.
    /// Uses If-None-Match:* header. Returns 412 Precondition Failed if the record already exists.
    /// Useful for preventing accidental overwrites of existing data.
    /// </summary>
    PreventUpdate,

    /// <summary>
    /// Only update the record if it exists. Prevents creating new records.
    /// No If-Match header is sent. Returns 404 Not Found if the record doesn't exist.
    /// Useful when you want to ensure you're only modifying existing records.
    /// </summary>
    PreventCreate,

    /// <summary>
    /// Update only if the record hasn't been modified since it was retrieved (optimistic concurrency).
    /// Uses If-Match:{etag} header. Returns 412 Precondition Failed if the ETag doesn't match.
    /// Requires calling WithETag() to provide the ETag value from a previous retrieve operation.
    /// Prevents lost updates in concurrent scenarios.
    /// </summary>
    IfUnmodifiedSince
}
