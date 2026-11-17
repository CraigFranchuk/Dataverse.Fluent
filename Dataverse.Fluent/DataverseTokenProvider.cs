using System.Buffers.Text;
using System.Text.Json;
using Azure.Core;

namespace Dataverse.Fluent;

/// <summary>
/// Acquires and caches Dataverse access tokens until shortly before they expire, and drops a cached
/// token when the server rejects it (HTTP 401). Shared by <see cref="DataverseAuthenticationHandler"/>
/// and the inline authentication path of <see cref="DataverseClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// With a <see cref="TokenCredential"/> the real <see cref="AccessToken.ExpiresOn"/> is used. For a
/// string token delegate, the JWT <c>exp</c> claim is read; opaque (non-JWT) tokens fall back to a
/// fixed <see cref="FallbackLifetime"/>.
/// </para>
/// <para>
/// A token is refreshed <see cref="RefreshSkew"/> before it expires (or half-way through its lifetime
/// for short-lived tokens), so requests never go out with a token that is about to expire.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The SemaphoreSlim's wait handle is never created, so it holds nothing to release; the provider lives as long as its client.")]
internal sealed class DataverseTokenProvider
{
    internal static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan FallbackLifetime = TimeSpan.FromMinutes(5);

    private readonly Func<CancellationToken, ValueTask<AccessToken>> _acquire;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private CachedToken? _cached; // accessed via Volatile/Interlocked

    private sealed record CachedToken(string Token, DateTimeOffset RefreshAt);

    private DataverseTokenProvider(Func<CancellationToken, ValueTask<AccessToken>> acquire, TimeProvider? time)
    {
        _acquire = acquire;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Creates a provider backed by an Azure <see cref="TokenCredential"/> (e.g. DefaultAzureCredential,
    /// ManagedIdentityCredential) for the given scope.
    /// </summary>
    internal static DataverseTokenProvider FromCredential(TokenCredential credential, string scope, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        var context = new TokenRequestContext([scope]);
        return new DataverseTokenProvider(ct => credential.GetTokenAsync(context, ct), time);
    }

    /// <summary>
    /// Creates a provider backed by a delegate returning a raw bearer token.
    /// </summary>
    internal static DataverseTokenProvider FromDelegate(Func<Task<string>> tokenProvider, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        var clock = time ?? TimeProvider.System;
        return new DataverseTokenProvider(async _ =>
        {
            var token = await tokenProvider().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("The Dataverse token provider returned an empty token.");

            // Opaque tokens have no known expiry: report one RefreshSkew past FallbackLifetime so the
            // token is cached for exactly FallbackLifetime before it is refreshed.
            var expiresOn = TryReadJwtExpiry(token) ?? clock.GetUtcNow() + FallbackLifetime + RefreshSkew;
            return new AccessToken(token, expiresOn);
        }, clock);
    }

    /// <summary>
    /// Creates the provider configured on <paramref name="options"/>.
    /// </summary>
    internal static DataverseTokenProvider FromOptions(DataverseClientOptions options, TimeProvider? time = null)
    {
        if (options.Credential is not null)
            return FromCredential(options.Credential, options.GetScope(), time);
        if (options.TokenProvider is not null)
            return FromDelegate(options.TokenProvider, time);

        throw new InvalidOperationException("DataverseClientOptions requires either Credential or TokenProvider.");
    }

    /// <summary>
    /// Returns a cached token, acquiring a new one when none is cached or the cached one is due for refresh.
    /// </summary>
    internal async ValueTask<string> GetTokenAsync(CancellationToken ct = default)
    {
        var cached = Volatile.Read(ref _cached);
        if (cached is not null && _time.GetUtcNow() < cached.RefreshAt)
            return cached.Token;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            cached = Volatile.Read(ref _cached);
            if (cached is not null && _time.GetUtcNow() < cached.RefreshAt)
                return cached.Token;

            var acquiredAt = _time.GetUtcNow();
            var token = await _acquire(ct).ConfigureAwait(false);
            Volatile.Write(ref _cached, new CachedToken(token.Token, ComputeRefreshAt(acquiredAt, token.ExpiresOn)));
            return token.Token;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Drops <paramref name="rejectedToken"/> from the cache so the next call acquires a new token.
    /// A token that has already been replaced by a concurrent refresh is left untouched.
    /// </summary>
    internal void Invalidate(string rejectedToken)
    {
        var cached = Volatile.Read(ref _cached);
        if (cached is not null && string.Equals(cached.Token, rejectedToken, StringComparison.Ordinal))
            Interlocked.CompareExchange(ref _cached, null, cached);
    }

    private static DateTimeOffset ComputeRefreshAt(DateTimeOffset acquiredAt, DateTimeOffset expiresOn)
    {
        var lifetime = expiresOn - acquiredAt;
        if (lifetime <= TimeSpan.Zero)
            return acquiredAt; // already expired: never serve it from the cache

        var skew = lifetime >= RefreshSkew + RefreshSkew ? RefreshSkew : lifetime / 2;
        return expiresOn - skew;
    }

    /// <summary>
    /// Reads the <c>exp</c> claim of a JWT without validating it (the token is opaque to the client; the
    /// expiry is only used to decide when to refresh). Returns <see langword="null"/> for non-JWT tokens.
    /// </summary>
    internal static DateTimeOffset? TryReadJwtExpiry(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            return null;

        try
        {
            var payload = Base64Url.DecodeFromChars(parts[1]);
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
