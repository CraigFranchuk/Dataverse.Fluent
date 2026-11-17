using System.Text.Json.Nodes;

namespace Dataverse.Fluent;

/// <summary>
/// JSON conveniences over <see cref="IDataverseTransport.SendAsync"/>, for calling the Web API directly
/// when no fluent builder covers a request.
/// </summary>
public static class DataverseTransportExtensions
{
    /// <summary>
    /// Sends a request and returns the response body as a <see cref="JsonObject"/>.
    /// Returns an empty object when the response has no body.
    /// </summary>
    /// <param name="transport">The transport.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="requestUrl">A URL relative to the base URL, or a full absolute URL (@odata.nextLink).</param>
    /// <param name="payload">Optional JSON payload for POST/PATCH requests.</param>
    /// <param name="headers">Optional additional HTTP headers.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<JsonObject> ExecuteRequestAsync(
        this IDataverseTransport transport,
        HttpMethod method,
        string requestUrl,
        JsonObject? payload = null,
        IDictionary<string, string>? headers = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transport);

        using var response = await transport.SendAsync(CreateRequest(method, requestUrl, payload, headers), ct).ConfigureAwait(false);
        return await response.ReadJsonObjectAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a request and deserializes the response body to <typeparamref name="TResult"/>, directly from
    /// the response stream. Returns <c>default</c> when the response has no body.
    /// </summary>
    /// <param name="transport">The transport.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="requestUrl">A URL relative to the base URL, or a full absolute URL (@odata.nextLink).</param>
    /// <param name="payload">Optional JSON payload for POST/PATCH requests.</param>
    /// <param name="headers">Optional additional HTTP headers.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<TResult?> ExecuteRequestAsync<TResult>(
        this IDataverseTransport transport,
        HttpMethod method,
        string requestUrl,
        JsonObject? payload = null,
        IDictionary<string, string>? headers = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transport);

        using var response = await transport.SendAsync(CreateRequest(method, requestUrl, payload, headers), ct).ConfigureAwait(false);
        return await response.ReadAsync<TResult>(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a raw <see cref="HttpRequestMessage"/> through the transport and returns the HTTP response.
    /// The caller owns the returned (successful) response and must dispose it.
    /// </summary>
    /// <remarks>
    /// The message is translated to a <see cref="DataverseRequest"/>: its method, URL, headers and content
    /// are sent; its version and options are not. Prefer <see cref="IDataverseTransport.SendAsync"/>.
    /// </remarks>
    public static async Task<HttpResponseMessage> SendRequestAsync(
        this IDataverseTransport transport,
        HttpRequestMessage request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestUri is null)
            throw new ArgumentException("The request has no URL.", nameof(request));

        var dataverseRequest = new DataverseRequest(request.Method, request.RequestUri.OriginalString)
        {
            Content = request.Content
        };

        foreach (var header in request.Headers)
            dataverseRequest.Headers[header.Key] = string.Join(", ", header.Value);

        var response = await transport.SendAsync(dataverseRequest, ct).ConfigureAwait(false);
        return response.Message;
    }

    private static DataverseRequest CreateRequest(
        HttpMethod method, string requestUrl, JsonObject? payload, IDictionary<string, string>? headers)
    {
        var request = new DataverseRequest(method, requestUrl) { Payload = payload };

        if (headers != null)
        {
            foreach (var header in headers)
                request.Headers[header.Key] = header.Value;
        }

        return request;
    }
}
