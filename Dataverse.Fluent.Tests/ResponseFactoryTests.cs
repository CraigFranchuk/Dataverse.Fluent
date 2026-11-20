using System.Net;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Tests;

/// <summary>
/// <see cref="DataverseResponse.FromEntity{T}"/> and <see cref="DataverseResponse.FromEntities{T}"/> write records in
/// the shape Dataverse returns, so the builders read them back unchanged.
/// </summary>
public class ResponseFactoryTests
{
    private static readonly Guid ContactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task FromEntity_WritesTheNamesAReadExpects()
    {
        using var response = DataverseResponse.FromEntity(SampleContact());
        var json = await response.ReadJsonObjectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ContactId, json["contactid"]!.GetValue<Guid>());
        Assert.Equal("Smith", (string?)json["lastname"]);
        Assert.Equal(42, (int?)json["numberofchildren"]);
        Assert.Equal(UserId, json["_preferredsystemuserid_value"]!.GetValue<Guid>());
        Assert.Equal(AccountId, json["_parentcustomerid_value"]!.GetValue<Guid>());
        Assert.Equal("account", (string?)json["_parentcustomerid_value@Microsoft.Dynamics.CRM.lookuplogicalname"]);
        Assert.False(json.ContainsKey("ParentCustomerId"));
    }

    [Fact]
    public async Task FromEntity_AnUnsetReference_IsWrittenAsANullIdWithoutTheAnnotation()
    {
        using var response = DataverseResponse.FromEntity(new Contact { ContactId = ContactId });
        var json = await response.ReadJsonObjectAsync(TestContext.Current.CancellationToken);

        Assert.True(json.ContainsKey("_parentcustomerid_value"));
        Assert.Null(json["_parentcustomerid_value"]);
        Assert.False(json.ContainsKey("_parentcustomerid_value@Microsoft.Dynamics.CRM.lookuplogicalname"));
    }

    [Fact]
    public async Task FromEntity_RoundTripsThroughARetrieve()
    {
        var transport = new StubTransport(_ => DataverseResponse.FromEntity(SampleContact()));

        var contact = await new DataverseClient(transport).Entity<Contact>(ContactId).RetrieveAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(contact);
        Assert.Equal(ContactId, contact.ContactId);
        Assert.Equal("Smith", contact.LastName);
        Assert.Equal(42, contact.Children);
        Assert.Equal(UserId, contact.PreferredUserId);
        Assert.Equal(new DataverseReference("account", AccountId), contact.ParentCustomerId);
        Assert.Equal("Contoso", Assert.Single(contact.Accounts!).Name);
    }

    [Fact]
    public async Task FromEntity_AnUnsetReference_ReadsBackAsNull()
    {
        var transport = new StubTransport(_ => DataverseResponse.FromEntity(new Contact { ContactId = ContactId }));

        var contact = await new DataverseClient(transport).Entity<Contact>(ContactId).RetrieveAsync(TestContext.Current.CancellationToken);

        Assert.Null(contact!.ParentCustomerId);
    }

    [Fact]
    public async Task FromEntity_KeepsTheDefaultNamesOfATypeThatIsNotAnEntity()
    {
        using var response = DataverseResponse.FromEntity(new WhoAmI { UserId = UserId });
        var json = await response.ReadJsonObjectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UserId, json["UserId"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task FromEntity_UsesTheStatusCode()
    {
        using var response = DataverseResponse.FromEntity(SampleContact(), HttpStatusCode.Created);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.HasBody);
        Assert.NotEmpty(await response.ReadJsonObjectAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void FromEntity_RejectsNull()
        => Assert.Throws<ArgumentNullException>(() => DataverseResponse.FromEntity<Contact>(null!));

    [Fact]
    public async Task FromEntities_WritesTheCollectionEnvelope()
    {
        using var response = DataverseResponse.FromEntities([SampleContact()], nextLink: "contacts?$skiptoken=2", totalCount: 7);
        var json = await response.ReadJsonObjectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(7, (long?)json["@odata.count"]);
        Assert.Equal("contacts?$skiptoken=2", (string?)json["@odata.nextLink"]);
        Assert.Equal("Smith", (string?)json["value"]![0]!["lastname"]);
    }

    [Fact]
    public async Task FromEntities_WithoutPagingOrCount_WritesOnlyTheValues()
    {
        using var response = DataverseResponse.FromEntities(Array.Empty<Contact>());
        var json = await response.ReadJsonObjectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["value"], json.Select(p => p.Key));
        Assert.Empty(json["value"]!.AsArray());
    }

    [Fact]
    public async Task FromEntities_RoundTripsThroughAPagedQuery()
    {
        var second = new Contact { ContactId = Guid.NewGuid(), LastName = "Jones" };
        var transport = new StubTransport(request => request.Url.Contains("skiptoken", StringComparison.Ordinal)
            ? DataverseResponse.FromEntities([second])
            : DataverseResponse.FromEntities([SampleContact()], nextLink: "https://test.crm.dynamics.com/api/data/v9.2/contacts?$skiptoken=2"));

        var all = await new DataverseClient(transport).Query<Contact>().GetAllAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(["Smith", "Jones"], all.Select(c => c.LastName));
        Assert.Equal(new DataverseReference("account", AccountId), all[0].ParentCustomerId);
    }

    [Fact]
    public async Task FromEntities_TheCountIsRead()
    {
        var transport = new StubTransport(_ => DataverseResponse.FromEntities([SampleContact()], totalCount: 12));

        var (_, count) = await new DataverseClient(transport).Query<Contact>().GetListWithCountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(12, count);
    }

    private static Contact SampleContact() => new()
    {
        ContactId = ContactId,
        LastName = "Smith",
        Children = 42,
        PreferredUserId = UserId,
        ParentCustomerId = new DataverseReference("account", AccountId),
        Accounts = [new Account { Name = "Contoso" }]
    };

    [DataverseEntity("contact", "contacts")]
    private sealed class Contact
    {
        public Guid ContactId { get; set; }

        public string? LastName { get; set; }

        [DataverseColumn("numberofchildren")]
        public int? Children { get; set; }

        [DataverseLookup("preferredsystemuserid", "systemusers")]
        public Guid? PreferredUserId { get; set; }

        [DataverseLookup("parentcustomerid")]
        [DataverseLookupTarget("account", "accounts", "parentcustomerid_account")]
        [DataverseLookupTarget("contact", "contacts", "parentcustomerid_contact")]
        public DataverseReference? ParentCustomerId { get; set; }

        [JsonPropertyName("contact_customer_accounts")]
        public List<Account>? Accounts { get; set; }
    }

    [DataverseEntity("account", "accounts")]
    private sealed class Account
    {
        public string? Name { get; set; }
    }

    private sealed class WhoAmI
    {
        public Guid UserId { get; set; }
    }

    private sealed class StubTransport(Func<DataverseRequest, DataverseResponse> respond) : IDataverseTransport
    {
        public string BaseUrl => "https://test.crm.dynamics.com/api/data/v9.2";

        public Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
            => Task.FromResult(respond(request));
    }
}
