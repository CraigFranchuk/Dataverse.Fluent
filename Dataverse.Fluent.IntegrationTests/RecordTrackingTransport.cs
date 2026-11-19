using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.IntegrationTests;

/// <summary>
/// Records every record the integration tests create, so the fixture can delete them when the run ends, whether or
/// not a test cleans up after itself.
/// </summary>
/// <remarks>
/// <para>
/// Only records this transport saw being created are tracked: a successful POST to an entity set, a create-only
/// upsert (<c>If-None-Match: *</c>) or an upsert that answered 201 Created, and the same operations inside a
/// <c>$batch</c>. Records are never chosen by name or by creator, because the application user the tests run as may
/// also create real records in the environment.
/// </para>
/// <para>
/// Related records created by a deep insert, or by an action such as QualifyLead, are not seen here. Deep-inserted
/// children are removed with their parent where the relationship cascades deletes; tests that create records through
/// actions delete them themselves.
/// </para>
/// </remarks>
internal sealed partial class RecordTrackingTransport(IDataverseTransport inner) : IDataverseTransport
{
    private readonly List<(string EntitySet, Guid Id)> _created = [];
    private readonly Lock _lock = new();

    public string BaseUrl => inner.BaseUrl;

    /// <summary>The records created so far, oldest first.</summary>
    public IReadOnlyList<(string EntitySet, Guid Id)> Created
    {
        get { lock (_lock) return [.. _created]; }
    }

    public async Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
    {
        var path = RelativePath(request.Url);
        var batchBody = path == "$batch" && request.Content != null
            ? await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false)
            : null;

        DataverseResponse response;
        try
        {
            response = await inner.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (DataverseException ex) when (batchBody != null)
        {
            // A batch that stops at a failed operation is answered with an error status, but the changesets before
            // the failure were committed. Their results are in the multipart error body.
            TrackBatch(batchBody, ex.RawResponse);
            throw;
        }

        if (batchBody != null)
        {
            TrackBatch(batchBody, await response.ReadStringAsync(ct).ConfigureAwait(false));
        }
        else if (CreatesRecord(request.Method, path, request.Headers, response.StatusCode))
        {
            var id = response.EntityId ?? await ReadKeyAsync(response, EntitySetOf(path), ct).ConfigureAwait(false);
            if (id is { } created)
                Track(EntitySetOf(path), created);
        }

        return response;
    }

    // POST to a bare entity set ("accounts"), or an upsert that created the record. Entity sets are lowercase, which
    // tells them apart from unbound actions ("WinOpportunity").
    private static bool CreatesRecord(HttpMethod method, string path, IDictionary<string, string> headers, HttpStatusCode status)
    {
        if (method == HttpMethod.Post)
            return EntitySetPath().IsMatch(path);

        if (method == HttpMethod.Patch && KeyedPath().IsMatch(path))
        {
            return status == HttpStatusCode.Created
                || (headers.TryGetValue("If-None-Match", out var noneMatch) && noneMatch.Trim() == "*");
        }

        return false;
    }

    private void TrackBatch(string requestBody, string responseBody)
    {
        // Request parts: "Content-ID: n" header, then the request line "METHOD url HTTP/1.1" and its headers.
        var requests = new Dictionary<int, (HttpMethod Method, string Path, Dictionary<string, string> Headers)>();
        foreach (Match part in BatchRequestPart().Matches(requestBody))
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match header in HeaderLine().Matches(part.Groups["headers"].Value))
                headers[header.Groups["name"].Value] = header.Groups["value"].Value.Trim();

            requests[int.Parse(part.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture)] =
                (new HttpMethod(part.Groups["method"].Value), RelativePath(part.Groups["url"].Value), headers);
        }

        // Response parts: "Content-ID: n", then "HTTP/1.1 status" and the OData-EntityId header of a created record.
        foreach (Match part in BatchResponsePart().Matches(responseBody))
        {
            var contentId = int.Parse(part.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var status = (HttpStatusCode)int.Parse(part.Groups["status"].Value, System.Globalization.CultureInfo.InvariantCulture);
            if ((int)status >= 300 || !requests.TryGetValue(contentId, out var sent))
                continue;

            if (CreatesRecord(sent.Method, sent.Path, sent.Headers, status)
                && Guid.TryParse(part.Groups["key"].Value, out var id))
            {
                Track(EntitySetOf(sent.Path), id);
            }
        }
    }

    private void Track(string entitySet, Guid id)
    {
        lock (_lock)
        {
            if (!_created.Contains((entitySet, id)))
                _created.Add((entitySet, id));
        }
    }

    // With Prefer: return=representation Dataverse omits OData-EntityId; the key is then the record's
    // "<logicalname>id" column. The test tables' logical names are their entity sets without the plural ending.
    private static async Task<Guid?> ReadKeyAsync(DataverseResponse response, string entitySet, CancellationToken ct)
    {
        if (!response.HasBody)
            return null;

        var body = await response.ReadJsonObjectAsync(ct).ConfigureAwait(false);
        foreach (var logicalName in SingularCandidates(entitySet))
        {
            if (body[logicalName + "id"] is JsonValue value && value.TryGetValue<string>(out var text) && Guid.TryParse(text, out var id))
                return id;
        }

        return null;
    }

    private static IEnumerable<string> SingularCandidates(string entitySet)
    {
        if (entitySet.EndsWith("ies", StringComparison.Ordinal))
            yield return entitySet[..^3] + "y";
        if (entitySet.EndsWith("es", StringComparison.Ordinal))
            yield return entitySet[..^2];
        if (entitySet.EndsWith('s'))
            yield return entitySet[..^1];

        // Activity tables (tasks, emails, appointments, ...) all key on activityid.
        yield return "activity";
    }

    private string RelativePath(string url)
    {
        var path = url;
        if (path.StartsWith(inner.BaseUrl, StringComparison.OrdinalIgnoreCase))
            path = path[inner.BaseUrl.Length..];

        var query = path.IndexOf('?');
        return (query >= 0 ? path[..query] : path).Trim('/');
    }

    private static string EntitySetOf(string path)
    {
        var key = path.IndexOf('(');
        return key >= 0 ? path[..key] : path;
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*$")]
    private static partial Regex EntitySetPath();

    [GeneratedRegex(@"^[a-z][a-z0-9_]*\([^/]+\)$")]
    private static partial Regex KeyedPath();

    [GeneratedRegex(@"Content-ID:\s*(?<id>\d+)\s*\r?\n\s*\r?\n(?<method>[A-Z]+) (?<url>\S+) HTTP/1\.1\r?\n(?<headers>(?:[^\r\n]+\r?\n)*)", RegexOptions.IgnoreCase)]
    private static partial Regex BatchRequestPart();

    [GeneratedRegex(@"^(?<name>[A-Za-z-]+):\s*(?<value>[^\r\n]*)", RegexOptions.Multiline)]
    private static partial Regex HeaderLine();

    [GeneratedRegex(@"Content-ID:\s*(?<id>\d+)\s*\r?\n\s*\r?\nHTTP/1\.1 (?<status>\d{3})[^\r\n]*\r?\n(?:(?!HTTP/1\.1)(?!--)[^\r\n]*\r?\n)*?OData-EntityId:\s*\S*\((?<key>[0-9a-fA-F-]{36})\)", RegexOptions.IgnoreCase)]
    private static partial Regex BatchResponsePart();
}
