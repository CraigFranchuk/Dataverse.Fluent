using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    /// The activity ID for tracing in Dataverse logs (correlation ID).
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

        string? activityId = null;
        if (response.Headers.TryGetValues("x-ms-activity-id", out var activityValues))
        {
            activityId = activityValues?.FirstOrDefault();
        }

        return new DataverseException(response.StatusCode, rawResponse, activityId);
    }

    /// <summary>
    /// Private constructor that accepts pre-read content. No I/O occurs in the constructor.
    /// </summary>
    private DataverseException(HttpStatusCode statusCode, string rawResponse, string? activityId, Exception? innerException = null)
        : base("Dataverse API request failed. See ErrorCode and InnerError for details.", innerException)
    {
        StatusCode = statusCode;
        RawResponse = rawResponse;

        string parsedMessage = base.Message;
        string? errorCode = null;
        JsonNode? innerError = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(RawResponse))
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
}