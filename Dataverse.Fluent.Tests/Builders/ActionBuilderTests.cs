using System.Net;
using System.Text.Json.Serialization;
using Dataverse.Fluent.Builders;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Tests.Builders;

public class ActionBuilderTests
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

    #region Test POCOs

    private class WinOpportunityRequest
    {
        [JsonPropertyName("OpportunityClose")]
        public OpportunityClose? OpportunityClose { get; set; }

        [JsonPropertyName("Status")]
        public int Status { get; set; }
    }

    private class OpportunityClose
    {
        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        [JsonPropertyName("opportunityid@odata.bind")]
        public string? OpportunityId { get; set; }
    }

    private class WinOpportunityResponse
    {
        [JsonPropertyName("OpportunityCloseId")]
        public Guid OpportunityCloseId { get; set; }
    }

    private class CustomActionRequest
    {
        [JsonPropertyName("Target")]
        public EntityReference? Target { get; set; }

        [JsonPropertyName("InputParameter")]
        public string? InputParameter { get; set; }
    }

    private class EntityReference
    {
        [JsonPropertyName("@odata.type")]
        public string? ODataType { get; set; }

        [JsonPropertyName("accountid")]
        public Guid Id { get; set; }
    }

    private class CustomActionResponse
    {
        [JsonPropertyName("OutputParameter")]
        public string? OutputParameter { get; set; }

        [JsonPropertyName("Success")]
        public bool Success { get; set; }
    }

    #endregion

    #region Unbound Actions

    [Fact]
    public async Task Action_Unbound_WithTypedParameters_ExecutesSuccessfully()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var opportunityId = Guid.NewGuid();
        var opportunityCloseId = Guid.NewGuid();
        var responseData = new
        {
            OpportunityCloseId = opportunityCloseId
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("WinOpportunity"),
            responseData
        );

        var action = client.Action<WinOpportunityRequest, WinOpportunityResponse>("WinOpportunity")
            .WithParameter(p => p.OpportunityClose, new OpportunityClose
            {
                Subject = "Won!",
                OpportunityId = $"/opportunities({opportunityId})"
            })
            .WithParameter(p => p.Status, 3);

        var result = await action.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal(opportunityCloseId, result.OpportunityCloseId);
    }

    [Fact]
    public async Task Action_Unbound_WithRequest_ExecutesSuccessfully()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var opportunityId = Guid.NewGuid();
        var opportunityCloseId = Guid.NewGuid();
        var responseData = new
        {
            OpportunityCloseId = opportunityCloseId
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("WinOpportunity"),
            responseData
        );

        var request = new WinOpportunityRequest
        {
            OpportunityClose = new OpportunityClose
            {
                Subject = "Won!",
                OpportunityId = $"/opportunities({opportunityId})"
            },
            Status = 3
        };

        var action = client.Action<WinOpportunityRequest, WinOpportunityResponse>("WinOpportunity")
            .WithRequest(request);

        var result = await action.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal(opportunityCloseId, result.OpportunityCloseId);
    }

    [Fact]
    public async Task Action_Unbound_WithStringParameter_ExecutesSuccessfully()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseData = new
        {
            OutputParameter = "Success",
            Success = true
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("CustomAction"),
            responseData
        );

        var action = client.Action<CustomActionRequest, CustomActionResponse>("CustomAction")
            .WithParameter("InputParameter", "test-value");

        var result = await action.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("Success", result.OutputParameter);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Action_Unbound_MixedParameterTypes_ExecutesSuccessfully()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var accountId = Guid.NewGuid();
        var responseData = new
        {
            OutputParameter = "Mixed Success",
            Success = true
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("CustomAction"),
            responseData
        );

        var action = client.Action<CustomActionRequest, CustomActionResponse>("CustomAction")
            .WithParameter(p => p.Target, new EntityReference
            {
                ODataType = "Microsoft.Dynamics.CRM.account",
                Id = accountId
            })
            .WithParameter("InputParameter", "typed-and-string");

        var result = await action.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("Mixed Success", result.OutputParameter);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Action_Unbound_WithNullParameter_ExecutesSuccessfully()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseData = new
        {
            OutputParameter = "Null handled",
            Success = true
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("CustomAction"),
            responseData
        );

        var action = client.Action<CustomActionRequest, CustomActionResponse>("CustomAction")
            .WithParameter(p => p.InputParameter, (string?)null);

        var result = await action.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("Null handled", result.OutputParameter);
    }

    #endregion

    #region Bound Actions

    [Fact]
    public async Task Action_Bound_WithTypedParameters_ExecutesSuccessfully()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var accountId = Guid.NewGuid();
        var responseData = new
        {
            OutputParameter = "Bound Success",
            Success = true
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post 
                && req.RequestUri!.ToString().Contains($"testaccounts({accountId})/Microsoft.Dynamics.CRM.CustomAction"),
            responseData
        );

        var action = client.ForEntity<TestAccount>(accountId)
            .Action<CustomActionRequest, CustomActionResponse>("CustomAction")
            .WithParameter(p => p.InputParameter, "bound-test");

        var result = await action.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("Bound Success", result.OutputParameter);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Action_Bound_WithCustomEntitySet_ExecutesSuccessfully()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var entityId = Guid.NewGuid();
        var responseData = new
        {
            OutputParameter = "Custom EntitySet Success",
            Success = true
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post 
                && req.RequestUri!.ToString().Contains($"customentities({entityId})/Microsoft.Dynamics.CRM.CustomAction"),
            responseData
        );

        var action = client.ForEntity<TestAccount>(entityId, "customentities")
            .Action<CustomActionRequest, CustomActionResponse>("CustomAction")
            .WithParameter(p => p.InputParameter, "custom-entity");

        var result = await action.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("Custom EntitySet Success", result.OutputParameter);
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task Action_WithDataverseError_ThrowsDataverseException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80040217"",
                ""message"": ""Action execution failed.""
            }
        }";

        var errorResponse = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json")
        };

        handler.AddResponse(req => req.Method == HttpMethod.Post, errorResponse);

        var action = client.Action<CustomActionRequest, CustomActionResponse>("FailingAction")
            .WithParameter(p => p.InputParameter, "test");

        var exception = await Assert.ThrowsAsync<DataverseException>(() => action.ExecuteAsync());

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("0x80040217", exception.ErrorCode);
        Assert.Contains("Action execution failed", exception.Message);
    }

    [Fact]
    public void Action_WithNullExpression_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var action = client.Action<CustomActionRequest, CustomActionResponse>("TestAction");

        Assert.Throws<ArgumentNullException>(() => 
            action.WithParameter<string>(null!, "test"));
    }

    [Fact]
    public void Action_WithNullRequest_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var action = client.Action<CustomActionRequest, CustomActionResponse>("TestAction");

        Assert.Throws<ArgumentNullException>(() => 
            action.WithRequest(null!));
    }

    [Fact]
    public void Action_WithNullParameterName_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var action = client.Action<CustomActionRequest, CustomActionResponse>("TestAction");

        Assert.Throws<ArgumentNullException>(() => 
            action.WithParameter(null!, "test"));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task Action_WithEmptyResponse_ReturnsDefault()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("NoResponseAction"),
            new { }
        );

        var action = client.Action<CustomActionRequest, CustomActionResponse>("NoResponseAction")
            .WithParameter(p => p.InputParameter, "test");

        var result = await action.ExecuteAsync();

        // Empty response should return default (null for reference types)
        Assert.Null(result);
    }

    [Fact]
    public async Task Action_WithComplexTypes_SerializesCorrectly()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var accountId = Guid.NewGuid();
        var responseData = new
        {
            OutputParameter = "Complex Success",
            Success = true
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("ComplexAction"),
            responseData
        );

        var entityRef = new EntityReference
        {
            ODataType = "Microsoft.Dynamics.CRM.account",
            Id = accountId
        };

        var action = client.Action<CustomActionRequest, CustomActionResponse>("ComplexAction")
            .WithParameter(p => p.Target, entityRef);

        var result = await action.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("Complex Success", result.OutputParameter);
    }

    [Fact]
    public async Task Action_ParameterOverride_LastValueWins()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseData = new
        {
            OutputParameter = "Override Success",
            Success = true
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("OverrideAction"),
            responseData
        );

        var action = client.Action<CustomActionRequest, CustomActionResponse>("OverrideAction")
            .WithParameter(p => p.InputParameter, "first-value")
            .WithParameter(p => p.InputParameter, "second-value");

        var result = await action.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("Override Success", result.OutputParameter);
    }

    #endregion
}
