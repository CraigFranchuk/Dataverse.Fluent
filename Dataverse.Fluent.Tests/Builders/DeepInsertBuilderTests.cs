using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Dataverse.Fluent.Builders;
using Dataverse.Fluent.Models;
using Xunit;

namespace Dataverse.Fluent.Tests.Builders;

public class DeepInsertBuilderTests
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
        // Arrange
        var parent = new TestAccount { Name = "Parent Account" };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DeepInsertBuilder<TestAccount>(null!, parent));
    }

    [Fact]
    public void Constructor_WithNullParent_ThrowsArgumentNullException()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DeepInsertBuilder<TestAccount>(client.Transport, null!));
    }

    [Fact]
    public void WithRelated_NullCollection_ThrowsArgumentNullException()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var parent = new TestAccount { Name = "Parent Account" };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            client.DeepInsert(parent)
                .WithRelated((IEnumerable<TestContact>)null!, "contact_customer_accounts"));
    }

    [Fact]
    public void WithRelated_NullSingleEntity_ThrowsArgumentNullException()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var parent = new TestAccount { Name = "Parent Account" };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            client.DeepInsert(parent)
                .WithRelated<TestContact>((TestContact)null!, "primarycontactid"));
    }

    [Fact]
    public void WithRelated_EmptyRelationshipName_ThrowsArgumentException()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var parent = new TestAccount { Name = "Parent Account" };
        var children = new List<TestContact> { new TestContact { FirstName = "John", LastName = "Doe" } };

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            client.DeepInsert(parent)
                .WithRelated(children, ""));
    }

    [Fact]
    public async Task ReturnRepresentation_SetsPreferHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") });

        await client.DeepInsert(new TestAccount { Name = "Parent Account" }).ReturnRepresentation().ExecuteAsync();

        Assert.Equal("return=representation", Assert.Single(handler.Requests).Header("Prefer"));
    }

    [Fact]
    public async Task ReturnRepresentation_False_TurnsItOff()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.DeepInsert(new TestAccount { Name = "Parent Account" })
            .ReturnRepresentation()
            .ReturnRepresentation(false)
            .ExecuteAsync();

        Assert.Null(Assert.Single(handler.Requests).Header("Prefer"));
    }

    [Fact]
    public async Task WithPrefer_AddsCustomPreferHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.DeepInsert(new TestAccount { Name = "Parent Account" })
            .WithPrefer("odata.include-annotations=\"*\"")
            .ExecuteAsync();

        Assert.Equal("odata.include-annotations=\"*\"", Assert.Single(handler.Requests).Header("Prefer"));
    }

    [Fact]
    public async Task ExecuteAsync_WithParentOnly_SendsPostRequest()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var parent = new TestAccount { Name = "Parent Account", Revenue = 1000000 };

        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{ "accountid": "11111111-1111-1111-1111-111111111111", "name": "Parent Account", "revenue": 1000000 }""",
                System.Text.Encoding.UTF8, "application/json")
        });

        var result = (await client.DeepInsert(parent).ReturnRepresentation().ExecuteAsync()).Entity;

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/data/v9.2/testaccounts", request.Uri.AbsolutePath);
        Assert.Equal("Parent Account", request.Json.GetProperty("name").GetString());
        Assert.Equal(1000000m, request.Json.GetProperty("revenue").GetDecimal());

        Assert.NotNull(result);
        Assert.Equal("Parent Account", result.Name);
        Assert.Equal(1000000, result.Revenue);
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleRelationships_SendsAllNestedData()
    {
        var contacts = new List<TestContact> { new TestContact { FirstName = "John", LastName = "Doe" } };
        var opportunities = new List<TestOpportunity> { new TestOpportunity { Name = "Big Deal", EstimatedValue = 500000 } };

        using var payload = await CapturePayloadAsync(b => b
            .WithRelated(contacts, "contact_customer_accounts")
            .WithRelated(opportunities, "opportunity_customer_accounts"));

        Assert.Equal("John", payload.RootElement.GetProperty("contact_customer_accounts")[0].GetProperty("firstname").GetString());
        var opportunity = payload.RootElement.GetProperty("opportunity_customer_accounts")[0];
        Assert.Equal("Big Deal", opportunity.GetProperty("name").GetString());
        Assert.Equal(500000m, opportunity.GetProperty("estimatedvalue").GetDecimal());
    }

    [Fact]
    public async Task ExecuteAsync_WithoutReturnRepresentation_ReturnsNull()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.NoContent));

        var result = (await client.DeepInsert(new TestAccount { Name = "Parent Account" }).ExecuteAsync()).Entity;

        Assert.Null(result);
        Assert.Null(Assert.Single(handler.Requests).Header("Prefer"));
    }

    [Fact]
    public async Task ExecuteAsync_WithPreferHeader_IncludesInRequest()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") });

        await client.DeepInsert(new TestAccount { Name = "Parent Account" })
            .ReturnRepresentation()
            .WithPrefer("odata.include-annotations=\"*\"")
            .ExecuteAsync();

        Assert.Equal("return=representation,odata.include-annotations=\"*\"", Assert.Single(handler.Requests).Header("Prefer"));
    }

    [Fact]
    public async Task ExecuteAsync_WithDataverseError_ThrowsDataverseException()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var parent = new TestAccount { Name = "Parent Account" };

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80040217"",
                ""message"": ""Validation error""
            }
        }";

        handler.AddResponse(
            req => req.Method == HttpMethod.Post,
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json")
            }
        );

        // Act & Assert
        await Assert.ThrowsAsync<DataverseException>(() =>
            client.DeepInsert(parent).ExecuteAsync());
    }

    [Fact]
    public async Task ExecuteAsync_UsesCorrectEntitySetName()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.DeepInsert(new TestAccount { Name = "Parent Account" }).ExecuteAsync();

        Assert.Equal("/api/data/v9.2/testaccounts", Assert.Single(handler.Requests).Uri.AbsolutePath);
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomEntitySet_UsesProvidedName()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.DeepInsert(new TestAccount { Name = "Parent Account" }, "custom_accounts").ExecuteAsync();

        Assert.Equal("/api/data/v9.2/custom_accounts", Assert.Single(handler.Requests).Uri.AbsolutePath);
    }

    // Test POCOs
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

    [EntitySet("testcontacts")]
    private class TestContact
    {
        [System.Text.Json.Serialization.JsonPropertyName("contactid")]
        public Guid ContactId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("firstname")]
        public string FirstName { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("lastname")]
        public string LastName { get; set; } = string.Empty;
    }

    [EntitySet("testopportunities")]
    private class TestOpportunity
    {
        [System.Text.Json.Serialization.JsonPropertyName("opportunityid")]
        public Guid OpportunityId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("estimatedvalue")]
        public decimal? EstimatedValue { get; set; }
    }

    private class ReadOnlyChild
    {
        [DataverseReadOnly]
        [System.Text.Json.Serialization.JsonPropertyName("contactid")]
        public Guid ContactId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("firstname")]
        public string? FirstName { get; set; }
    }

    // ────────── Collections bind to the single-entity overload ──────────
    // A List<T> or T[] argument binds to WithRelated<TRelated>(TRelated, string) with TRelated = List<T>,
    // because an exact type match beats the conversion to IEnumerable<T>.

    private static async Task<JsonDocument> CapturePayloadAsync(Func<DeepInsertBuilder<TestAccount>, DeepInsertBuilder<TestAccount>> configure)
    {
        var handler = new TestHttpMessageHandler();
        var client = new DataverseClient("https://test.crm.dynamics.com/api/data/v9.2", () => Task.FromResult("t"), new HttpClient(handler));

        var body = string.Empty;
        handler.AddResponse(
            req => { body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult(); return true; },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await configure(client.DeepInsert(new TestAccount { Name = "Parent" })).ExecuteAsync();
        return JsonDocument.Parse(body);
    }

    [Fact]
    public async Task ExecuteAsync_WithRelatedList_SendsEveryChild()
    {
        var children = new List<TestContact> { new() { FirstName = "John" }, new() { FirstName = "Jane" } };

        using var payload = await CapturePayloadAsync(b => b.WithRelated(children, "contact_customer_accounts"));

        var nested = payload.RootElement.GetProperty("contact_customer_accounts");
        Assert.Equal(JsonValueKind.Array, nested.ValueKind);
        Assert.Equal(["John", "Jane"], nested.EnumerateArray().Select(c => c.GetProperty("firstname").GetString()));
    }

    [Fact]
    public async Task ExecuteAsync_WithRelatedArray_SendsEveryChild()
    {
        TestContact[] children = [new() { FirstName = "John" }, new() { FirstName = "Jane" }];

        using var payload = await CapturePayloadAsync(b => b.WithRelated(children, "contact_customer_accounts"));

        Assert.Equal(2, payload.RootElement.GetProperty("contact_customer_accounts").GetArrayLength());
    }

    [Fact]
    public async Task ExecuteAsync_WithRelatedList_ExcludesReadOnlyChildProperties()
    {
        var children = new List<ReadOnlyChild> { new() { ContactId = Guid.NewGuid(), FirstName = "John" } };

        using var payload = await CapturePayloadAsync(b => b.WithRelated(children, "contact_customer_accounts"));

        var child = payload.RootElement.GetProperty("contact_customer_accounts")[0];
        Assert.False(child.TryGetProperty("contactid", out _));
        Assert.Equal("John", child.GetProperty("firstname").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyRelatedList_OmitsNavigationProperty()
    {
        using var payload = await CapturePayloadAsync(b => b.WithRelated(new List<TestContact>(), "contact_customer_accounts"));

        Assert.False(payload.RootElement.TryGetProperty("contact_customer_accounts", out _));
        Assert.Equal("Parent", payload.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_WithSingleRelated_SendsNestedObject_NotArray()
    {
        using var payload = await CapturePayloadAsync(b => b.WithRelated(new TestContact { FirstName = "John" }, "primarycontactid"));

        var nested = payload.RootElement.GetProperty("primarycontactid");
        Assert.Equal(JsonValueKind.Object, nested.ValueKind);
        Assert.Equal("John", nested.GetProperty("firstname").GetString());
    }
}
