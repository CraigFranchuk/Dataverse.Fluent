using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Testing;

/// <summary>
/// A route of a <see cref="FakeDataverseTransport"/>: which requests it answers, and how. Created by
/// <see cref="FakeDataverseTransport.On(HttpMethod, string)"/> and its shortcuts.
/// </summary>
/// <remarks>
/// <para>
/// Each <c>Respond…</c> or <c>Throw…</c> call queues one response. The first matching request gets the first, the
/// next request the second, and the last one repeats for every request after that. A route without a response fails
/// the request that matches it, so a test cannot pass by accident.
/// </para>
/// <para>
/// The methods return the route, so the responses chain:
/// <c>fake.OnGet("accounts").RespondWithEntities(page1, nextLink).RespondWithEntities(page2)</c>.
/// </para>
/// </remarks>
public sealed class FakeRoute
{
    private readonly FakeDataverseTransport _transport;
    private readonly Regex? _path;
    private readonly List<Func<FakeRequest, bool>> _conditions = [];
    private readonly List<Func<FakeRequest, DataverseResponse>> _responses = [];
    private readonly List<FakeRequest> _requests = [];
    private int _next;

    internal FakeRoute(FakeDataverseTransport transport, HttpMethod? method, string? pathPattern, Func<FakeRequest, bool>? predicate)
    {
        _transport = transport;
        Method = method;
        PathPattern = pathPattern;
        _path = pathPattern is null ? null : ToRegex(pathPattern);
        if (predicate != null)
            _conditions.Add(predicate);
    }

    /// <summary>The HTTP method the route answers, or <see langword="null"/> for any.</summary>
    public HttpMethod? Method { get; }

    /// <summary>The path pattern the route answers, or <see langword="null"/> for a route matched by a predicate only.</summary>
    public string? PathPattern { get; }

    /// <summary>The requests this route answered, in order.</summary>
    public IReadOnlyList<FakeRequest> Requests
    {
        get
        {
            lock (_transport.Sync)
                return [.. _requests];
        }
    }

    /// <summary>
    /// Narrows the route to requests that also satisfy <paramref name="condition"/>, for example a query option:
    /// <c>.When(r =&gt; r.GetQueryOption("$filter")?.Contains("statecode") == true)</c>.
    /// </summary>
    public FakeRoute When(Func<FakeRequest, bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        lock (_transport.Sync)
            _conditions.Add(condition);
        return this;
    }

