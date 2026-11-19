using System.Net;
using System.Text.Json;
using Dataverse.Fluent.Builders;
using Dataverse.Fluent.Models;
using Xunit;

namespace Dataverse.Fluent.Tests.Builders;

public class DeltaBuilderTests
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
    public async Task ExecuteAsync_InitialQuery_ReturnsChangedEntities()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var jsonResponse = @"{
            ""value"": [
                {
                    ""accountid"": ""11111111-1111-1111-1111-111111111111"",
                    ""name"": ""Test Account 1"",
                    ""revenue"": 1000000
                },
                {
                    ""accountid"": ""22222222-2222-2222-2222-222222222222"",
                    ""name"": ""Test Account 2"",
                    ""revenue"": 2000000
                }
            ],
            ""@odata.deltaLink"": ""https://test.crm.dynamics.com/api/data/v9.2/accounts?$deltatoken=abc123""
        }";

        handler.AddResponse(
            req => req.Method == HttpMethod.Get && req.DecodedUrl().Contains("testaccounts"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
            }
        );

        // Act
        var results = await client.Delta<TestAccount>()
            .ExecuteAsync();

        // Assert
        Assert.NotNull(results);
        Assert.Equal(2, results.Value.Count);
        Assert.Equal("Test Account 1", results.Value[0].Name);
        Assert.Equal("Test Account 2", results.Value[1].Name);
        Assert.NotNull(results.DeltaToken);
        Assert.Empty(results.Deleted);
    }

    [Fact]
    public async Task StreamAsync_SendsPageSizeAsMaxPageSizePreference_OnEveryPage()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var prefers = new List<string>();
        bool Capture(HttpRequestMessage req)
        {
            prefers.Add(string.Join(",", req.Headers.GetValues("Prefer")));
            return true;
        }

        handler.AddResponse(Capture, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(@"{ ""value"": [], ""@odata.nextLink"": ""https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$skiptoken=page2"" }")
        });
        handler.AddResponse(Capture, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(@"{ ""value"": [], ""@odata.deltaLink"": ""https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$deltatoken=abc"" }")
        });

        await foreach (var _ in client.Delta<TestAccount>().StreamAsync(pageSize: 250)) { }

        Assert.Equal(2, prefers.Count);
        Assert.All(prefers, p =>
        {
            Assert.Contains("odata.track-changes", p);
            Assert.Contains("odata.maxpagesize=250", p);
        });
    }

    [Fact]
    public async Task ExecuteAsync_WithDeltaToken_ReturnsChangesAndDeletions()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        // Build JSON manually to include @ symbols
        var jsonResponse = @"{
            ""value"": [
                {
                    ""accountid"": ""33333333-3333-3333-3333-333333333333"",
                    ""name"": ""Updated Account"",
                    ""revenue"": 3000000
                },
                {
                    ""@removed"": {
                        ""reason"": ""deleted""
                    },
                    ""@id"": ""accounts(44444444-4444-4444-4444-444444444444)"",
                    ""name"": null
                }
            ],
            ""@odata.deltaLink"": ""https://test.crm.dynamics.com/api/data/v9.2/accounts?$deltatoken=xyz789""
        }";

        handler.AddResponse(
            req => req.Method == HttpMethod.Get,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
            }
        );

        // Act
        var results = await client.Delta<TestAccount>()
            .Since("https://test.crm.dynamics.com/api/data/v9.2/accounts?$deltatoken=abc123")
            .ExecuteAsync();

        // Assert
        Assert.NotNull(results);
        Assert.Single(results.Value);
        Assert.Equal("Updated Account", results.Value[0].Name);
        Assert.Single(results.Deleted);
        Assert.Equal("deleted", results.Deleted[0].Removed?.Reason);
        Assert.NotNull(results.Deleted[0].GetEntityId());
        Assert.Equal(Guid.Parse("44444444-4444-4444-4444-444444444444"), results.Deleted[0].GetEntityId());
    }

    // Runs a delta query against an empty response and returns the request that was sent.
    private async Task<CapturedRequest> SendDeltaAsync(Func<DeltaBuilder<TestAccount>, DeltaBuilder<TestAccount>> configure)
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddJsonResponse(_ => true, new Dictionary<string, object> { ["value"] = Array.Empty<object>(), ["@odata.deltaLink"] = "test" });

        await configure(client.Delta<TestAccount>()).ExecuteAsync();

        return Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_WithSelect_RequestsSpecificColumns()
    {
        var request = await SendDeltaAsync(d => d.Select("name", "revenue"));

        Assert.Equal("name,revenue", request.QueryOption("$select"));
    }

    [Fact]
    public async Task ExecuteAsync_ReadsDataversesDeletedEntityShape()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        // As Dataverse returned it on 2026-11-18: a deletion is a $deletedEntity with a bare id, not "@removed".
        var jsonResponse = """
            {
              "@odata.context": "https://test.crm.dynamics.com/api/data/v9.2/$metadata#testaccounts(name)/$delta",
              "@odata.deltaLink": "https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$select=name&$deltatoken=2",
              "value": [
                { "@odata.etag": "W/\"1450899602\"", "name": "Changed", "accountid": "9ae3c50a-d3e3-f111-84ea-001dd80d92a5" },
                { "@odata.context": "https://test.crm.dynamics.com/api/data/v9.2/$metadata#testaccounts/$deletedEntity", "id": "a0e3c50a-d3e3-f111-84ea-001dd80d92a5", "reason": "deleted" }
              ]
            }
            """;
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
        });

        var results = await client.Delta<TestAccount>().Since("https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$deltatoken=1").ExecuteAsync();

        Assert.Equal("Changed", Assert.Single(results.Value).Name);
        var deleted = Assert.Single(results.Deleted);
        Assert.Equal(Guid.Parse("a0e3c50a-d3e3-f111-84ea-001dd80d92a5"), deleted.GetEntityId());
        Assert.Equal("deleted", deleted.Removed?.Reason);
    }

    [Fact]
    public async Task ExecuteAsync_WithIncludeFormattedValues_SetsPreferHeader()
    {
        var request = await SendDeltaAsync(d => d.IncludeFormattedValues());

        Assert.Equal(
            "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\",odata.track-changes",
            request.Header("Prefer"));
    }

    [Fact]
    public async Task StreamAsync_WithPagination_ReturnsAllPages()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var page1Json = @"{
            ""value"": [
                {
                    ""accountid"": ""11111111-1111-1111-1111-111111111111"",
                    ""name"": ""Page 1 Account""
                }
            ],
            ""@odata.nextLink"": ""https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$deltatoken=abc123&$skiptoken=page2"",
            ""@odata.deltaLink"": ""https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$deltatoken=final""
        }";

        var page2Json = @"{
            ""value"": [
                {
                    ""accountid"": ""22222222-2222-2222-2222-222222222222"",
                    ""name"": ""Page 2 Account""
                }
            ],
            ""@odata.deltaLink"": ""https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$deltatoken=final""
        }";

        handler.AddResponse(
            req => req.Method == HttpMethod.Get && !req.DecodedUrl().Contains("skiptoken"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(page1Json, System.Text.Encoding.UTF8, "application/json")
            }
        );

        handler.AddResponse(
            req => req.Method == HttpMethod.Get && req.DecodedUrl().Contains("skiptoken=page2"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(page2Json, System.Text.Encoding.UTF8, "application/json")
            }
        );

        // Act
        var pages = new List<DeltaResults<TestAccount>>();
        await foreach (var page in client.Delta<TestAccount>().StreamAsync(pageSize: 1))
        {
            pages.Add(page);
        }

        // Assert
        Assert.Equal(2, pages.Count);
        Assert.Equal("Page 1 Account", pages[0].Value[0].Name);
        Assert.Equal("Page 2 Account", pages[1].Value[0].Name);
    }

    [Fact]
    public void Since_WithNullToken_ThrowsArgumentException()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            client.Delta<TestAccount>().Since(null!));

        Assert.Contains("Delta token cannot be null", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_SetsTrackChangesPreference()
    {
        var request = await SendDeltaAsync(d => d);

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/data/v9.2/testaccounts", request.Uri.AbsolutePath);
        Assert.Equal("odata.track-changes", request.Header("Prefer"));
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomHeaders_SendsThem()
    {
        var request = await SendDeltaAsync(d => d.WithHeaders(new Dictionary<string, string> { ["MSCRM.SuppressDuplicateDetection"] = "true" }));

        Assert.Equal("true", request.Header("MSCRM.SuppressDuplicateDetection"));
        Assert.Equal("odata.track-changes", request.Header("Prefer"));
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomPreferHeader_MergesItWithTrackChanges()
    {
        var request = await SendDeltaAsync(d => d
            .WithHeaders(new Dictionary<string, string> { ["Prefer"] = "odata.continue-on-error" })
            .IncludeFormattedValues());

        Assert.Equal(
            "odata.continue-on-error,odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\",odata.track-changes",
            request.Header("Prefer"));
    }

    [Fact]
    public async Task WithHeaders_CalledTwice_LaterValueWins()
    {
        var request = await SendDeltaAsync(d => d
            .WithHeaders(new Dictionary<string, string> { ["x-custom"] = "first" })
            .WithHeaders(new Dictionary<string, string> { ["x-custom"] = "second" }));

        Assert.Equal("second", request.Header("x-custom"));
    }

    [Fact]
    public void EntityReference_GetEntityId_ParsesGuidFromId()
    {
        // Arrange
        var entityRef = new EntityReference
        {
            Id = "accounts(12345678-1234-1234-1234-123456789012)"
        };

        // Act
        var entityId = entityRef.GetEntityId();

        // Assert
        Assert.NotNull(entityId);
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789012"), entityId);
    }

    [Fact]
    public void EntityReference_GetEntityId_WithSlashFormat_ParsesGuid()
    {
        // Arrange
        var entityRef = new EntityReference
        {
            Id = "https://test.crm.dynamics.com/api/data/v9.2/accounts(12345678-1234-1234-1234-123456789012)"
        };

        // Act
        var entityId = entityRef.GetEntityId();

        // Assert
        Assert.NotNull(entityId);
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789012"), entityId);
    }

    [Fact]
    public void EntityReference_GetEntityId_WithNullId_ReturnsNull()
    {
        // Arrange
        var entityRef = new EntityReference { Id = null };

        // Act
        var entityId = entityRef.GetEntityId();

        // Assert
        Assert.Null(entityId);
    }

    // Test POCO
    [EntitySet("testaccounts")]
    private class TestAccount
    {
        [System.Text.Json.Serialization.JsonPropertyName("accountid")]
        public Guid AccountId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("revenue")]
        public decimal? Revenue { get; set; }
    }
}
