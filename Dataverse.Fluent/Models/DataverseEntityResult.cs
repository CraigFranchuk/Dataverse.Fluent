namespace Dataverse.Fluent.Models;

/// <summary>
/// Envelope returned by metadata-aware retrieve operations. Surfaces OData annotations
/// (such as <c>@odata.etag</c> and formatted values) alongside the strongly-typed entity,
/// so optimistic concurrency does not require leaking those annotations into every POCO.
/// </summary>
/// <typeparam name="T">The POCO type for the entity.</typeparam>
public class DataverseEntityResult<T>
{
    /// <summary>
    /// The deserialized entity, or <c>default</c> when the response body was empty.
    /// </summary>
    public T? Entity { get; }

    /// <summary>
    /// The <c>@odata.etag</c> value for the retrieved record, used for optimistic
    /// concurrency control via <see cref="Builders.EntityBuilder{T}.WithETag(string)"/>.
    /// May be <c>null</c> if the server did not return an ETag.
    /// </summary>
    public string? ETag { get; }

    /// <summary>
    /// Formatted (display) values keyed by their logical property name, populated when
    /// <see cref="Builders.EntityBuilder{T}.IncludeFormattedValues(bool)"/> was requested.
    /// Empty when no formatted values were returned.
    /// </summary>
    public IReadOnlyDictionary<string, string> FormattedValues { get; }

    /// <summary>
    /// Indicates whether an ETag was returned for the retrieved record.
    /// </summary>
    public bool HasETag => !string.IsNullOrEmpty(ETag);

    /// <summary>
    /// Initializes a new <see cref="DataverseEntityResult{T}"/>.
    /// </summary>
    public DataverseEntityResult(T? entity, string? etag, IReadOnlyDictionary<string, string> formattedValues)
    {
        Entity = entity;
        ETag = etag;
        FormattedValues = formattedValues ?? throw new ArgumentNullException(nameof(formattedValues));
    }
}
