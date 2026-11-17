using System.Text;
using System.Text.Json;
using Azure.Core;

namespace Dataverse.Fluent.Tests
{
    /// <summary>A controllable clock for token-expiry tests.</summary>
    internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>A <see cref="TokenCredential"/> that issues numbered tokens with a fixed lifetime.</summary>
    internal sealed class FakeTokenCredential(TimeProvider time, TimeSpan lifetime) : TokenCredential
    {
        public int CallCount { get; private set; }
        public List<string> RequestedScopes { get; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestedScopes.AddRange(requestContext.Scopes);
            return new AccessToken($"credential-token-{CallCount}", time.GetUtcNow() + lifetime);
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    internal static class Jwt
    {
        /// <summary>Builds an unsigned JWT whose payload carries the given <c>exp</c>.</summary>
        public static string WithExpiry(DateTimeOffset expiresOn, string subject = "test")
        {
            static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

            var payload = JsonSerializer.Serialize(new { sub = subject, exp = expiresOn.ToUnixTimeSeconds() });
            return $"{Encode("{\"alg\":\"none\"}")}.{Encode(payload)}.sig";
        }
    }

    /// <summary>A primary handler that returns responses from a delegate and records each request.</summary>
    internal sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> AuthorizationTokens { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            AuthorizationTokens.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(respond(request, Requests.Count));
        }

        public static HttpResponseMessage Json(string json = "{\"value\":[]}", System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
            => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
