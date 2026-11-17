using System.Net;
using System.Text.Json;

namespace Dataverse.Fluent.Tests
{
    internal class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(Func<HttpRequestMessage, bool> matcher, HttpResponseMessage response)> _responses = new();

        /// <summary>
        /// Every request sent through the handler, recorded at send time (the transport disposes the
        /// request, and its content, once the response is returned).
        /// </summary>
        public List<CapturedRequest> Requests { get; } = [];

        public void AddResponse(Func<HttpRequestMessage, bool> matcher, HttpResponseMessage response)
        {
            _responses.Enqueue((matcher, response));
        }

        public void AddJsonResponse(Func<HttpRequestMessage, bool> matcher, object jsonObject, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var json = JsonSerializer.Serialize(jsonObject);
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            AddResponse(matcher, response);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Like a real handler, a cancelled request is never sent (and so is not recorded).
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(await CapturedRequest.FromAsync(request));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No more responses configured");
            }

            var (matcher, response) = _responses.Dequeue();

            if (!matcher(request))
            {
                throw new InvalidOperationException($"Request did not match expected pattern. URL: {request.RequestUri}");
            }

            return response;
        }
    }

    /// <summary>A snapshot of a sent request, safe to inspect after the request is disposed.</summary>
    internal sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, string> Headers,
        string? Body)
    {
        /// <summary>The request URL with percent-encoding removed (see <see cref="HttpRequestMessageExtensions.DecodedUrl"/>).</summary>
        public string DecodedUrl => Uri.UnescapeDataString(Uri.AbsoluteUri);

        /// <summary>The decoded value of a query option (e.g. <c>$select</c>), or <see langword="null"/> when absent.</summary>
        public string? QueryOption(string name)
        {
            foreach (var pair in Uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = pair.IndexOf('=');
                var key = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
                if (key == name)
                    return separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
            return null;
        }

        /// <summary>The header's values joined with ",", or <see langword="null"/> when absent.</summary>
        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

        /// <summary>The body parsed as JSON.</summary>
        public JsonElement Json
        {
            get
            {
                using var document = JsonDocument.Parse(Body ?? throw new InvalidOperationException("The request has no body."));
                return document.RootElement.Clone();
            }
        }

        public static async Task<CapturedRequest> FromAsync(HttpRequestMessage request)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
                headers[header.Key] = string.Join(",", header.Value);
            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                    headers[header.Key] = string.Join(",", header.Value);
            }

            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            return new CapturedRequest(request.Method, request.RequestUri!, headers, body);
        }
    }
}
