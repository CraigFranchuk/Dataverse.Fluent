namespace Dataverse.Fluent.Models;

/// <summary>
/// What happened to one chunk of a bulk operation.
/// </summary>
public enum BulkChunkStatus
{
    /// <summary>
    /// The chunk was not sent, because an earlier chunk failed and <see cref="Builders.BulkBuilder{T}.ContinueOnError"/>
    /// was not set.
    /// </summary>
    NotSent = 0,

    /// <summary>Dataverse applied every record in the chunk.</summary>
    Succeeded = 1,

    /// <summary>
    /// The request for the chunk failed. On a standard table Dataverse rolled back every record in it. On an elastic
    /// table some records may have been written. When the failure is not a <see cref="DataverseException"/> (for
    /// example a timeout), the outcome is unknown.
    /// </summary>
    Failed = 2
}

/// <summary>
/// The outcome of one request of a bulk operation: a contiguous run of the records passed in.
/// </summary>
public sealed class BulkChunkResult
{
    internal BulkChunkResult(int startIndex, int count, BulkChunkStatus status, IReadOnlyList<Guid> ids, Exception? error)
    {
        StartIndex = startIndex;
        Count = count;
        Status = status;
        Ids = ids;
        Error = error;
    }

    /// <summary>The index, in the records passed in, of the first record in this chunk.</summary>
    public int StartIndex { get; }

    /// <summary>The number of records in this chunk.</summary>
    public int Count { get; }

    /// <summary>What happened to the chunk.</summary>
    public BulkChunkStatus Status { get; }

    /// <summary>Whether Dataverse applied every record in the chunk.</summary>
    public bool IsSuccess => Status == BulkChunkStatus.Succeeded;

    /// <summary>
    /// For a successful create, the IDs of the created records in the order they were passed in: <c>Ids[i]</c> is the
    /// record at <c>StartIndex + i</c>. Empty for updates, upserts and chunks that did not succeed.
    /// </summary>
    public IReadOnlyList<Guid> Ids { get; }

    /// <summary>The failure, usually a <see cref="DataverseException"/>, when <see cref="Status"/> is <see cref="BulkChunkStatus.Failed"/>.</summary>
    public Exception? Error { get; }
}

/// <summary>
/// The result of a bulk operation (<c>CreateMultiple</c>, <c>UpdateMultiple</c> or <c>UpsertMultiple</c>), one entry
/// per request sent or skipped. A failed chunk does not throw; call <see cref="ThrowIfAnyFailed"/> or inspect
/// <see cref="Chunks"/>.
/// </summary>
public sealed class BulkResult
{
    internal BulkResult(IReadOnlyList<BulkChunkResult> chunks)
    {
        Chunks = chunks;
    }

    /// <summary>The chunks, in the order of the records passed in.</summary>
    public IReadOnlyList<BulkChunkResult> Chunks { get; }

    /// <summary>Whether every chunk succeeded. <see langword="true"/> when there were no records.</summary>
    public bool AllSucceeded => Chunks.All(c => c.IsSuccess);

    /// <summary>The number of records in chunks that succeeded.</summary>
    public int SucceededCount => Chunks.Where(c => c.IsSuccess).Sum(c => c.Count);

    /// <summary>The chunks that failed. Chunks that were not sent have the status <see cref="BulkChunkStatus.NotSent"/>.</summary>
    public IEnumerable<BulkChunkResult> Failures => Chunks.Where(c => c.Status == BulkChunkStatus.Failed);

    /// <summary>
    /// For a create, the IDs of the records created by the chunks that succeeded, in the order they were passed in.
    /// When <see cref="AllSucceeded"/> is <see langword="true"/>, <c>Ids[i]</c> is the ID of the <c>i</c>th record.
    /// </summary>
    public IReadOnlyList<Guid> Ids => [.. Chunks.Where(c => c.IsSuccess).SelectMany(c => c.Ids)];

    /// <summary>
    /// Throws a <see cref="BulkOperationException"/> when any chunk failed or was not sent.
    /// </summary>
    /// <exception cref="BulkOperationException">Not every chunk succeeded.</exception>
    public void ThrowIfAnyFailed()
    {
        if (!AllSucceeded)
            throw new BulkOperationException(this);
    }
}

/// <summary>
/// Thrown by <see cref="BulkResult.ThrowIfAnyFailed"/> when a bulk operation did not write every record. The inner
/// exceptions are the errors of the failed chunks, in order; <see cref="Result"/> tells which records they held.
/// </summary>
public sealed class BulkOperationException : AggregateException
{
    internal BulkOperationException(BulkResult result)
        : base(BuildMessage(result), result.Failures.Select(f => f.Error!))
    {
        Result = result;
    }

    /// <summary>The result, including the chunks that succeeded.</summary>
    public BulkResult Result { get; }

    private static string BuildMessage(BulkResult result)
    {
        var total = result.Chunks.Sum(c => c.Count);
        var failed = result.Failures.ToList();
        var notSent = result.Chunks.Count(c => c.Status == BulkChunkStatus.NotSent);

        var message = $"{total - result.SucceededCount} of {total} records were not written: {failed.Count} chunk(s) failed";
        if (notSent > 0)
            message += $" and {notSent} chunk(s) were not sent";

        var first = failed.FirstOrDefault();
        return first == null
            ? message + "."
            : $"{message}. First failure, records {first.StartIndex} to {first.StartIndex + first.Count - 1}: {first.Error!.Message}";
    }
}
