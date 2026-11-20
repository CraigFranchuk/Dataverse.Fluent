using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Testing;

/// <summary>
/// An <see cref="IDataverseTransport"/> for unit tests. It answers requests from routes the test sets up, records every
/// request, and answers <c>$batch</c> requests operation by operation, so code under test runs the real fluent builders
/// without a Dataverse environment.
/// </summary>
/// <remarks>
/// <para>
/// A route matches a method and a path (<see cref="FakeRequest.Path"/>: relative to the base URL, decoded, without the
/// query), where <c>*</c> matches any text. When several routes match, the one added last answers, so a test can
/// override a route set up for every test. A request that no route matches throws <see cref="InvalidOperationException"/>
/// naming the request and the routes, so a test cannot pass on a request it did not expect.
/// </para>
/// <para>
/// A <c>$batch</c> request is split into its operations. Each is recorded (see <see cref="Operations"/>) and answered by
/// the routes like a request of its own, and the answers are returned as the multipart response Dataverse sends: a
/// changeset with a failed operation reports that failure, and without <c>ContinueOnError</c> the batch stops at the first
/// failure. Add a route for <c>POST $batch</c> to answer the batch request yourself instead.
/// </para>
/// <para>The fake is thread-safe, so parallel bulk operations can use it.</para>
/// </remarks>
/// <example>
/// <code>
/// var fake = new FakeDataverseTransport();
/// fake.OnGet("accounts").RespondWithEntities([new Account { Name = "Contoso" }]);
/// fake.OnPost("contacts").RespondWithCreated(contactId);
///
/// var service = new MyService(fake.CreateClient());
/// await service.OnboardAsync();
///
/// var create = fake.Operations.Single(r => r.Method == HttpMethod.Post);
/// Assert.Equal("Smith", (string?)create.Payload!["lastname"]);
/// </code>
/// </example>
public sealed class FakeDataverseTransport : IDataverseTransport
{
    /// <summary>The base URL a fake uses when none is given.</summary>
    public const string DefaultBaseUrl = "https://fake.crm.dynamics.com/api/data/v9.2";

    private readonly List<FakeRoute> _routes = [];
    private readonly List<FakeRequest> _requests = [];
    private readonly List<FakeRequest> _operations = [];

    /// <summary>Creates a fake transport.</summary>
    /// <param name="baseUrl">The Web API URL the builders see, e.g. for the <c>OData-EntityId</c> of a create. Default:
    /// <see cref="DefaultBaseUrl"/>.</param>
    public FakeDataverseTransport(string baseUrl = DefaultBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        BaseUrl = baseUrl.TrimEnd('/');
    }

    internal object Sync { get; } = new();

    /// <inheritdoc />
    public string BaseUrl { get; }

    /// <summary>
    /// Every request sent through the transport, in order: one per call, so a <c>$batch</c> is one request (see
    /// <see cref="FakeRequest.BatchOperations"/>).
    /// </summary>
    public IReadOnlyList<FakeRequest> Requests
    {
        get
        {
            lock (Sync)
                return [.. _requests];
        }
    }

    /// <summary>
    /// Every operation, in order: each request sent on its own, and each operation inside a <c>$batch</c> in place of
    /// the batch. Use it to assert on what was written regardless of whether it was batched.
    /// </summary>
    public IReadOnlyList<FakeRequest> Operations
    {
        get
        {
            lock (Sync)
                return [.. _operations];
        }
    }

    /// <summary>Creates a client whose builders send through this transport.</summary>
    public DataverseClient CreateClient() => new(this);

    /// <summary>Adds a route for requests with <paramref name="method"/> and a path matching <paramref name="pathPattern"/>.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="pathPattern">The path relative to the base URL, without the query; <c>*</c> matches any text and
    /// the comparison ignores case. For example <c>accounts</c>, <c>accounts(*)</c> or <c>WhoAmI</c>.</param>
    public FakeRoute On(HttpMethod method, string pathPattern)
    {
        ArgumentNullException.ThrowIfNull(method);
        return Add(new FakeRoute(this, method, pathPattern, predicate: null));
    }

    /// <summary>Adds a route for the requests that satisfy <paramref name="predicate"/>.</summary>
    public FakeRoute On(Func<FakeRequest, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Add(new FakeRoute(this, method: null, pathPattern: null, predicate));
    }

    /// <summary>Adds a route for GET requests: queries, retrieves, functions, FetchXML. See <see cref="On(HttpMethod, string)"/>.</summary>
    public FakeRoute OnGet(string pathPattern) => On(HttpMethod.Get, pathPattern);

    /// <summary>Adds a route for POST requests: creates, actions, bulk operations. See <see cref="On(HttpMethod, string)"/>.</summary>
    public FakeRoute OnPost(string pathPattern) => On(HttpMethod.Post, pathPattern);

    /// <summary>Adds a route for PATCH requests: updates and upserts. See <see cref="On(HttpMethod, string)"/>.</summary>
    public FakeRoute OnPatch(string pathPattern) => On(HttpMethod.Patch, pathPattern);

    /// <summary>Adds a route for DELETE requests. See <see cref="On(HttpMethod, string)"/>.</summary>
    public FakeRoute OnDelete(string pathPattern) => On(HttpMethod.Delete, pathPattern);

    /// <inheritdoc />
    public async Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Like the HTTP transport, a cancelled request is never sent.
        ct.ThrowIfCancellationRequested();

