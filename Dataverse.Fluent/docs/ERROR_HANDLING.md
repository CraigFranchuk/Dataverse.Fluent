# Error Handling

Every failed Dataverse Web API call is surfaced as a `DataverseException`. The exception is thrown by the transport
(`IDataverseTransport`), so it applies to everything built on top of it: `Query<T>()`, `Entity<T>()`, batches,
FetchXML, delta queries, actions and functions, and direct calls through `client.Transport`.

`DataverseException` parses the OData error payload so you do not have to.

## Properties

| Property | Description |
|----------|-------------|
| `ErrorCode` | Dataverse error code, such as `0x80040217` |
| `StatusCode` | HTTP status code |
| `ActivityId` | Correlation id for finding the request in Dataverse logs and support cases |
| `InnerError` | Nested error details (`JsonNode`), including a stack trace when the server returns one |
| `RawResponse` | The full raw response body |
| `Message` | Human-readable message from Dataverse |

## Catching errors

```csharp
try
{
    var accounts = await client.Query<Account>()
        .Where(a => a.Revenue > 1000000)
        .GetListAsync();
}
catch (DataverseException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    // record or table does not exist
}
catch (DataverseException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
{
    // the caller lacks a privilege
}
catch (DataverseException ex)
{
    logger.LogError(ex,
        "Dataverse operation failed. ErrorCode: {ErrorCode}, ActivityId: {ActivityId}, StatusCode: {StatusCode}",
        ex.ErrorCode, ex.ActivityId, ex.StatusCode);
    throw;
}
```

Raw requests behave the same way:

```csharp
using var response = await client.Transport.SendAsync(
    new DataverseRequest(HttpMethod.Post, "accounts") { Payload = new JsonObject { ["name"] = "x" } });
// throws DataverseException on non-success
```

## Common status codes

| Status | Typical cause |
|--------|---------------|
| 400 Bad Request | Invalid query, payload or option; change tracking not enabled for a delta query |
| 401 Unauthorized | Rejected token. The client refreshes once and retries before this surfaces. |
| 403 Forbidden | Missing security role or privilege |
| 404 Not Found | Record does not exist, or `UpdateAsync` on a missing record |
| 410 Gone | Expired delta token |
| 412 Precondition Failed | Optimistic concurrency conflict, or `PreventUpdate` on an existing record. See [UPSERT_CONFLICT_RESOLUTION.md](UPSERT_CONFLICT_RESOLUTION.md). |
| 429 Too Many Requests | Throttled. Retried automatically, honouring `Retry-After`. |
| 5xx | Server error. GET requests are retried; writes are not. |

For the Dataverse error code catalogue see the
[Web API error codes](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/web-service-error-codes)
reference.

## What is retried for you

The default resilience pipeline retries transient failures before an exception reaches your code: GET requests on
transient errors, and writes only on HTTP 429 or when the connection could not be established. A write that times out or
fails with a 5xx is never sent twice. See the [README](../README.md#1-initialize-the-client) for the full description.

## Creating exceptions in tests

```csharp
var json = """{ "error": { "code": "0x80040217", "message": "Record not found" } }""";
var exception = DataverseException.FromErrorContent(json, HttpStatusCode.NotFound);
```

`FromErrorContent` is also useful for fake transports that need to simulate Dataverse failures. With the
`Dataverse.Fluent.Testing` package, a route does it for you:
`fake.OnGet("accounts(*)").RespondWithError(HttpStatusCode.NotFound, "0x80040217", "Record not found")`.

## Network failures

A failure that never reaches Dataverse, such as a DNS error or a refused connection, is retried by the resilience
pipeline and then surfaces as `HttpRequestException`. A request that exceeds its timeout surfaces as
`TimeoutRejectedException` (from Polly), and a cancelled one as `OperationCanceledException`.

## Best practices

1. Catch `DataverseException` for Dataverse-specific handling.
2. Always log `ActivityId`; it is what Microsoft support asks for.
3. Branch on `StatusCode` and `ErrorCode`, not on message text.
4. Inspect `InnerError` and `RawResponse` when the message is not enough.
5. Rethrow with `throw;` to keep the original stack trace.
