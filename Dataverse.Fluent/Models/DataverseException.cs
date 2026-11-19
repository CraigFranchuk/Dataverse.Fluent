using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Dataverse.Fluent.Models;

/// <summary>
/// Custom exception for Dataverse Web API errors, providing rich details from the OData error payload.
/// This enhances developer diagnostics without relying on metadata or heavy SDK dependencies.
/// </summary>
public class DataverseException : Exception
{
    /// <summary>
    /// The error code from the Dataverse response (e.g., "0x80040200" for general errors).
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// The HTTP status code of the failed response.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The ID Dataverse gave the failed request, to quote to Microsoft support or find in Dataverse logs. Read from the
    /// <c>x-ms-service-request-id</c> (or <c>REQ_ID</c>) response header. <see langword="null"/> when the request was
    /// rejected before it reached Dataverse, such as by IIS for an invalid or over-long URL.
    /// </summary>
    public string? ActivityId { get; }

    /// <summary>
    /// Inner error details, if available (e.g., stack trace or nested errors).
    /// </summary>
    public JsonNode? InnerError { get; }

    /// <summary>
    /// The full raw error response content for advanced debugging.
    /// </summary>
    public string RawResponse { get; }

    private readonly string _message;

    /// <summary>
    /// Creates a <see cref="DataverseException"/> asynchronously by reading the response content without blocking.
    /// This is the preferred way to create a DataverseException from an <see cref="HttpResponseMessage"/>.
    /// </summary>
    /// <param name="response">The HTTP response message containing the error.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A new <see cref="DataverseException"/> with parsed error details.</returns>
    public static async Task<DataverseException> CreateAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        var rawResponse = response.Content != null
            ? await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)
            : string.Empty;

        // Dataverse sends the request's ID as x-ms-service-request-id, repeated as REQ_ID (checked 2026-11-18); it does
        // not send x-ms-activity-id, which is still read first in case it does.
        var activityId = FirstHeader(response, "x-ms-activity-id")
            ?? FirstHeader(response, "x-ms-service-request-id")
            ?? FirstHeader(response, "REQ_ID");

        // A 401 has an empty body; Dataverse puts the reason in this header.
        var reason = FirstHeader(response, "401_Error_Reason");

        return new DataverseException(response.StatusCode, rawResponse, activityId, reason);
    }

    private static string? FirstHeader(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) : null;

    /// <summary>
    /// Private constructor that accepts pre-read content. No I/O occurs in the constructor.
    /// </summary>
    private DataverseException(HttpStatusCode statusCode, string rawResponse, string? activityId, string? reason = null, Exception? innerException = null)
        : base("Dataverse API request failed. See ErrorCode and InnerError for details.", innerException)
    {
        StatusCode = statusCode;
        RawResponse = rawResponse;

        string parsedMessage = base.Message;
        string? errorCode = null;
        JsonNode? innerError = null;

        try
        {
            if (string.IsNullOrWhiteSpace(RawResponse))
            {
                parsedMessage = $"Dataverse API failed with status {(int)StatusCode} ({StatusCode})" + (reason is null ? "." : $": {reason}");
            }
            else if (RawResponse.TrimStart().StartsWith('<'))
            {
                // An HTML page, from IIS rather than Dataverse: keep the message short; the page stays in RawResponse.
                parsedMessage = DescribeHtmlError(StatusCode, RawResponse);
            }
            else
            {
                var errorJson = JsonSerializer.Deserialize<JsonObject>(RawResponse);
                if (errorJson?.TryGetPropertyValue("error", out var errorNode) == true && errorNode is JsonObject errorObj)
                {
                    // Values are read as text whatever their JSON type (some services send a numeric code).
                    errorCode = errorObj.TryGetPropertyValue("code", out var codeNode) ? AsText(codeNode) : null;
                    parsedMessage = errorObj.TryGetPropertyValue("message", out var msgNode) ? AsText(msgNode) ?? parsedMessage : parsedMessage;
                    innerError = errorObj.TryGetPropertyValue("innererror", out var innerNode) ? innerNode : null;
                }
                else
                {
                    // Not the OData error shape (e.g. a gateway or proxy error): keep the body in the message.
                    parsedMessage = $"Dataverse API failed with status {StatusCode}: {RawResponse}";
                }
            }
        }
        catch (JsonException)
        {
            parsedMessage = $"Dataverse API failed with status {StatusCode}: {RawResponse}";
        }
        catch (Exception)
        {
            parsedMessage = $"Dataverse API failed with status {StatusCode}. Unable to parse error details.";
        }

        _message = parsedMessage;
        ErrorCode = errorCode;
        ActivityId = activityId;
        InnerError = innerError;
    }

    private static string? AsText(JsonNode? node) => node is JsonValue value ? value.ToString() : null;

    // e.g. "Dataverse API failed with status 414 (RequestUriTooLong): Request URL Too Long. The response was an HTML
    // page, not a Dataverse error: ..."
    private static string DescribeHtmlError(HttpStatusCode statusCode, string html)
    {
        var title = Regex.Match(html, "<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        var heading = title.Success ? $": {WebUtility.HtmlDecode(title.Groups[1].Value).Trim()}" : string.Empty;

        return $"Dataverse API failed with status {(int)statusCode} ({statusCode}){heading}. The response was an HTML page, not a " +
               "Dataverse error: the request was rejected before it reached Dataverse, usually by IIS for a URL that is too long " +
               "or contains a character it does not allow. The page is in RawResponse.";
    }

    /// <summary>
    /// Overridden message that includes the error code and primary message.
    /// </summary>
    public override string Message => string.IsNullOrEmpty(ErrorCode)
        ? _message 
        : $"{_message} (ErrorCode: {ErrorCode})";

    /// <summary>
    /// Factory method to create from a raw error string (e.g., for non-HTTP scenarios).
    /// </summary>
    /// <param name="errorContent">The raw JSON error content.</param>
    /// <param name="statusCode">Optional HTTP status code.</param>
    /// <returns>A new <see cref="DataverseException"/> instance.</returns>
    public static DataverseException FromErrorContent(string errorContent, HttpStatusCode statusCode = HttpStatusCode.InternalServerError)
    {
        return new DataverseException(statusCode, errorContent ?? string.Empty, activityId: null);
    }

    /// <summary>The exception for an operation that failed inside a <c>$batch</c>, with that operation's request ID.</summary>
    internal static DataverseException FromBatchOperation(BatchOperationResult operation)
        => new(operation.StatusCode, operation.Body, operation.RequestId);
}