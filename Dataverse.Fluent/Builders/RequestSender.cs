using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Sends a builder's request, inside a <c>$batch</c> when Dataverse would reject it as a direct request. In a batch
/// the request's URL travels in the body, which is not subject to the limits IIS applies to a request line. The caller
/// gets the same response, or the same <see cref="DataverseException"/>, as from a direct request.
/// </summary>
/// <remarks>
/// A request is batched when, as confirmed against Dataverse on 2026-11-18:
/// <list type="bullet">
/// <item>Its full URL is longer than <see cref="MaxDirectUrlLength"/>. A direct GET failed above 29,725 characters
/// (400, then 414), well below the documented 32,768; inside <c>$batch</c> a URL of up to 64,000 characters worked.
/// For example, a filter with about 700 IDs in an <c>In</c> list, or a FetchXML query with about 500.</item>
/// <item>Its path holds an alternate key containing <c>&amp;</c>, <c>?</c> or <c>+</c>, which IIS rejects in a path
/// with an HTML error page.</item>
/// </list>
/// A URL longer than <see cref="MaxBatchedUrlLength"/> cannot be sent at all: Dataverse fails to parse it even inside a
/// batch ("Invalid URI: The Uri string is too long", the .NET <see cref="Uri"/> limit of 65,519 characters).
/// </remarks>
internal static class RequestSender
{
    /// <summary>The longest full URL sent directly. Leaves a margin below the 29,725 measured, for longer host names.</summary>
    internal const int MaxDirectUrlLength = 28_000;

    /// <summary>The longest full URL sent at all, inside <c>$batch</c>.</summary>
    internal const int MaxBatchedUrlLength = 64_000;

    /// <summary>Sends <paramref name="request"/>, through <c>$batch</c> when a direct request would be rejected.</summary>
    /// <exception cref="InvalidOperationException">The URL is too long to send even inside <c>$batch</c>.</exception>
    public static Task<DataverseResponse> SendAsync(IDataverseTransport transport, DataverseRequest request, CancellationToken ct)
        => NeedsBatch(transport, request) ? SendInBatchAsync(transport, request, ct) : transport.SendAsync(request, ct);

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
    /// Whether the request must be sent inside <c>$batch</c>: its full URL is too long for a direct request, or its path
    /// holds an encoded <c>&amp;</c>, <c>?</c> or <c>+</c>, which only a percent-encoded alternate-key value puts there.
    /// </summary>
    /// <exception cref="InvalidOperationException">The URL is too long to send even inside <c>$batch</c>.</exception>
    internal static bool NeedsBatch(IDataverseTransport transport, DataverseRequest request)
    {
        // A raw body (a $batch itself, a file) cannot be nested in a batch.
        if (request.Content != null)
            return false;

        var relative = ToRelativeUrl(transport, request.Url);
        if (relative is null)
            return false;

        var fullLength = transport.BaseUrl.Length + 1 + relative.Length;
        if (fullLength > MaxBatchedUrlLength)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                "The request URL is {0:N0} characters, more than Dataverse accepts even inside $batch (about {1:N0}). " +
                "Split a long In() list or FetchXML condition into several queries and combine the results.",
                fullLength, MaxBatchedUrlLength));
        }

        if (fullLength > MaxDirectUrlLength)
            return true;

        var query = relative.IndexOf('?', StringComparison.Ordinal);
        var path = query >= 0 ? relative[..query] : relative;
        return path.Contains("%26", StringComparison.OrdinalIgnoreCase)
            || path.Contains("%3F", StringComparison.OrdinalIgnoreCase)
            || path.Contains("%2B", StringComparison.OrdinalIgnoreCase);
    }

    // The URL relative to the transport's base URL. An absolute URL (an @odata.nextLink) on the same base is made
    // relative, so it can be batched; one on another base is sent as is (null).
    private static string? ToRelativeUrl(IDataverseTransport transport, string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;

        var baseUrl = transport.BaseUrl.TrimEnd('/');
        return url.StartsWith(baseUrl + "/", StringComparison.OrdinalIgnoreCase) ? url[(baseUrl.Length + 1)..] : null;
    }

    private static async Task<DataverseResponse> SendInBatchAsync(IDataverseTransport transport, DataverseRequest request, CancellationToken ct)
    {
        var relative = new DataverseRequest(request.Method, ToRelativeUrl(transport, request.Url)!) { Payload = request.Payload };
        foreach (var (name, value) in request.Headers)
            relative.Headers[name] = value;

        using var activity = DataverseTelemetry.Source.StartActivity("Dataverse Request Sent In Batch");
        activity?.SetTag("dataverse.batched.method", request.Method.ToString());
        activity?.SetTag("dataverse.batched.url_length", transport.BaseUrl.Length + 1 + relative.Url.Length);

        var result = await new BatchBuilder(transport).Add(relative).ExecuteAsync(ct).ConfigureAwait(false);
        if (result.Results.Count != 1)
            throw new InvalidOperationException($"Dataverse returned {result.Results.Count} results for a batch of one request ({request.Method} {relative.Url[..Math.Min(200, relative.Url.Length)]}).");

        var operation = result.Results[0];
        if (!operation.IsSuccess)
            throw DataverseException.FromBatchOperation(operation);

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