        var body = request.Content is null ? request.Payload?.ToJsonString() : await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var recorded = new FakeRequest(
            request.Method,
            request.Url,
            BaseUrl,
            new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase),
            request.Payload?.DeepClone().AsObject(),
            body);

        FakeRoute? route;
        lock (Sync)
        {
            _requests.Add(recorded);
            route = Find(recorded);
        }

        if (recorded.IsBatch && request.Content != null)
        {
            // The operations are recorded either way; they are answered one by one unless the test routes the batch.
            var groups = ParseBatch(recorded, request.Content.Headers.ContentType);
            return route is null ? SendBatch(recorded, groups) : Answer(recorded, route);
        }

        lock (Sync)
            _operations.Add(recorded);

        return Answer(recorded, route);
    }

    private FakeRoute Add(FakeRoute route)
    {
        lock (Sync)
            _routes.Add(route);
        return route;
    }

    // The route added last wins. Called under the lock.
    private FakeRoute? Find(FakeRequest request)
    {
        for (var i = _routes.Count - 1; i >= 0; i--)
        {
            if (_routes[i].Matches(request))
                return _routes[i];
        }
        return null;
    }

    private DataverseResponse Answer(FakeRequest request, FakeRoute? route)
    {
        Func<FakeRequest, DataverseResponse>? responder;
        lock (Sync)
        {
            if (route is null)
                throw new InvalidOperationException(DescribeUnmatched(request));
            responder = route.Take(request);
        }

        if (responder is null)
        {
            throw new InvalidOperationException(
                $"The route {route} matched {request} but has no response. Add one with RespondWith…, RespondWithError or Throw.");
        }

        return responder(request);
    }

    private string DescribeUnmatched(FakeRequest request)
    {
        var message = new StringBuilder()
            .Append("No route of the fake transport matches ").Append(request.Method).Append(' ').Append(request.Path);
        if (request.ContentId is { } contentId)
            message.Append(" (operation ").Append(contentId).Append(" of a $batch)");
        message.Append(". Add one, e.g. ").Append(SuggestRoute(request)).Append('.');

        message.AppendLine().Append("Routes: ");
        message.Append(_routes.Count == 0 ? "none" : string.Join(", ", _routes));
        return message.ToString();
    }

    private static string SuggestRoute(FakeRequest request)
    {
        var shortcut = request.Method == HttpMethod.Get ? "OnGet"
            : request.Method == HttpMethod.Post ? "OnPost"
            : request.Method == HttpMethod.Patch ? "OnPatch"
            : request.Method == HttpMethod.Delete ? "OnDelete"
            : null;

        return shortcut is null
            ? $"fake.On(new HttpMethod(\"{request.Method.Method}\"), \"{request.Path}\")"
            : $"fake.{shortcut}(\"{request.Path}\")";
    }

    // ────────── $batch ──────────

    private List<BatchFormat.Group> ParseBatch(FakeRequest batch, System.Net.Http.Headers.MediaTypeHeaderValue? contentType)
    {
        var boundary = contentType?.Parameters.FirstOrDefault(p => p.Name == "boundary")?.Value?.Trim('"')
            ?? throw new InvalidOperationException("The $batch request has no multipart boundary.");

        var groups = BatchFormat.ParseRequest(batch, boundary, BaseUrl);
        batch.BatchOperations = [.. groups.SelectMany(g => g.Operations)];
        lock (Sync)
            _operations.AddRange(batch.BatchOperations);
        return groups;
    }

    private DataverseResponse SendBatch(FakeRequest batch, List<BatchFormat.Group> groups)
    {
        var continueOnError = batch.GetHeader("Prefer")?.Contains("odata.continue-on-error", StringComparison.OrdinalIgnoreCase) == true;
        var writer = new BatchFormat.ResponseWriter();
        var failed = false;

        foreach (var group in groups)
        {
            var results = new List<BatchFormat.OperationResult>();
            foreach (var operation in group.Operations)
            {
                var result = Execute(operation);
                results.Add(result);
                // A changeset is atomic: its remaining operations are not run.
                if (!result.IsSuccess)
                    break;
            }

            var groupFailed = !results[^1].IsSuccess;
            if (group.IsChangeSet)
                // Dataverse reports a failed changeset as its one failure.
                writer.AddChangeSet(groupFailed ? [results[^1]] : results);
            else
                writer.AddPart(results[0]);

            if (groupFailed)
            {
                failed = true;
                if (!continueOnError)
                    break;
            }
        }

        var responseBody = writer.Finish();

        // Without continue-on-error, Dataverse answers a batch that stopped at a failure with 400 and the multipart
        // body, which the HTTP transport throws as a DataverseException; with it, the batch succeeds.
        if (failed && !continueOnError)
        {
            using var error = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = writer.CreateContent(responseBody) };
            throw DataverseException.CreateAsync(error).GetAwaiter().GetResult();
        }

        return new DataverseResponse(new HttpResponseMessage(HttpStatusCode.OK) { Content = writer.CreateContent(responseBody) });
    }

    private BatchFormat.OperationResult Execute(FakeRequest operation)
    {
        FakeRoute? route;
        lock (Sync)
            route = Find(operation);

        try
        {
            using var response = Answer(operation, route);
            var text = response.HasBody ? response.ReadStringAsync().GetAwaiter().GetResult() : string.Empty;
            var headers = response.Message.Headers.Select(h => (h.Key, string.Join(", ", h.Value)))
                .Concat(response.HasBody ? response.Message.Content.Headers.Select(h => (h.Key, string.Join(", ", h.Value))) : [])
                .ToList();
            return new BatchFormat.OperationResult(operation.ContentId, response.StatusCode, response.Message.ReasonPhrase, headers, text);
        }
        catch (DataverseException ex)
        {
            List<(string, string)> headers = [("Content-Type", "application/json; odata.metadata=minimal")];
            if (ex.ActivityId != null)
                headers.Add(("REQ_ID", ex.ActivityId));
            return new BatchFormat.OperationResult(operation.ContentId, ex.StatusCode, null, headers, ex.RawResponse);
        }
    }

    internal static JsonObject? TryParseObject(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
