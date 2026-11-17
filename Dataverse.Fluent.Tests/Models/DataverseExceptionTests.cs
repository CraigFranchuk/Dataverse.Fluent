using System.Net;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Tests.Models;

public class DataverseExceptionTests
{
    [Fact]
    public async Task CreateAsync_WithHttpResponseMessage_ParsesErrorDetails()
    {
        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80040200"",
                ""message"": ""The record you are trying to access does not exist."",
                ""innererror"": {
                    ""message"": ""Detailed error information"",
                    ""stacktrace"": ""at System.Something...""
                }
            }
        }";

        var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(errorJson),
            Headers = { { "x-ms-activity-id", "12345-abcde-67890" } }
        };

        var exception = await DataverseException.CreateAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("0x80040200", exception.ErrorCode);
        Assert.Contains("record you are trying to access does not exist", exception.Message);
        Assert.Equal("12345-abcde-67890", exception.ActivityId);
        Assert.NotNull(exception.InnerError);
        Assert.Equal(errorJson, exception.RawResponse);
    }

    [Fact]
    public async Task CreateAsync_WithNonJsonResponse_FallsBackToRawMessage()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Plain text error message")
        };

        var exception = await DataverseException.CreateAsync(response);

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Null(exception.ErrorCode);
        Assert.Contains("InternalServerError", exception.Message);
        Assert.Equal("Plain text error message", exception.RawResponse);
    }

    [Fact]
    public async Task CreateAsync_WithEmptyResponse_HandlesGracefully()
    {
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("")
        };

        var exception = await DataverseException.CreateAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Empty(exception.RawResponse);
        Assert.NotNull(exception.Message);
    }

    [Fact]
    public async Task CreateAsync_WithNullResponse_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => DataverseException.CreateAsync(null!));
    }

    [Fact]
    public void FromErrorContent_PreservesErrorDetails()
    {
        var errorContent = @"{""error"":{""code"":""0x123"",""message"":""Test error""}}";

        var exception = DataverseException.FromErrorContent(errorContent, HttpStatusCode.BadRequest);

        Assert.Equal("0x123", exception.ErrorCode);
        Assert.Contains("Test error", exception.Message);
    }

    [Fact]
    public async Task Message_WithErrorCode_IncludesErrorCodeInMessage()
    {
        var errorJson = @"{""error"":{""code"":""0x80040200"",""message"":""Test message""}}";
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(errorJson)
        };

        var exception = await DataverseException.CreateAsync(response);

        Assert.Contains("0x80040200", exception.Message);
        Assert.Contains("Test message", exception.Message);
    }

    [Fact]
    public async Task Message_WithoutErrorCode_ReturnsBasicMessage()
    {
        var errorJson = @"{""error"":{""message"":""Test message without code""}}";
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(errorJson)
        };

        var exception = await DataverseException.CreateAsync(response);

        Assert.DoesNotContain("ErrorCode:", exception.Message);
        Assert.Contains("Test message without code", exception.Message);
    }

    [Fact]
    public void FromErrorContent_CreatesExceptionFromJsonString()
    {
        var errorContent = @"{""error"":{""code"":""0x999"",""message"":""Factory method test""}}";

        var exception = DataverseException.FromErrorContent(errorContent, HttpStatusCode.Forbidden);

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal("0x999", exception.ErrorCode);
        Assert.Contains("Factory method test", exception.Message);
    }

    [Fact]
    public void FromErrorContent_WithNullContent_HandlesGracefully()
    {
        var exception = DataverseException.FromErrorContent(null);

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Empty(exception.RawResponse);
    }

    [Fact]
    public async Task ActivityId_FromResponseHeaders_ExtractedCorrectly()
    {
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(@"{""error"":{""message"":""Test""}}"),
            Headers = { { "x-ms-activity-id", "test-activity-123" } }
        };

        var exception = await DataverseException.CreateAsync(response);

        Assert.Equal("test-activity-123", exception.ActivityId);
    }

    [Fact]
    public async Task ActivityId_WhenHeaderMissing_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(@"{""error"":{""message"":""Test""}}")
        };

        var exception = await DataverseException.CreateAsync(response);

        Assert.Null(exception.ActivityId);
    }

    [Fact]
    public async Task InnerError_WithNestedDetails_PreservesStructure()
    {
        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80040200"",
                ""message"": ""Parent error"",
                ""innererror"": {
                    ""message"": ""Child error"",
                    ""type"": ""System.InvalidOperationException"",
                    ""stacktrace"": ""at MyMethod()""
                }
            }
        }";

        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(errorJson)
        };

        var exception = await DataverseException.CreateAsync(response);

        Assert.NotNull(exception.InnerError);
        Assert.Equal("Child error", exception.InnerError["message"]?.GetValue<string>());
        Assert.Equal("System.InvalidOperationException", exception.InnerError["type"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateAsync_WithMalformedJson_HandlesParseFailureGracefully()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{this is not valid json")
        };

        var exception = await DataverseException.CreateAsync(response);

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Contains("InternalServerError", exception.Message);
        Assert.Null(exception.ErrorCode);
    }

    // ────────── Error bodies in other shapes ──────────

    [Fact]
    public void FromErrorContent_WithNumericCode_KeepsTheCodeAndMessage()
    {
        var exception = DataverseException.FromErrorContent("{\"error\":{\"code\":-2147220891,\"message\":\"Record is locked\"}}", HttpStatusCode.BadRequest);

        Assert.Equal("-2147220891", exception.ErrorCode);
        Assert.Contains("Record is locked", exception.Message);
    }

    [Theory]
    [InlineData("{\"error\":\"Something broke\"}", "Something broke")]                   // error is a string
    [InlineData("{\"Message\":\"An error has occurred.\"}", "An error has occurred.")]   // no error object (gateway/ASP.NET shape)
    [InlineData("[\"unexpected\"]", "unexpected")]                                       // not an object at all
    public void FromErrorContent_WithoutAnErrorObject_IncludesTheBodyInTheMessage(string body, string expected)
    {
        var exception = DataverseException.FromErrorContent(body, HttpStatusCode.BadGateway);

        Assert.Contains(expected, exception.Message);
        Assert.Contains("BadGateway", exception.Message);
        Assert.Null(exception.ErrorCode);
        Assert.Equal(body, exception.RawResponse);
    }

    [Fact]
    public void FromErrorContent_WithNullMessage_KeepsTheDefaultMessage()
    {
        var exception = DataverseException.FromErrorContent("{\"error\":{\"code\":\"0x1\",\"message\":null}}");

        Assert.Equal("0x1", exception.ErrorCode);
        Assert.StartsWith("Dataverse API request failed.", exception.Message);
    }
}
