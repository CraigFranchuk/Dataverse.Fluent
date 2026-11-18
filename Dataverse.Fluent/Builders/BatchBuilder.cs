using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Fluent builder for batch operations in Dataverse.
/// </summary>
/// <remarks>
/// This type is not thread-safe. Each instance should be used for a single operation and not shared across threads.
/// Terminal methods (e.g., <see cref="ExecuteAsync"/>) mark the builder as consumed; subsequent calls will throw
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed partial class BatchBuilder
{
    private readonly IDataverseTransport _client;
    private readonly Guid _batchId;

    // An ordered list of batch items. Each item is either a transactional changeset
    // (one or more write operations) or a standalone GET operation. Standalone GETs
    // cannot live inside a changeset per the Dataverse/OData batch specification.
    private readonly List<BatchItem> _items = [];

    // The changeset currently accepting write operations. Created lazily on the first
    // write and closed when NewChangeset() is called or a standalone GET is added.
    private ChangeSet? _currentChangeSet;

    private bool _continueOnError;
    private SingleUseGuard _guard;

    // Headers from DataverseClientExtensions.WithOptions. Dataverse does not apply the headers of the $batch
    // request to the operations inside it, so they are written into every part as well.
    private readonly IReadOnlyDictionary<string, string>? _scopedHeaders;

    // Content-IDs are assigned at add-time (1-based, monotonically increasing across the whole
    // batch) so that references to records created earlier in the same changeset ($contentId) are
    // deterministic and known to the caller as soon as an operation is queued.
    private int _nextContentId = 1;

    internal BatchBuilder(IDataverseTransport client, Guid? batchId = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _batchId = batchId ?? Guid.NewGuid();
        _scopedHeaders = (client as RequestOptionsTransport)?.Headers;
    }

    /// <summary>
    /// Adds a create operation to the current changeset.
    /// </summary>
    /// <param name="entity">The entity to create.</param>
    /// <param name="entitySet">Optional entity set name (inferred from <typeparamref name="T"/> if not provided).</param>
    public BatchBuilder AddCreate<T>(T entity, string? entitySet = null) where T : class
    {
        AddCreateCore(entity, entitySet, null);
        return this;
    }

    /// <summary>
    /// Adds a create operation and returns a <see cref="BatchReference"/> handle to it via
    /// <paramref name="reference"/>, so subsequent operations in the same changeset can link to the
    /// not-yet-created record (e.g., a child record's lookup pointing at this parent).
    /// </summary>
    /// <param name="entity">The entity to create.</param>
    /// <param name="reference">Receives a reference to the queued create operation.</param>
    /// <param name="entitySet">Optional entity set name (inferred from <typeparamref name="T"/> if not provided).</param>
    public BatchBuilder AddCreate<T>(T entity, out BatchReference reference, string? entitySet = null) where T : class
    {
        reference = AddCreateCore(entity, entitySet, null);
        return this;
    }

    /// <summary>
    /// Adds a create operation whose lookup navigation properties are bound to records created
    /// earlier in the same changeset. Each entry maps a single-valued navigation property logical
    /// name (e.g., <c>"primarycontactid"</c>) to a <see cref="BatchReference"/> returned by a prior
    /// <c>AddCreate</c>. The binding is emitted as <c>"{navigation}@odata.bind": "$contentId"</c>.
    /// </summary>
    /// <param name="entity">The entity to create.</param>
    /// <param name="lookupBindings">Navigation property to prior-operation reference mappings.</param>
    /// <param name="entitySet">Optional entity set name (inferred from <typeparamref name="T"/> if not provided).</param>
    public BatchBuilder AddCreate<T>(T entity, IReadOnlyDictionary<string, BatchReference> lookupBindings, string? entitySet = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(lookupBindings);
        AddCreateCore(entity, entitySet, lookupBindings);
        return this;
    }

    /// <summary>
    /// Adds a create operation with intra-changeset lookup bindings and returns a
    /// <see cref="BatchReference"/> to this operation via <paramref name="reference"/>.
    /// </summary>
    /// <param name="entity">The entity to create.</param>
    /// <param name="lookupBindings">Navigation property to prior-operation reference mappings.</param>
    /// <param name="reference">Receives a reference to the queued create operation.</param>
    /// <param name="entitySet">Optional entity set name (inferred from <typeparamref name="T"/> if not provided).</param>
    public BatchBuilder AddCreate<T>(T entity, IReadOnlyDictionary<string, BatchReference> lookupBindings, out BatchReference reference, string? entitySet = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(lookupBindings);
        reference = AddCreateCore(entity, entitySet, lookupBindings);
        return this;
    }

    private BatchReference AddCreateCore<T>(T entity, string? entitySet, IReadOnlyDictionary<string, BatchReference>? lookupBindings) where T : class
    {
        var payload = SerializeEntity(entity, DataverseOperation.Create);

        if (lookupBindings is { Count: > 0 })
        {
            var obj = payload.AsObject();
            foreach (var (navigationProperty, reference) in lookupBindings)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(navigationProperty, nameof(lookupBindings));

                // Dataverse resolves $n references only within the same changeset.
                if (_currentChangeSet?.Requests.Any(r => r.ContentId == reference.ContentId) != true)
                    throw new ArgumentException(
                        $"The reference {reference.Token} bound to '{navigationProperty}' is not an operation in the current changeset. " +
                        "Content-ID references are resolved only within the same changeset; do not call NewChangeset() between the operations.",
                        nameof(lookupBindings));

                obj[$"{navigationProperty}@odata.bind"] = reference.Token;
            }
        }

        var request = CreateRequest(HttpMethod.Post, entitySet ?? InferEntitySet<T>(), payload);
        GetOrCreateChangeSet().Requests.Add(request);
        return new BatchReference(request.ContentId);
    }

    /// <summary>
    /// Adds an update operation. The update never creates a record: it is sent with
    /// <c>If-Match: *</c>, so it fails (404) when the record does not exist. Use
    /// <see cref="AddUpsert{T}(Guid, T, string?)"/> to create-or-update.
    /// </summary>
    public BatchBuilder AddUpdate<T>(Guid id, T entity, string? entitySet = null) where T : class
    {
        var payload = SerializeEntity(entity, DataverseOperation.Create, DataverseOperation.Update);
        GetOrCreateChangeSet().Requests.Add(CreateRequest(HttpMethod.Patch, $"{entitySet ?? InferEntitySet<T>()}({id})", payload, UpdateOnlyHeaders));
        return this;
    }

    /// <summary>
    /// Adds an upsert operation: updates the record when it exists, otherwise creates it with the given ID.
    /// </summary>
    public BatchBuilder AddUpsert<T>(Guid id, T entity, string? entitySet = null) where T : class
    {
        var payload = SerializeEntity(entity, DataverseOperation.Create, DataverseOperation.Update);
        GetOrCreateChangeSet().Requests.Add(CreateRequest(HttpMethod.Patch, $"{entitySet ?? InferEntitySet<T>()}({id})", payload));
        return this;
    }

    // A Dataverse PATCH without If-Match is an upsert; If-Match: * restricts it to existing records.
    private static readonly IReadOnlyDictionary<string, string> UpdateOnlyHeaders =
        new Dictionary<string, string> { ["If-Match"] = "*" };

    /// <summary>
    /// Adds a delete operation to the current changeset.
    /// </summary>
    /// <param name="id">The ID of the record to delete.</param>
    /// <param name="entitySet">Optional entity set name (inferred from <typeparamref name="T"/> if not provided).</param>
    public BatchBuilder AddDelete<T>(Guid id, string? entitySet = null)
    {
        GetOrCreateChangeSet().Requests.Add(CreateRequest(HttpMethod.Delete, $"{entitySet ?? InferEntitySet<T>()}({id})", null));
        return this;
    }

    /// <summary>
    /// Adds a delete operation to the current changeset, using an explicit entity set name.
    /// </summary>
    /// <param name="entitySet">The entity set name (e.g., "accounts").</param>
    /// <param name="id">The ID of the record to delete.</param>
    public BatchBuilder AddDelete(string entitySet, Guid id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entitySet);
        GetOrCreateChangeSet().Requests.Add(CreateRequest(HttpMethod.Delete, $"{entitySet}({id})", null));
        return this;
    }

    /// <summary>
    /// Adds a deep insert to the current changeset: creates <paramref name="parent"/> together with related
    /// records in a single operation.
    /// </summary>
    /// <param name="parent">The parent entity to create.</param>
    /// <param name="navigationProperty">The collection-valued navigation property that holds the children.</param>
    /// <param name="children">The related entities to create. A collection passed as one argument is expanded.</param>
    public BatchBuilder AddDeepInsert<T>(T parent, string navigationProperty, params object[] children) where T : class
    {
        // Serialize parent with navigation expansion
        var payload = SerializeEntity(parent, DataverseOperation.Create);

        // Children are serialized by their runtime type with the library's options so that
        // [DataverseColumn]/resolver naming and [DataverseReadOnly] apply to them as well.
        var childArray = new JsonArray();
        foreach (var child in children)
        {
            var childNode = SerializeChild(child);

            // A List<T> is not an object[], so params passes it as a single child: expand a child that
            // serializes to an array into its elements.
            if (childNode is JsonArray && child is System.Collections.IEnumerable items)
            {
                foreach (var item in items)
                {
                    if (item is not null)
                        childArray.Add(SerializeChild(item));
                }
            }
            else
            {
                childArray.Add(childNode);
            }
        }
        // No children creates no related records, so the navigation property is omitted (as in DeepInsertBuilder).
        if (childArray.Count > 0)
            payload.AsObject().Add(navigationProperty, childArray);
        GetOrCreateChangeSet().Requests.Add(CreateRequest(HttpMethod.Post, InferEntitySet<T>(), payload));
        return this;

        static JsonNode? SerializeChild(object child)
        {
            var childNode = JsonSerializer.SerializeToNode(child, child.GetType(), JsonDefaults.EntityPayload);
            if (childNode is JsonObject childObject)
                PropertyFilter.RemoveReadOnlyProperties(child.GetType(), childObject, DataverseOperation.Create);
            return childNode;
        }
    }

    /// <summary>
    /// Adds an association between two existing records through a collection-valued navigation property
    /// (N:N or 1:N) to the current changeset.
    /// </summary>
    /// <param name="entitySet1">The entity set of the source record.</param>
    /// <param name="id1">The ID of the source record.</param>
    /// <param name="navigationProperty">The navigation property on the source record.</param>
    /// <param name="entitySet2">The entity set of the record to associate.</param>
    /// <param name="id2">The ID of the record to associate.</param>
    public BatchBuilder AddAssociate(string entitySet1, Guid id1, string navigationProperty, string entitySet2, Guid id2)
    {
        var payload = new JsonObject { ["@odata.id"] = $"{_client.BaseUrl}/{entitySet2}({id2})" };
        GetOrCreateChangeSet().Requests.Add(CreateRequest(HttpMethod.Post, $"{entitySet1}({id1})/{navigationProperty}/$ref", payload));
        return this;
    }

    /// <summary>
    /// Adds a request produced by a fluent builder, such as
    /// <see cref="EntityWriteBuilder{T}.ToUpsertRequest(UpsertConflictResolution)"/>,
    /// <see cref="EntityBuilder{T}.ToDeleteRequest"/> or <see cref="ActionBuilder{TRequest, TResponse}.ToRequest"/>.
    /// This gives batched operations everything the builders support: alternate keys, ETags, lookups and actions.
    /// </summary>
    /// <param name="request">The request. A GET is added at the top level of the batch (closing the current
    /// changeset); any other method is added to the current changeset.</param>
    /// <exception cref="ArgumentException">The request has an absolute URL or a raw <see cref="DataverseRequest.Content"/> body.</exception>
    public BatchBuilder Add(DataverseRequest request)
    {
        AddCore(request);
        return this;
    }

    /// <summary>
    /// Adds a request produced by a fluent builder and returns a <see cref="BatchReference"/> to it, so later
    /// operations in the same changeset can bind to the record it creates (see <see cref="Add(DataverseRequest)"/>).
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="reference">Receives a reference to the queued operation.</param>
    public BatchBuilder Add(DataverseRequest request, out BatchReference reference)
    {
        reference = AddCore(request);
        return this;
    }

    private BatchReference AddCore(DataverseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Content != null)
            throw new ArgumentException("A batch operation must have a JSON Payload; a raw Content body cannot be batched.", nameof(request));
        if (Uri.TryCreate(request.Url, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp))
            throw new ArgumentException("A batch operation needs a URL relative to the base URL (e.g., \"accounts(...)\").", nameof(request));

        var headers = request.Headers.Count > 0
            ? new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase)
            : null;
        var batchRequest = CreateRequest(request.Method, request.Url, request.Payload?.DeepClone(), headers);

        if (request.Method == HttpMethod.Get)
            AddStandalone(batchRequest);
        else
            GetOrCreateChangeSet().Requests.Add(batchRequest);

        return new BatchReference(batchRequest.ContentId);
    }

    /// <summary>
    /// Closes the current changeset so that subsequent write operations are grouped into a new,
    /// independent transaction. Operations within a single changeset succeed or fail together;
    /// separate changesets are applied independently in order.
    /// </summary>
    /// <remarks>
    /// Calling this with no preceding writes, or multiple times in a row, is a no-op (no empty
    /// changeset is emitted). The next write operation begins a fresh changeset.
    /// </remarks>
    public BatchBuilder NewChangeset()
    {
        _currentChangeSet = null;
        return this;
    }

    /// <summary>
    /// Adds a standalone retrieve (GET) operation for a single entity by ID. Retrieve operations
    /// are emitted at the top level of the batch (outside any changeset), as required by Dataverse.
    /// Adding a retrieve closes the current changeset.
    /// </summary>
    /// <typeparam name="T">The POCO type used to infer the entity set name.</typeparam>
    /// <param name="id">The primary key ID of the entity to retrieve.</param>
    /// <param name="entitySet">Optional entity set name (inferred from <typeparamref name="T"/> if not provided).</param>
    /// <param name="select">Optional property logical names to include via $select.</param>
    public BatchBuilder AddRetrieve<T>(Guid id, string? entitySet = null, params string[] select)
    {
        var url = $"{entitySet ?? InferEntitySet<T>()}({id})";
        if (select is { Length: > 0 })
        {
            url += "?$select=" + Uri.EscapeDataString(string.Join(",", select));
        }

        // A polymorphic lookup on T needs the table annotation, which each batch part must request itself.
        var headers = ODataPaging.IncludeAnnotationsPreference(typeof(T), formattedValues: false) is { } annotations
            ? new Dictionary<string, string> { ["Prefer"] = annotations }
            : null;
        AddStandalone(CreateRequest(HttpMethod.Get, url, null, headers));
        return this;
    }

    /// <summary>
    /// Adds a standalone GET operation using a raw relative URL (e.g., "accounts?$top=5&amp;$select=name").
    /// GET operations are emitted at the top level of the batch (outside any changeset).
    /// Adding a GET closes the current changeset.
    /// </summary>
    /// <param name="relativeUrl">The relative request URL, without the API base.</param>
    public BatchBuilder AddGet(string relativeUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUrl);
        AddStandalone(CreateRequest(HttpMethod.Get, relativeUrl, null));
        return this;
    }

    /// <summary>
    /// Continues with the remaining operations when one fails (<c>Prefer: odata.continue-on-error</c>).
    /// Without it, Dataverse stops at the first failure. A changeset is still applied atomically.
    /// </summary>
    public BatchBuilder ContinueOnError(bool value = true)
    {
        _continueOnError = value;
        return this;
    }

    private ChangeSet GetOrCreateChangeSet()
    {
        if (_currentChangeSet is null)
        {
            _currentChangeSet = new ChangeSet($"changeset_{Guid.NewGuid():N}");
            _items.Add(_currentChangeSet);
        }
        return _currentChangeSet;
    }

    // Creates a request with the next Content-ID assigned. Centralizing assignment keeps the
    // counter and the BatchReference returned to callers in sync.
    private BatchRequest CreateRequest(HttpMethod method, string relativeUrl, JsonNode? payload, IReadOnlyDictionary<string, string>? headers = null)
        => new(method, relativeUrl, payload, _nextContentId++, headers);

    private void AddStandalone(BatchRequest request)
    {
        // A standalone GET cannot be part of a changeset; close the current one.
        _currentChangeSet = null;
        _items.Add(new StandaloneItem(request));
    }

    /// <summary>
    /// Executes the batch operation and returns a structured <see cref="BatchResult"/>.
    /// Individual operation results are parsed from the multipart response,
    /// providing consistent error handling with other builders.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A <see cref="BatchResult"/> containing per-operation results. Call
    /// <see cref="BatchResult.ThrowIfAnyFailed"/> to throw a <see cref="DataverseException"/>
    /// if any individual operation failed.</returns>
    /// <exception cref="DataverseException">Thrown when the overall batch HTTP request fails (non-2xx).</exception>
    public async Task<BatchResult> ExecuteAsync(CancellationToken ct = default)
    {
        _guard.Use();

        // If there are no operations, return a successful empty result
        if (_items.Count == 0 || _items.All(i => i is ChangeSet { Requests.Count: 0 }))
        {
            return new BatchResult([]);
        }

        var batchBoundary = $"batch_{_batchId:N}";
        using var batchContent = new MultipartContent("mixed", batchBoundary);

        // Records the structural layout of the batch so the response can be reconciled afterward:
        // each changeset's set of Content-IDs (operations that succeed or fail together) and the
        // Content-ID of each top-level standalone GET.
        var layout = new List<BatchGroup>();

        foreach (var item in _items)
        {
            switch (item)
            {
                case ChangeSet changeSet:
                    // Skip empty changesets (e.g., NewChangeset() called without subsequent writes).
                    if (changeSet.Requests.Count == 0)
                    {
                        continue;
                    }

                    // Each changeset is a nested multipart body whose parts succeed or fail together.
                    var changesetContent = new MultipartContent("mixed", changeSet.Boundary);
                    var changesetIds = new List<int>(changeSet.Requests.Count);
                    foreach (var request in changeSet.Requests)
                    {
                        changesetContent.Add(CreateBatchPart(request, request.ContentId));
                        changesetIds.Add(request.ContentId);
                    }
                    batchContent.Add(changesetContent);
                    layout.Add(new BatchGroup(changesetIds, IsChangeSet: true));
                    break;

                case StandaloneItem standalone:
                    // Standalone GETs are emitted directly at the top level of the batch.
                    batchContent.Add(CreateBatchPart(standalone.Request, standalone.Request.ContentId));
                    layout.Add(new BatchGroup([standalone.Request.ContentId], IsChangeSet: false));
                    break;
            }
        }

        var batchRequest = new DataverseRequest(HttpMethod.Post, "$batch") { Content = batchContent };

        if (_continueOnError)
        {
            batchRequest.Headers["Prefer"] = "odata.continue-on-error";
        }

        string responseBody;
        string? boundary;
        try
        {
            using var response = await _client.SendAsync(batchRequest, ct).ConfigureAwait(false);

            responseBody = await response.ReadStringAsync(ct).ConfigureAwait(false);
            boundary = response.Message.Content.Headers.ContentType?.Parameters
                .FirstOrDefault(p => p.Name == "boundary")?.Value?.Trim('"')
                ?? ExtractBoundaryFromBody(responseBody);
        }
        catch (DataverseException ex) when (ExtractBoundaryFromBody(ex.RawResponse) is { } errorBoundary)
        {
            // When an operation fails without continue-on-error, Dataverse returns the whole batch with a
            // non-success status but still sends the multipart body, which carries each operation's error.
            // Only an error body that is not multipart (a failure of the batch itself) is rethrown.
            responseBody = ex.RawResponse;
            boundary = errorBoundary;
        }

        var parsed = ParseBatchResponse(responseBody, boundary);
        var results = ReconcileResults(layout, parsed);
        return new BatchResult(results);
    }

    private StringContent CreateBatchPart(BatchRequest request, int contentId)
    {
        // The multipart/HTTP grammar requires CRLF line endings. StringBuilder.AppendLine emits
        // Environment.NewLine ("\n" on Linux/containers), which produces malformed batch parts,
        // so line breaks are written explicitly as "\r\n".
        const string Crlf = "\r\n";
        var sb = new StringBuilder();

        // HTTP request line
        sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"{request.Method} {_client.BaseUrl}/{request.RelativeUrl.TrimStart('/')} HTTP/1.1").Append(Crlf);

        foreach (var (name, value) in PartHeaders(request))
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"{name}: {value}").Append(Crlf);

        // GET requests (e.g., standalone retrieves) have no request body and must not declare a
        // JSON content type. Write operations within a changeset send a JSON payload.
        if (request.Payload != null)
        {
            sb.Append("Content-Type: application/json").Append(Crlf);
            var jsonContent = request.Payload.ToJsonString();
            var contentLength = Encoding.UTF8.GetByteCount(jsonContent);
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"Content-Length: {contentLength}").Append(Crlf);
            sb.Append(Crlf); // Empty line between headers and body
            sb.Append(jsonContent);
        }
        else
        {
            sb.Append(Crlf); // Empty line after headers
        }

        var partContent = new StringContent(sb.ToString());
        partContent.Headers.ContentType = new MediaTypeHeaderValue("application/http");
        partContent.Headers.Add("Content-Transfer-Encoding", "binary");

        // Content-ID is a header of the MIME part, not of the HTTP request inside it: that is where the
        // OData batch format defines it and where Dataverse resolves $<content-id> references from.
        partContent.Headers.Add("Content-ID", contentId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return partContent;
    }

    // The headers of one part: the scoped headers from WithOptions, then the operation's own, which take precedence.
    private IEnumerable<KeyValuePair<string, string>> PartHeaders(BatchRequest request)
    {
        if (_scopedHeaders == null)
            return request.Headers ?? Enumerable.Empty<KeyValuePair<string, string>>();

        var headers = new Dictionary<string, string>(_scopedHeaders, StringComparer.OrdinalIgnoreCase);
        if (request.Headers != null)
        {
            foreach (var (name, value) in request.Headers)
                headers[name] = value;
        }
        return headers;
    }

    private static JsonNode SerializeEntity<T>(T entity, params DataverseOperation[] operations) where T : class
        => EntityPayload.Serialize(entity, operations);

    private static string InferEntitySet<T>()
    {
        return ODataPropertyNameCache.GetEntitySetName<T>();
    }

    [GeneratedRegex(@"HTTP/1\.1\s+(\d{3})\s+")]
    private static partial Regex HttpStatusLineRegex();

    private static List<BatchOperationResult> ParseBatchResponse(string responseBody, string? boundary)
    {
        var results = new List<BatchOperationResult>();
        if (string.IsNullOrWhiteSpace(responseBody) || string.IsNullOrEmpty(boundary))
            return results;

        ParseMultipartParts(responseBody, boundary, results);

        // Assign sequential Content-IDs where the header was missing
        for (int i = 0; i < results.Count; i++)
        {
            if (results[i].ContentId == 0)
                results[i] = new BatchOperationResult(i + 1, results[i].StatusCode, results[i].Body, results[i].EntityId, results[i].Location);
        }

        return results;
    }

    // Reconciles the flat list of parsed response parts against the structural layout of the batch.
    //
    // A transactional changeset is atomic: on success Dataverse returns one response part per
    // operation; on failure it returns a SINGLE error part tagged with the failing Content-ID, and
    // every other operation in that changeset is rolled back. This method correlates the error to
    // its operation and synthesizes HTTP 424 (Failed Dependency) results for the rolled-back siblings
    // so callers get one result per queued operation, correctly ordered and correlated.
    private static List<BatchOperationResult> ReconcileResults(
        IReadOnlyList<BatchGroup> layout,
        List<BatchOperationResult> parsed)
    {
        // No structural information (or nothing parsed): fall back to the raw parsed results.
        if (layout.Count == 0)
            return parsed;

        var reconciled = new List<BatchOperationResult>();
        int idx = 0;

        foreach (var group in layout)
        {
            if (!group.IsChangeSet)
            {
                // Standalone GET: consume a single response part if present.
                reconciled.Add(idx < parsed.Count
                    ? WithContentId(parsed[idx++], group.ContentIds[0])
                    : NotExecuted(group.ContentIds[0]));
                continue;
            }

            // Changeset. If the next part is a failure, the whole changeset was rolled back and
            // Dataverse emitted exactly one error part for it.
            if (idx < parsed.Count && !parsed[idx].IsSuccess)
            {
                var error = parsed[idx++];
                var failingId = group.ContentIds.Contains(error.ContentId)
                    ? error.ContentId
                    : group.ContentIds[0];

                foreach (var opId in group.ContentIds)
                {
                    if (opId == failingId)
                    {
                        reconciled.Add(WithContentId(error, opId));
                    }
                    else
                    {
                        // Sibling operation that never took effect due to the changeset rollback.
                        reconciled.Add(new BatchOperationResult(
                            opId,
                            HttpStatusCode.FailedDependency,
                            string.Empty,
                            entityId: null,
                            location: null,
                            wasRolledBack: true));
                    }
                }
            }
            else
            {
                // Changeset succeeded: consume one response part per operation. When the parts carry
                // exactly this changeset's Content-IDs they are matched by ID (in any order); otherwise
                // (e.g. the headers were missing) they are matched in order.
                var parts = parsed.GetRange(idx, Math.Min(group.ContentIds.Count, parsed.Count - idx));
                idx += parts.Count;

                var byId = parts.Count == group.ContentIds.Count
                           && parts.Select(p => p.ContentId).Order().SequenceEqual(group.ContentIds.Order())
                    ? parts.ToDictionary(p => p.ContentId)
                    : null;

                for (int i = 0; i < group.ContentIds.Count; i++)
                {
                    var opId = group.ContentIds[i];
                    if (byId != null)
                        reconciled.Add(byId[opId]);
                    else if (i < parts.Count)
                        reconciled.Add(WithContentId(parts[i], opId));
                    else
                        reconciled.Add(NotExecuted(opId));
                }
            }
        }

        return reconciled;
    }

    // The result for an operation that has no response part: the batch stopped at an earlier failure
    // (without continue-on-error), or the response could not be parsed. It is reported as a failure so
    // that a missing result never counts as a success.
    private static BatchOperationResult NotExecuted(int contentId)
        => new(
            contentId,
            HttpStatusCode.FailedDependency,
            "No response was returned for this operation: the batch stopped at an earlier failure, or the response could not be parsed.");

    // Returns the result with its Content-ID forced to the authoritative layout value when the
    // parsed value is missing (0) or does not match; otherwise returns it unchanged.
    private static BatchOperationResult WithContentId(BatchOperationResult result, int contentId)
    {
        if (result.ContentId == contentId)
            return result;

        return new BatchOperationResult(
            contentId,
            result.StatusCode,
            result.Body,
            result.EntityId,
            result.Location,
            result.WasRolledBack);
    }

    private static void ParseMultipartParts(string body, string boundary, List<BatchOperationResult> results)
    {
        var delimiter = $"--{boundary}";
        var segments = body.Split(delimiter);

        for (int i = 1; i < segments.Length; i++)
        {
            var segment = segments[i];
            var trimmedSegment = segment.TrimStart('\r', '\n');

            // Skip the closing delimiter (starts with "--") and empty segments
            if (string.IsNullOrWhiteSpace(trimmedSegment) || trimmedSegment.StartsWith("--", StringComparison.Ordinal))
                continue;

            var (headers, partBody) = SplitHeadersAndBody(segment);

            // Nested changeset — recurse with the inner boundary
            if (TryExtractBoundary(headers, out var nestedBoundary))
            {
                ParseMultipartParts(partBody, nestedBoundary, results);
            }
            else
            {
                var result = ParseHttpResponsePart(headers, partBody);
                if (result != null)
                    results.Add(result);
            }
        }
    }

    private static BatchOperationResult? ParseHttpResponsePart(string partHeaders, string partBody)
    {
        if (string.IsNullOrWhiteSpace(partBody))
            return null;

        var statusMatch = HttpStatusLineRegex().Match(partBody);
        if (!statusMatch.Success)
            return null;

        var statusCode = (HttpStatusCode)int.Parse(statusMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var contentId = ExtractContentId(partHeaders);

        // The body starts after the first blank line following the HTTP status line and headers.
        // The text between the status line and that blank line holds the inner HTTP response
        // headers (e.g., OData-EntityId/Location for create operations).
        string body = string.Empty;
        string innerHeaders = string.Empty;
        var httpSection = partBody[statusMatch.Index..];
        int blankLine = httpSection.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (blankLine >= 0)
        {
            innerHeaders = httpSection[..blankLine];
            body = httpSection[(blankLine + 4)..].Trim();
        }
        else
        {
            blankLine = httpSection.IndexOf("\n\n", StringComparison.Ordinal);
            if (blankLine >= 0)
            {
                innerHeaders = httpSection[..blankLine];
                body = httpSection[(blankLine + 2)..].Trim();
            }
            else
            {
                innerHeaders = httpSection;
            }
        }

        var location = ExtractEntityIdHeader(innerHeaders);
        var entityId = ODataEntityId.TryParse(location);

        return new BatchOperationResult(contentId, statusCode, body, entityId, location);
    }

    private static (string Headers, string Body) SplitHeadersAndBody(string segment)
    {
        int idx = segment.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (idx >= 0)
            return (segment[..idx], segment[(idx + 4)..]);

        idx = segment.IndexOf("\n\n", StringComparison.Ordinal);
        if (idx >= 0)
            return (segment[..idx], segment[(idx + 2)..]);

        return (segment, string.Empty);
    }

    private static bool TryExtractBoundary(
        string headers,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? boundary)
    {
        boundary = null;
        foreach (var line in headers.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
                continue;

            var idx = trimmed.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;

            boundary = trimmed[(idx + "boundary=".Length)..].Trim().Trim('"');
            return !string.IsNullOrEmpty(boundary);
        }
        return false;
    }

    private static int ExtractContentId(string headers)
    {
        foreach (var line in headers.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Content-ID:", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed["Content-ID:".Length..].Trim();
                if (int.TryParse(value, out var id))
                    return id;
            }
        }
        return 0;
    }

    // Reads the OData-EntityId header (preferred) or Location header from a response part's
    // inner HTTP headers. Dataverse returns the URL of a newly created record in these headers.
    private static string? ExtractEntityIdHeader(string headers)
    {
        string? location = null;
        foreach (var line in headers.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("OData-EntityId:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed["OData-EntityId:".Length..].Trim();
            }
            if (location is null && trimmed.StartsWith("Location:", StringComparison.OrdinalIgnoreCase))
            {
                location = trimmed["Location:".Length..].Trim();
            }
        }
        return location;
    }

    private static string? ExtractBoundaryFromBody(string body)
    {
        var span = body.AsSpan().TrimStart();
        var lineEnd = span.IndexOfAny('\r', '\n');
        if (lineEnd > 2 && span.StartsWith("--"))
            return span[2..lineEnd].ToString();
        return null;
    }

    private record BatchRequest(HttpMethod Method, string RelativeUrl, JsonNode? Payload, int ContentId, IReadOnlyDictionary<string, string>? Headers = null);

    // A batch is an ordered sequence of items: either a transactional changeset
    // (a group of write operations) or a single standalone GET operation.
    private abstract record BatchItem;

    // A transactional changeset containing one or more write operations that
    // succeed or fail together. Each changeset has its own multipart boundary.
    private sealed record ChangeSet(string Boundary) : BatchItem
    {
        public List<BatchRequest> Requests { get; } = [];
    }

    // A single GET operation emitted at the top level of the batch (outside any changeset).
    private sealed record StandaloneItem(BatchRequest Request) : BatchItem;

    // Captures the structural layout of an emitted batch: the Content-IDs in a group and whether
    // the group is a transactional changeset (members roll back together) or a standalone GET.
    private sealed record BatchGroup(IReadOnlyList<int> ContentIds, bool IsChangeSet);
}