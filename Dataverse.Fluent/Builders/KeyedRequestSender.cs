using System.Text;
using System.Text.Json.Nodes;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Sends a request that addresses a record by alternate key. A key containing <c>&amp;</c>, <c>?</c> or <c>+</c> is
/// rejected by IIS in a request path, with an HTML error page, so such a request is sent as the only operation of a
/// <c>$batch</c>, where its URL travels in the body. The caller gets the same response, or the same
/// <see cref="DataverseException"/>, as from a direct request. This was confirmed against Dataverse on 2026-11-18.
/// </summary>
internal static class KeyedRequestSender
{
    /// <summary>Sends <paramref name="request"/>, through <c>$batch</c> when its path holds a key IIS would reject.</summary>
    public static Task<DataverseResponse> SendAsync(IDataverseTransport transport, DataverseRequest request, CancellationToken ct)
        => NeedsBatch(request) ? SendInBatchAsync(transport, request, ct) : transport.SendAsync(request, ct);

    /// <summary>Sends a request and returns its body as a <see cref="JsonObject"/> (empty when there is no body).</summary>
    public static async Task<JsonObject> ExecuteJsonAsync(IDataverseTransport transport, HttpMethod method, string url,
        JsonObject? payload, IDictionary<string, string>? headers, CancellationToken ct)
    {
        using var response = await SendAsync(transport, CreateRequest(method, url, payload, headers), ct).ConfigureAwait(false);
        return await response.ReadJsonObjectAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Sends a request and deserializes its body to <typeparamref name="TResult"/>.</summary>
    public static async Task<TResult?> ExecuteAsync<TResult>(IDataverseTransport transport, HttpMethod method, string url,
        IDictionary<string, string>? headers, CancellationToken ct)
    {
        using var response = await SendAsync(transport, CreateRequest(method, url, null, headers), ct).ConfigureAwait(false);
        return await response.ReadAsync<TResult>(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the request's path (before any query string) holds an encoded <c>&amp;</c>, <c>?</c> or <c>+</c>, which
    /// only a percent-encoded alternate-key value puts there.
    /// </summary>
    internal static bool NeedsBatch(DataverseRequest request)
    {
        if (request.Content != null || request.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return false;

        var query = request.Url.IndexOf('?', StringComparison.Ordinal);
        var path = query >= 0 ? request.Url[..query] : request.Url;
        return path.Contains("%26", StringComparison.OrdinalIgnoreCase)
            || path.Contains("%3F", StringComparison.OrdinalIgnoreCase)
            || path.Contains("%2B", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<DataverseResponse> SendInBatchAsync(IDataverseTransport transport, DataverseRequest request, CancellationToken ct)
    {
        var result = await new BatchBuilder(transport).Add(request).ExecuteAsync(ct).ConfigureAwait(false);
        if (result.Results.Count != 1)
            throw new InvalidOperationException($"Dataverse returned {result.Results.Count} results for a batch of one request ({request}).");

        var operation = result.Results[0];
        if (!operation.IsSuccess)
            throw DataverseException.FromErrorContent(operation.Body, operation.StatusCode);

        var message = new HttpResponseMessage(operation.StatusCode);
        if (!string.IsNullOrEmpty(operation.Body))
            message.Content = new StringContent(operation.Body, Encoding.UTF8, "application/json");
        if (operation.Location != null)
            message.Headers.TryAddWithoutValidation(ODataEntityId.HeaderName, operation.Location);

        return new DataverseResponse(message);
    }

    private static DataverseRequest CreateRequest(HttpMethod method, string url, JsonObject? payload, IDictionary<string, string>? headers)
    {
        var request = new DataverseRequest(method, url) { Payload = payload };
        if (headers != null)
        {
            foreach (var (name, value) in headers)
                request.Headers[name] = value;
        }
        return request;
    }
}
