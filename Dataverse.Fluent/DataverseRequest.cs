using System.Text.Json.Nodes;

namespace Dataverse.Fluent;

/// <summary>
/// One request to the Dataverse Web API: the method, URL, headers and body. Every fluent builder produces
/// one of these and sends it through <see cref="IDataverseTransport.SendAsync"/>; a write request can
/// instead be queued in a batch with <see cref="Builders.BatchBuilder.Add(DataverseRequest)"/>.
/// </summary>
public sealed class DataverseRequest
{
    /// <summary>
    /// Initializes a request.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="url">
    /// The URL, relative to <see cref="IDataverseTransport.BaseUrl"/> (<c>accounts?$top=1</c>) or absolute
    /// (an <c>@odata.nextLink</c>). The host of an absolute URL is ignored: requests always go to the
    /// transport's base URL.
    /// </param>
    public DataverseRequest(HttpMethod method, string url)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        Method = method;
        Url = url;
    }

    /// <summary>The HTTP method.</summary>
    public HttpMethod Method { get; }

    /// <summary>The request URL, relative to the transport's base URL or absolute.</summary>
    public string Url { get; }

    /// <summary>
    /// Request headers such as <c>Prefer</c> and <c>If-Match</c>. Names are case-insensitive.
    /// <c>Authorization</c> is set by the transport, and <c>Accept: application/json</c> is added when the
    /// request has no <c>Accept</c> header.
    /// </summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The JSON body of a create, update or action request, or <see langword="null"/> for a request
    /// without one. Mutually exclusive with <see cref="Content"/>.
    /// </summary>
    public JsonObject? Payload { get; set; }

    /// <summary>
    /// A raw body for requests that are not JSON, such as the multipart body of a <c>$batch</c>.
    /// Mutually exclusive with <see cref="Payload"/>. The caller keeps ownership: the transport does not
    /// dispose it, so it can be read again after the call (for example, by a test).
    /// </summary>
    public HttpContent? Content { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Method} {Url}";
}
