using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Dataverse.Fluent.Builders;
using Dataverse.Fluent.Models;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;

namespace Dataverse.Fluent;

/// <summary>
/// The HTTP implementation of <see cref="IDataverseTransport"/>: authentication, resilience, telemetry,
/// logging and error handling for the Dataverse Web API.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DataverseClient"/> creates one of these for you. Construct it directly when you want to wrap it,
/// for example in a decorator that records or audits requests, and pass the decorator to
/// <see cref="DataverseClient(IDataverseTransport)"/>.
/// </para>
/// <para>
/// When constructed without an <see cref="HttpClient"/>, the transport creates and owns one whose pipeline
/// matches the DI registration: the Dataverse resilience pipeline (write-safe retries, circuit breaker,
/// timeouts — see <see cref="DataverseResilienceOptions"/>), then authentication with token caching and
/// refresh on 401, over a pooled <see cref="SocketsHttpHandler"/>.
/// </para>
/// <para>
/// When you supply your own <see cref="HttpClient"/>, its pipeline is used as-is: the transport still sets the
/// <c>Authorization</c> header and refreshes a rejected token once (when the request body can be replayed),
/// but resilience is your responsibility.
/// </para>
/// </remarks>
public sealed class DataverseHttpTransport : IDataverseTransport, ITransportLogging, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly ILogger _logger;
    private readonly bool _ownsHttpClient;
    private readonly bool _includeQueryInTelemetry;
    private bool _disposed;

    ILogger ITransportLogging.Logger => _logger;

    // Set only when the transport authenticates requests itself (caller-supplied HttpClient). When it owns
    // its pipeline, or is created through DI, DataverseAuthenticationHandler authenticates instead.
    private readonly DataverseTokenProvider? _inlineTokens;

    private static readonly MediaTypeWithQualityHeaderValue JsonMediaType = new("application/json");

    /// <inheritdoc />
    public string BaseUrl => _baseUrl;

    /// <summary>
    /// Initializes a new HTTP transport from <see cref="DataverseClientOptions"/>.
    /// </summary>
    /// <param name="options">The client options. Exactly one of <see cref="DataverseClientOptions.Credential"/> and <see cref="DataverseClientOptions.TokenProvider"/> must be set.</param>
    /// <param name="httpClient">
    /// Optional custom HttpClient. When provided, the caller retains ownership and its pipeline is used
    /// as-is (no resilience is added). When omitted, the transport creates a resilient pipeline configured by
    /// <see cref="DataverseClientOptions.Resilience"/>.
    /// </param>
    /// <param name="logger">Optional logger for request/response diagnostics.</param>
    public DataverseHttpTransport(DataverseClientOptions options, HttpClient? httpClient = null, ILogger<DataverseHttpTransport>? logger = null)
        : this(options, httpClient, primaryHandler: null, logger)
    {
    }

    /// <summary>
    /// Creates a transport with an owned pipeline over <paramref name="primaryHandler"/> (used by tests), or over
    /// <paramref name="httpClient"/> when supplied.
    /// </summary>
    internal DataverseHttpTransport(DataverseClientOptions options, HttpClient? httpClient, HttpMessageHandler? primaryHandler, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(requireAuthentication: true);

        _logger = logger ?? NullLogger<DataverseHttpTransport>.Instance;
        _baseUrl = options.BaseUrl.TrimEnd('/');
        _includeQueryInTelemetry = options.IncludeQueryInTelemetry;
        var tokens = DataverseTokenProvider.FromOptions(options);

        if (httpClient is null)
        {
            _httpClient = CreateHttpClient(options.Resilience, tokens, primaryHandler);
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
            _inlineTokens = tokens;
        }
    }

    /// <summary>
    /// Internal constructor for the DI path where authentication and resilience are provided by the
    /// <see cref="IHttpClientFactory"/> pipeline.
    /// </summary>
    internal DataverseHttpTransport(string baseUrl, HttpClient httpClient, ILogger? logger = null, bool includeQueryInTelemetry = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(httpClient);
        new DataverseClientOptions { BaseUrl = baseUrl }.Validate(requireAuthentication: false);

        _logger = logger ?? NullLogger<DataverseHttpTransport>.Instance;
        _baseUrl = baseUrl.TrimEnd('/');
        _ownsHttpClient = false;
        _httpClient = httpClient;
        _includeQueryInTelemetry = includeQueryInTelemetry;
    }

    // Builds the same pipeline as the DI registration: resilience (outer) → authentication → transport.
    internal static HttpClient CreateHttpClient(DataverseResilienceOptions resilience, DataverseTokenProvider tokens, HttpMessageHandler? primaryHandler)
    {
        HttpMessageHandler handler = new DataverseAuthenticationHandler(tokens)
        {
            InnerHandler = primaryHandler ?? CreatePrimaryHandler()
        };

        if (resilience.Enabled)
        {
            var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>();
            DataverseResiliencePipeline.Configure(pipeline, resilience);
            handler = new ResilienceHandler(pipeline.Build()) { InnerHandler = handler };
        }

        var client = new HttpClient(handler, disposeHandler: true);
        if (resilience.Enabled)
        {
            // The pipeline's TotalRequestTimeout governs; HttpClient's default 100 s would cut off retries.
            client.Timeout = Timeout.InfiniteTimeSpan;
        }

        return client;
    }

    // The handler that sends the requests of an owned pipeline.
    internal static SocketsHttpHandler CreatePrimaryHandler()
    {
        var handler = new SocketsHttpHandler
        {
            // Recycle pooled connections so DNS changes are picked up by long-lived clients.
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AutomaticDecompression = DecompressionMethods.All
        };
        DisableAffinityCookie(handler);
        return handler;
    }

    /// <summary>
    /// Stops <paramref name="handler"/> storing cookies. Dataverse sets an <c>ARRAffinity</c> cookie on every response
    /// (checked 2026-11-20), which sends every later request from the client to the same web server; Microsoft's
    /// guidance for parallel requests, such as bulk operations, is to drop it. The client uses no other cookie.
    /// A handler that is neither a <see cref="SocketsHttpHandler"/> nor an <see cref="HttpClientHandler"/> is left as it is.
    /// </summary>
    internal static void DisableAffinityCookie(HttpMessageHandler handler)
    {
        switch (handler)
        {
            case SocketsHttpHandler sockets:
                sockets.UseCookies = false;
                break;
            case HttpClientHandler client:
                client.UseCookies = false;
                break;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The request URL is resolved against <see cref="BaseUrl"/>: the host of an absolute URL is ignored.
    /// An <c>Accept: application/json</c> header is added when the request has none. On failure the HTTP
    /// response is disposed and a <see cref="DataverseException"/> is thrown.
    /// </remarks>
    public async Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Payload != null && request.Content != null)
            throw new ArgumentException("A request has either a JSON Payload or a raw Content, not both.", nameof(request));

        using var message = new HttpRequestMessage(request.Method, $"{_baseUrl}/{ToRelativeUrl(request.Url)}");

        // Note: headers (including Authorization) are intentionally excluded from logging.
        foreach (var header in request.Headers)
        {
            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
                throw new ArgumentException($"'{header.Key}' is not a valid request header.", nameof(request));
        }

        if (message.Headers.Accept.Count == 0)
            message.Headers.Accept.Add(JsonMediaType);

        if (request.Payload != null)
            message.Content = new StringContent(request.Payload.ToJsonString(JsonDefaults.PayloadSerialization), Encoding.UTF8, "application/json");
        else if (request.Content != null)
            message.Content = request.Content;

        try
        {
            var response = await SendAuthenticatedAsync(message, ct).ConfigureAwait(false);
            await ThrowIfUnsuccessfulAsync(response, ct).ConfigureAwait(false);
            return new DataverseResponse(response);
        }
        finally
        {
            // Disposing the message would dispose its content; raw content belongs to the caller.
            if (request.Content != null)
                message.Content = null;
        }
    }

    // On the inline-authentication path, sets the bearer token (unless the caller set one) and, when the
    // server rejects it with 401, refreshes it and resends the request once. The resend is skipped when the
    // body cannot be replayed or the token provider returns the same token. Returns the response unchecked.
    private async Task<HttpResponseMessage> SendAuthenticatedAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string? token = null;
        if (_inlineTokens != null && request.Headers.Authorization == null)
        {
            token = await _inlineTokens.GetTokenAsync(ct).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await ExecuteCoreAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || token == null
            || !DataverseAuthenticationHandler.IsReplayable(request.Content))
            return response;

        _inlineTokens!.Invalidate(token);
        var freshToken = await _inlineTokens.GetTokenAsync(ct).ConfigureAwait(false);
        if (string.Equals(freshToken, token, StringComparison.Ordinal))
            return response;

        response.Dispose();
        return await ExecuteCoreAsync(CopyWithToken(request, freshToken), ct).ConfigureAwait(false);
    }

    // A request message can only be sent once, so a resend goes out as a copy. The copy shares the original's
    // buffered content and is deliberately not disposed: disposing it would dispose content the original's
    // owner disposes.
    private static HttpRequestMessage CopyWithToken(HttpRequestMessage request, string token)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Content = request.Content,
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);

        foreach (var option in request.Options)
            ((IDictionary<string, object?>)copy.Options)[option.Key] = option.Value;

        copy.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return copy;
    }

    // Shared HTTP send logic: stopwatch, telemetry, logging. Returns the response without inspecting
    // its status; see ThrowIfUnsuccessfulAsync.
    private async Task<HttpResponseMessage> ExecuteCoreAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var activity = DataverseTelemetry.Source.StartActivity("Dataverse Request");
        activity?.SetTag("http.request.method", request.Method.ToString());
        // Query strings carry $filter values, which can be personal data: only the path is recorded
        // unless DataverseClientOptions.IncludeQueryInTelemetry is set.
        var url = _includeQueryInTelemetry || request.RequestUri is not { IsAbsoluteUri: true } uri
            ? request.RequestUri?.ToString()
            : uri.GetLeftPart(UriPartial.Path);
        activity?.SetTag("url.full", url);

        _logger.LogDebug("Dataverse request: {Method} {Url}", request.Method, url);
        var sw = Stopwatch.StartNew();
        var methodTag = new KeyValuePair<string, object?>("http.request.method", request.Method.ToString());

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            DataverseTelemetry.ErrorCount.Add(1, methodTag);
            DataverseTelemetry.RequestDuration.Record(sw.Elapsed.TotalMilliseconds, methodTag);
            DataverseTelemetry.RequestCount.Add(1, methodTag);
            throw;
        }

        sw.Stop();
        var statusCode = (int)response.StatusCode;
        activity?.SetTag("http.response.status_code", statusCode);
        var statusTag = new KeyValuePair<string, object?>("http.response.status_code", statusCode);

        DataverseTelemetry.RequestCount.Add(1, methodTag, statusTag);
        DataverseTelemetry.RequestDuration.Record(sw.Elapsed.TotalMilliseconds, methodTag, statusTag);

        if (!response.IsSuccessStatusCode)
        {
            activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {statusCode}");
            DataverseTelemetry.ErrorCount.Add(1, methodTag, statusTag);
            _logger.LogWarning("Dataverse request failed: {Method} {Url} returned {StatusCode} in {ElapsedMs}ms",
                request.Method, url, statusCode, sw.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogDebug("Dataverse request succeeded: {Method} {Url} returned {StatusCode} in {ElapsedMs}ms",
                request.Method, url, statusCode, sw.ElapsedMilliseconds);
        }

        return response;
    }

    // Converts a failed response into a DataverseException, disposing the response.
    private static async Task ThrowIfUnsuccessfulAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        using (response)
        {
            throw await DataverseException.CreateAsync(response, ct).ConfigureAwait(false);
        }
    }

    // Converts full absolute nextLink URLs to relative paths safely
    private string ToRelativeUrl(string urlOrPath)
    {
        if (string.IsNullOrWhiteSpace(urlOrPath))
            return string.Empty;

        var basePath = new Uri(_baseUrl).AbsolutePath.TrimEnd('/');

        // Only http(s) URLs are absolute here: on Unix "/api/..." also parses as an absolute file:// URI.
        if (!Uri.TryCreate(urlOrPath, UriKind.Absolute, out var absoluteUri)
            || (absoluteUri.Scheme != Uri.UriSchemeHttps && absoluteUri.Scheme != Uri.UriSchemeHttp))
        {
            // Relative: a rooted path may already carry the API path (/api/data/v9.2/...).
            return StripBasePath(urlOrPath, basePath).TrimStart('/');
        }

        // Warn if nextLink points to a different host — always use configured base URL
        var baseHost = new Uri(_baseUrl).Host;
        if (!string.Equals(absoluteUri.Host, baseHost, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Received nextLink with host {NextLinkHost} that does not match configured host {BaseUrlHost}. Using configured base URL.",
                absoluteUri.Host, baseHost);
        }

        // For absolute URLs, keep PathAndQuery only — the host is always ignored. The base URL already
        // carries the API path (e.g. /api/data/v9.2), so strip it to avoid sending it twice.
        return StripBasePath(absoluteUri.PathAndQuery, basePath).TrimStart('/');
    }

    private static string StripBasePath(string pathAndQuery, string basePath)
    {
        if (basePath.Length > 0 &&
            pathAndQuery.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) &&
            (pathAndQuery.Length == basePath.Length || pathAndQuery[basePath.Length] is '/' or '?'))
        {
            return pathAndQuery[basePath.Length..];
        }

        return pathAndQuery;
    }

    /// <summary>
    /// Disposes the underlying <see cref="HttpClient"/> when the transport created it.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }

            _disposed = true;
        }
    }
}
