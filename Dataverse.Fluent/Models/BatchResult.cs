using System.Net;

namespace Dataverse.Fluent.Models;

/// <summary>
/// Represents the result of a single operation within a batch request.
/// </summary>
public class BatchOperationResult
{
    /// <summary>
    /// The Content-ID that correlates this result to the original request (1-based).
    /// </summary>
    public int ContentId { get; }

    /// <summary>
    /// The HTTP status code of the individual operation.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The raw response body of the individual operation.
    /// </summary>
    public string Body { get; }

    /// <summary>
    /// The entity ID returned by Dataverse for a create operation, parsed from the
    /// <c>OData-EntityId</c> (or <c>Location</c>) response header. <see langword="null"/> when the
    /// operation did not create a record or no ID could be determined.
    /// </summary>
    public Guid? EntityId { get; }

    /// <summary>
    /// The raw value of the operation's <c>OData-EntityId</c> or <c>Location</c> response header,
    /// if present. Useful when the ID cannot be parsed as a <see cref="Guid"/>.
    /// </summary>
    public string? Location { get; }

    /// <summary>
    /// Whether this individual operation succeeded (2xx status code).
    /// </summary>
    public bool IsSuccess => (int)StatusCode >= 200 && (int)StatusCode < 300;

    /// <summary>
    /// Whether this operation was rolled back because another operation in the same transactional
    /// changeset failed. Dataverse applies a changeset atomically, so when one operation fails the
    /// others never take effect even though they were otherwise valid. Such operations are reported
    /// with <see cref="HttpStatusCode.FailedDependency"/> (424) and this flag set to <see langword="true"/>.
    /// </summary>
    public bool WasRolledBack { get; }

    /// <summary>
    /// Deserializes the operation's response body (a retrieved record, or a written one returned with
    /// <c>Prefer: return=representation</c>) using the library's naming rules.
    /// Returns <c>default</c> when the operation returned no body.
    /// </summary>
    /// <typeparam name="T">The POCO type to deserialize into.</typeparam>
    public T? GetEntity<T>()
        => string.IsNullOrWhiteSpace(Body)
            ? default
            : System.Text.Json.JsonSerializer.Deserialize<T>(Body, Builders.JsonDefaults.CaseInsensitive);

    /// <summary>
    /// The ID Dataverse gave this operation (its <c>REQ_ID</c> header in the batch response), reported as
    /// <see cref="DataverseException.ActivityId"/> when the operation failed.
    /// </summary>
    internal string? RequestId { get; init; }

    /// <summary>
    /// Initializes a new <see cref="BatchOperationResult"/>.
    /// </summary>
    public BatchOperationResult(int contentId, HttpStatusCode statusCode, string body, Guid? entityId = null, string? location = null, bool wasRolledBack = false)
    {
        ContentId = contentId;
        StatusCode = statusCode;
        Body = body ?? string.Empty;
        EntityId = entityId;
        Location = location;
        WasRolledBack = wasRolledBack;
    }
}

/// <summary>
/// A handle to a write operation queued in a batch changeset. Use the <see cref="ContentId"/> to
/// reference a record that is created earlier in the same changeset (for example, to set a lookup
/// on a child record to its not-yet-created parent). The reference is resolved server-side via the
/// OData <c>$&lt;content-id&gt;</c> syntax.
/// </summary>
/// <remarks>
/// A reference is only valid within the same changeset and only for operations added before the
/// referencing operation.
/// </remarks>
public readonly struct BatchReference
{
    /// <summary>
    /// The Content-ID assigned to the referenced operation (1-based).
    /// </summary>
    public int ContentId { get; }

    internal BatchReference(int contentId) => ContentId = contentId;

    /// <summary>
    /// The OData reference token for this operation's Content-ID, e.g. <c>$1</c>.
    /// </summary>
    public string Token => $"${ContentId}";

    /// <summary>
    /// Returns the OData reference token (e.g. <c>$1</c>).
    /// </summary>
    public override string ToString() => Token;
}

/// <summary>
/// Represents the parsed result of a Dataverse batch request,
/// containing individual operation results with structured error information.
/// </summary>
public class BatchResult
{
    /// <summary>
    /// The individual operation results, in the same order as the batch requests.
    /// </summary>
    public IReadOnlyList<BatchOperationResult> Results { get; }

    /// <summary>
    /// Whether all operations in the batch succeeded.
    /// </summary>
    public bool AllSucceeded => Results.All(r => r.IsSuccess);

    /// <summary>
    /// Whether any operation in the batch failed.
    /// </summary>
    public bool HasErrors => Results.Any(r => !r.IsSuccess);

    /// <summary>
    /// The operation results that failed (non-2xx status codes).
    /// </summary>
    public IEnumerable<BatchOperationResult> Errors => Results.Where(r => !r.IsSuccess);

    /// <summary>
    /// Initializes a new <see cref="BatchResult"/>.
    /// </summary>
    public BatchResult(IReadOnlyList<BatchOperationResult> results)
    {
        Results = results ?? throw new ArgumentNullException(nameof(results));
    }

    /// <summary>
    /// Throws if any operation in the batch failed.
    /// </summary>
    /// <remarks>
    /// When a single operation failed, a <see cref="DataverseException"/> is thrown with that
    /// operation's details. When multiple operations failed, a <see cref="BatchOperationException"/>
    /// is thrown aggregating every failure (including the originating Content-ID of each), while
    /// still exposing the first failure's parsed details for convenience.
    /// </remarks>
    /// <exception cref="DataverseException">Thrown when exactly one operation failed.</exception>
    /// <exception cref="BatchOperationException">Thrown when more than one operation failed.</exception>
    public void ThrowIfAnyFailed()
    {
        var failures = Results.Where(r => !r.IsSuccess).ToList();
        if (failures.Count == 0)
        {
            return;
        }

        if (failures.Count == 1)
        {
            var only = failures[0];
            throw DataverseException.FromBatchOperation(only);
        }

        var inner = failures
            .Select(DataverseException.FromBatchOperation)
            .ToList();

        throw new BatchOperationException(failures, inner);
    }
}

/// <summary>
/// Exception thrown when more than one operation in a batch fails. Aggregates the individual
/// <see cref="DataverseException"/> instances and exposes the failed <see cref="BatchOperationResult"/>s
/// so callers can correlate each failure back to its originating request via <see cref="BatchOperationResult.ContentId"/>.
/// </summary>
public class BatchOperationException : AggregateException
{
    /// <summary>
    /// The failed operation results, in batch order.
    /// </summary>
    public IReadOnlyList<BatchOperationResult> FailedOperations { get; }

    internal BatchOperationException(
        IReadOnlyList<BatchOperationResult> failedOperations,
        IReadOnlyList<DataverseException> innerExceptions)
        : base(BuildMessage(failedOperations), innerExceptions)
    {
        FailedOperations = failedOperations;
    }

    private static string BuildMessage(IReadOnlyList<BatchOperationResult> failedOperations)
    {
        var summary = string.Join(
            "; ",
            failedOperations.Select(f => $"Content-ID {f.ContentId}: {(int)f.StatusCode} {f.StatusCode}"));
        return $"{failedOperations.Count} batch operation(s) failed: {summary}.";
    }
}
