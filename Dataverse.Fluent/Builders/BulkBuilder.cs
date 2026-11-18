using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Nodes;
using Dataverse.Fluent.Builders.Expressions;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Writes many records of one table with the Dataverse bulk operation messages <c>CreateMultiple</c>,
/// <c>UpdateMultiple</c> and <c>UpsertMultiple</c>, which are much faster than one request per record or a
/// <c>$batch</c>. The records are sent in chunks of <see cref="ChunkSize"/>, optionally in parallel.
/// </summary>
/// <typeparam name="T">The POCO type of the table.</typeparam>
/// <remarks>
/// <para>
/// On a standard table, Dataverse applies each chunk in one transaction: when any record in a chunk fails, none of
/// the chunk is written. Chunks are independent, so earlier chunks stay written. On an elastic table a chunk can
/// partly succeed. Not every standard table supports these messages; Dataverse rejects the request when it does not.
/// </para>
/// <para>
/// Records are serialized as by <see cref="EntityBuilder{T}.SetFrom(T)"/>: <see langword="null"/> properties are left
/// out, lookups are written as <c>@odata.bind</c>, and properties marked <see cref="DataverseReadOnlyAttribute"/> for
/// the operation are removed.
/// </para>
/// <para>
/// This type is not thread-safe. A terminal method (<see cref="CreateAsync"/>, <see cref="UpdateAsync"/>,
/// <see cref="UpsertAsync"/>, <see cref="UpsertByAlternateKeyAsync{TKey}"/>) consumes the builder.
/// </para>
/// </remarks>
public sealed class BulkBuilder<T> where T : class
{
    private const int DefaultChunkSize = 100;

    private readonly IDataverseTransport _transport;
    private readonly string _entitySet;
    private readonly string _odataType;

    private int _chunkSize = DefaultChunkSize;
    private int _maxDegreeOfParallelism = 1;
    private bool _continueOnError;
    private SingleUseGuard _guard;

