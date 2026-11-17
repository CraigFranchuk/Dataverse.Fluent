using System.Net;

namespace Dataverse.Fluent.Tests.Builders;

public class AssociationBuilderTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";

    private DataverseClient CreateTestClient(TestHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new DataverseClient(BaseUrl, () => Task.FromResult("test-token"), httpClient);
    }

    // ───────────────────────── Associate with primary keys ─────────────────────────

    [Fact]
    public async Task AssociateAsync_WithPrimaryKeys_PostsCorrectRefUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        handler.AddResponse(
            req =>
            {
                var url = req.RequestUri!.ToString();
                return req.Method == HttpMethod.Post &&
                       url.Contains($"testaccounts({accountId})/contact_customer_accounts/$ref");
            },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Associate()
            .From<TestAccount>(accountId)
            .Via("contact_customer_accounts")
            .To<TestContact>(contactId)
            .AssociateAsync();
    }

    [Fact]
    public async Task AssociateAsync_WithPrimaryKeys_SendsCorrectODataIdPayload()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        string? capturedBody = null;
        handler.AddResponse(
            req =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().Result;
                return req.Method == HttpMethod.Post &&
                       req.RequestUri!.ToString().Contains("/$ref");
            },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Associate()
            .From<TestAccount>(accountId)
            .Via("contact_customer_accounts")
            .To<TestContact>(contactId)
            .AssociateAsync();

        Assert.NotNull(capturedBody);
        Assert.Contains($"testcontacts({contactId})", capturedBody);
        Assert.Contains("@odata.id", capturedBody);
        Assert.Contains(BaseUrl, capturedBody);
    }

    [Fact]
    public async Task AssociateAsync_WithExplicitEntitySets_PostsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        handler.AddResponse(
            req =>
            {
                var url = req.RequestUri!.ToString();
                return req.Method == HttpMethod.Post &&
                       url.Contains($"accounts({accountId})/contact_customer_accounts/$ref");
            },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Associate()
            .From("accounts", accountId)
            .Via("contact_customer_accounts")
            .To("contacts", contactId)
            .AssociateAsync();
    }

    // ───────────────────────── Associate with alternate keys ─────────────────────────

    [Fact]
    public async Task AssociateAsync_WithSourceAlternateKey_PostsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var contactId = Guid.NewGuid();

        handler.AddResponse(
            req =>
            {
                var url = req.RequestUri!.ToString();
                return req.Method == HttpMethod.Post &&
                       url.Contains("testaccounts(accountnumber='ACC-001')/contact_customer_accounts/$ref");
            },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Associate()
            .From<TestAccount>("accountnumber", "ACC-001")
            .Via("contact_customer_accounts")
            .To<TestContact>(contactId)
            .AssociateAsync();
    }

    [Fact]
    public async Task AssociateAsync_WithTargetAlternateKey_SendsCorrectODataIdPayload()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        string? capturedBody = null;
        handler.AddResponse(
            req =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().Result;
                return req.Method == HttpMethod.Post &&
                       req.RequestUri!.ToString().Contains("/$ref");
            },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Associate()
            .From<TestAccount>(accountId)
            .Via("contact_customer_accounts")
            .To<TestContact>("emailaddress1", "alice@example.com")
            .AssociateAsync();

        Assert.NotNull(capturedBody);
        Assert.Contains("testcontacts(emailaddress1='alice@example.com')", capturedBody);
    }

    [Fact]
    public async Task AssociateAsync_WithStronglyTypedAlternateKey_PostsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var contactId = Guid.NewGuid();

        handler.AddResponse(
            req =>
            {
                var url = req.RequestUri!.ToString();
                return req.Method == HttpMethod.Post &&
                       url.Contains("testaccounts(accountnumber='ACC-002')");
            },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Associate()
            .From<TestAccount, string?>(a => a.AccountNumber, "ACC-002")
            .Via("contact_customer_accounts")
            .To<TestContact>(contactId)
            .AssociateAsync();
    }

    [Fact]
    public async Task AssociateAsync_WithCompositeAlternateKey_PostsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var contactId = Guid.NewGuid();

        handler.AddResponse(
            req =>
            {
                var url = req.RequestUri!.ToString();
                return req.Method == HttpMethod.Post &&
                       url.Contains("testaccounts(accountnumber='ACC-003',name='Contoso')");
            },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Associate()
            .From<TestAccount>(new Dictionary<string, object>
            {
                { "accountnumber", "ACC-003" },
                { "name", "Contoso" }
            })
            .Via("contact_customer_accounts")
            .To<TestContact>(contactId)
            .AssociateAsync();
    }

    [Fact]
    public async Task AssociateAsync_WithExplicitEntitySetAndAlternateKey_PostsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var contactId = Guid.NewGuid();

        handler.AddResponse(
            req =>
            {
                var url = req.RequestUri!.ToString();
                return req.Method == HttpMethod.Post &&
                       url.Contains("accounts(accountnumber='ACC-004')/contact_customer_accounts/$ref");
            },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Associate()
            .From("accounts", "accountnumber", "ACC-004")
            .Via("contact_customer_accounts")
            .To("contacts", contactId)
            .AssociateAsync();
    }

    [Fact]
    public async Task AssociateAsync_WithExplicitEntitySetAndCompositeKey_PostsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var contactId = Guid.NewGuid();

        handler.AddResponse(
            req =>
            {
                var url = req.RequestUri!.ToString();
                return req.Method == HttpMethod.Post &&
                       url.Contains("accounts(accountnumber='ACC-005',name='Fabrikam')");
            },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Associate()
            .From("accounts", new Dictionary<string, object>
            {
                { "accountnumber", "ACC-005" },
                { "name", "Fabrikam" }
            })
            .Via("contact_customer_accounts")
            .To("contacts", contactId)
            .AssociateAsync();
    }

    [Fact]
    public async Task AssociateAsync_WithBothAlternateKeys_PostsCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        string? capturedBody = null;
        handler.AddResponse(
            req =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().Result;
                var url = req.RequestUri!.ToString();
                return req.Method == HttpMethod.Post &&
                       url.Contains("testaccounts(accountnumber='ACC-006')");
            },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Associate()
            .From<TestAccount>("accountnumber", "ACC-006")
            .Via("contact_customer_accounts")
            .To<TestContact>("emailaddress1", "bob@example.com")
            .AssociateAsync();

        Assert.NotNull(capturedBody);
        Assert.Contains("testcontacts(emailaddress1='bob@example.com')", capturedBody);
    }

    // ───────────────────────── Disassociate collection-valued ─────────────────────────

    [Fact]
    public async Task DisassociateAsync_CollectionValued_DeletesCorrectRefUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        handler.AddResponse(
            req =>
            {
                var url = req.RequestUri!.ToString();
                return req.Method == HttpMethod.Delete &&
                       url.Contains($"testaccounts({accountId})/contact_customer_accounts({contactId})/$ref");
            },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Associate()
            .From<TestAccount>(accountId)
            .Via("contact_customer_accounts")
            .To<TestContact>(contactId)
            .DisassociateAsync();
    }

    [Fact]
    public async Task DisassociateAsync_CollectionValuedWithAlternateKeys_DeletesCorrectUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var accountId = Guid.NewGuid();

        handler.AddResponse(
            req =>
            {
                var url = req.RequestUri!.ToString();
                return req.Method == HttpMethod.Delete &&
                       url.Contains($"testaccounts({accountId})/contact_customer_accounts(emailaddress1='test@example.com')/$ref");
            },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Associate()
            .From<TestAccount>(accountId)
            .Via("contact_customer_accounts")
            .To<TestContact>("emailaddress1", "test@example.com")
            .DisassociateAsync();
    }

    // ───────────────────────── Disassociate single-valued ─────────────────────────

    [Fact]
    public async Task DisassociateAsync_SingleValued_DeletesCorrectRefUrl()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        var contactId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        handler.AddResponse(
            req =>
            {
                var url = req.RequestUri!.ToString();
                return req.Method == HttpMethod.Delete &&
                       url.Contains($"testcontacts({contactId})/parentcustomerid_account/$ref") &&
                       !url.Contains($"({accountId})/$ref");
            },
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.Associate()
            .From<TestContact>(contactId)
            .ViaSingleValued("parentcustomerid_account")
            .To<TestAccount>(accountId)
            .DisassociateAsync();
    }

    // ───────────────────────── Validation ─────────────────────────

    [Fact]
    public async Task AssociateAsync_WithoutSource_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.Associate()
                .Via("contact_customer_accounts")
                .To<TestContact>(Guid.NewGuid())
                .AssociateAsync());
    }

    [Fact]
    public async Task AssociateAsync_WithoutNavigation_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.Associate()
                .From<TestAccount>(Guid.NewGuid())
                .To<TestContact>(Guid.NewGuid())
                .AssociateAsync());
    }

    [Fact]
    public async Task AssociateAsync_WithoutTarget_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.Associate()
                .From<TestAccount>(Guid.NewGuid())
                .Via("contact_customer_accounts")
                .AssociateAsync());
    }

    [Fact]
    public void Via_WithNullNavigationProperty_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        Assert.Throws<ArgumentException>(() =>
            client.Associate().Via(null!));
    }

    [Fact]
    public void Via_WithEmptyNavigationProperty_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        Assert.Throws<ArgumentException>(() =>
            client.Associate().Via(""));
    }

    [Fact]
    public void ViaSingleValued_WithNullNavigationProperty_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        Assert.Throws<ArgumentException>(() =>
            client.Associate().ViaSingleValued(null!));
    }

    [Fact]
    public void From_WithEmptyEntitySet_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        Assert.Throws<ArgumentException>(() =>
            client.Associate().From("", Guid.NewGuid()));
    }

    [Fact]
    public void To_WithEmptyEntitySet_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        Assert.Throws<ArgumentException>(() =>
            client.Associate().To("", Guid.NewGuid()));
    }

    [Fact]
    public void From_WithEmptyAlternateKeyDict_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        Assert.Throws<ArgumentException>(() =>
            client.Associate().From<TestAccount>(new Dictionary<string, object>()));
    }

    [Fact]
    public void To_WithNullAlternateKeyDict_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        Assert.Throws<ArgumentException>(() =>
            client.Associate().To<TestContact>((Dictionary<string, object>)null!));
    }

    [Fact]
    public async Task DisassociateAsync_WithoutSource_ThrowsInvalidOperationException()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.Associate()
                .Via("contact_customer_accounts")
                .To<TestContact>(Guid.NewGuid())
                .DisassociateAsync());
    }

    // ───────────────────────── Fluent chaining ─────────────────────────

    [Fact]
    public async Task Associate_FluentChaining_AllMethodsReturnSameBuilder()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);

        handler.AddResponse(
            req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains("/$ref"),
            new HttpResponseMessage(HttpStatusCode.NoContent));

        // Verify fluent chaining compiles and executes without error
        await client.Associate()
            .From<TestAccount>(Guid.NewGuid())
            .Via("contact_customer_accounts")
            .To<TestContact>(Guid.NewGuid())
            .AssociateAsync();
    }

    // ───────────────────────── Target addressed by alternate key ─────────────────────────

    [Theory]
    [InlineData("typed", "testcontacts(emailaddress1='a@example.com')")]
    [InlineData("typed composite", "testcontacts(emailaddress1='a@example.com',lastname='O''Neil')")]
    [InlineData("entity set", "contacts(emailaddress1='a@example.com')")]
    [InlineData("entity set composite", "contacts(emailaddress1='a@example.com',lastname='O''Neil')")]
    public async Task AssociateAsync_TargetByAlternateKey_SendsODataIdWithTheKey(string overload, string expectedTarget)
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateTestClient(handler);
        handler.AddResponse(_ => true, new HttpResponseMessage(HttpStatusCode.NoContent));
        var keys = new Dictionary<string, object> { ["emailaddress1"] = "a@example.com", ["lastname"] = "O'Neil" };
        var accountId = Guid.NewGuid();

        var builder = client.Associate().From<TestAccount>(accountId).Via("contact_customer_accounts");
        builder = overload switch
        {
            "typed" => builder.To<TestContact, string?>(c => c.EmailAddress, "a@example.com"),
            "typed composite" => builder.To<TestContact>(keys),
            "entity set" => builder.To("contacts", "emailaddress1", "a@example.com"),
            "entity set composite" => builder.To("contacts", keys),
            _ => throw new ArgumentOutOfRangeException(nameof(overload))
        };
        await builder.AssociateAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"/api/data/v9.2/testaccounts({accountId})/contact_customer_accounts/$ref", request.Uri.AbsolutePath);
        Assert.Equal($"{BaseUrl}/{expectedTarget}", request.Json.GetProperty("@odata.id").GetString());
    }

    [Fact]
    public void To_WithInvalidAlternateKeyArguments_Throws()
    {
        var client = CreateTestClient(new TestHttpMessageHandler());
        var builder = client.Associate().From<TestAccount>(Guid.NewGuid()).Via("nav");

        Assert.Throws<ArgumentNullException>(() => builder.To<TestContact, string?>(null!, "x"));
        Assert.Throws<ArgumentException>(() => builder.To<TestContact>(new Dictionary<string, object>()));
        Assert.Throws<ArgumentException>(() => builder.To("contacts", new Dictionary<string, object>()));
        Assert.ThrowsAny<ArgumentException>(() => builder.To(" ", "emailaddress1", "x"));
        Assert.ThrowsAny<ArgumentException>(() => builder.To(" ", new Dictionary<string, object> { ["k"] = 1 }));
    }
}
