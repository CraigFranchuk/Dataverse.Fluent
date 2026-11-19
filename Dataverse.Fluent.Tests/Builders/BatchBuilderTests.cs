using System.Net;
using System.Text;
using Dataverse.Fluent.Builders;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Tests.Builders;

public class BatchBuilderTests
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
    public void Constructor_WithNullClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new BatchBuilder(null!));
    }

    [Fact]
    public async Task Constructor_WithCustomBatchId_UsesProvidedId()
    {
        var batchId = Guid.NewGuid();

        var request = await CaptureBatchRequestAsync(b => b.AddCreate(new TestAccount { Name = "x" }), batchId);

        Assert.Equal($"multipart/mixed; boundary=\"batch_{batchId:N}\"", request.Header("Content-Type"));
        Assert.StartsWith($"--batch_{batchId:N}\r\n", request.Body);
    }

    [Fact]
    public async Task AddCreate_WithEntity_EmitsPostWithPayload()
    {
        var body = await CaptureBatchBodyAsync(b => b.AddCreate(new TestAccount { Name = "Test Account", Revenue = 100000m }));

        Assert.Contains($"POST {BaseUrl}/testaccounts HTTP/1.1\r\n", body);
        Assert.Contains("\"name\":\"Test Account\"", body);
        Assert.Contains("\"revenue\":100000", body);
    }

    [Fact]
    public async Task AddCreate_WithCustomEntitySet_UsesProvidedName()
    {
        var body = await CaptureBatchBodyAsync(b => b.AddCreate(new TestAccount { Name = "Test Account" }, "custom_accounts"));

        Assert.Contains($"POST {BaseUrl}/custom_accounts HTTP/1.1\r\n", body);
    }

    [Fact]
    public async Task AddUpdate_WithEntityAndId_EmitsPatchToRecord()
    {
        var accountId = Guid.NewGuid();

        var body = await CaptureBatchBodyAsync(b => b.AddUpdate(accountId, new TestAccount { Name = "Updated Account" }));

        Assert.Contains($"PATCH {BaseUrl}/testaccounts({accountId}) HTTP/1.1\r\n", body);
        Assert.Contains("\"name\":\"Updated Account\"", body);
    }

    [Fact]
    public async Task AddUpdate_WithCustomEntitySet_UsesProvidedName()
    {
        var accountId = Guid.NewGuid();

        var body = await CaptureBatchBodyAsync(b => b.AddUpdate(accountId, new TestAccount { Name = "x" }, "custom_accounts"));

        Assert.Contains($"PATCH {BaseUrl}/custom_accounts({accountId}) HTTP/1.1\r\n", body);
    }

    [Fact]
    public async Task AddDeepInsert_WithParentAndChildren_EmitsNestedArray()
    {
        var body = await CaptureBatchBodyAsync(b => b.AddDeepInsert(
            new TestAccount { Name = "Parent Account" }, "contact_customer_accounts", new { name = "Child 1" }, new { name = "Child 2" }));

        Assert.Contains($"POST {BaseUrl}/testaccounts HTTP/1.1\r\n", body);
        Assert.Contains("\"contact_customer_accounts\":[{\"name\":\"Child 1\"},{\"name\":\"Child 2\"}]", body);
    }

    [Fact]
    public async Task AddAssociate_EmitsRefPostWithODataId()
    {
        var accountId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        var body = await CaptureBatchBodyAsync(b => b.AddAssociate("accounts", accountId, "contact_customer_accounts", "contacts", contactId));

        Assert.Contains($"POST {BaseUrl}/accounts({accountId})/contact_customer_accounts/$ref HTTP/1.1\r\n", body);
        Assert.Contains($"{{\"@odata.id\":\"{BaseUrl}/contacts({contactId})\"}}", body);
    }

    [Fact]
    public async Task ContinueOnError_WithFalseValue_TurnsItOff()
    {
        var request = await CaptureBatchRequestAsync(b => b.AddCreate(new TestAccount { Name = "x" }).ContinueOnError().ContinueOnError(false));

        Assert.Null(request.Header("Prefer"));
    }

    [Fact]
    public async Task ContinueOnError_WithDefaultValue_EnablesIt()
    {
        var request = await CaptureBatchRequestAsync(b => b.AddCreate(new TestAccount { Name = "x" }).ContinueOnError());

        Assert.Equal("odata.continue-on-error", request.Header("Prefer"));
    }

    [Fact]
    public async Task ExecuteAsync_WithSingleCreate_SendsBatchRequest()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddResponse(
            req => req.Method == HttpMethod.Post &&
                   req.RequestUri!.ToString().Contains("/$batch") &&
                   req.Content is MultipartContent,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("--batch_response\r\nContent-Type: application/http\r\n\r\nHTTP/1.1 201 Created\r\n\r\n{}", Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        var account = new TestAccount { Name = "Test Account", Revenue = 100000m };
        batch.AddCreate(account);

        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
        Assert.True(response.AllSucceeded);
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleOperations_EmitsOneChangesetWithSequentialContentIds()
    {
        var accountId = Guid.NewGuid();

        var body = await CaptureBatchBodyAsync(b => b
            .AddCreate(new TestAccount { Name = "Account 1" })
            .AddCreate(new TestAccount { Name = "Account 2" })
            .AddUpdate(accountId, new TestAccount { Name = "Updated" }));

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(body, "Content-Type: multipart/mixed; boundary=\"?changeset_"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(body, $"POST {BaseUrl}/testaccounts HTTP/1.1").Count);
        Assert.Contains($"PATCH {BaseUrl}/testaccounts({accountId}) HTTP/1.1", body);
        Assert.True(
            body.IndexOf("Content-ID: 1\r\n") < body.IndexOf("Content-ID: 2\r\n")
            && body.IndexOf("Content-ID: 2\r\n") < body.IndexOf("Content-ID: 3\r\n"),
            "Content-IDs 1-3 should be emitted in order");
    }

    [Fact]
    public async Task ExecuteAsync_WithContinueOnError_AddsPreferHeader()
    {
        var request = await CaptureBatchRequestAsync(b => b.AddCreate(new TestAccount { Name = "Test Account" }).ContinueOnError(true));

        Assert.Equal("odata.continue-on-error", request.Header("Prefer"));
    }

    [Fact]
    public async Task ExecuteAsync_WithoutContinueOnError_DoesNotAddPreferHeader()
    {
        var request = await CaptureBatchRequestAsync(b => b.AddCreate(new TestAccount { Name = "Test Account" }));

        Assert.Null(request.Header("Prefer"));
    }

    [Fact]
    public async Task ExecuteAsync_UsesMultipartMixedContentType()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddResponse(
            req => req.Content is MultipartContent multipart &&
                   multipart.Headers.ContentType!.MediaType == "multipart/mixed",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("--batch_response\r\n", Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        var account = new TestAccount { Name = "Test Account" };
        batch.AddCreate(account);

        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ExecuteAsync_IncludesBatchBoundary()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddResponse(
            req =>
            {
                if (req.Content is MultipartContent multipart)
                {
                    var boundary = multipart.Headers.ContentType!.Parameters
                        .FirstOrDefault(p => p.Name == "boundary")?.Value;
                    return boundary != null && boundary.StartsWith("\"batch_") && boundary.EndsWith("\"");
                }
                return false;
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("--batch_response\r\n", Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        var account = new TestAccount { Name = "Test Account" };
        batch.AddCreate(account);

        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ExecuteAsync_IncludesChangesetBoundary()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddResponse(
            req =>
            {
                if (req.Content is MultipartContent batchContent)
                {
                    // The changeset should be the first (and only) part of the batch
                    var changesetPart = batchContent.FirstOrDefault();
                    if (changesetPart is MultipartContent changesetContent)
                    {
                        var boundary = changesetContent.Headers.ContentType!.Parameters
                            .FirstOrDefault(p => p.Name == "boundary")?.Value;
                        return boundary != null && boundary.StartsWith("\"changeset_") && boundary.EndsWith("\"");
                    }
                }
                return false;
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("--batch_response\r\n", Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        var account = new TestAccount { Name = "Test Account" };
        batch.AddCreate(account);

        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ExecuteAsync_WithAuthenticationToken_IncludesAuthorizationHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddResponse(
            req => req.Headers.Authorization != null &&
                   req.Headers.Authorization.Scheme == "Bearer" &&
                   req.Headers.Authorization.Parameter == "test-token",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("--batch_response\r\n", Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        var account = new TestAccount { Name = "Test Account" };
        batch.AddCreate(account);

        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ExecuteAsync_SendsRequestToBatchEndpoint()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddResponse(
            req => req.RequestUri!.ToString() == "https://test.crm.dynamics.com/api/data/v9.2/$batch",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("--batch_response\r\n", Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        var account = new TestAccount { Name = "Test Account" };
        batch.AddCreate(account);

        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyBatch_SendsNoRequest()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var response = await client.Batch().ExecuteAsync();

        Assert.Empty(handler.Requests);
        Assert.Empty(response.Results);
        Assert.True(response.AllSucceeded);
    }

    [Fact]
    public async Task AddCreate_WithNullProperties_OmitsThem()
    {
        var body = await CaptureBatchBodyAsync(b => b.AddCreate(new TestAccount { Name = "Test Account", AccountNumber = null }));

        Assert.Contains("\"name\":\"Test Account\"", body);
        Assert.DoesNotContain("accountnumber", body);
    }

    [Fact]
    public async Task AddUpdate_WithPartialEntity_SendsOnlyItsProperties()
    {
        var accountId = Guid.NewGuid();

        // Anonymous types cannot have [EntitySet], so the entity set name must be provided explicitly
        var body = await CaptureBatchBodyAsync(b => b.AddUpdate(accountId, new { name = "Updated Name Only" }, "accounts"));

        Assert.Contains($"PATCH {BaseUrl}/accounts({accountId}) HTTP/1.1\r\n", body);
        Assert.Contains("\r\n\r\n{\"name\":\"Updated Name Only\"}", body);
    }

    [Fact]
    public async Task AddDeepInsert_WithNoChildren_CreatesSimpleInsert()
    {
        var body = await CaptureBatchBodyAsync(b => b.AddDeepInsert(new TestAccount { Name = "Parent Account" }, "contact_customer_accounts"));

        Assert.Contains($"POST {BaseUrl}/testaccounts HTTP/1.1\r\n", body);
        Assert.DoesNotContain("contact_customer_accounts", body);
    }

    [Fact]
    public async Task AddDeepInsert_WithChildArray_CombinesIntoSingleRequest()
    {
        var children = new[]
        {
            new { name = "Child 1" },
            new { name = "Child 2" },
            new { name = "Child 3" }
        };

        var body = await CaptureBatchBodyAsync(b => b.AddDeepInsert(new TestAccount { Name = "Parent Account" }, "contact_customer_accounts", children));

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(body, " HTTP/1.1\r\n"));
        Assert.Contains("\"contact_customer_accounts\":[{\"name\":\"Child 1\"},{\"name\":\"Child 2\"},{\"name\":\"Child 3\"}]", body);
    }

    [Fact]
    public async Task ExecuteAsync_WithHttpRequestException_PropagatesError()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddResponse(
            req => true,
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":{\"code\":\"0x80040217\",\"message\":\"Test error\"}}", Encoding.UTF8, "application/json")
            }
        );

        var batch = client.Batch();
        var account = new TestAccount { Name = "Test Account" };
        batch.AddCreate(account);

        await Assert.ThrowsAsync<DataverseException>(() => batch.ExecuteAsync());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsBatchResultWithParsedOperations()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var expectedContent = "--batch_response\r\nContent-Type: application/http\r\n\r\nHTTP/1.1 201 Created\r\nContent-Type: application/json\r\n\r\n{\"accountid\":\"00000000-0000-0000-0000-000000000001\"}\r\n--batch_response--";
        handler.AddResponse(
            req => true,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(expectedContent, Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        var account = new TestAccount { Name = "Test Account" };
        batch.AddCreate(account);

        var result = await batch.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Single(result.Results);
        Assert.True(result.AllSucceeded);
        Assert.Equal(HttpStatusCode.Created, result.Results[0].StatusCode);
    }

    [Fact]
    public void Client_Batch_CreatesNewBatchBuilder()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var batch1 = client.Batch();
        var batch2 = client.Batch();

        Assert.NotNull(batch1);
        Assert.NotNull(batch2);
        Assert.NotSame(batch1, batch2);
    }

    // ───────────────────────── Multi-changeset, GET-in-batch, and delete ─────────────────────────

    [Fact]
    public async Task AddDelete_Generic_EmitsDeleteToRecord()
    {
        var id = Guid.NewGuid();

        var body = await CaptureBatchBodyAsync(b => b.AddDelete<TestAccount>(id));

        Assert.Contains($"DELETE {BaseUrl}/testaccounts({id}) HTTP/1.1\r\n", body);
        Assert.DoesNotContain("Content-Type: application/json", body);
    }

    [Fact]
    public async Task AddDelete_WithExplicitEntitySet_UsesProvidedName()
    {
        var id = Guid.NewGuid();

        var body = await CaptureBatchBodyAsync(b => b.AddDelete("accounts", id));

        Assert.Contains($"DELETE {BaseUrl}/accounts({id}) HTTP/1.1\r\n", body);
    }

    [Fact]
    public void AddGet_WithNullOrWhitespace_Throws()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var batch = client.Batch();

        Assert.ThrowsAny<ArgumentException>(() => batch.AddGet(" "));
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleChangesets_EmitsSeparateChangesetParts()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddResponse(
            req =>
            {
                if (req.Content is not MultipartContent batchContent)
                    return false;

                // Two writes split across two changesets => two nested multipart parts.
                var changesetParts = batchContent.OfType<MultipartContent>().ToList();
                return changesetParts.Count == 2 &&
                       changesetParts.All(c =>
                           (c.Headers.ContentType!.Parameters.FirstOrDefault(p => p.Name == "boundary")?.Value ?? "")
                               .StartsWith("\"changeset_"));
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("--batch_response\r\n", Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        batch.AddCreate(new TestAccount { Name = "Account 1" })
             .NewChangeset()
             .AddCreate(new TestAccount { Name = "Account 2" });

        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ExecuteAsync_WithStandaloneGet_EmitsTopLevelHttpPart()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddResponse(
            req =>
            {
                if (req.Content is not MultipartContent batchContent)
                    return false;

                // A standalone GET is emitted as a direct application/http part at the batch top level
                // (not wrapped in a nested multipart changeset).
                var hasTopLevelHttpPart = batchContent.Any(p =>
                    p is not MultipartContent &&
                    p.Headers.ContentType?.MediaType == "application/http");
                var hasNoChangeset = !batchContent.OfType<MultipartContent>().Any();
                return hasTopLevelHttpPart && hasNoChangeset;
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("--batch_response\r\n", Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        batch.AddRetrieve<TestAccount>(Guid.NewGuid(), select: "name");

        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ExecuteAsync_MixedWritesAndGet_EmitsChangesetAndTopLevelGet()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddResponse(
            req =>
            {
                if (req.Content is not MultipartContent batchContent)
                    return false;

                var changesetCount = batchContent.OfType<MultipartContent>().Count();
                var topLevelGetCount = batchContent.Count(p =>
                    p is not MultipartContent &&
                    p.Headers.ContentType?.MediaType == "application/http");

                // One changeset (the two writes) + one standalone GET at the top level.
                return changesetCount == 1 && topLevelGetCount == 1;
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("--batch_response\r\n", Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        batch.AddCreate(new TestAccount { Name = "Account 1" })
             .AddUpdate(Guid.NewGuid(), new TestAccount { Name = "Account 2" })
             .AddRetrieve<TestAccount>(Guid.NewGuid());

        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ExecuteAsync_GetPart_DoesNotDeclareJsonContentType()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string capturedBody = string.Empty;
        handler.AddResponse(
            req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("--batch_response\r\n", Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        batch.AddGet("accounts?$select=name&$top=2");

        await batch.ExecuteAsync();

        Assert.Contains("GET", capturedBody);
        Assert.Contains("accounts?$select=name&$top=2", capturedBody);
        // The GET part must not declare a request body content type.
        Assert.DoesNotContain("Content-Type: application/json", capturedBody);
    }

    [Fact]
    public async Task ExecuteAsync_NewChangesetWithNoWrites_DoesNotEmitEmptyChangeset()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddResponse(
            req =>
            {
                if (req.Content is not MultipartContent batchContent)
                    return false;

                // Only one changeset should be emitted even though NewChangeset() was called twice.
                return batchContent.OfType<MultipartContent>().Count() == 1;
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("--batch_response\r\n", Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        batch.NewChangeset()
             .AddCreate(new TestAccount { Name = "Account 1" })
             .NewChangeset(); // trailing empty changeset must be ignored

        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ExecuteAsync_OnlyEmptyChangesets_ReturnsEmptySuccessResult()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var batch = client.Batch();
        batch.NewChangeset().NewChangeset();

        var response = await batch.ExecuteAsync();

        Assert.NotNull(response);
        Assert.True(response.AllSucceeded);
        Assert.Empty(response.Results);
    }

    [Fact]
    public async Task ExecuteAsync_MixedResponse_CorrelatesAllOperations()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        // Outer batch response containing a changeset (one create -> 201) and a top-level GET (-> 200).
        var responseBody =
            "--batchresponse_1\r\n" +
            "Content-Type: multipart/mixed; boundary=changesetresponse_1\r\n" +
            "\r\n" +
            "--changesetresponse_1\r\n" +
            "Content-Type: application/http\r\n" +
            "Content-ID: 1\r\n" +
            "\r\n" +
            "HTTP/1.1 201 Created\r\n" +
            "Content-Type: application/json\r\n" +
            "\r\n" +
            "{\"accountid\":\"00000000-0000-0000-0000-000000000001\"}\r\n" +
            "--changesetresponse_1--\r\n" +
            "--batchresponse_1\r\n" +
            "Content-Type: application/http\r\n" +
            "Content-ID: 2\r\n" +
            "\r\n" +
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json\r\n" +
            "\r\n" +
            "{\"name\":\"Existing\"}\r\n" +
            "--batchresponse_1--";

        handler.AddResponse(
            req => true,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        batch.AddCreate(new TestAccount { Name = "New" })
             .AddRetrieve<TestAccount>(Guid.NewGuid());

        var result = await batch.ExecuteAsync();

        Assert.Equal(2, result.Results.Count);
        Assert.True(result.AllSucceeded);
        Assert.Equal(HttpStatusCode.Created, result.Results[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, result.Results[1].StatusCode);
    }

    // ───────────────────────── CRLF line endings, created IDs, and error aggregation ─────────────────────────

    [Fact]
    public async Task ExecuteAsync_BatchPart_UsesCrlfLineEndings()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? capturedPart = null;
        handler.AddResponse(
            req =>
            {
                if (req.Content is MultipartContent batchContent &&
                    batchContent.FirstOrDefault() is MultipartContent changesetContent &&
                    changesetContent.FirstOrDefault() is { } part)
                {
                    capturedPart = part.ReadAsStringAsync().GetAwaiter().GetResult();
                }
                return true;
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("--batch_response--", Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        batch.AddCreate(new TestAccount { Name = "Test Account" });

        await batch.ExecuteAsync();

        Assert.NotNull(capturedPart);
        // Every line break in the part must be CRLF, never a bare LF.
        Assert.Contains("\r\n", capturedPart);
        Assert.DoesNotContain(capturedPart!.Replace("\r\n", string.Empty), "\n");
    }

    [Fact]
    public async Task ExecuteAsync_CreateOperation_ParsesEntityIdFromODataHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var newId = Guid.NewGuid();
        var responseBody =
            "--batchresponse_1\r\n" +
            "Content-Type: multipart/mixed; boundary=changesetresponse_1\r\n" +
            "\r\n" +
            "--changesetresponse_1\r\n" +
            "Content-Type: application/http\r\n" +
            "Content-ID: 1\r\n" +
            "\r\n" +
            "HTTP/1.1 204 No Content\r\n" +
            $"OData-EntityId: https://test.crm.dynamics.com/api/data/v9.2/accounts({newId})\r\n" +
            "\r\n" +
            "--changesetresponse_1--\r\n" +
            "--batchresponse_1--";

        handler.AddResponse(
            req => true,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        batch.AddCreate(new TestAccount { Name = "New" });

        var result = await batch.ExecuteAsync();

        Assert.Single(result.Results);
        Assert.Equal(newId, result.Results[0].EntityId);
        Assert.Contains(newId.ToString(), result.Results[0].Location);
    }

    [Fact]
    public async Task ExecuteAsync_CreateOperation_ParsesEntityIdFromLocationHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var newId = Guid.NewGuid();
        var responseBody =
            "--batchresponse_1\r\n" +
            "Content-Type: multipart/mixed; boundary=changesetresponse_1\r\n" +
            "\r\n" +
            "--changesetresponse_1\r\n" +
            "Content-Type: application/http\r\n" +
            "Content-ID: 1\r\n" +
            "\r\n" +
            "HTTP/1.1 201 Created\r\n" +
            $"Location: https://test.crm.dynamics.com/api/data/v9.2/accounts({newId})\r\n" +
            "\r\n" +
            "--changesetresponse_1--\r\n" +
            "--batchresponse_1--";

        handler.AddResponse(
            req => true,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        batch.AddCreate(new TestAccount { Name = "New" });

        var result = await batch.ExecuteAsync();

        Assert.Single(result.Results);
        Assert.Equal(newId, result.Results[0].EntityId);
    }

    [Fact]
    public async Task ExecuteAsync_RetrieveOperation_HasNoEntityId()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseBody =
            "--batchresponse_1\r\n" +
            "Content-Type: application/http\r\n" +
            "Content-ID: 1\r\n" +
            "\r\n" +
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json\r\n" +
            "\r\n" +
            "{\"name\":\"Existing\"}\r\n" +
            "--batchresponse_1--";

        handler.AddResponse(
            req => true,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        batch.AddGet("accounts?$top=1");

        var result = await batch.ExecuteAsync();

        Assert.Single(result.Results);
        Assert.Null(result.Results[0].EntityId);
    }

    [Fact]
    public void ThrowIfAnyFailed_WithNoFailures_DoesNotThrow()
    {
        var result = new BatchResult(
        [
            new BatchOperationResult(1, HttpStatusCode.OK, "{}"),
            new BatchOperationResult(2, HttpStatusCode.Created, "{}")
        ]);

        result.ThrowIfAnyFailed();
    }

    [Fact]
    public void ThrowIfAnyFailed_WithSingleFailure_ThrowsDataverseException()
    {
        var result = new BatchResult(
        [
            new BatchOperationResult(1, HttpStatusCode.OK, "{}"),
            new BatchOperationResult(2, HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"0x1\",\"message\":\"bad\"}}")
        ]);

        var ex = Assert.Throws<DataverseException>(result.ThrowIfAnyFailed);
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public void ThrowIfAnyFailed_WithMultipleFailures_ThrowsAggregatedException()
    {
        var result = new BatchResult(
        [
            new BatchOperationResult(1, HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"0x1\",\"message\":\"bad one\"}}"),
            new BatchOperationResult(2, HttpStatusCode.OK, "{}"),
            new BatchOperationResult(3, HttpStatusCode.NotFound, "{\"error\":{\"code\":\"0x2\",\"message\":\"missing\"}}")
        ]);

        var ex = Assert.Throws<BatchOperationException>(result.ThrowIfAnyFailed);

        Assert.Equal(2, ex.FailedOperations.Count);
        Assert.Equal(2, ex.InnerExceptions.Count);
        Assert.All(ex.InnerExceptions, e => Assert.IsType<DataverseException>(e));
        Assert.Contains("Content-ID 1", ex.Message);
        Assert.Contains("Content-ID 3", ex.Message);
        Assert.Equal(1, ex.FailedOperations[0].ContentId);
        Assert.Equal(3, ex.FailedOperations[1].ContentId);
    }

    // ───────────────────────── Transactional changeset failure correlation (#3) ─────────────────────────

    [Fact]
    public async Task ExecuteAsync_ChangesetFailure_MarksSiblingsAsRolledBack()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        // A transactional changeset with three writes fails on the second operation. Dataverse
        // returns a single error part tagged with the failing Content-ID; the other operations are
        // rolled back and not echoed individually.
        var responseBody =
            "--batchresponse_1\r\n" +
            "Content-Type: application/http\r\n" +
            "Content-ID: 2\r\n" +
            "\r\n" +
            "HTTP/1.1 400 Bad Request\r\n" +
            "Content-Type: application/json\r\n" +
            "\r\n" +
            "{\"error\":{\"code\":\"0x80040265\",\"message\":\"validation failed\"}}\r\n" +
            "--batchresponse_1--";

        handler.AddResponse(
            req => true,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        batch.AddCreate(new TestAccount { Name = "First" })
             .AddCreate(new TestAccount { Name = "Second" })
             .AddCreate(new TestAccount { Name = "Third" });

        var result = await batch.ExecuteAsync();

        // One result per queued operation, in order.
        Assert.Equal(3, result.Results.Count);

        // The failing operation carries the actual error.
        Assert.Equal(2, result.Results[1].ContentId);
        Assert.Equal(HttpStatusCode.BadRequest, result.Results[1].StatusCode);
        Assert.False(result.Results[1].WasRolledBack);

        // The siblings are reported as rolled back (HTTP 424 Failed Dependency).
        Assert.Equal(HttpStatusCode.FailedDependency, result.Results[0].StatusCode);
        Assert.True(result.Results[0].WasRolledBack);
        Assert.Equal(HttpStatusCode.FailedDependency, result.Results[2].StatusCode);
        Assert.True(result.Results[2].WasRolledBack);

        Assert.False(result.AllSucceeded);
    }

    [Fact]
    public async Task ThrowIfAnyFailed_ReportsTheFailedPartsReqIdAsActivityId()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        // As Dataverse sends it (probed 2026-11-18): each part carries its own REQ_ID header.
        var responseBody =
            "--batchresponse_1\r\n" +
            "Content-Type: application/http\r\n" +
            "Content-ID: 2\r\n" +
            "\r\n" +
            "HTTP/1.1 400 Bad Request\r\n" +
            "REQ_ID: 7d1a2f8e-0000-4000-8000-000000000002\r\n" +
            "Content-Type: application/json\r\n" +
            "\r\n" +
            "{\"error\":{\"code\":\"0x80040265\",\"message\":\"validation failed\"}}\r\n" +
            "--batchresponse_1--";

        handler.AddResponse(req => true, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "multipart/mixed")
        });

        var result = await client.Batch()
            .AddCreate(new TestAccount { Name = "First" })
            .AddCreate(new TestAccount { Name = "Second" })
            .ExecuteAsync();

        var ex = Assert.Throws<BatchOperationException>(result.ThrowIfAnyFailed);
        var failed = ex.InnerExceptions.Cast<DataverseException>().Single(e => e.StatusCode == HttpStatusCode.BadRequest);
        Assert.Equal("7d1a2f8e-0000-4000-8000-000000000002", failed.ActivityId);
        Assert.Equal("0x80040265", failed.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_ErrorStatusWithMultipartBody_ReturnsFailedResults()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        // Without continue-on-error, Dataverse returns the batch itself with an error status, but the
        // body is still a multipart response carrying the failed operation inside its changeset.
        var responseBody =
            "--batchresponse_1\r\n" +
            "Content-Type: multipart/mixed; boundary=changesetresponse_1\r\n" +
            "\r\n" +
            "--changesetresponse_1\r\n" +
            "Content-Type: application/http\r\n" +
            "Content-Transfer-Encoding: binary\r\n" +
            "Content-ID: 1\r\n" +
            "\r\n" +
            "HTTP/1.1 500 Internal Server Error\r\n" +
            "Content-Type: application/json; odata.metadata=minimal\r\n" +
            "OData-Version: 4.0\r\n" +
            "\r\n" +
            "{\"error\":{\"code\":\"0x80040216\",\"message\":\"An unexpected error occurred.\"}}\r\n" +
            "--changesetresponse_1--\r\n" +
            "--batchresponse_1--\r\n";

        handler.AddResponse(
            req => true,
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "multipart/mixed")
            }
        );

        var result = await client.Batch()
            .AddUpdate(Guid.NewGuid(), new TestAccount { Name = "Missing" })
            .ExecuteAsync();

        var operation = Assert.Single(result.Results);
        Assert.False(operation.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, operation.StatusCode);
        Assert.Contains("0x80040216", operation.Body);
    }

    [Fact]
    public async Task ExecuteAsync_ChangesetFailure_DoesNotAffectSeparateChangeset()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        // First changeset (Content-IDs 1,2) fails on op 1; second changeset (Content-ID 3) succeeds.
        var responseBody =
            "--batchresponse_1\r\n" +
            "Content-Type: multipart/mixed; boundary=changesetresponse_1\r\n" +
            "\r\n" +
            "--changesetresponse_1\r\n" +
            "Content-Type: application/http\r\n" +
            "Content-ID: 1\r\n" +
            "\r\n" +
            "HTTP/1.1 400 Bad Request\r\n" +
            "Content-Type: application/json\r\n" +
            "\r\n" +
            "{\"error\":{\"code\":\"0x1\",\"message\":\"bad\"}}\r\n" +
            "--changesetresponse_1--\r\n" +
            "--batchresponse_1\r\n" +
            "Content-Type: multipart/mixed; boundary=changesetresponse_2\r\n" +
            "\r\n" +
            "--changesetresponse_2\r\n" +
            "Content-Type: application/http\r\n" +
            "Content-ID: 3\r\n" +
            "\r\n" +
            "HTTP/1.1 204 No Content\r\n" +
            "\r\n" +
            "--changesetresponse_2--\r\n" +
            "--batchresponse_1--";

        handler.AddResponse(
            req => true,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        batch.AddCreate(new TestAccount { Name = "A1" })
             .AddCreate(new TestAccount { Name = "A2" })
             .NewChangeset()
             .AddCreate(new TestAccount { Name = "B1" });

        var result = await batch.ExecuteAsync();

        Assert.Equal(3, result.Results.Count);

        // First changeset: op 1 failed, op 2 rolled back.
        Assert.Equal(HttpStatusCode.BadRequest, result.Results[0].StatusCode);
        Assert.False(result.Results[0].WasRolledBack);
        Assert.Equal(HttpStatusCode.FailedDependency, result.Results[1].StatusCode);
        Assert.True(result.Results[1].WasRolledBack);

        // Second, independent changeset succeeded.
        Assert.Equal(3, result.Results[2].ContentId);
        Assert.Equal(HttpStatusCode.NoContent, result.Results[2].StatusCode);
        Assert.False(result.Results[2].WasRolledBack);
    }

    // ───────────────────────── Intra-changeset content-id references (#7) ─────────────────────────

    [Fact]
    public void AddCreate_WithReferenceOut_ReturnsSequentialContentIds()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var batch = client.Batch();
        batch.AddCreate(new TestAccount { Name = "Parent" }, out var parentRef)
             .AddCreate(new TestAccount { Name = "Child" }, out var childRef);

        Assert.Equal(1, parentRef.ContentId);
        Assert.Equal("$1", parentRef.Token);
        Assert.Equal(2, childRef.ContentId);
        Assert.Equal("$2", childRef.Token);
    }

    [Fact]
    public async Task ExecuteAsync_WithLookupBinding_EmitsODataBindToContentId()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? childPart = null;
        handler.AddResponse(
            req =>
            {
                if (req.Content is MultipartContent batchContent &&
                    batchContent.FirstOrDefault() is MultipartContent changesetContent)
                {
                    var parts = changesetContent.ToList();
                    if (parts.Count > 1)
                    {
                        childPart = parts[1].ReadAsStringAsync().GetAwaiter().GetResult();
                    }
                }
                return true;
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("--batch_response--", Encoding.UTF8, "multipart/mixed")
            }
        );

        var batch = client.Batch();
        batch.AddCreate(new TestAccount { Name = "Parent" }, out var parentRef)
             .AddCreate(
                 new TestAccount { Name = "Child" },
                 new Dictionary<string, BatchReference> { ["parentaccountid"] = parentRef });

        await batch.ExecuteAsync();

        Assert.NotNull(childPart);
        // The child create must bind its lookup to the parent's Content-ID reference.
        Assert.Contains("\"parentaccountid@odata.bind\":\"$1\"", childPart);
    }

    private static async Task<string> CaptureBatchBodyAsync(Action<BatchBuilder> configure)
        => (await CaptureBatchRequestAsync(configure)).Body!;

    private static async Task<CapturedRequest> CaptureBatchRequestAsync(Action<BatchBuilder> configure, Guid? batchId = null)
    {
        var handler = new TestHttpMessageHandler();
        var client = new DataverseClient("https://test.crm.dynamics.com/api/data/v9.2", () => Task.FromResult("t"), new HttpClient(handler));

        handler.AddResponse(
            _ => true,
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("--batch_response\r\n", Encoding.UTF8, "multipart/mixed") });

        var batch = client.Batch(batchId);
        configure(batch);
        await batch.ExecuteAsync();
        return Assert.Single(handler.Requests);
    }

    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";

    [Fact]
    public async Task AddUpdate_SendsIfMatchStar_SoTheUpdateNeverCreates()
    {
        // A PATCH without If-Match is an upsert in Dataverse.
        var body = await CaptureBatchBodyAsync(b => b.AddUpdate(Guid.NewGuid(), new TestAccount { Name = "x" }));

        Assert.Contains("PATCH ", body);
        Assert.Contains("If-Match: *\r\n", body);
    }

    [Fact]
    public async Task AddUpsert_SendsPatchWithoutIfMatch()
    {
        var body = await CaptureBatchBodyAsync(b => b.AddUpsert(Guid.NewGuid(), new TestAccount { Name = "x" }));

        Assert.Contains("PATCH ", body);
        Assert.DoesNotContain("If-Match", body);
    }

    [EntitySet("testaccounts")]
    private class ReadOnlyRulesAccount
    {
        [DataverseReadOnly] // Create only
        [DataverseColumn("accountid")]
        public Guid AccountId { get; set; }

        [DataverseColumn("name")]
        public string? Name { get; set; }

        [DataverseReadOnly(DataverseOperation.Update)]
        [DataverseColumn("accountnumber")]
        public string? AccountNumber { get; set; }
    }

    [Theory]
    [InlineData("update")]
    [InlineData("upsert")]
    public async Task AddUpdateAndUpsert_ExcludePropertiesReadOnlyForEitherOperation(string operation)
    {
        // A create-only marker (e.g. on the primary key) must not send its default value over an existing record.
        var entity = new ReadOnlyRulesAccount { Name = "x", AccountNumber = "A-1" };

        var body = await CaptureBatchBodyAsync(b => _ = operation == "update"
            ? b.AddUpdate(Guid.NewGuid(), entity)
            : b.AddUpsert(Guid.NewGuid(), entity));

        Assert.Contains("{\"name\":\"x\"}", body);
        Assert.DoesNotContain("accountid", body);
        Assert.DoesNotContain("accountnumber", body);
    }

    [Fact]
    public async Task AddCreate_ExcludesOnlyCreateReadOnlyProperties()
    {
        var body = await CaptureBatchBodyAsync(b => b.AddCreate(new ReadOnlyRulesAccount { Name = "x", AccountNumber = "A-1" }));

        Assert.Contains("{\"name\":\"x\",\"accountnumber\":\"A-1\"}", body);
    }

    private class ColumnChild
    {
        [DataverseReadOnly]
        [DataverseColumn("contactid")]
        public Guid ContactId { get; set; }

        [DataverseColumn("firstname")]
        public string? FirstName { get; set; }
    }

    [Fact]
    public async Task AddDeepInsert_SerializesChildrenWithLibraryNamingAndReadOnlyRules()
    {
        var body = await CaptureBatchBodyAsync(b =>
            b.AddDeepInsert(new TestAccount { Name = "Parent" }, "contact_customer_accounts", new ColumnChild { FirstName = "Child" }));

        Assert.Contains("\"contact_customer_accounts\":[{\"firstname\":\"Child\"}]", body);
    }

    [Fact]
    public async Task AddDeepInsert_WithChildList_SendsChildrenAsFlatArray()
    {
        // A List<T> is not an object[], so params wraps it as a single child unless it is flattened.
        var children = new List<ColumnChild> { new() { FirstName = "A" }, new() { FirstName = "B" } };

        var body = await CaptureBatchBodyAsync(b =>
            b.AddDeepInsert(new TestAccount { Name = "Parent" }, "contact_customer_accounts", children));

        Assert.Contains("\"contact_customer_accounts\":[{\"firstname\":\"A\"},{\"firstname\":\"B\"}]", body);
    }

    [Fact]
    public async Task AddRetrieve_EscapesSelect()
    {
        var body = await CaptureBatchBodyAsync(b => b.AddRetrieve<TestAccount>(Guid.NewGuid(), null, "name", "revenue"));

        Assert.Contains("?$select=name%2Crevenue HTTP/1.1", body);
    }
}
