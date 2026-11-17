using System.Net;
using Dataverse.Fluent.Models;
using Microsoft.Extensions.Logging;

namespace Dataverse.Fluent.Tests;

public class DataverseClientTests
{
    private DataverseClient CreateTestClient(TestHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new DataverseClient(
            "https://test.crm.dynamics.com/api/data/v9.2",
            () => Task.FromResult("test-token"),
            httpClient
        );
    }

    [Fact]
    public async Task SendRequestAsync_WithSuccessfulResponse_ReturnsResponse()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseData = new { value = "success" };
        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get,
            responseData
        );

        var request = new HttpRequestMessage(HttpMethod.Get, "https://test.crm.dynamics.com/api/data/v9.2/accounts");
        var response = await client.Transport.SendRequestAsync(request);

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task SendRequestAsync_WithBadRequest_ThrowsDataverseException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80040200"",
                ""message"": ""Invalid request payload.""
            }
        }";

        var errorResponse = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json")
        };

        handler.AddResponse(req => true, errorResponse);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://test.crm.dynamics.com/api/data/v9.2/accounts");

        var exception = await Assert.ThrowsAsync<DataverseException>(() => client.Transport.SendRequestAsync(request));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("0x80040200", exception.ErrorCode);
        Assert.Contains("Invalid request payload", exception.Message);
    }

    [Fact]
    public async Task SendRequestAsync_WithUnauthorized_ThrowsDataverseExceptionWithAuthError()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80072560"",
                ""message"": ""Unauthorized: Access token has expired or is invalid.""
            }
        }";

        var errorResponse = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json"),
            Headers = { { "x-ms-activity-id", "auth-error-789" } }
        };

        handler.AddResponse(req => true, errorResponse);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://test.crm.dynamics.com/api/data/v9.2/accounts");

        var exception = await Assert.ThrowsAsync<DataverseException>(() => client.Transport.SendRequestAsync(request));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal("0x80072560", exception.ErrorCode);
        Assert.Equal("auth-error-789", exception.ActivityId);
        Assert.Contains("Access token", exception.Message);
    }

    [Fact]
    public async Task SendRequestAsync_WithServerError_ThrowsDataverseException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80040265"",
                ""message"": ""An unexpected error occurred while processing the request."",
                ""innererror"": {
                    ""message"": ""System.NullReferenceException: Object reference not set"",
                    ""type"": ""System.NullReferenceException"",
                    ""stacktrace"": ""at Microsoft.Crm.Platform.Server...""
                }
            }
        }";

        var errorResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json"),
            Headers = { { "x-ms-activity-id", "server-error-999" } }
        };

        handler.AddResponse(req => true, errorResponse);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://test.crm.dynamics.com/api/data/v9.2/accounts");

        var exception = await Assert.ThrowsAsync<DataverseException>(() => client.Transport.SendRequestAsync(request));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Equal("0x80040265", exception.ErrorCode);
        Assert.Equal("server-error-999", exception.ActivityId);
        Assert.Contains("unexpected error", exception.Message);
        Assert.NotNull(exception.InnerError);
        Assert.Contains("NullReferenceException", exception.InnerError["type"]?.GetValue<string>());
    }

    [Fact]
    public async Task SendRequestAsync_WithRateLimitError_ThrowsDataverseException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80072321"",
                ""message"": ""Number of requests exceeded the limit of 6000 per 5 minutes.""
            }
        }";

        var errorResponse = new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json"),
            Headers = { { "Retry-After", "60" } }
        };

        handler.AddResponse(req => true, errorResponse);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://test.crm.dynamics.com/api/data/v9.2/accounts");

        var exception = await Assert.ThrowsAsync<DataverseException>(() => client.Transport.SendRequestAsync(request));

        Assert.Equal((HttpStatusCode)429, exception.StatusCode);
        Assert.Equal("0x80072321", exception.ErrorCode);
        Assert.Contains("exceeded the limit", exception.Message);
    }

    [Fact]
    public async Task SendRequestAsync_WithNonJsonError_ThrowsDataverseException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("Service temporarily unavailable", System.Text.Encoding.UTF8, "text/plain")
        };

        handler.AddResponse(req => true, errorResponse);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://test.crm.dynamics.com/api/data/v9.2/accounts");

        var exception = await Assert.ThrowsAsync<DataverseException>(() => client.Transport.SendRequestAsync(request));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Null(exception.ErrorCode);
        Assert.Equal("Service temporarily unavailable", exception.RawResponse);
    }

    [Fact]
    public void Query_ReturnsQueryBuilder()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var query = client.Query<TestAccount>();

        Assert.NotNull(query);
    }

    [Fact]
    public void Entity_ReturnsEntityBuilder()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var entity = client.Entity<TestAccount>();

        Assert.NotNull(entity);
    }

    [Fact]
    public void Batch_ReturnsBatchBuilder()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var batch = client.Batch();

        Assert.NotNull(batch);
    }

    [Fact]
    public void Constructor_WithNullTokenProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DataverseClient(
            "https://test.crm.dynamics.com/api/data/v9.2",
            (Func<Task<string>>)null!
        ));
    }

    [Fact]
    public async Task Dispose_DoesNotDisposeExternallyProvidedHttpClient()
    {
        var handler = new TestHttpMessageHandler();
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.OK));
        var httpClient = new HttpClient(handler);
        var client = new DataverseClient(
            "https://test.crm.dynamics.com/api/data/v9.2",
            () => Task.FromResult("test-token"),
            httpClient
        );

        client.Dispose();

        // The caller retains ownership: the HttpClient still sends after the DataverseClient is disposed.
        using var response = await httpClient.GetAsync("https://test.com");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dispose_DisposesInternallyCreatedHttpClient()
    {
        var client = new DataverseClient(
            "https://test.crm.dynamics.com/api/data/v9.2",
            () => Task.FromResult("test-token")
        );

        client.Dispose();

        // After disposal, DataverseClient methods should throw ObjectDisposedException
        var request = new HttpRequestMessage(HttpMethod.Get, "https://test.crm.dynamics.com/api/data/v9.2/accounts");
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.Transport.SendRequestAsync(request));
    }

    [Fact]
    public async Task Dispose_PreventsSubsequentCalls()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        client.Dispose();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://test.crm.dynamics.com/api/data/v9.2/accounts");
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.Transport.SendRequestAsync(request));
    }

    // ────────── ILogger Tests ──────────

    private DataverseClient CreateTestClientWithLogger(TestHttpMessageHandler handler, TestLogger<DataverseClient> logger)
    {
        var httpClient = new HttpClient(handler);
        return new DataverseClient(
            "https://test.crm.dynamics.com/api/data/v9.2",
            () => Task.FromResult("test-token"),
            httpClient,
            logger
        );
    }

    [Fact]
    public async Task SendRequestAsync_LogsDebugOnSuccess()
    {
        var handler = new TestHttpMessageHandler();
        var logger = new TestLogger<DataverseClient>();
        var client = CreateTestClientWithLogger(handler, logger);

        handler.AddJsonResponse(req => true, new { value = "ok" });

        var request = new HttpRequestMessage(HttpMethod.Get, "https://test.crm.dynamics.com/api/data/v9.2/accounts");
        await client.Transport.SendRequestAsync(request);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("Dataverse request:") && e.Message.Contains("GET"));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("succeeded") && e.Message.Contains("200"));
    }

    [Fact]
    public async Task SendRequestAsync_LogsWarningOnFailure()
    {
        var handler = new TestHttpMessageHandler();
        var logger = new TestLogger<DataverseClient>();
        var client = CreateTestClientWithLogger(handler, logger);

        var errorResponse = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":{\"code\":\"0x1\",\"message\":\"bad\"}}", System.Text.Encoding.UTF8, "application/json")
        };
        handler.AddResponse(req => true, errorResponse);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://test.crm.dynamics.com/api/data/v9.2/accounts");
        await Assert.ThrowsAsync<DataverseException>(() => client.Transport.SendRequestAsync(request));

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("failed") && e.Message.Contains("400"));
    }

    [Fact]
    public async Task ExecuteRequestAsync_LogsDebugOnSuccess()
    {
        var handler = new TestHttpMessageHandler();
        var logger = new TestLogger<DataverseClient>();
        var client = CreateTestClientWithLogger(handler, logger);

        handler.AddJsonResponse(req => true, new { value = new TestAccount[] { } });

        var result = await client.Query<TestAccount>().GetListAsync();

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("Dataverse request:") && e.Message.Contains("GET"));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("succeeded"));
    }

    [Fact]
    public async Task ExecuteRequestAsync_LogsWarningOnFailure()
    {
        var handler = new TestHttpMessageHandler();
        var logger = new TestLogger<DataverseClient>();
        var client = CreateTestClientWithLogger(handler, logger);

        var errorResponse = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"error\":{\"code\":\"0x2\",\"message\":\"not found\"}}", System.Text.Encoding.UTF8, "application/json")
        };
        handler.AddResponse(req => true, errorResponse);

        await Assert.ThrowsAsync<DataverseException>(() => client.Query<TestAccount>().GetListAsync());

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("failed") && e.Message.Contains("404"));
    }

    [Fact]
    public async Task LogEntries_IncludeElapsedMs()
    {
        var handler = new TestHttpMessageHandler();
        var logger = new TestLogger<DataverseClient>();
        var client = CreateTestClientWithLogger(handler, logger);

        handler.AddJsonResponse(req => true, new { value = new TestAccount[] { } });

        await client.Query<TestAccount>().GetListAsync();

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("ms"));
    }

    [Fact]
    public async Task WithNullLogger_DoesNotThrow()
    {
        var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var client = new DataverseClient(
            "https://test.crm.dynamics.com/api/data/v9.2",
            () => Task.FromResult("test-token"),
            httpClient,
            logger: null
        );

        handler.AddJsonResponse(req => true, new { value = new TestAccount[] { } });

        var result = await client.Query<TestAccount>().GetListAsync();

        Assert.NotNull(result);
    }
}
