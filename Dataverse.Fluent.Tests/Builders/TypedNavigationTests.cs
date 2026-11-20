using System.Net;
using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Tests.Builders;

/// <summary>
/// <c>Expand</c>, <c>WithRelated</c> and <c>Via</c> taking the relationship from a navigation property of the model.
/// </summary>
public class TypedNavigationTests
{
    [DataverseEntity("account", "accounts")]
    private class NavAccount
    {
        [JsonPropertyName("accountid")]
        public Guid AccountId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [DataverseLookup("primarycontactid", "contacts")]
        public Guid? PrimaryContactId { get; set; }

        [JsonPropertyName("primarycontactid")]
        public NavContact? PrimaryContact { get; set; }

        [JsonPropertyName("contact_customer_accounts")]
        public List<NavContact>? Contacts { get; set; }

        [JsonPropertyName("account_tasks")]
        public NavContact[]? TaskOwners { get; set; }

        [JsonIgnore]
        public NavContact? Cached { get; set; }
    }

    [DataverseEntity("contact", "contacts")]
    private class NavContact
    {
        [JsonPropertyName("contactid")]
        public Guid ContactId { get; set; }

        [JsonPropertyName("fullname")]
        public string? FullName { get; set; }

        [JsonPropertyName("lastname")]
        public string? LastName { get; set; }
    }

    private class DerivedAccount : NavAccount
    {
    }

    // The handler answers requests in order: one empty collection per read, or one 204 per write.
    private static (DataverseClient Client, TestHttpMessageHandler Handler) Create(int writes = 0)
    {
        var handler = new TestHttpMessageHandler();
        if (writes == 0)
            handler.AddJsonResponse(req => req.Method == HttpMethod.Get, new { value = Array.Empty<object>() });
        for (var i = 0; i < writes; i++)
            handler.AddResponse(req => req.Method != HttpMethod.Get, new HttpResponseMessage(HttpStatusCode.NoContent));

        var client = new DataverseClient("https://test.crm.dynamics.com/api/data/v9.2", () => Task.FromResult("test-token"), new HttpClient(handler));
        return (client, handler);
    }

    private static async Task<string?> ExpandOf(Func<DataverseClient, Task> send)
    {
        var (client, handler) = Create();
        await send(client);
        return Assert.Single(handler.Requests).QueryOption("$expand");
    }

    // ───────────────────────── Expand on a query ─────────────────────────

    [Fact]
    public async Task Query_Expand_Collection_UsesTheMappedName()
    {
        var expand = await ExpandOf(c => c.Query<NavAccount>().Expand(a => a.Contacts).GetListAsync());

        Assert.Equal("contact_customer_accounts", expand);
    }

    [Fact]
    public async Task Query_Expand_SingleValued_UsesTheMappedName()
    {
        var expand = await ExpandOf(c => c.Query<NavAccount>().Expand(a => a.PrimaryContact).GetListAsync());

        Assert.Equal("primarycontactid", expand);
    }

    [Fact]
    public async Task Query_Expand_Collection_ConfiguresTheRelatedType()
    {
        var expand = await ExpandOf(c => c.Query<NavAccount>()
            .Expand(a => a.Contacts, e => e.Select(x => new { x.FullName, x.LastName }).Where(x => x.LastName == "Smith").Top(5))
            .GetListAsync());

        Assert.Equal("contact_customer_accounts($select=fullname,lastname;$filter=lastname eq 'Smith';$top=5)", expand);
    }

    [Fact]
    public async Task Query_Expand_SingleValued_ConfiguresTheRelatedType()
    {
        var expand = await ExpandOf(c => c.Query<NavAccount>()
            .Expand(a => a.PrimaryContact, e => e.Select(x => x.FullName))
            .GetListAsync());

        Assert.Equal("primarycontactid($select=fullname)", expand);
    }

    [Fact]
    public async Task Query_Expand_Collection_WithOptionsThatDoNotNameTheRelatedType()
    {
        var expand = await ExpandOf(c => c.Query<NavAccount>().Expand(a => a.Contacts, e => e.Top(3)).GetListAsync());

        Assert.Equal("contact_customer_accounts($top=3)", expand);
    }

    [Fact]
    public async Task Query_Expand_Array_ConfiguresTheElementType()
    {
        var expand = await ExpandOf(c => c.Query<NavAccount>()
            .Expand(a => a.TaskOwners, e => e.Select(x => x.FullName))
            .GetListAsync());

        Assert.Equal("account_tasks($select=fullname)", expand);
    }

    [Fact]
    public async Task Query_Expand_MatchesTheStringOverload()
    {
        var typed = await ExpandOf(c => c.Query<NavAccount>()
            .Expand(a => a.Contacts, e => e.Select(x => x.FullName).OrderBy(x => x.LastName))
            .GetListAsync());
        var named = await ExpandOf(c => c.Query<NavAccount>()
            .Expand<NavContact>("contact_customer_accounts", e => e.Select(x => x.FullName).OrderBy(x => x.LastName))
            .GetListAsync());

        Assert.Equal(named, typed);
    }

