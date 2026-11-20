namespace Dataverse.Fluent;

/// <summary>
/// A transport decorator that adds the headers of a <see cref="DataverseRequestOptions"/> to every request. Created by
/// <see cref="DataverseClientExtensions.WithOptions(IDataverseClient, DataverseRequestOptions)"/>.
/// </summary>
/// <remarks>
/// Headers on a <c>$batch</c> request do not apply to the operations inside it, so <see cref="Builders.BatchBuilder"/>
/// also writes <see cref="Headers"/> into each part of a batch sent through this transport.
/// </remarks>
internal sealed class RequestOptionsTransport : IDataverseTransport
{
    private readonly IDataverseTransport _inner;

    private RequestOptionsTransport(IDataverseTransport inner, IReadOnlyDictionary<string, string> headers)
    {
        _inner = inner;
        Headers = headers;
    }

    /// <summary>The headers added to each request. A header the request sets itself takes precedence.</summary>
    internal IReadOnlyDictionary<string, string> Headers { get; }

    /// <inheritdoc />
    public string BaseUrl => _inner.BaseUrl;

    /// <summary>
    /// Wraps <paramref name="inner"/>. When it is already a <see cref="RequestOptionsTransport"/> the two are merged
    /// into one decorator, the new headers replacing existing ones with the same name, so that a batch sees them all.
    /// </summary>
    internal static RequestOptionsTransport Wrap(IDataverseTransport inner, IReadOnlyDictionary<string, string> headers)
    {
        if (inner is not RequestOptionsTransport scoped)
            return new RequestOptionsTransport(inner, headers);

        var merged = new Dictionary<string, string>(scoped.Headers, StringComparer.OrdinalIgnoreCase);

        // The two impersonation headers are alternatives: impersonating a new user replaces the previous one,
        // whichever header named it.
        if (headers.ContainsKey(DataverseRequestOptions.CallerObjectIdHeader) || headers.ContainsKey(DataverseRequestOptions.CallerSystemUserIdHeader))
        {
            merged.Remove(DataverseRequestOptions.CallerObjectIdHeader);
            merged.Remove(DataverseRequestOptions.CallerSystemUserIdHeader);
        }

        foreach (var (name, value) in headers)
            merged[name] = value;

        return new RequestOptionsTransport(scoped._inner, merged);
    }

    /// <inheritdoc />
    public Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A copy, so the caller's request (which may be reused, or queued in a batch) is not modified.
        // Copies every member of DataverseRequest.
        var scoped = new DataverseRequest(request.Method, request.Url)
        {
            Payload = request.Payload,
            Content = request.Content
        };

        foreach (var (name, value) in Headers)
            scoped.Headers[name] = value;
        foreach (var (name, value) in request.Headers)
            scoped.Headers[name] = value;

        return _inner.SendAsync(scoped, ct);
    }
}
