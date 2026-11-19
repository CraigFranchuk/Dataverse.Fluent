using System.Net;
using Dataverse.Fluent;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Tests.Builders;

public class EntityBuilderTests
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
    public async Task RetrieveAsync_WithValidId_ReturnsEntity()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var testData = new
        {
            accountid = Guid.NewGuid(),
            name = "Contoso",
            revenue = 1000000.00m,
            numberofemployees = 100,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains($"testaccounts({testData.accountid})"),
            testData
        );

        var entity = client.Entity<TestAccount>().WithId(testData.accountid);
        var result = await entity.RetrieveAsync();

        Assert.NotNull(result);
        Assert.Equal(testData.accountid, result.AccountId);
        Assert.Equal("Contoso", result.Name);
    }

    [Fact]
    public async Task RetrieveWithMetadataAsync_SurfacesETagAndEntity()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var accountId = Guid.NewGuid();
        var json = $$"""
        {
            "@odata.etag": "W/\"123456\"",
            "accountid": "{{accountId}}",
            "name": "Contoso"
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        handler.AddResponse(req => req.RequestUri!.ToString().Contains($"testaccounts({accountId})"), response);

        var result = await client.Entity<TestAccount>().WithId(accountId).RetrieveWithMetadataAsync();

        Assert.NotNull(result.Entity);
        Assert.Equal(accountId, result.Entity!.AccountId);
        Assert.Equal("Contoso", result.Entity.Name);
        Assert.True(result.HasETag);
        Assert.Equal("W/\"123456\"", result.ETag);
        Assert.Empty(result.FormattedValues);
    }

    [Fact]
    public async Task RetrieveWithMetadataAsync_WithFormattedValues_PopulatesFormattedValues()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var accountId = Guid.NewGuid();
        var json = $$"""
        {
            "@odata.etag": "W/\"99\"",
            "accountid": "{{accountId}}",
            "name": "Contoso",
            "revenue": 1000000,
            "revenue@OData.Community.Display.V1.FormattedValue": "$1,000,000.00"
        }
        """;

        string? sentPrefer = null;
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        handler.AddResponse(
            req =>
            {
                sentPrefer = req.Headers.TryGetValues("Prefer", out var values) ? string.Join(",", values) : null;
                return true;
            },
            response);

        var result = await client.Entity<TestAccount>()
            .WithId(accountId)
            .IncludeFormattedValues()
            .RetrieveWithMetadataAsync();

        Assert.NotNull(sentPrefer);
        Assert.Contains("OData.Community.Display.V1.FormattedValue", sentPrefer);
        Assert.Equal("$1,000,000.00", result.FormattedValues["revenue"]);
        Assert.Equal("W/\"99\"", result.ETag);
    }

    [Fact]
    public async Task RetrieveAsync_WithNotFoundError_ThrowsDataverseException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80040217"",
                ""message"": ""Entity with the specified ID does not exist.""
            }
        }";

        var errorResponse = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json"),
            Headers = { { "x-ms-activity-id", "retrieve-not-found-123" } }
        };

        handler.AddResponse(req => true, errorResponse);

        var entityId = Guid.NewGuid();
        var entity = client.Entity<TestAccount>().WithId(entityId);

        var exception = await Assert.ThrowsAsync<DataverseException>(() => entity.RetrieveAsync());

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("0x80040217", exception.ErrorCode);
        Assert.Equal("retrieve-not-found-123", exception.ActivityId);
        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_WithValidData_ReturnsCreatedEntity()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var newAccountId = Guid.NewGuid();
        var responseData = new
        {
            accountid = newAccountId,
            name = "New Account",
            revenue = 500000.00m,
            numberofemployees = 50,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("testaccounts"),
            responseData
        );

        var entity = client.Entity<TestAccount>()
            .Set("name", "New Account")
            .Set("revenue", 500000.00m)
            .ReturnRepresentation();

        var result = await entity.CreateAsync();

        Assert.NotNull(result);
        Assert.Equal("New Account", result.Entity!.Name);
        Assert.Equal(500000.00m, result.Entity!.Revenue);
    }

    [Fact]
    public async Task CreateAsync_WithValidationError_ThrowsDataverseException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80040200"",
                ""message"": ""Required field 'name' is missing."",
                ""innererror"": {
                    ""message"": ""Validation error details"",
                    ""type"": ""Microsoft.Crm.CrmException""
                }
            }
        }";

        var errorResponse = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json")
        };

        handler.AddResponse(req => req.Method == HttpMethod.Post, errorResponse);

        var entity = client.Entity<TestAccount>()
            .Set("revenue", 100000.00m)
            .ReturnRepresentation();

        var exception = await Assert.ThrowsAsync<DataverseException>(() => entity.CreateAsync());

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("0x80040200", exception.ErrorCode);
        Assert.Contains("Required field", exception.Message);
        Assert.NotNull(exception.InnerError);
    }

    [Fact]
    public async Task CreateAsync_WithoutRepresentation_ReturnsIdFromODataEntityIdHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var newId = Guid.NewGuid();
        var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        response.Headers.Add("OData-EntityId", $"https://test.crm.dynamics.com/api/data/v9.2/testaccounts({newId})");
        handler.AddResponse(req => req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/testaccounts"), response);

        var result = await client.Entity<TestAccount>().Set("name", "New Account").CreateAsync();

        Assert.Equal(newId, result.Id);
        Assert.Null(result.Entity);
        Assert.EndsWith($"testaccounts({newId})", result.EntityUrl);
    }

    [Fact]
    public async Task CreateAsync_WithRepresentation_ReturnsEntity_AndNullIdWhenHeaderAbsent()
    {
        // Dataverse does not return OData-EntityId when Prefer: return=representation is used.
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var newId = Guid.NewGuid();
        handler.AddJsonResponse(req => req.Method == HttpMethod.Post, new { accountid = newId, name = "New Account" }, HttpStatusCode.Created);

        var result = await client.Entity<TestAccount>().Set("name", "New Account").ReturnRepresentation().CreateAsync();

        Assert.Null(result.Id);
        Assert.Equal(newId, result.Entity!.AccountId);
    }

    [Fact]
    public async Task CreateAsync_SendsPayloadAndPreferHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? body = null;
        string? prefer = null;
        handler.AddResponse(req =>
        {
            body = req.Content!.ReadAsStringAsync().Result;
            prefer = req.Headers.TryGetValues("Prefer", out var v) ? string.Join(",", v) : null;
            return true;
        }, new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") });

        await client.Entity<TestAccount>().Set("name", "Contoso").ReturnRepresentation().CreateAsync();

        Assert.Equal("{\"name\":\"Contoso\"}", body);
        Assert.Equal("return=representation", prefer);
    }

    [Fact]
    public async Task UpdateAsync_AlwaysSendsIfMatchStar_SoItNeverCreates()
    {
        // A PATCH without If-Match is an upsert in Dataverse; an update must not create records.
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddResponse(
            req => req.Method == HttpMethod.Patch && req.Headers.IfMatch.Count == 1 && req.Headers.IfMatch.First().Tag == "*",
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Entity<TestAccount>().WithId(Guid.NewGuid()).Set("name", "x").UpdateAsync();
    }

    [Fact]
    public async Task UpdateAsync_WithETag_SendsETagAsIfMatch()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? ifMatch = null;
        handler.AddResponse(
            req => { ifMatch = string.Join(",", req.Headers.GetValues("If-Match")); return true; },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Entity<TestAccount>().WithId(Guid.NewGuid()).WithETag("W/\"12345\"").Set("name", "x").UpdateAsync();

        Assert.Equal("W/\"12345\"", ifMatch);
    }

    [Fact]
    public async Task RetrieveAsync_EscapesExpandQueryOption()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? query = null;
        handler.AddJsonResponse(req => { query = req.RequestUri!.Query; return true; }, new { accountid = Guid.NewGuid() });

        await client.Entity<TestAccount>(Guid.NewGuid())
            .Expand<TestContact>("contact_customer_accounts", e => e.Where("fullname eq 'R&D #1'"))
            .RetrieveAsync();

        // '&' and '#' must be percent-encoded, or the URL would be split or truncated.
        Assert.Contains("%26", query);
        Assert.Contains("%23", query);
        Assert.Equal("?$expand=" + Uri.EscapeDataString("contact_customer_accounts($filter=fullname eq 'R&D #1')"), query);
    }


    [Fact]
    public async Task UsingAlternateKey_WithDateTimeValue_SendsIsoLiteral()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? url = null;
        handler.AddJsonResponse(req => { url = req.RequestUri!.AbsoluteUri; return true; }, new { name = "x" });

        await client.Entity<TestAccount>()
            .UsingAlternateKey(x => x.CreatedOn, new DateTime(2026, 10, 26, 14, 0, 0, DateTimeKind.Utc))
            .RetrieveAsync();

        Assert.EndsWith("/testaccounts(createdon=2026-10-26T14:00:00Z)", url);
    }

    [Fact]
    public async Task UpdateAsync_WithValidData_ReturnsUpdatedEntity()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var accountId = Guid.NewGuid();
        var responseData = new
        {
            accountid = accountId,
            name = "Updated Account",
            revenue = 750000.00m,
            numberofemployees = 75,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.Method.Method == "PATCH" && req.RequestUri!.ToString().Contains($"testaccounts({accountId})"),
            responseData
        );

        var entity = client.Entity<TestAccount>()
            .WithId(accountId)
            .Set("name", "Updated Account")
            .Set("revenue", 750000.00m)
            .ReturnRepresentation();

        var result = await entity.UpdateAsync();

        Assert.NotNull(result);
        Assert.Equal("Updated Account", result.Name);
    }

    [Fact]
    public async Task UpdateAsync_WithConcurrencyError_ThrowsDataverseException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80060882"",
                ""message"": ""The record you are trying to update has been modified by another user."",
                ""innererror"": {
                    ""message"": ""Concurrency violation"",
                    ""type"": ""Microsoft.Crm.CrmException""
                }
            }
        }";

        var errorResponse = new HttpResponseMessage(HttpStatusCode.PreconditionFailed)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json")
        };

        handler.AddResponse(req => req.Method.Method == "PATCH", errorResponse);

        var entity = client.Entity<TestAccount>()
            .WithId(Guid.NewGuid())
            .Set("name", "Updated Name")
            .ReturnRepresentation();

        var exception = await Assert.ThrowsAsync<DataverseException>(() => entity.UpdateAsync());

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
        Assert.Equal("0x80060882", exception.ErrorCode);
        Assert.Contains("modified by another user", exception.Message);
    }

    [Fact]
    public async Task DeleteAsync_WithSuccess_CompletesWithoutError()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var accountId = Guid.NewGuid();

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Delete && req.RequestUri!.ToString().Contains($"testaccounts({accountId})"),
            new { }
        );

        var entity = client.Entity<TestAccount>().WithId(accountId);

        await entity.DeleteAsync();
    }

    [Fact]
    public async Task DeleteAsync_WithNotFoundError_ThrowsDataverseException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80040217"",
                ""message"": ""Cannot delete a record that does not exist.""
            }
        }";

        var errorResponse = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json")
        };

        handler.AddResponse(req => req.Method == HttpMethod.Delete, errorResponse);

        var entity = client.Entity<TestAccount>().WithId(Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<DataverseException>(() => entity.DeleteAsync());

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("0x80040217", exception.ErrorCode);
        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public async Task UpsertAsync_WithPermissionError_ThrowsDataverseException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80040220"",
                ""message"": ""You do not have sufficient permissions to perform this operation."",
                ""innererror"": {
                    ""message"": ""Principal user is missing prvWriteAccount privilege""
                }
            }
        }";

        var errorResponse = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json"),
            Headers = { { "x-ms-activity-id", "upsert-forbidden-456" } }
        };

        handler.AddResponse(req => req.Method.Method == "PATCH", errorResponse);

        var entity = client.Entity<TestAccount>()
            .WithId(Guid.NewGuid())
            .Set("name", "Test Upsert")
            .ReturnRepresentation();

        var exception = await Assert.ThrowsAsync<DataverseException>(() => entity.UpsertAsync());

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal("0x80040220", exception.ErrorCode);
        Assert.Equal("upsert-forbidden-456", exception.ActivityId);
        Assert.Contains("sufficient permissions", exception.Message);
        Assert.Contains("prvWriteAccount", exception.InnerError?["message"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateAsync_WithIdSet_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var entity = client.Entity<TestAccount>()
            .WithId(Guid.NewGuid())
            .Set("name", "Test");

        await Assert.ThrowsAsync<InvalidOperationException>(() => entity.CreateAsync());
    }

    [Fact]
    public async Task CreateAsync_WithSetFrom_CreatesEntityWithAllProperties()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var newAccountId = Guid.NewGuid();
        var responseData = new
        {
            accountid = newAccountId,
            name = "Contoso Ltd",
            revenue = 2500000.00m,
            numberofemployees = 150,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("testaccounts"),
            responseData
        );

        var account = new TestAccount
        {
            Name = "Contoso Ltd",
            Revenue = 2500000.00m,
            NumberOfEmployees = 150
        };

        var entity = client.Entity<TestAccount>()
            .SetFrom(account)
            .ReturnRepresentation();

        var result = await entity.CreateAsync();

        Assert.NotNull(result);
        Assert.Equal("Contoso Ltd", result.Entity!.Name);
        Assert.Equal(2500000.00m, result.Entity!.Revenue);
        Assert.Equal(150, result.Entity!.NumberOfEmployees);
    }

    [Fact]
    public async Task UpdateAsync_WithSetFrom_UpdatesEntityWithAllProperties()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var accountId = Guid.NewGuid();
        var responseData = new
        {
            accountid = accountId,
            name = "Updated Company",
            revenue = 3000000.00m,
            numberofemployees = 200,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.Method.Method == "PATCH" && req.RequestUri!.ToString().Contains($"testaccounts({accountId})"),
            responseData
        );

        var account = new TestAccount
        {
            Name = "Updated Company",
            Revenue = 3000000.00m,
            NumberOfEmployees = 200
        };

        var entity = client.Entity<TestAccount>()
            .WithId(accountId)
            .SetFrom(account)
            .ReturnRepresentation();

        var result = await entity.UpdateAsync();

        Assert.NotNull(result);
        Assert.Equal("Updated Company", result.Name);
        Assert.Equal(3000000.00m, result.Revenue);
        Assert.Equal(200, result.NumberOfEmployees);
    }

    [Fact]
    public void SetFrom_WithNullEntity_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var entity = client.Entity<TestAccount>();

        Assert.Throws<ArgumentNullException>(() => entity.SetFrom(null!));
    }

    [Fact]
    public async Task SetFrom_CanBeCombinedWithSet_OverridesSpecificProperties()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var newAccountId = Guid.NewGuid();
        var responseData = new
        {
            accountid = newAccountId,
            name = "Overridden Name",
            revenue = 1000000.00m,
            numberofemployees = 50,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("testaccounts"),
            responseData
        );

        var account = new TestAccount
        {
            Name = "Original Name",
            Revenue = 1000000.00m,
            NumberOfEmployees = 50
        };

        var entity = client.Entity<TestAccount>()
            .SetFrom(account)
            .Set("name", "Overridden Name")
            .ReturnRepresentation();

        var result = await entity.CreateAsync();

        Assert.NotNull(result);
        Assert.Equal("Overridden Name", result.Entity!.Name);
    }

    [Fact]
    public async Task Set_WithTypedExpression_SetsPropertyValue()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var newAccountId = Guid.NewGuid();
        var responseData = new
        {
            accountid = newAccountId,
            name = "Typed Account",
            revenue = 1500000.00m,
            numberofemployees = 100,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("testaccounts"),
            responseData
        );

        var entity = client.Entity<TestAccount>()
            .Set(x => x.Name, "Typed Account")
            .Set(x => x.Revenue, 1500000.00m)
            .Set(x => x.NumberOfEmployees, 100)
            .ReturnRepresentation();

        var result = await entity.CreateAsync();

        Assert.NotNull(result);
        Assert.Equal("Typed Account", result.Entity!.Name);
        Assert.Equal(1500000.00m, result.Entity!.Revenue);
        Assert.Equal(100, result.Entity!.NumberOfEmployees);
    }

    [Fact]
    public async Task Set_WithTypedExpression_RespectsJsonPropertyNameAttribute()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var newAccountId = Guid.NewGuid();
        var responseData = new
        {
            accountid = newAccountId,
            name = "Attribute Test",
            revenue = 2000000.00m,
            numberofemployees = 75,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("testaccounts"),
            responseData
        );

        // This should use the JsonPropertyName "name" from the TestAccount class
        var entity = client.Entity<TestAccount>()
            .Set(x => x.Name, "Attribute Test")
            .ReturnRepresentation();

        var result = await entity.CreateAsync();

        Assert.NotNull(result);
        Assert.Equal("Attribute Test", result.Entity!.Name);
    }

    [Fact]
    public async Task Set_TypedAndStringVersions_CanBeMixed()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var newAccountId = Guid.NewGuid();
        var responseData = new
        {
            accountid = newAccountId,
            name = "Mixed Account",
            revenue = 3500000.00m,
            numberofemployees = 200,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("testaccounts"),
            responseData
        );

        var entity = client.Entity<TestAccount>()
            .Set(x => x.Name, "Mixed Account")
            .Set("revenue", 3500000.00m)
            .Set(x => x.NumberOfEmployees, 200)
            .ReturnRepresentation();

        var result = await entity.CreateAsync();

        Assert.NotNull(result);
        Assert.Equal("Mixed Account", result.Entity!.Name);
        Assert.Equal(3500000.00m, result.Entity!.Revenue);
        Assert.Equal(200, result.Entity!.NumberOfEmployees);
    }

    [Fact]
    public async Task Set_WithNullExpression_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var entity = client.Entity<TestAccount>();

        Assert.Throws<ArgumentNullException>(() => 
            entity.Set<string>(null!, "Test"));
    }

    [Fact]
    public async Task Set_WithTypedExpression_CanClearFields()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var accountId = Guid.NewGuid();
        var responseData = new
        {
            accountid = accountId,
            name = "",
            revenue = 0m,
            numberofemployees = 0,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.Method.Method == "PATCH" && req.RequestUri!.ToString().Contains($"testaccounts({accountId})"),
            responseData
        );

        // Clear the name field by setting it to empty string
        var entity = client.Entity<TestAccount>()
            .WithId(accountId)
            .Set(x => x.Name, string.Empty)
            .ReturnRepresentation();

        var result = await entity.UpdateAsync();

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Name);
    }

    [Fact]
    public async Task SetFrom_CanBeCombinedWithTypedSet_OverridesSpecificProperties()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var newAccountId = Guid.NewGuid();
        var responseData = new
        {
            accountid = newAccountId,
            name = "Type-Safe Override",
            revenue = 1000000.00m,
            numberofemployees = 50,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("testaccounts"),
            responseData
        );

        var account = new TestAccount
        {
            Name = "Original Name",
            Revenue = 1000000.00m,
            NumberOfEmployees = 50
        };

        var entity = client.Entity<TestAccount>()
            .SetFrom(account)
            .Set(x => x.Name, "Type-Safe Override")
            .ReturnRepresentation();

        var result = await entity.CreateAsync();

        Assert.NotNull(result);
        Assert.Equal("Type-Safe Override", result.Entity!.Name);
    }

    [Fact]
    public async Task UsingAlternateKey_WithTypedExpression_RetrievesEntity()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var testData = new
        {
            accountid = Guid.NewGuid(),
            name = "Alternate Key Account",
            accountnumber = "ACC-001",
            revenue = 1000000.00m,
            numberofemployees = 100,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("testaccounts(accountnumber='ACC-001')"),
            testData
        );

        var entity = client.Entity<TestAccount>()
            .UsingAlternateKey(x => x.AccountNumber, "ACC-001");

        var result = await entity.RetrieveAsync();

        Assert.NotNull(result);
        Assert.Equal("Alternate Key Account", result.Name);
        Assert.Equal("ACC-001", result.AccountNumber);
    }

    [Fact]
    public async Task UsingAlternateKey_WithTypedExpression_CompositeKey_RetrievesEntity()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var testData = new
        {
            accountid = Guid.NewGuid(),
            name = "Composite Key Account",
            accountnumber = "ACC-002",
            revenue = 2000000.00m,
            numberofemployees = 150,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("testaccounts(accountnumber='ACC-002',statecode=0)"),
            testData
        );

        var entity = client.Entity<TestAccount>()
            .UsingAlternateKey(x => x.AccountNumber, "ACC-002")
            .UsingAlternateKey(x => x.StateCode, 0);

        var result = await entity.RetrieveAsync();

        Assert.NotNull(result);
        Assert.Equal("Composite Key Account", result.Name);
        Assert.Equal("ACC-002", result.AccountNumber);
    }


    [Fact]
    public async Task UsingAlternateKey_WithTypedExpression_UpsertEntity()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var testData = new
        {
            accountid = Guid.NewGuid(),
            name = "Upserted Account",
            accountnumber = "ACC-003",
            revenue = 3000000.00m,
            numberofemployees = 200,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.Method.Method == "PATCH" && req.RequestUri!.ToString().Contains("testaccounts(accountnumber='ACC-003')"),
            testData
        );

        var entity = client.Entity<TestAccount>()
            .UsingAlternateKey(x => x.AccountNumber, "ACC-003")
            .Set(x => x.Name, "Upserted Account")
            .Set(x => x.Revenue, 3000000.00m)
            .ReturnRepresentation();

        var result = (await entity.UpsertAsync()).Entity;

        Assert.NotNull(result);
        Assert.Equal("Upserted Account", result.Name);
        Assert.Equal("ACC-003", result.AccountNumber);
    }

    [Fact]
    public async Task UsingAlternateKey_TypedAndStringVersions_CanBeMixed()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var testData = new
        {
            accountid = Guid.NewGuid(),
            name = "Mixed Key Account",
            accountnumber = "ACC-004",
            revenue = 1500000.00m,
            numberofemployees = 75,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("testaccounts(accountnumber='ACC-004',customfield='custom')"),
            testData
        );

        var entity = client.Entity<TestAccount>()
            .UsingAlternateKey(x => x.AccountNumber, "ACC-004")
            .UsingAlternateKey("customfield", "custom");

        var result = await entity.RetrieveAsync();

        Assert.NotNull(result);
        Assert.Equal("Mixed Key Account", result.Name);
    }

    [Fact]
    public void UsingAlternateKey_WithNullExpression_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var entity = client.Entity<TestAccount>();

        Assert.Throws<ArgumentNullException>(() => 
            entity.UsingAlternateKey<string>(null!, "Test"));
    }

    [Fact]
    public async Task UsingAlternateKey_WithTypedExpression_RespectsJsonPropertyNameAttribute()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var testData = new
        {
            accountid = Guid.NewGuid(),
            name = "Attribute Respect Test",
            accountnumber = "ACC-005",
            revenue = 1000000.00m,
            numberofemployees = 100,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        handler.AddJsonResponse(
            req => req.RequestUri!.ToString().Contains("testaccounts(accountnumber='ACC-005')"),
            testData
        );

        var entity = client.Entity<TestAccount>()
            .UsingAlternateKey(x => x.AccountNumber, "ACC-005");

        var result = await entity.RetrieveAsync();

        Assert.NotNull(result);
        Assert.Equal("ACC-005", result.AccountNumber);
    }

    #region Upsert Conflict Resolution Tests

    [Fact]
    public async Task UpsertAsync_WithDefaultConflictResolution_DoesNotSendIfMatchHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var accountId = Guid.NewGuid();
        var responseData = new
        {
            accountid = accountId,
            name = "Upserted Account",
            revenue = 1000000.00m,
            numberofemployees = 100,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        bool hasIfMatch = false;
        bool hasIfNoneMatch = false;
        handler.AddJsonResponse(
            req =>
            {
                if (req.Method.Method == "PATCH" && req.RequestUri!.ToString().Contains($"testaccounts({accountId})"))
                {
                    hasIfMatch = req.Headers.Contains("If-Match");
                    hasIfNoneMatch = req.Headers.Contains("If-None-Match");
                    return true;
                }
                return false;
            },
            responseData
        );

        var entity = client.Entity<TestAccount>()
            .WithId(accountId)
            .Set(x => x.Name, "Upserted Account")
            .ReturnRepresentation();

        var result = (await entity.UpsertAsync()).Entity;

        Assert.NotNull(result);
        // Standard upsert sends no If-Match or If-None-Match — server creates or updates as needed
        Assert.False(hasIfMatch);
        Assert.False(hasIfNoneMatch);
    }

    [Fact]
    public async Task UpsertAsync_WithPreventUpdate_SendsIfNoneMatchHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var accountId = Guid.NewGuid();
        var responseData = new
        {
            accountid = accountId,
            name = "New Account",
            revenue = 1000000.00m,
            numberofemployees = 100,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        string? ifNoneMatchHeader = null;
        handler.AddJsonResponse(
            req =>
            {
                if (req.Method.Method == "PATCH" && req.RequestUri!.ToString().Contains($"testaccounts({accountId})"))
                {
                    ifNoneMatchHeader = req.Headers.Contains("If-None-Match")
                        ? req.Headers.GetValues("If-None-Match")?.FirstOrDefault()
                        : null;
                    return true;
                }
                return false;
            },
            responseData
        );

        var entity = client.Entity<TestAccount>()
            .WithId(accountId)
            .Set(x => x.Name, "New Account")
            .ReturnRepresentation();

        var result = (await entity.UpsertAsync(UpsertConflictResolution.PreventUpdate)).Entity;

        Assert.NotNull(result);
        Assert.NotNull(ifNoneMatchHeader);
        Assert.Equal("*", ifNoneMatchHeader);
    }

    [Fact]
    public async Task UpsertAsync_WithPreventUpdate_WhenRecordExists_ThrowsPreconditionFailed()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80060882"",
                ""message"": ""The record already exists. Cannot insert duplicate record.""
            }
        }";

        var errorResponse = new HttpResponseMessage(HttpStatusCode.PreconditionFailed)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json")
        };

        handler.AddResponse(req => req.Method.Method == "PATCH", errorResponse);

        var entity = client.Entity<TestAccount>()
            .WithId(Guid.NewGuid())
            .Set(x => x.Name, "Duplicate Account")
            .ReturnRepresentation();

        var exception = await Assert.ThrowsAsync<DataverseException>(
            () => entity.UpsertAsync(UpsertConflictResolution.PreventUpdate));

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
        Assert.Contains("already exists", exception.Message);
    }

    [Fact]
    public async Task UpsertAsync_WithPreventCreate_SendsIfMatchHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var accountId = Guid.NewGuid();
        var responseData = new
        {
            accountid = accountId,
            name = "Updated Account",
            revenue = 2000000.00m,
            numberofemployees = 150,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        string? ifMatchHeader = null;
        handler.AddJsonResponse(
            req =>
            {
                if (req.Method.Method == "PATCH" && req.RequestUri!.ToString().Contains($"testaccounts({accountId})"))
                {
                    ifMatchHeader = req.Headers.Contains("If-Match")
                        ? req.Headers.GetValues("If-Match")?.FirstOrDefault()
                        : null;
                    return true;
                }
                return false;
            },
            responseData
        );

        var entity = client.Entity<TestAccount>()
            .WithId(accountId)
            .Set(x => x.Name, "Updated Account")
            .ReturnRepresentation();

        var result = (await entity.UpsertAsync(UpsertConflictResolution.PreventCreate)).Entity;

        Assert.NotNull(result);
        Assert.NotNull(ifMatchHeader);
        Assert.Equal("*", ifMatchHeader);
    }

    [Fact]
    public async Task UpsertAsync_WithPreventCreate_WhenRecordDoesNotExist_ThrowsNotFound()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80040217"",
                ""message"": ""Record does not exist.""
            }
        }";

        var errorResponse = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json")
        };

        handler.AddResponse(req => req.Method.Method == "PATCH", errorResponse);

        var entity = client.Entity<TestAccount>()
            .WithId(Guid.NewGuid())
            .Set(x => x.Name, "Updated Account")
            .ReturnRepresentation();

        var exception = await Assert.ThrowsAsync<DataverseException>(
            () => entity.UpsertAsync(UpsertConflictResolution.PreventCreate));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public async Task UpsertAsync_WithIfUnmodifiedSince_SendsETagInIfMatchHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var accountId = Guid.NewGuid();
        var etag = "W/\"12345678\"";
        var responseData = new
        {
            accountid = accountId,
            name = "Concurrency Safe Update",
            revenue = 3000000.00m,
            numberofemployees = 200,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        string? ifMatchHeader = null;
        handler.AddJsonResponse(
            req =>
            {
                if (req.Method.Method == "PATCH" && req.RequestUri!.ToString().Contains($"testaccounts({accountId})"))
                {
                    ifMatchHeader = req.Headers.Contains("If-Match")
                        ? req.Headers.GetValues("If-Match")?.FirstOrDefault()
                        : null;
                    return true;
                }
                return false;
            },
            responseData
        );

        var entity = client.Entity<TestAccount>()
            .WithId(accountId)
            .WithETag(etag)
            .Set(x => x.Name, "Concurrency Safe Update")
            .ReturnRepresentation();

        var result = (await entity.UpsertAsync(UpsertConflictResolution.IfUnmodifiedSince)).Entity;

        Assert.NotNull(result);
        Assert.NotNull(ifMatchHeader);
        Assert.Equal(etag, ifMatchHeader);
    }

    [Fact]
    public void UpsertAsync_WithIfUnmodifiedSince_WithoutETag_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var entity = client.Entity<TestAccount>()
            .WithId(Guid.NewGuid())
            .Set(x => x.Name, "Test");

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => entity.UpsertAsync(UpsertConflictResolution.IfUnmodifiedSince));

        Assert.NotNull(exception);
    }

    [Fact]
    public async Task UpsertAsync_WithIfUnmodifiedSince_WhenETagMismatch_ThrowsPreconditionFailed()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var errorJson = @"{
            ""error"": {
                ""code"": ""0x80060882"",
                ""message"": ""The record has been modified by another user. Please refresh and try again.""
            }
        }";

        var errorResponse = new HttpResponseMessage(HttpStatusCode.PreconditionFailed)
        {
            Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json")
        };

        handler.AddResponse(req => req.Method.Method == "PATCH", errorResponse);

        var entity = client.Entity<TestAccount>()
            .WithId(Guid.NewGuid())
            .WithETag("W/\"12345678\"")
            .Set(x => x.Name, "Stale Update")
            .ReturnRepresentation();

        var exception = await Assert.ThrowsAsync<DataverseException>(
            () => entity.UpsertAsync(UpsertConflictResolution.IfUnmodifiedSince));

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
        Assert.Contains("modified by another user", exception.Message);
    }

    [Fact]
    public async Task UpsertAsync_WithAlternateKey_AndPreventUpdate_SendsIfNoneMatchHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var responseData = new
        {
            accountid = Guid.NewGuid(),
            name = "New Account via AltKey",
            accountnumber = "ACC-NEW-001",
            revenue = 1000000.00m,
            numberofemployees = 100,
            createdon = DateTime.UtcNow,
            statecode = 0
        };

        string? ifNoneMatchHeader = null;
        handler.AddJsonResponse(
            req =>
            {
                if (req.Method.Method == "PATCH" && req.RequestUri!.ToString().Contains("testaccounts(accountnumber='ACC-NEW-001')"))
                {
                    ifNoneMatchHeader = req.Headers.Contains("If-None-Match")
                        ? req.Headers.GetValues("If-None-Match")?.FirstOrDefault()
                        : null;
                    return true;
                }
                return false;
            },
            responseData
        );

        var entity = client.Entity<TestAccount>()
            .UsingAlternateKey(x => x.AccountNumber, "ACC-NEW-001")
            .Set(x => x.Name, "New Account via AltKey")
            .ReturnRepresentation();

        var result = (await entity.UpsertAsync(UpsertConflictResolution.PreventUpdate)).Entity;

        Assert.NotNull(result);
        Assert.NotNull(ifNoneMatchHeader);
        Assert.Equal("*", ifNoneMatchHeader);
    }

    #endregion
}
