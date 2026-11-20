using System.Globalization;
using System.Net;
using System.Text;

namespace Dataverse.Fluent.Testing;

/// <summary>
/// Reads the multipart body of a <c>$batch</c> request and writes the multipart response, in the OData batch format
/// Dataverse uses: top-level parts are a standalone request or a changeset, a nested multipart of requests that succeed
/// or fail together.
/// </summary>
internal static class BatchFormat
{
    private const string Crlf = "\r\n";

    internal sealed record Group(bool IsChangeSet, IReadOnlyList<FakeRequest> Operations);

    internal sealed record OperationResult(
        int? ContentId, HttpStatusCode StatusCode, string? ReasonPhrase, IReadOnlyList<(string Name, string Value)> Headers, string Body)
    {
        public bool IsSuccess => (int)StatusCode is >= 200 and < 300;
    }

    internal static List<Group> ParseRequest(FakeRequest batch, string boundary, string baseUrl)
    {
        var groups = new List<Group>();
        foreach (var (headers, content) in SplitParts(batch.Body ?? string.Empty, boundary))
        {
            if (GetBoundary(headers) is { } changeSetBoundary)
            {
                var operations = SplitParts(content, changeSetBoundary)
                    .Select(part => ParseOperation(part.Headers, part.Content, batch, baseUrl))
                    .ToList();
                if (operations.Count > 0)
                    groups.Add(new Group(IsChangeSet: true, operations));
            }
            else
            {
                groups.Add(new Group(IsChangeSet: false, [ParseOperation(headers, content, batch, baseUrl)]));
            }
        }
        return groups;
    }

    // One application/http part: the request line, the request's headers, a blank line and the body.
    private static FakeRequest ParseOperation(Dictionary<string, string> partHeaders, string content, FakeRequest batch, string baseUrl)
    {
        var (head, body) = SplitHead(content);
        var lines = head.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0)
            throw new InvalidOperationException("A $batch operation has no request line.");

        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2)
            throw new InvalidOperationException($"A $batch operation has an invalid request line: '{lines[0]}'.");

        var headers = ParseHeaders(lines.Skip(1));
        int? contentId = partHeaders.TryGetValue("Content-ID", out var id) && int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
        var text = body.Length == 0 ? null : body;

        return new FakeRequest(
            new HttpMethod(requestLine[0]),
            requestLine[1],
            baseUrl,
            headers,
            FakeDataverseTransport.TryParseObject(text),
            text,
            contentId,
            batch);
    }

    // The parts between "--boundary" delimiters, each split into its MIME headers and content.
    private static List<(Dictionary<string, string> Headers, string Content)> SplitParts(string body, string boundary)
    {
        var parts = new List<(Dictionary<string, string>, string)>();
        var segments = body.Split("--" + boundary);
        for (var i = 1; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(segment))
                continue;

            var (head, content) = SplitHead(segment.TrimStart('\r', '\n'));
            parts.Add((ParseHeaders(head.Split('\n').Select(l => l.TrimEnd('\r'))), content));
        }
        return parts;
    }

    private static (string Head, string Body) SplitHead(string text)
    {
        var index = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var length = 4;
        if (index < 0)
        {
            index = text.IndexOf("\n\n", StringComparison.Ordinal);
            length = 2;
        }
        return index < 0 ? (text, string.Empty) : (text[..index], text[(index + length)..].Trim('\r', '\n'));
    }

    private static Dictionary<string, string> ParseHeaders(IEnumerable<string> lines)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return headers;
    }

    private static string? GetBoundary(Dictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Content-Type", out var contentType)
            || !contentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
            return null;

        var index = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? null : contentType[(index + "boundary=".Length)..].Split(';')[0].Trim().Trim('"');
    }

    /// <summary>Writes the multipart response of a batch, part by part.</summary>
    internal sealed class ResponseWriter
    {
        private readonly string _boundary = $"batchresponse_{Guid.NewGuid()}";
        private readonly StringBuilder _body = new();

        public void AddPart(OperationResult result)
        {
            _body.Append("--").Append(_boundary).Append(Crlf);
            WritePart(_body, result);
        }

        public void AddChangeSet(IEnumerable<OperationResult> results)
        {
            var changeSet = $"changesetresponse_{Guid.NewGuid()}";
            _body.Append("--").Append(_boundary).Append(Crlf)
                .Append("Content-Type: multipart/mixed; boundary=").Append(changeSet).Append(Crlf)
                .Append(Crlf);

            foreach (var result in results)
            {
                _body.Append("--").Append(changeSet).Append(Crlf);
                WritePart(_body, result);
            }

            _body.Append("--").Append(changeSet).Append("--").Append(Crlf);
        }

        public string Finish() => _body.Append("--").Append(_boundary).Append("--").Append(Crlf).ToString();

        public HttpContent CreateContent(string body)
        {
            var content = new StringContent(body, Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("multipart/mixed");
            content.Headers.ContentType.Parameters.Add(new System.Net.Http.Headers.NameValueHeaderValue("boundary", _boundary));
            return content;
        }

        private static void WritePart(StringBuilder body, OperationResult result)
        {
            body.Append("Content-Type: application/http").Append(Crlf)
                .Append("Content-Transfer-Encoding: binary").Append(Crlf);
            if (result.ContentId is { } contentId)
                body.Append("Content-ID: ").Append(contentId.ToString(CultureInfo.InvariantCulture)).Append(Crlf);
            body.Append(Crlf);

            var reason = string.IsNullOrEmpty(result.ReasonPhrase) ? result.StatusCode.ToString() : result.ReasonPhrase;
            body.Append("HTTP/1.1 ").Append(((int)result.StatusCode).ToString(CultureInfo.InvariantCulture)).Append(' ').Append(reason).Append(Crlf);
            foreach (var (name, value) in result.Headers)
                body.Append(name).Append(": ").Append(value).Append(Crlf);
            body.Append(Crlf);

            if (result.Body.Length > 0)
                body.Append(result.Body).Append(Crlf);
        }
    }
}