    /// <summary>Queues a response built from the request.</summary>
    /// <param name="responder">Builds the response. It may also throw, for example a <see cref="DataverseException"/>.</param>
    public FakeRoute Respond(Func<FakeRequest, DataverseResponse> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);
        lock (_transport.Sync)
            _responses.Add(responder);
        return this;
    }

    /// <summary>Queues a response with a JSON body.</summary>
    /// <param name="body">The body; <see langword="null"/> responds without one.</param>
    /// <param name="statusCode">The status code. Default: 200 OK.</param>
    public FakeRoute RespondWithJson(JsonNode? body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        // Serialized once: the response is created fresh for every request, and the caller may change the node later.
        var text = body?.ToJsonString();
        return Respond(_ => DataverseResponse.FromJson(text is null ? null : JsonNode.Parse(text), statusCode));
    }

    /// <summary>
    /// Queues a response with one record, written in the shape Dataverse returns it (see
    /// <see cref="DataverseResponse.FromEntity{T}"/>): for a retrieve, an action or function result, or a create or
    /// upsert that asked for the record back.
    /// </summary>
    /// <param name="entity">The record.</param>
    /// <param name="statusCode">The status code. Default: 200 OK. Use 201 Created for a create or an upsert that
    /// created the record.</param>
    /// <param name="etag">The record's <c>@odata.etag</c>, read by <c>RetrieveWithMetadataAsync</c>; optional.</param>
    public FakeRoute RespondWithEntity<T>(T entity, HttpStatusCode statusCode = HttpStatusCode.OK, string? etag = null)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var json = ToJson(DataverseResponse.FromEntity(entity));
        if (etag != null)
            json["@odata.etag"] = etag;

        return RespondWithJson(json, statusCode);
    }

    /// <summary>
    /// Queues a response with a page of records (<c>{ "value": [...] }</c>), for a query, FetchXML or a delta query.
    /// </summary>
    /// <param name="entities">The records.</param>
    /// <param name="nextLink">The <c>@odata.nextLink</c> of a page that is not the last. The request that follows it has
    /// the same path, so queue the next page on the same route.</param>
    /// <param name="totalCount">The <c>@odata.count</c>, for a query that asked for it.</param>
    public FakeRoute RespondWithEntities<T>(IEnumerable<T> entities, string? nextLink = null, long? totalCount = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        return RespondWithJson(ToJson(DataverseResponse.FromEntities(entities, nextLink, totalCount)));
    }

    /// <summary>
    /// Queues the response to a create (or upsert) without the record: 204 No Content with the record's URL in the
    /// <c>OData-EntityId</c> header, which is where <c>CreateAsync</c> reads the new ID from.
    /// </summary>
    /// <param name="id">The new record's ID. When omitted each request gets a new ID.</param>
    public FakeRoute RespondWithCreated(Guid? id = null)
        => Respond(request =>
        {
            var recordId = id ?? Guid.NewGuid();
            // A create is POST accounts; an upsert is PATCH accounts(<key>), whose key is replaced by the ID.
            var path = request.Path;
            var keyStart = path.IndexOf('(', StringComparison.Ordinal);
            var entitySet = keyStart < 0 ? path : path[..keyStart];
            return DataverseResponse.Empty(entityUrl: $"{_transport.BaseUrl}/{entitySet}({recordId})");
        });

    /// <summary>
    /// Queues the response to a <c>CreateMultiple</c> (<c>client.Bulk&lt;T&gt;().CreateAsync</c>): one new ID per record in
    /// the request's <c>Targets</c>.
    /// </summary>
    public FakeRoute RespondWithBulkCreated()
        => Respond(request =>
        {
            var count = request.Payload?["Targets"] is JsonArray targets ? targets.Count : 0;
            var ids = new JsonArray([.. Enumerable.Range(0, count).Select(_ => (JsonNode?)Guid.NewGuid().ToString())]);
            return DataverseResponse.FromJson(new JsonObject { ["Ids"] = ids });
        });

    /// <summary>Queues a 204 No Content response, as Dataverse returns for an update, a delete or an association.</summary>
    public FakeRoute RespondWithNoContent()
        => Respond(_ => DataverseResponse.Empty());

    /// <summary>
    /// Queues a Dataverse error: the request throws <see cref="DataverseException"/> with this status, error code and
    /// message, as the HTTP transport does. Inside a <c>$batch</c> the operation fails instead.
    /// </summary>
    /// <param name="statusCode">The status code, e.g. 404 Not Found or 412 Precondition Failed.</param>
    /// <param name="errorCode">The Dataverse error code, e.g. <c>0x80040217</c>; optional.</param>
    /// <param name="message">The error message; optional.</param>
    public FakeRoute RespondWithError(HttpStatusCode statusCode, string? errorCode = null, string? message = null)
        => Respond(_ => throw CreateError(statusCode, errorCode, message));

    /// <summary>
    /// Queues an exception thrown in place of a response, for a failure that never reached Dataverse:
    /// <see cref="HttpRequestException"/> for a network error, <see cref="TimeoutException"/>, and so on.
    /// </summary>
    public FakeRoute Throw(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Respond(_ => throw exception);
    }

    /// <summary>Returns <c>METHOD pattern</c>.</summary>
    public override string ToString() => $"{Method?.Method ?? "*"} {PathPattern ?? "(predicate)"}";

    internal bool Matches(FakeRequest request)
    {
        if (Method != null && Method != request.Method)
            return false;
        if (_path != null && !_path.IsMatch(request.Path))
            return false;
        return _conditions.TrueForAll(c => c(request));
    }

    // Called under the transport's lock: records the request and picks its response.
    internal Func<FakeRequest, DataverseResponse>? Take(FakeRequest request)
    {
        _requests.Add(request);
        if (_responses.Count == 0)
            return null;

        var responder = _responses[Math.Min(_next, _responses.Count - 1)];
        _next++;
        return responder;
    }

    internal static DataverseException CreateError(HttpStatusCode statusCode, string? errorCode, string? message)
    {
        var error = new JsonObject();
        if (errorCode != null)
            error["code"] = errorCode;
        error["message"] = message ?? $"The fake transport returned {(int)statusCode} ({statusCode}).";

        using var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(new JsonObject { ["error"] = error }.ToJsonString(), Encoding.UTF8, "application/json")
        };
        // A request ID, as Dataverse sends, so code that logs DataverseException.ActivityId has one.
        response.Headers.TryAddWithoutValidation("x-ms-service-request-id", Guid.NewGuid().ToString());

        // The response is in memory, so creating the exception completes synchronously.
        return DataverseException.CreateAsync(response).GetAwaiter().GetResult();
    }

    private static JsonObject ToJson(DataverseResponse response)
    {
        using (response)
            return (JsonObject)JsonNode.Parse(response.Message.Content.ReadAsStringAsync().GetAwaiter().GetResult())!;
    }

    // "*" matches any text; everything else is literal and case-insensitive, as Dataverse's URLs are.
    private static Regex ToRegex(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var escaped = Regex.Escape(pattern.TrimStart('/')).Replace("\\*", ".*", StringComparison.Ordinal);
        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
