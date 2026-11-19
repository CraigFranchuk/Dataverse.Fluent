using System.Net;
using System.Text;
using Dataverse.Fluent.Builders;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Tests.Builders;

public class AnnotationBuilderTests
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

    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // Creates the annotation against an empty response and returns the request that was sent.
    private async Task<CapturedRequest> SendCreateAsync(Func<AnnotationBuilder, AnnotationBuilder> configure)
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.NoContent));

        await configure(client.Annotation()).CreateAsync();

        return Assert.Single(handler.Requests);
    }

    private static string? Property(CapturedRequest request, string name)
        => request.Json.TryGetProperty(name, out var value) ? value.ToString() : null;

    [Fact]
    public void Constructor_WithNullClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new AnnotationBuilder(null!));
    }

    [Fact]
    public async Task Regarding_WithEntityTypeAndId_BindsToEntitySet()
    {
        var request = await SendCreateAsync(a => a.Regarding("account", AccountId).WithSubject("s"));

        Assert.Equal($"/accounts({AccountId})", Property(request, "objectid_account@odata.bind"));
    }

    [Fact]
    public async Task Regarding_WithGenericType_BindsToTypeEntitySet()
    {
        var request = await SendCreateAsync(a => a.Regarding<TestAccount>(AccountId).WithSubject("s"));

        Assert.Equal($"/testaccounts({AccountId})", Property(request, "objectid_testaccount@odata.bind"));
    }

    [Fact]
    public void Regarding_WithNullEntityType_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var annotation = client.Annotation();

        Assert.Throws<ArgumentNullException>(() => annotation.Regarding(null!, Guid.NewGuid()));
    }

    [Fact]
    public async Task WithFile_WithCustomMimeType_SendsThatMimeType()
    {
        var request = await SendCreateAsync(a => a.Regarding<TestAccount>(AccountId)
            .WithFile("test.dat", Encoding.UTF8.GetBytes("Test file content"), "application/custom"));

        Assert.Equal("application/custom", Property(request, "mimetype"));
    }

    [Fact]
    public void WithFile_WithNullFileName_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var fileContent = Encoding.UTF8.GetBytes("Test");

        var annotation = client.Annotation();

        Assert.Throws<ArgumentNullException>(() => annotation.WithFile(null!, fileContent));
    }

    [Fact]
    public void WithFile_WithNullContent_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var annotation = client.Annotation();

        Assert.Throws<ArgumentNullException>(() => annotation.WithFile("test.txt", (byte[])null!));
    }

    [Fact]
    public async Task WithFile_WithStream_SendsStreamContent()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Stream content"));

        var request = await SendCreateAsync(a => a.Regarding<TestAccount>(AccountId).WithFile("test.txt", stream));

        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("Stream content")), Property(request, "documentbody"));
    }

    [Fact]
    public void WithFile_WithNullStream_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var annotation = client.Annotation();

        Assert.Throws<ArgumentNullException>(() => annotation.WithFile("test.txt", (Stream)null!));
    }

    [Fact]
    public async Task WithFileAsync_WithStream_SendsStreamContent()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.NoContent));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Stream content"));

        var annotation = await client.Annotation().Regarding<TestAccount>(AccountId).WithFileAsync("test.txt", stream);
        await annotation.CreateAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("Stream content")), Property(request, "documentbody"));
        Assert.Equal("test.txt", Property(request, "filename"));
    }

    [Fact]
    public async Task WithFileAsync_WithNullStream_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var annotation = client.Annotation();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await annotation.WithFileAsync("test.txt", null!));
    }

    [Fact]
    public async Task ReturnRepresentation_False_SendsNoPreferHeader()
    {
        var request = await SendCreateAsync(a => a.Regarding<TestAccount>(AccountId)
            .ReturnRepresentation()
            .ReturnRepresentation(false));

        Assert.Null(request.Header("Prefer"));
    }

    [Fact]
    public async Task CreateAsync_WithoutRegarding_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var annotation = client.Annotation()
            .WithSubject("Test Subject");

        await Assert.ThrowsAsync<InvalidOperationException>(() => annotation.CreateAsync());
    }

    [Fact]
    public async Task CreateAsync_WithBasicAnnotation_SendsPostRequest()
    {
        var request = await SendCreateAsync(a => a.Regarding<TestAccount>(AccountId)
            .WithSubject("Test Subject")
            .WithNoteText("Test note text"));

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/data/v9.2/annotations", request.Uri.AbsolutePath);
        Assert.Equal("Test Subject", Property(request, "subject"));
        Assert.Equal("Test note text", Property(request, "notetext"));
        Assert.Null(Property(request, "documentbody"));
        Assert.Null(Property(request, "isdocument"));
        Assert.Null(request.Header("Prefer"));
    }

    [Fact]
    public async Task CreateAsync_WithFile_SendsBase64EncodedContent()
    {
        var fileContent = Encoding.UTF8.GetBytes("Test file content");

        var request = await SendCreateAsync(a => a.Regarding<TestAccount>(AccountId)
            .WithSubject("Document")
            .WithFile("test.txt", fileContent));

        Assert.Equal(Convert.ToBase64String(fileContent), Property(request, "documentbody"));
        Assert.Equal("test.txt", Property(request, "filename"));
        Assert.Equal("text/plain", Property(request, "mimetype")); // inferred from the extension
        Assert.True(request.Json.GetProperty("isdocument").GetBoolean());
    }

    [Fact]
    public async Task CreateAsync_WithReturnRepresentation_AddsPreferHeader()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var annotationId = Guid.NewGuid();

        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(
                $"{{\"annotationid\":\"{annotationId}\",\"subject\":\"Test\",\"notetext\":\"Note\"}}",
                Encoding.UTF8, "application/json")
        });

        var annotation = await client.Annotation()
            .Regarding<TestAccount>(AccountId)
            .WithSubject("Test")
            .WithNoteText("Note")
            .ReturnRepresentation()
            .CreateAsync();

        Assert.Equal("return=representation", Assert.Single(handler.Requests).Header("Prefer"));
        Assert.Equal(annotationId, annotation.AnnotationId);
        Assert.Equal("Test", annotation.Subject);
    }

    [Fact]
    public async Task UpdateAsync_WithoutAnnotationId_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var annotation = client.Annotation()
            .WithSubject("Updated Subject");

        await Assert.ThrowsAsync<InvalidOperationException>(() => annotation.UpdateAsync());
    }

    [Fact]
    public async Task UpdateAsync_WithAnnotationId_SendsPatchRequest()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var annotationId = Guid.NewGuid();
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Annotation()
            .WithId(annotationId)
            .WithSubject("Updated Subject")
            .WithNoteText("Updated text")
            .UpdateAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal($"/api/data/v9.2/annotations({annotationId})", request.Uri.AbsolutePath);
        Assert.Equal("*", request.Header("If-Match")); // update-only: never creates a note
        Assert.Equal("Updated Subject", Property(request, "subject"));
        Assert.Equal("Updated text", Property(request, "notetext"));
        Assert.Null(Property(request, "documentbody"));
    }

    [Fact]
    public async Task UpdateAsync_WithFile_UpdatesFileContent()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var annotationId = Guid.NewGuid();
        var fileContent = Encoding.UTF8.GetBytes("Updated file content");
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Annotation()
            .WithId(annotationId)
            .WithFile("updated.pdf", fileContent)
            .UpdateAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(Convert.ToBase64String(fileContent), Property(request, "documentbody"));
        Assert.Equal("updated.pdf", Property(request, "filename"));
        Assert.Equal("application/pdf", Property(request, "mimetype"));
        Assert.True(request.Json.GetProperty("isdocument").GetBoolean());
    }

    [Fact]
    public async Task DeleteAsync_WithAnnotationIdParameter_SendsDeleteRequest()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var annotationId = Guid.NewGuid();
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Annotation().DeleteAsync(annotationId);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"/api/data/v9.2/annotations({annotationId})", request.Uri.AbsolutePath);
        Assert.Null(request.Body);
    }

    [Fact]
    public async Task DeleteAsync_WithoutAnnotationId_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var annotation = client.Annotation();

        await Assert.ThrowsAsync<InvalidOperationException>(() => annotation.DeleteAsync());
    }

    [Fact]
    public async Task DeleteAsync_WithAnnotationIdSet_SendsDeleteRequest()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var annotationId = Guid.NewGuid();
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Annotation().WithId(annotationId).DeleteAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"/api/data/v9.2/annotations({annotationId})", request.Uri.AbsolutePath);
    }

    [Fact]
    public async Task RetrieveAsync_WithAnnotationIdParameter_ReturnsAnnotation()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var annotationId = Guid.NewGuid();

        handler.AddResponse(
            req => req.Method == HttpMethod.Get &&
                   req.RequestUri!.ToString().Contains($"/annotations({annotationId})"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"annotationid\":\"{annotationId}\",\"subject\":\"Test\",\"notetext\":\"Note\"}}", 
                    Encoding.UTF8, "application/json")
            }
        );

        var annotation = await client.Annotation().RetrieveAsync(annotationId);

        Assert.NotNull(annotation);
        Assert.Equal(annotationId, annotation.AnnotationId);
    }

    [Fact]
    public async Task RetrieveAsync_WithoutAnnotationId_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var annotation = client.Annotation();

        await Assert.ThrowsAsync<InvalidOperationException>(() => annotation.RetrieveAsync());
    }

    [Fact]
    public async Task RetrieveAsync_WithAnnotationIdSet_ReturnsAnnotation()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var annotationId = Guid.NewGuid();

        handler.AddResponse(
            req => req.Method == HttpMethod.Get &&
                   req.RequestUri!.ToString().Contains($"/annotations({annotationId})"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"annotationid\":\"{annotationId}\",\"subject\":\"Test\"}}", 
                    Encoding.UTF8, "application/json")
            }
        );

        var annotation = await client.Annotation()
            .WithId(annotationId)
            .RetrieveAsync();

        Assert.NotNull(annotation);
        Assert.Equal(annotationId, annotation.AnnotationId);
    }

    [Fact]
    public async Task DownloadFileAsync_WithAnnotationIdParameter_ReturnsFileContent()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var annotationId = Guid.NewGuid();
        var fileContent = Encoding.UTF8.GetBytes("Test file content");
        var base64Content = Convert.ToBase64String(fileContent);

        handler.AddResponse(
            req => req.Method == HttpMethod.Get &&
                   req.RequestUri!.ToString().Contains($"/annotations({annotationId})") &&
                   req.RequestUri.ToString().Contains("$select=filename,mimetype,documentbody"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"filename\":\"test.txt\",\"mimetype\":\"text/plain\",\"documentbody\":\"{base64Content}\"}}", 
                    Encoding.UTF8, "application/json")
            }
        );

        var (fileName, mimeType, content) = await client.Annotation().DownloadFileAsync(annotationId);

        Assert.Equal("test.txt", fileName);
        Assert.Equal("text/plain", mimeType);
        Assert.Equal(fileContent, content);
    }

    [Fact]
    public async Task DownloadFileAsync_WithoutAnnotationId_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var annotation = client.Annotation();

        await Assert.ThrowsAsync<InvalidOperationException>(() => annotation.DownloadFileAsync());
    }

    [Fact]
    public async Task DownloadFileAsync_WithAnnotationIdSet_ReturnsFileContent()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var annotationId = Guid.NewGuid();
        var fileContent = Encoding.UTF8.GetBytes("Test content");
        var base64Content = Convert.ToBase64String(fileContent);

        handler.AddResponse(
            req => req.Method == HttpMethod.Get &&
                   req.RequestUri!.ToString().Contains($"/annotations({annotationId})"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"filename\":\"doc.pdf\",\"mimetype\":\"application/pdf\",\"documentbody\":\"{base64Content}\"}}", 
                    Encoding.UTF8, "application/json")
            }
        );

        var (fileName, mimeType, content) = await client.Annotation()
            .WithId(annotationId)
            .DownloadFileAsync();

        Assert.Equal("doc.pdf", fileName);
        Assert.Equal("application/pdf", mimeType);
        Assert.Equal(fileContent, content);
    }

    [Fact]
    public async Task CreateAsync_WithAllFields_SendsCompletePayload()
    {
        var fileContent = Encoding.UTF8.GetBytes("Test content");

        var request = await SendCreateAsync(a => a
            .Regarding<TestAccount>(AccountId)
            .WithSubject("Contract Document")
            .WithNoteText("Signed contract attached")
            .WithFile("contract.pdf", fileContent, "application/pdf")
            .ReturnRepresentation());

        Assert.Equal(
            ["documentbody", "filename", "isdocument", "mimetype", "notetext", "objectid_testaccount@odata.bind", "subject"],
            request.Json.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("return=representation", request.Header("Prefer"));
    }

    [Fact]
    public async Task CreateAsync_WithAuthenticationToken_IncludesAuthorizationHeader()
    {
        var request = await SendCreateAsync(a => a.Regarding<TestAccount>(AccountId).WithSubject("Test"));

        Assert.Equal("Bearer test-token", request.Header("Authorization"));
    }

    [Fact]
    public async Task CreateAsync_SendsRequestToAnnotationsEndpoint()
    {
        var request = await SendCreateAsync(a => a.Regarding<TestAccount>(AccountId).WithSubject("Test"));

        Assert.Equal("https://test.crm.dynamics.com/api/data/v9.2/annotations", request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task CreateAsync_WithDataverseException_PropagatesError()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddResponse(
            req => true,
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":{\"code\":\"0x80040217\",\"message\":\"Test error\"}}", Encoding.UTF8, "application/json")
            }
        );

        await Assert.ThrowsAsync<DataverseException>(
            () => client.Annotation()
                .Regarding<TestAccount>(accountId)
                .WithSubject("Test")
                .CreateAsync());
    }

    [Fact]
    public void Client_Annotation_CreatesNewAnnotationBuilder()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var annotation1 = client.Annotation();
        var annotation2 = client.Annotation();

        Assert.NotNull(annotation1);
        Assert.NotNull(annotation2);
        Assert.NotSame(annotation1, annotation2);
    }

    [Fact]
    public async Task Client_Annotation_WithAnnotationId_UsesProvidedId()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var annotationId = Guid.NewGuid();
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Annotation(annotationId).DeleteAsync();

        Assert.Equal($"/api/data/v9.2/annotations({annotationId})", Assert.Single(handler.Requests).Uri.AbsolutePath);
    }

    #region Alternate Key Tests

    [Fact]
    public async Task Regarding_WithAlternateKeyString_BindsByAlternateKey()
    {
        var request = await SendCreateAsync(a => a.Regarding("account", "accountnumber", "ACC-001").WithSubject("s"));

        Assert.Equal("/accounts(accountnumber='ACC-001')", Property(request, "objectid_account@odata.bind"));
    }

    [Fact]
    public async Task Regarding_WithAlternateKey_ClearsEntityId()
    {
        // First set using ID, then switch to alternate key
        var request = await SendCreateAsync(a => a
            .Regarding<TestAccount>(AccountId)
            .Regarding<TestAccount, string>(x => x.AccountNumber!, "ACC-001"));

        Assert.Equal("/testaccounts(accountnumber='ACC-001')", Property(request, "objectid_testaccount@odata.bind"));
    }

    [Fact]
    public async Task Regarding_WithEntityId_ClearsAlternateKey()
    {
        // First set using alternate key, then switch to ID
        var request = await SendCreateAsync(a => a
            .Regarding<TestAccount, string>(x => x.AccountNumber!, "ACC-001")
            .Regarding<TestAccount>(AccountId));

        Assert.Equal($"/testaccounts({AccountId})", Property(request, "objectid_testaccount@odata.bind"));
    }

    [Fact]
    public void WithAlternateKey_WithoutPriorRegardingAlternateKey_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var annotation = client.Annotation();

        Assert.Throws<InvalidOperationException>(() => 
            annotation.WithAlternateKey("somekey", "value"));
    }

    [Fact]
    public void WithAlternateKey_TypedExpression_WithoutPriorRegarding_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var annotation = client.Annotation();

        Assert.Throws<InvalidOperationException>(() =>
            annotation.WithAlternateKey<TestAccount, string>(a => a.AccountNumber, "value"));
    }

    [Fact]
    public async Task WithAlternateKey_TypedExpression_WithPriorRegarding_AddsKeyPart()
    {
        var request = await SendCreateAsync(a => a
            .Regarding<TestAccount, string>(x => x.AccountNumber!, "ACC-001")
            .WithAlternateKey<TestAccount, string>(x => x.Name, "Test"));

        Assert.Equal("/testaccounts(accountnumber='ACC-001',name='Test')", Property(request, "objectid_testaccount@odata.bind"));
    }

    [Fact]
    public void WithAlternateKey_TypedExpression_WithNullExpression_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var annotation = client.Annotation()
            .Regarding<TestAccount, string>(a => a.AccountNumber, "ACC-001");

        Assert.Throws<ArgumentNullException>(() =>
            annotation.WithAlternateKey<TestAccount, string>(null!, "value"));
    }

    [Fact]
    public void Regarding_TypedExpression_WithNullExpression_ThrowsArgumentNullException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var annotation = client.Annotation();

        Assert.Throws<ArgumentNullException>(() =>
            annotation.Regarding<TestAccount, string>(null!, "value"));
    }

    [Fact]
    public async Task CreateAsync_WithAlternateKey_SendsCorrectODataBind()
    {
        var request = await SendCreateAsync(a => a
            .Regarding<TestAccount, string>(x => x.AccountNumber!, "ACC-001")
            .WithSubject("Test with Alternate Key"));

        Assert.Equal("/testaccounts(accountnumber='ACC-001')", Property(request, "objectid_testaccount@odata.bind"));
        Assert.Equal("Test with Alternate Key", Property(request, "subject"));
    }

    [Fact]
    public async Task CreateAsync_WithCompositeAlternateKey_SendsCorrectODataBind()
    {
        var request = await SendCreateAsync(a => a
            .Regarding<TestAccount, string>(x => x.AccountNumber!, "ACC-001")
            .WithAlternateKey("customfield", "value")
            .WithSubject("Test with Composite Key"));

        Assert.Equal("/testaccounts(accountnumber='ACC-001',customfield='value')", Property(request, "objectid_testaccount@odata.bind"));
    }

    [Fact]
    public async Task CreateAsync_WithAlternateKeyAndFile_SendsCorrectRequest()
    {
        var fileContent = Encoding.UTF8.GetBytes("Test file");

        var request = await SendCreateAsync(a => a
            .Regarding<TestAccount, string>(x => x.AccountNumber!, "ACC-001")
            .WithSubject("Document with Alternate Key")
            .WithFile("test.txt", fileContent));

        Assert.Equal("/testaccounts(accountnumber='ACC-001')", Property(request, "objectid_testaccount@odata.bind"));
        Assert.Equal(Convert.ToBase64String(fileContent), Property(request, "documentbody"));
    }

    [Fact]
    public async Task CreateAsync_WithoutRegardingObjectId_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        var annotation = client.Annotation()
            .WithSubject("Test");

        await Assert.ThrowsAsync<InvalidOperationException>(() => annotation.CreateAsync());
    }

    #endregion

    [Theory]
    [InlineData("report.pdf", "application/pdf")]
    [InlineData("letter.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("budget.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("deck.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("legacy.doc", "application/msword")]
    [InlineData("data.csv", "text/csv")]
    [InlineData("page.htm", "text/html")]
    [InlineData("photo.JPG", "image/jpeg")]          // extension match is case-insensitive
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("logo.svg", "image/svg+xml")]
    [InlineData("bundle.zip", "application/zip")]
    [InlineData("payload.json", "application/json")]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("archive.tar.gz", "application/octet-stream")] // unknown extension
    [InlineData("README", "application/octet-stream")]         // no extension
    public async Task WithFile_InfersTheMimeTypeFromTheExtension(string fileName, string expected)
    {
        var request = await SendCreateAsync(a => a.Regarding<TestAccount>(AccountId).WithFile(fileName, [1, 2, 3]));

        Assert.Equal(expected, Property(request, "mimetype"));
    }

    #region Regarding Entity Set Tests

    [Theory]
    [InlineData("account", "accounts")]
    [InlineData("opportunity", "opportunities")]     // consonant + y
    [InlineData("msdyn_journey", "msdyn_journeys")]  // vowel + y
    [InlineData("customeraddress", "customeraddresses")]
    [InlineData("fax", "faxes")]
    [InlineData("new_match", "new_matches")]
    [InlineData("new_wish", "new_wishes")]
    public async Task Regarding_WithEntityType_PluralizesTheEntitySetName(string entityType, string expectedEntitySet)
    {
        var request = await SendCreateAsync(a => a.Regarding(entityType, AccountId).WithSubject("s"));

        Assert.Equal($"/{expectedEntitySet}({AccountId})", Property(request, $"objectid_{entityType}@odata.bind"));
    }

    [Fact]
    public async Task Regarding_WithAlternateKey_PluralizesTheEntitySetName()
    {
        var request = await SendCreateAsync(a => a.Regarding("opportunity", "name", "BigDeal").WithSubject("s"));

        Assert.Equal("/opportunities(name='BigDeal')", Property(request, "objectid_opportunity@odata.bind"));
    }

    [Fact]
    public async Task Regarding_WithExplicitEntitySet_UsesIt()
    {
        var request = await SendCreateAsync(a => a.Regarding("new_widget", AccountId, entitySet: "new_widgetcollection").WithSubject("s"));

        Assert.Equal($"/new_widgetcollection({AccountId})", Property(request, "objectid_new_widget@odata.bind"));
    }

    [Fact]
    public async Task Regarding_WithAlternateKeyAndExplicitEntitySet_UsesIt()
    {
        var request = await SendCreateAsync(a => a.Regarding("new_widget", "new_code", "W-1", entitySet: "new_widgetcollection").WithSubject("s"));

        Assert.Equal("/new_widgetcollection(new_code='W-1')", Property(request, "objectid_new_widget@odata.bind"));
    }

    [Fact]
    public async Task Regarding_WithGenericType_UsesTheEntitySetAttribute()
    {
        var request = await SendCreateAsync(a => a.Regarding<Opportunity>(AccountId).WithSubject("s"));

        Assert.Equal($"/opportunities({AccountId})", Property(request, "objectid_opportunity@odata.bind"));
    }

    [Fact]
    public async Task Regarding_WithGenericTypeAndAlternateKey_UsesTheEntitySetAttribute()
    {
        var request = await SendCreateAsync(a => a.Regarding<Opportunity, string>(o => o.Name, "BigDeal").WithSubject("s"));

        Assert.Equal("/opportunities(name='BigDeal')", Property(request, "objectid_opportunity@odata.bind"));
    }

    [Fact]
    public void Regarding_WithGenericTypeWithoutAnEntitySet_Throws()
    {
        var handler = new TestHttpMessageHandler();
        var annotation = CreateTestClient(handler).Annotation();

        var ex = Assert.Throws<InvalidOperationException>(() => annotation.Regarding<Widget>(AccountId));
        Assert.Contains("EntitySet", ex.Message);
        Assert.Throws<InvalidOperationException>(() => annotation.Regarding<Widget, string>(w => w.Code, "W-1"));
    }

    [EntitySet("opportunities")]
    private class Opportunity
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    private class Widget
    {
        [System.Text.Json.Serialization.JsonPropertyName("new_code")]
        public string Code { get; set; } = "";
    }

    #endregion

    [Fact]
    public async Task CreateAsync_WithoutARepresentation_ReadsTheIdFromTheHeader()
    {
        // As Dataverse answers: 204 with no body, the new record's URL in OData-EntityId. The ID was Guid.Empty until 1.0.2.
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var noteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        response.Headers.Add("OData-EntityId", $"https://test.crm.dynamics.com/api/data/v9.2/annotations({noteId})");
        handler.AddResponse(_ => true, response);

        var note = await client.Annotation().Regarding("account", AccountId).WithSubject("s").CreateAsync();

        Assert.Equal(noteId, note.AnnotationId);
    }
}
