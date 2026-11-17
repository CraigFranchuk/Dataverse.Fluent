using System.Net;
using System.Net.Http.Headers;

namespace Dataverse.Fluent;

/// <summary>
/// A <see cref="DelegatingHandler"/> that sets the <c>Authorization</c> header from a
/// <see cref="DataverseTokenProvider"/> and recovers from a rejected token.
/// </summary>
/// <remarks>
/// <para>
/// The handler sits inside the resilience pipeline, so every retry attempt (including ones issued after a
/// long <c>Retry-After</c> wait) is sent with a currently valid token.
/// </para>
/// <para>
/// When Dataverse answers 401 Unauthorized, the rejected token is invalidated and the request is sent once
/// more with a freshly acquired token, provided the request body can be replayed (no body, or a buffered
/// body such as <see cref="StringContent"/>, <see cref="ByteArrayContent"/> or a <see cref="MultipartContent"/>
/// of those). A second 401 is returned to the caller.
/// </para>
/// </remarks>
internal sealed class DataverseAuthenticationHandler : DelegatingHandler
{
    private readonly DataverseTokenProvider _tokens;

    public DataverseAuthenticationHandler(DataverseTokenProvider tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokens.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || !IsReplayable(request.Content))
            return response;

        // The token was rejected (revoked, or expired earlier than advertised): refresh and retry once.
        _tokens.Invalidate(token);
        var freshToken = await _tokens.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(freshToken, token, StringComparison.Ordinal))
            return response; // the provider handed back the same token; retrying cannot help

        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", freshToken);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    internal static bool IsReplayable(HttpContent? content) => content switch
    {
        null => true,
        ByteArrayContent => true, // includes StringContent and FormUrlEncodedContent
        ReadOnlyMemoryContent => true,
        MultipartContent multipart => multipart.All(IsReplayable),
        _ => false
    };
}