    [Fact]
    public async Task Query_Expand_OnADerivedModel_UsesTheInheritedProperty()
    {
        var expand = await ExpandOf(c => c.Query<DerivedAccount>().Expand(a => a.Contacts, e => e.Select(x => x.FullName)).GetListAsync());

        Assert.Equal("contact_customer_accounts($select=fullname)", expand);
    }

    [Fact]
    public void Query_Expand_RejectsPropertiesThatAreNotNavigationProperties()
    {
        var (client, _) = Create();

        Assert.Throws<ArgumentException>(() => client.Query<NavAccount>().Expand(a => a.Name));
        Assert.Throws<ArgumentException>(() => client.Query<NavAccount>().Expand(a => a.PrimaryContactId));
        Assert.Throws<ArgumentException>(() => client.Query<NavAccount>().Expand(a => a.Cached));
        Assert.Throws<ArgumentException>(() => client.Query<NavAccount>().Expand(a => a.PrimaryContact!.FullName));
    }

    // ───────────────────────── Expand on a single record ─────────────────────────

    [Fact]
    public async Task Entity_Expand_UsesTheMappedName()
    {
        var id = Guid.NewGuid();

        var fromEntity = await ExpandOf(c => c.Entity<NavAccount>(id).Expand(a => a.Contacts, e => e.Select(x => x.FullName)).RetrieveAsync());
        var chained = await ExpandOf(c => c.Entity<NavAccount>(id).Select(a => a.Name).Expand(a => a.PrimaryContact).Expand(a => a.Contacts).RetrieveAsync());

        Assert.Equal("contact_customer_accounts($select=fullname)", fromEntity);
        Assert.Equal("primarycontactid,contact_customer_accounts", chained);
    }

    // ───────────────────────── Deep insert ─────────────────────────

    [Fact]
    public async Task DeepInsert_WithRelated_Collection_UsesTheMappedName()
    {
        var (client, handler) = Create(writes: 1);
        var contacts = new List<NavContact> { new() { LastName = "One" }, new() { LastName = "Two" } };

        await client.DeepInsert(new NavAccount { Name = "Contoso" }).WithRelated(a => a.Contacts, contacts).ExecuteAsync();

        var related = Assert.Single(handler.Requests).Json.GetProperty("contact_customer_accounts");
        Assert.Equal(2, related.GetArrayLength());
        Assert.Equal("One", related[0].GetProperty("lastname").GetString());
    }

    [Fact]
    public async Task DeepInsert_WithRelated_ArrayForAListProperty_UsesTheMappedName()
    {
        var (client, handler) = Create(writes: 1);

        await client.DeepInsert(new NavAccount { Name = "Contoso" })
            .WithRelated(a => a.Contacts, new[] { new NavContact { LastName = "One" } })
            .ExecuteAsync();

        Assert.Equal(1, Assert.Single(handler.Requests).Json.GetProperty("contact_customer_accounts").GetArrayLength());
    }

    [Fact]
    public async Task DeepInsert_WithRelated_SingleValued_UsesTheMappedName()
    {
        var (client, handler) = Create(writes: 1);

        await client.DeepInsert(new NavAccount { Name = "Contoso" })
            .WithRelated(a => a.PrimaryContact, new NavContact { LastName = "Primary" })
            .ExecuteAsync();

        Assert.Equal("Primary", Assert.Single(handler.Requests).Json.GetProperty("primarycontactid").GetProperty("lastname").GetString());
    }

    // ───────────────────────── Associate ─────────────────────────

    [Fact]
    public async Task Associate_Via_Collection_MatchesViaByName()
    {
        var accountId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var (client, handler) = Create(writes: 2);

        await client.Associate().From<NavAccount>(accountId).Via<NavAccount>(a => a.Contacts).To<NavContact>(contactId).AssociateAsync();
        await client.Associate().From<NavAccount>(accountId).Via("contact_customer_accounts").To<NavContact>(contactId).AssociateAsync();

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[1].Method, handler.Requests[0].Method);
        Assert.Equal(handler.Requests[1].DecodedUrl, handler.Requests[0].DecodedUrl);
        Assert.Contains("contact_customer_accounts/$ref", handler.Requests[0].DecodedUrl);
    }

    [Fact]
    public async Task Associate_Via_SingleValued_MatchesViaSingleValuedByName()
    {
        var accountId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var (client, handler) = Create(writes: 2);

        await client.Associate().From<NavAccount>(accountId).Via<NavAccount>(a => a.PrimaryContact).To<NavContact>(contactId).AssociateAsync();
        await client.Associate().From<NavAccount>(accountId).ViaSingleValued("primarycontactid").To<NavContact>(contactId).AssociateAsync();

        Assert.Equal(handler.Requests[1].Method, handler.Requests[0].Method);
        Assert.Equal(handler.Requests[1].DecodedUrl, handler.Requests[0].DecodedUrl);
    }

    [Fact]
    public void Associate_Via_RejectsAColumn()
    {
        var (client, _) = Create();

        Assert.Throws<ArgumentException>(() => client.Associate().Via<NavAccount>(a => a.Name));
    }
}
