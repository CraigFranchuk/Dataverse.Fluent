using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent;

/// <summary>
/// A successful response from the Dataverse Web API: the status, headers and body. A transport returns
/// one only for a successful request; failures are thrown as <see cref="Models.DataverseException"/>.
/// </summary>
/// <remarks>
/// The caller owns the response and must dispose it. The body can be read more than once, so a transport decorator
/// can inspect it before returning the response. A test fake creates one with
/// <see cref="FromJson"/> or <see cref="Empty"/>.
/// </remarks>
public sealed class DataverseResponse : IDisposable
{
    /// <summary>
    /// Wraps an HTTP response. The <see cref="DataverseResponse"/> takes ownership of it.
    /// </summary>
    public DataverseResponse(HttpResponseMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Message = message;
    }

    /// <summary>
    /// Creates a response with a JSON body, for transports that are not HTTP (such as test fakes).
    /// </summary>
    /// <param name="body">The body; <see langword="null"/> creates a response without one.</param>
    /// <param name="statusCode">The status code. Default: 200 OK.</param>
    public static DataverseResponse FromJson(JsonNode? body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var message = new HttpResponseMessage(statusCode);
        if (body != null)
            message.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        return new DataverseResponse(message);
    }

    /// <summary>
    /// Creates a response without a body, for transports that are not HTTP (such as test fakes).
    /// </summary>
    /// <param name="statusCode">The status code. Default: 204 No Content.</param>
    /// <param name="entityUrl">Optional value of the <c>OData-EntityId</c> header, as returned by a create.</param>
    public static DataverseResponse Empty(HttpStatusCode statusCode = HttpStatusCode.NoContent, string? entityUrl = null)
    {
        var message = new HttpResponseMessage(statusCode);
        if (entityUrl != null)
            message.Headers.TryAddWithoutValidation(ODataEntityId.HeaderName, entityUrl);

        return new DataverseResponse(message);
    }

    /// <summary>The underlying HTTP response.</summary>
    public HttpResponseMessage Message { get; }

    /// <summary>The HTTP status code.</summary>
    public HttpStatusCode StatusCode => Message.StatusCode;

    /// <summary>
    /// Whether the response has a body. A 204 No Content response, or one with a zero content length, has none.
    /// </summary>
    // Responses are buffered by HttpClient, so ContentLength is known even for chunked responses.
    public bool HasBody => Message.StatusCode != HttpStatusCode.NoContent && Message.Content.Headers.ContentLength != 0;

    /// <summary>
    /// The URL of the record a create or upsert addressed, from the <c>OData-EntityId</c> (or
    /// <c>Location</c>) header, or <see langword="null"/> when the response has neither.
    /// </summary>
    public string? EntityUrl => ODataEntityId.GetEntityUrl(Message);

    /// <summary>
    /// The record ID parsed from <see cref="EntityUrl"/>, or <see langword="null"/> when it is absent or
    /// carries no GUID.
    /// </summary>
    public Guid? EntityId => ODataEntityId.TryParse(EntityUrl);

    /// <summary>
    /// Returns the first value of a response or content header, or <see langword="null"/> when it is absent.
    /// </summary>
    public string? GetHeader(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (Message.Headers.TryGetValues(name, out var values) || Message.Content.Headers.TryGetValues(name, out values))
            return values.FirstOrDefault();

        return null;
    }

    /// <summary>
    /// Reads the body as a JSON object. Returns an empty object when the response has no body or the body
    /// is not a JSON object.
    /// </summary>
    public async Task<JsonObject> ReadJsonObjectAsync(CancellationToken ct = default)
    {
        if (!HasBody)
            return [];

        var stream = await OpenBodyAsync(ct).ConfigureAwait(false);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return node as JsonObject ?? [];
    }

    /// <summary>
    /// Deserializes the body to <typeparamref name="TResult"/>, directly from the content stream, using the
    /// library's naming rules. Returns <c>default</c> when the response has no body.
    /// </summary>
    public async Task<TResult?> ReadAsync<TResult>(CancellationToken ct = default)
    {
        if (!HasBody)
            return default;

        var stream = await OpenBodyAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<TResult>(stream, JsonDefaults.CaseInsensitive, ct).ConfigureAwait(false);
    }

    // A buffered body is read through one shared stream. It is rewound rather than disposed, so the body can be read
    // more than once: by a transport decorator that inspects the response, and then by the builder. Disposing the
    // response releases it.
    private async Task<Stream> OpenBodyAsync(CancellationToken ct)
    {
        var stream = await Message.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        if (stream.CanSeek)
            stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Deserializes a single-record body to <typeparamref name="TResult"/>. Returns <c>default</c> when the
    /// response has no body or the body is an empty JSON object (<c>{}</c>), which Dataverse returns for
    /// actions without a result.
    /// </summary>
    internal async Task<TResult?> ReadEntityAsync<TResult>(CancellationToken ct = default)
    {
        var json = await ReadJsonObjectAsync(ct).ConfigureAwait(false);
        return DataverseClient.DeserializeEntity<TResult>(json);
    }

    /// <summary>
    /// Reads the body as text. Returns an empty string when the response has no body.
    /// </summary>
    public Task<string> ReadStringAsync(CancellationToken ct = default)
        => Message.Content.ReadAsStringAsync(ct);

    /// <summary>Disposes the underlying HTTP response.</summary>
    public void Dispose() => Message.Dispose();
}