    internal BulkBuilder(IDataverseTransport transport, string? entitySet, string? entityLogicalName)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));

        if (entitySet != null)
            ArgumentException.ThrowIfNullOrWhiteSpace(entitySet);
        if (entityLogicalName != null)
            ArgumentException.ThrowIfNullOrWhiteSpace(entityLogicalName);

        _entitySet = entitySet ?? ODataPropertyNameCache.GetEntitySetName<T>();
        var logicalName = entityLogicalName
            ?? DataverseTypeMapping.ResolveEntityLogicalName(typeof(T))
            ?? typeof(T).Name.ToLowerInvariant();
        _odataType = $"Microsoft.Dynamics.CRM.{logicalName}";
    }

    /// <summary>
    /// Sets the number of records per request. Default: 100. Microsoft suggests 100 to 1,000 for standard tables with
    /// small records and no plug-ins, and 100 for elastic tables. Lower it when requests hit the size or time limits.
    /// </summary>
    /// <param name="size">Records per request, at least 1.</param>
    public BulkBuilder<T> ChunkSize(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        _chunkSize = size;
        return this;
    }

    /// <summary>
    /// Sets how many requests are in flight at once. Default: 1, one chunk after another. Parallel requests raise
    /// throughput; Dataverse's service protection limits allow up to 52 concurrent requests per user, and the
    /// resilience pipeline waits out HTTP 429 responses.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">Concurrent requests, at least 1.</param>
    public BulkBuilder<T> MaxDegreeOfParallelism(int maxDegreeOfParallelism)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism, 1);
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
        return this;
    }

    /// <summary>
    /// Sends the remaining chunks after one fails. By default no new chunk is sent after a failure (chunks already in
    /// flight complete), and the unsent chunks are reported as <see cref="BulkChunkStatus.NotSent"/>.
    /// </summary>
    public BulkBuilder<T> ContinueOnError(bool value = true)
    {
        _continueOnError = value;
        return this;
    }

    /// <summary>
    /// Creates the records with <c>CreateMultiple</c>. The result holds their IDs in the order passed in.
    /// </summary>
    /// <param name="entities">The records to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome of each chunk. A failed chunk does not throw; see <see cref="BulkResult.ThrowIfAnyFailed"/>.</returns>
    public Task<BulkResult> CreateAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        _guard.Use();
        var items = Materialize(entities);

        return ExecuteAsync("CreateMultiple", items, (item, _) => Target(item, DataverseOperation.Create), readIds: true, ct);
    }

    /// <summary>
    /// Updates existing records with <c>UpdateMultiple</c>, each identified by its primary key. Only the properties
    /// that are set are sent, as with <see cref="EntityBuilder{T}.SetFrom(T)"/>.
    /// </summary>
    /// <param name="entities">The records to update.</param>
    /// <param name="primaryKey">The primary key property, e.g. <c>a =&gt; a.AccountId</c>. Every record must have a
    /// non-empty value, and the values must be distinct.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome of each chunk. A failed chunk does not throw; see <see cref="BulkResult.ThrowIfAnyFailed"/>.</returns>
    /// <exception cref="ArgumentException">A record has no primary key value, or two records have the same one.
    /// Dataverse would silently apply only the first of them.</exception>
    /// <remarks>The Web API does not support alternate keys with <c>UpdateMultiple</c>; use
    /// <see cref="UpsertByAlternateKeyAsync{TKey}"/> to write by alternate key.</remarks>
    public Task<BulkResult> UpdateAsync(IEnumerable<T> entities, Expression<Func<T, Guid?>> primaryKey, CancellationToken ct = default)
    {
        _guard.Use();
        var items = Materialize(entities);
        var (column, ids) = ReadPrimaryKeys(items, primaryKey);

        return ExecuteAsync("UpdateMultiple", items, (item, index) =>
        {
            // As for SetFrom on an update: create-only columns (e.g. statecode) never overwrite an existing record.
            var target = Target(item, DataverseOperation.Create, DataverseOperation.Update);
            target[column] = ids[index];
            return target;
        }, readIds: false, ct);
    }

    /// <summary>
    /// Creates or updates records with <c>UpsertMultiple</c>, each identified by its primary key: a record that
    /// exists is updated, any other is created with that ID.
    /// </summary>
    /// <param name="entities">The records to write.</param>
    /// <param name="primaryKey">The primary key property, e.g. <c>a =&gt; a.AccountId</c>. Every record must have a
    /// non-empty value, and the values must be distinct.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome of each chunk. Dataverse does not report which records were created.</returns>
    /// <exception cref="ArgumentException">A record has no primary key value, or two records have the same one.</exception>
    public Task<BulkResult> UpsertAsync(IEnumerable<T> entities, Expression<Func<T, Guid?>> primaryKey, CancellationToken ct = default)
    {
        _guard.Use();
        var items = Materialize(entities);
        var (column, ids) = ReadPrimaryKeys(items, primaryKey);

        return ExecuteAsync("UpsertMultiple", items, (item, index) =>
        {
            var target = Target(item, DataverseOperation.Create, DataverseOperation.Update);
            target[column] = ids[index];
            return target;
        }, readIds: false, ct);
    }

    /// <summary>
    /// Creates or updates records with <c>UpsertMultiple</c>, each identified by an alternate key: a record whose key
    /// matches is updated, any other is created.
    /// </summary>
    /// <typeparam name="TKey">The key property type, or an anonymous type for a composite key.</typeparam>
    /// <param name="entities">The records to write.</param>
    /// <param name="alternateKey">The key column, e.g. <c>a =&gt; a.AccountNumber</c>, or the columns of a composite
    /// key, e.g. <c>a =&gt; new { a.Code, a.Region }</c>. Every record must have a value for each, and the keys must be
    /// distinct.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome of each chunk. Dataverse does not report which records were created.</returns>
    /// <exception cref="ArgumentException">A record has a <see langword="null"/> key value, or two records have the
    /// same key. Keys are compared case-insensitively, as Dataverse compares them.</exception>
    public Task<BulkResult> UpsertByAlternateKeyAsync<TKey>(IEnumerable<T> entities, Expression<Func<T, TKey>> alternateKey, CancellationToken ct = default)
    {
        _guard.Use();
        ArgumentNullException.ThrowIfNull(alternateKey);
        var items = Materialize(entities);

        var members = MemberPathExtractor.GetMembers(alternateKey);
        var columns = members.ConvertAll(ODataPropertyNameCache.GetODataPropertyName);
        var references = new string[items.Count];

        for (var i = 0; i < items.Count; i++)
        {
            var pairs = new List<KeyValuePair<string, object>>(members.Count);
            for (var m = 0; m < members.Count; m++)
            {
                var value = GetValue(members[m], items[i])
                    ?? throw new ArgumentException($"The record at index {i} has no value for the alternate key column '{columns[m]}'.", nameof(entities));
                pairs.Add(new(columns[m], value));
            }
            references[i] = $"{_entitySet}({AlternateKeyFormatter.FormatCompositeKey(pairs)})";
        }

        // Dataverse compares key values with its case-insensitive collation.
        EnsureDistinct(references, StringComparer.OrdinalIgnoreCase, "alternate key");

        return ExecuteAsync("UpsertMultiple", items, (item, index) =>
        {
            var target = Target(item, DataverseOperation.Create, DataverseOperation.Update);
            target["@odata.id"] = references[index];
            return target;
        }, readIds: false, ct);
    }

    // Serializes one record and marks its table, which every item of Targets must carry.
    private JsonObject Target(T item, params DataverseOperation[] operations)
    {
        var target = EntityPayload.Serialize(item, operations) as JsonObject
            ?? throw new InvalidOperationException($"'{typeof(T).Name}' does not serialize to a JSON object.");
        target.Insert(0, "@odata.type", _odataType);
        return target;
    }

    private static List<T> Materialize(IEnumerable<T> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var items = entities.ToList();
        var nullIndex = items.FindIndex(item => item is null);
        if (nullIndex >= 0)
            throw new ArgumentException($"The record at index {nullIndex} is null.", nameof(entities));
        return items;
    }

    private static (string Column, Guid[] Ids) ReadPrimaryKeys(List<T> items, Expression<Func<T, Guid?>> primaryKey)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);

        var property = MemberPathExtractor.GetProperty(primaryKey);
        var column = ODataPropertyNameCache.GetODataPropertyName(property);
        var ids = new Guid[items.Count];

        for (var i = 0; i < items.Count; i++)
        {
            var value = property.GetValue(items[i]) as Guid?;
            if (value is not { } id || id == Guid.Empty)
                throw new ArgumentException($"The record at index {i} has no value for the primary key '{column}'.", nameof(primaryKey));
            ids[i] = id;
        }

        EnsureDistinct(ids.Select(id => id.ToString("D", CultureInfo.InvariantCulture)), StringComparer.Ordinal, "primary key");
        return (column, ids);
    }

    // UpdateMultiple silently applies only the first of several records with the same key, and UpsertMultiple
    // rejects the whole request, so duplicates are refused before anything is sent.
    private static void EnsureDistinct(IEnumerable<string> keys, StringComparer comparer, string kind)
    {
        var seen = new Dictionary<string, int>(comparer);
        var index = 0;
        foreach (var key in keys)
        {
            if (!seen.TryAdd(key, index))
                throw new ArgumentException($"The records at index {seen[key]} and {index} have the same {kind} ({key}).");
            index++;
        }
    }

    private static object? GetValue(MemberInfo member, T item) => member switch
    {
        PropertyInfo property => property.GetValue(item),
        FieldInfo field => field.GetValue(item),
        _ => throw new ArgumentException($"'{member.Name}' is not a property or field.")
    };

    private async Task<BulkResult> ExecuteAsync(
        string message, List<T> items, Func<T, int, JsonObject> buildTarget, bool readIds, CancellationToken ct)
    {
        var chunkCount = (items.Count + _chunkSize - 1) / _chunkSize;
        var results = new BulkChunkResult?[chunkCount];
        var url = $"{_entitySet}/Microsoft.Dynamics.CRM.{message}";
        var stop = 0;

        using var gate = new SemaphoreSlim(_maxDegreeOfParallelism, _maxDegreeOfParallelism);
        var running = new List<Task>(chunkCount);
        try
        {
            for (var chunk = 0; chunk < chunkCount; chunk++)
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                if (Volatile.Read(ref stop) != 0)
                {
                    gate.Release();
                    break;
                }
                running.Add(SendChunkAsync(chunk));
            }
        }
        finally
        {
            // Writes in flight are awaited even on cancellation, so the gate is not disposed under them.
            await Task.WhenAll(running).ConfigureAwait(false);
        }

        for (var chunk = 0; chunk < chunkCount; chunk++)
        {
            var (start, count) = Bounds(chunk);
            results[chunk] ??= new BulkChunkResult(start, count, BulkChunkStatus.NotSent, [], null);
        }

        return new BulkResult(results!);

        async Task SendChunkAsync(int chunk)
        {
            var (start, count) = Bounds(chunk);
            try
            {
                var targets = new JsonArray();
                for (var i = start; i < start + count; i++)
                    targets.Add(buildTarget(items[i], i));

                var request = new DataverseRequest(HttpMethod.Post, url) { Payload = new JsonObject { ["Targets"] = targets } };
                using var response = await _transport.SendAsync(request, ct).ConfigureAwait(false);

                IReadOnlyList<Guid> ids = readIds ? await ReadIdsAsync(response, ct).ConfigureAwait(false) : [];
                results[chunk] = new BulkChunkResult(start, count, BulkChunkStatus.Succeeded, ids, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                results[chunk] = new BulkChunkResult(start, count, BulkChunkStatus.Failed, [], ex);
                if (!_continueOnError)
                    Interlocked.Exchange(ref stop, 1);
            }
            finally
            {
                gate.Release();
            }
        }

        (int Start, int Count) Bounds(int chunk)
        {
            var start = chunk * _chunkSize;
            return (start, Math.Min(_chunkSize, items.Count - start));
        }
    }

    private static async Task<IReadOnlyList<Guid>> ReadIdsAsync(DataverseResponse response, CancellationToken ct)
    {
        var json = await response.ReadJsonObjectAsync(ct).ConfigureAwait(false);
        return json["Ids"] is JsonArray ids
            ? [.. ids.Select(id => Guid.Parse(id!.GetValue<string>(), CultureInfo.InvariantCulture))]
            : [];
    }
}
