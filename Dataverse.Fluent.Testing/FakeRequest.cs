using System.Text.Json.Nodes;

namespace Dataverse.Fluent.Testing;

/// <summary>
/// A request received by a <see cref="FakeDataverseTransport"/>: a snapshot taken when it was sent, so it can be
/// inspected after the call returns. An operation inside a <c>$batch</c> is recorded as its own request.
/// </summary>
public sealed class FakeRequest
{
    internal FakeRequest(
        HttpMethod method,
        string url,
        string baseUrl,
        IReadOnlyDictionary<string, string> headers,
        JsonObject? payload,
        string? body,
        int? contentId = null,
        FakeRequest? batch = null)
    {
        Method = method;
        Url = url;
        Headers = headers;
        Payload = payload;
        Body = body;
        ContentId = contentId;
        Batch = batch;

        var relative = ToRelative(url, baseUrl);
        var queryStart = relative.IndexOf('?', StringComparison.Ordinal);
        Path = Uri.UnescapeDataString(queryStart < 0 ? relative : relative[..queryStart]);
        Query = ParseQuery(queryStart < 0 ? string.Empty : relative[(queryStart + 1)..]);
    }

    /// <summary>The HTTP method.</summary>
    public HttpMethod Method { get; }

    /// <summary>
    /// The URL exactly as the builder sent it: relative to the base URL (<c>accounts?$top=1</c>), or absolute for an
    /// <c>@odata.nextLink</c> and for the operations of a batch.
    /// </summary>
    public string Url { get; }

    /// <summary>
    /// The URL's path relative to the base URL, decoded and without the query: <c>accounts</c>,
    /// <c>accounts(00000000-0000-0000-0000-000000000001)</c>, <c>accounts/Microsoft.Dynamics.CRM.CreateMultiple</c>.
    /// Routes match against it.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The decoded query options, such as <c>$filter</c> and <c>$select</c>. Names are case-sensitive, as OData's are.
    /// </summary>
    public IReadOnlyDictionary<string, string> Query { get; }

    /// <summary>The request headers, such as <c>Prefer</c>, <c>If-Match</c> and <c>MSCRM.BypassBusinessLogic</c>.
    /// Names are case-insensitive.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>The JSON body of a create, update or action, or <see langword="null"/>. A copy: changing it does not
    /// change the request.</summary>
    public JsonObject? Payload { get; }

    /// <summary>The body as text, or <see langword="null"/> for a request without one. For a <c>$batch</c> request it is
    /// the multipart body.</summary>
    public string? Body { get; }

    /// <summary>The operation's Content-ID inside a <c>$batch</c>, or <see langword="null"/> for a request sent on its own.</summary>
    public int? ContentId { get; }

    /// <summary>The <c>$batch</c> request this operation was part of, or <see langword="null"/>.</summary>
    public FakeRequest? Batch { get; }

    /// <summary>For a <c>$batch</c> request, its operations in order; otherwise empty.</summary>
    public IReadOnlyList<FakeRequest> BatchOperations { get; internal set; } = [];

    /// <summary>Whether this is a <c>$batch</c> request.</summary>
    public bool IsBatch => Method == HttpMethod.Post && Path == "$batch";

    /// <summary>Returns the value of a header, or <see langword="null"/> when the request does not have it.</summary>
    public string? GetHeader(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// Returns the decoded value of a query option such as <c>$filter</c>, or <see langword="null"/> when the request
    /// does not have it. Safe to use in <see cref="FakeRoute.When"/>, which sees every request.
    /// </summary>
    public string? GetQueryOption(string name) => Query.TryGetValue(name, out var value) ? value : null;

    /// <summary>Returns <c>METHOD url</c>.</summary>
    public override string ToString() => $"{Method} {Url}";

    // The part of an absolute URL after the base URL's path (/api/data/v9.2); a relative URL is returned as is.
    private static string ToRelative(string url, string baseUrl)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute) || absolute.Scheme is not ("http" or "https"))
            return url.TrimStart('/');

        var basePath = Uri.TryCreate(baseUrl, UriKind.Absolute, out var root) ? root.AbsolutePath.TrimEnd('/') : string.Empty;
        var path = absolute.AbsolutePath;
        if (basePath.Length > 0 && path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            path = path[basePath.Length..];

        return path.TrimStart('/') + absolute.Query;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var name = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
            options[name] = separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..]);
        }
        return options;
    }
}
