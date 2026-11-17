using System.Text.Json.Serialization;
using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent.Tests.Builders;

public class EntitySetAttributeTests
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
    public void GetEntitySetName_WithAttribute_UsesAttributeValue()
    {
        var name = ODataPropertyNameCache.GetEntitySetName<TestOpportunity>();

        Assert.Equal("opportunities", name);
    }

    [Fact]
    public void GetEntitySetName_WithoutAttribute_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ODataPropertyNameCache.GetEntitySetName<TestWidget>());

        Assert.Contains("EntitySet", ex.Message);
        Assert.Contains("TestWidget", ex.Message);
    }

    [Fact]
    public void Query_WithEntitySetAttribute_UsesAttributeName()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("/opportunities"),
            new { value = new TestOpportunity[] { } }
        );

        var query = client.Query<TestOpportunity>();
        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [Fact]
    public void Query_ExplicitEntitySet_OverridesAttribute()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("/custom_opps"),
            new { value = new TestOpportunity[] { } }
        );

        var query = client.Query<TestOpportunity>("custom_opps");
        var result = query.GetListAsync().Result;

        Assert.NotNull(result);
    }

    [EntitySet("opportunities")]
    internal class TestOpportunity
    {
        [JsonPropertyName("opportunityid")]
        public Guid OpportunityId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    internal class TestWidget
    {
        [JsonPropertyName("widgetid")]
        public Guid WidgetId { get; set; }
    }
}
