using System.Net;
using System.Text.Json.Serialization;
using Dataverse.Fluent.Builders;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Tests.Builders;

public class FunctionBuilderTests
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

    // Function parameters are sent as aliases: Func(Name=@p1)?@p1='value'. This substitutes the
    // decoded alias values back into the function segment (Func(Name='value')) so assertions can
    // check the logical call.
    private static string ResolveAliases(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (uri.Query.Length <= 1)
            return path;

        foreach (var pair in uri.Query[1..].Split('&'))
        {
            var separator = pair.IndexOf('=');
            var alias = Uri.UnescapeDataString(pair[..separator]);
            var value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            path = path.Replace($"={alias},", $"={value},").Replace($"={alias})", $"={value})");
        }

        return path;
    }

    #region Test POCOs

    private class WhoAmIResponse
    {
        [JsonPropertyName("UserId")]
        public Guid UserId { get; set; }

        [JsonPropertyName("BusinessUnitId")]
        public Guid BusinessUnitId { get; set; }

        [JsonPropertyName("OrganizationId")]
        public Guid OrganizationId { get; set; }
    }

    private class RetrieveVersionResponse
    {
        [JsonPropertyName("Version")]
        public string? Version { get; set; }
    }

    private class CalculatePriceResponse
    {
        [JsonPropertyName("Price")]
        public decimal Price { get; set; }
    }

    private class CustomFunctionResponse
    {
        [JsonPropertyName("Result")]
        public string? Result { get; set; }

        [JsonPropertyName("Count")]
        public int Count { get; set; }
    }

    #endregion

    #region Unbound Functions

    [Fact]
    public async Task Function_Unbound_NoParameters_ExecutesSuccessfully()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var userId = Guid.NewGuid();
        var businessUnitId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();

        var responseData = new
        {
            UserId = userId,
            BusinessUnitId = businessUnitId,
            OrganizationId = organizationId
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get && ResolveAliases(req).EndsWith("WhoAmI"),
            responseData
        );

        var function = client.Function<WhoAmIResponse>("WhoAmI");
        var result = await function.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal(userId, result.UserId);
        Assert.Equal(businessUnitId, result.BusinessUnitId);
        Assert.Equal(organizationId, result.OrganizationId);
    }

    [Fact]
    public async Task Function_Unbound_WithStringParameter_ExecutesSuccessfully()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseData = new
        {
            Version = "9.2.24011.00167"
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get 
                && ResolveAliases(req).Contains("RetrieveVersion")
                && ResolveAliases(req).Contains("AccessMode='Online'"),
            responseData
        );

        var function = client.Function<RetrieveVersionResponse>("RetrieveVersion")
            .WithParameter("AccessMode", "Online");

        var result = await function.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("9.2.24011.00167", result.Version);
    }

    [Fact]
    public async Task Function_Unbound_WithMultipleParameters_ExecutesSuccessfully()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseData = new
        {
            Price = 149.99m
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get 
                && ResolveAliases(req).Contains("CalculatePrice")
                && ResolveAliases(req).Contains("ProductId='")
                && ResolveAliases(req).Contains("Quantity=5"),
            responseData
        );

        var productId = Guid.NewGuid();
        var function = client.Function<CalculatePriceResponse>("CalculatePrice")
            .WithParameter("ProductId", productId)
            .WithParameter("Quantity", 5);

        var result = await function.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal(149.99m, result.Price);
    }

    [Fact]
    public async Task Function_Unbound_WithNullParameter_ExecutesSuccessfully()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseData = new
        {
            Result = "Null handled",
            Count = 0
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get 
                && ResolveAliases(req).Contains("CustomFunction")
                && ResolveAliases(req).Contains("Parameter=null"),
            responseData
        );

        var function = client.Function<CustomFunctionResponse>("CustomFunction")
            .WithParameter("Parameter", null);

        var result = await function.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("Null handled", result.Result);
    }

    [Fact]
    public async Task Function_Unbound_WithBoolParameter_FormatsCorrectly()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseData = new
        {
            Result = "Boolean handled",
            Count = 1
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get 
                && ResolveAliases(req).Contains("CustomFunction")
                && ResolveAliases(req).Contains("IsActive=true"),
            responseData
        );

        var function = client.Function<CustomFunctionResponse>("CustomFunction")
            .WithParameter("IsActive", true);

        var result = await function.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("Boolean handled", result.Result);
    }

    [Fact]
    public async Task Function_Unbound_WithDateTimeParameter_FormatsCorrectly()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var testDate = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var responseData = new
        {
            Result = "DateTime handled",
            Count = 10
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get 
                && ResolveAliases(req).Contains("CustomFunction")
                && ResolveAliases(req).Contains("StartDate='2024-01-15T10:30:00Z'"),
            responseData
        );

        var function = client.Function<CustomFunctionResponse>("CustomFunction")
            .WithParameter("StartDate", testDate);

        var result = await function.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("DateTime handled", result.Result);
    }

    [Fact]
    public async Task Function_Unbound_WithGuidParameter_FormatsCorrectly()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var testGuid = Guid.NewGuid();
        var responseData = new
        {
            Result = "Guid handled",
            Count = 1
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get 
                && ResolveAliases(req).Contains("CustomFunction")
                && ResolveAliases(req).Contains($"EntityId='{testGuid}'"),
            responseData
        );

        var function = client.Function<CustomFunctionResponse>("CustomFunction")
            .WithParameter("EntityId", testGuid);

        var result = await function.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("Guid handled", result.Result);
    }

    [Fact]
    public async Task Function_Unbound_WithNumericParameters_FormatsCorrectly()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseData = new
        {
            Result = "Numeric handled",
            Count = 42
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get 
                && ResolveAliases(req).Contains("CustomFunction")
                && ResolveAliases(req).Contains("IntValue=42")
                && ResolveAliases(req).Contains("DecimalValue=99.99"),
            responseData
        );

        var function = client.Function<CustomFunctionResponse>("CustomFunction")
            .WithParameter("IntValue", 42)
            .WithParameter("DecimalValue", 99.99m);

        var result = await function.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("Numeric handled", result.Result);
    }

    #endregion

    #region Bound Functions

    [Fact]
    public async Task Function_Bound_NoParameters_ExecutesSuccessfully()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var accountId = Guid.NewGuid();
        var responseData = new
        {
            Count = 5
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get 
                && ResolveAliases(req).Contains($"testaccounts({accountId})/Microsoft.Dynamics.CRM.GetRelatedCount"),
            responseData
        );

        var function = client.ForEntity<TestAccount>(accountId)
            .Function<CustomFunctionResponse>("GetRelatedCount");

        var result = await function.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task Function_Bound_WithParameters_ExecutesSuccessfully()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var accountId = Guid.NewGuid();
        var responseData = new
        {
            Result = "Bound with params",
            Count = 10
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get 
                && ResolveAliases(req).Contains($"testaccounts({accountId})/Microsoft.Dynamics.CRM.CustomFunction")
                && ResolveAliases(req).Contains("Filter='active'"),
            responseData
        );

        var function = client.ForEntity<TestAccount>(accountId)
            .Function<CustomFunctionResponse>("CustomFunction")
            .WithParameter("Filter", "active");

        var result = await function.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("Bound with params", result.Result);
        Assert.Equal(10, result.Count);
    }

    [Fact]
    public async Task Function_Bound_WithCustomEntitySet_ExecutesSuccessfully()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var entityId = Guid.NewGuid();
        var responseData = new
        {
            Result = "Custom EntitySet",
            Count = 3
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get 
                && ResolveAliases(req).Contains($"customentities({entityId})/Microsoft.Dynamics.CRM.CustomFunction"),
            responseData
        );

        var function = client.ForEntity<TestAccount>(entityId, "customentities")
            .Function<CustomFunctionResponse>("CustomFunction");

        var result = await function.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("Custom EntitySet", result.Result);
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task Function_WithDataverseError_ThrowsDataverseException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80040217"",
                ""message"": ""Function execution failed.""
            }
        }";

        var errorResponse = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json")
        };

        handler.AddResponse(req => req.Method == HttpMethod.Get, errorResponse);

        var function = client.Function<CustomFunctionResponse>("FailingFunction")
            .WithParameter("Test", "value");

        var exception = await Assert.ThrowsAsync<DataverseException>(() => function.ExecuteAsync());

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("0x80040217", exception.ErrorCode);
        Assert.Contains("Function execution failed", exception.Message);
    }

    [Fact]
    public void Function_WithNullParameterName_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var function = client.Function<CustomFunctionResponse>("TestFunction");

        Assert.Throws<ArgumentNullException>(() => 
            function.WithParameter(null!, "test"));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task Function_WithEmptyResponse_ReturnsDefault()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get && ResolveAliases(req).Contains("EmptyFunction"),
            new { }
        );

        var function = client.Function<CustomFunctionResponse>("EmptyFunction");

        var result = await function.ExecuteAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task Function_ParameterOverride_LastValueWins()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseData = new
        {
            Result = "Override handled",
            Count = 1
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get 
                && ResolveAliases(req).Contains("OverrideFunction")
                && ResolveAliases(req).Contains("Param='second'"),
            responseData
        );

        var function = client.Function<CustomFunctionResponse>("OverrideFunction")
            .WithParameter("Param", "first")
            .WithParameter("Param", "second");

        var result = await function.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("Override handled", result.Result);
    }

    [Fact]
    public async Task Function_WithSpecialCharactersInString_EncodesCorrectly()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseData = new
        {
            Result = "Special chars handled",
            Count = 1
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get 
                && ResolveAliases(req).Contains("CustomFunction")
                && ResolveAliases(req).Contains("Text="),
            responseData
        );

        var function = client.Function<CustomFunctionResponse>("CustomFunction")
            .WithParameter("Text", "Hello & goodbye");

        var result = await function.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("Special chars handled", result.Result);
    }

    [Fact]
    public async Task Function_WithDateTimeOffsetParameter_FormatsCorrectly()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var testDateOffset = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.FromHours(-5));
        var responseData = new
        {
            Result = "DateTimeOffset handled",
            Count = 1
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Get 
                && ResolveAliases(req).Contains("CustomFunction")
                && ResolveAliases(req).Contains("OffsetDate='"),
            responseData
        );

        var function = client.Function<CustomFunctionResponse>("CustomFunction")
            .WithParameter("OffsetDate", testDateOffset);

        var result = await function.ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("DateTimeOffset handled", result.Result);
    }

    [Fact]
    public async Task Function_WithMultipleParameters_UsesCommaSeparatedAliases()
    {
        // OData separates function parameters with ',' (not '&'); values travel as escaped aliases.
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        Uri? captured = null;
        handler.AddJsonResponse(req => { captured = req.RequestUri; return true; }, new { Result = "ok" });

        await client.Function<CustomFunctionResponse>("CustomFunction")
            .WithParameter("Name", "hello world")
            .WithParameter("Amount", 1.5m)
            .ExecuteAsync();

        Assert.EndsWith("/CustomFunction(Name=@p1,Amount=@p2)", captured!.AbsolutePath);
        Assert.Equal("?@p1=%27hello%20world%27&@p2=1.5", captured.Query);
    }

    [Fact]
    public async Task Function_WithStringContainingQuoteAndReservedChars_EscapesLiteral()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? resolved = null;
        handler.AddJsonResponse(req => { resolved = ResolveAliases(req); return true; }, new { Result = "ok" });

        await client.Function<CustomFunctionResponse>("CustomFunction")
            .WithParameter("Text", "O'Brien & Co #1+2")
            .ExecuteAsync();

        // Single quotes are doubled per the OData literal rule; '&', '#', '+' and spaces survive intact.
        Assert.EndsWith("CustomFunction(Text='O''Brien & Co #1+2')", resolved);
    }

    [Fact]
    public async Task Function_WithNumericParameter_UsesInvariantCulture()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? resolved = null;
        handler.AddJsonResponse(req => { resolved = ResolveAliases(req); return true; }, new { Result = "ok" });

        var original = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            await client.Function<CustomFunctionResponse>("CustomFunction")
                .WithParameter("Amount", 1.5m)
                .ExecuteAsync();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }

        Assert.EndsWith("CustomFunction(Amount=1.5)", resolved);
    }

    [Fact]
    public async Task Function_WithComplexParameter_SendsJsonAlias()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? resolved = null;
        handler.AddJsonResponse(req => { resolved = ResolveAliases(req); return true; }, new { Result = "ok" });

        await client.Function<CustomFunctionResponse>("CustomFunction")
            .WithParameter("Target", new Dictionary<string, string> { ["@odata.id"] = "accounts(00000000-0000-0000-0000-000000000001)" })
            .ExecuteAsync();

        Assert.EndsWith("CustomFunction(Target={\"@odata.id\":\"accounts(00000000-0000-0000-0000-000000000001)\"})", resolved);
    }

    #endregion
}
