using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent.Tests.Builders;

/// <summary>
/// The mapping model: lookups, navigation properties, entity logical names and payload naming.
/// </summary>
public class MappingModelTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";
    private static readonly Guid ContactId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [DataverseEntity("account", "accounts")]
    private class MappedAccount
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        public Guid AccountId { get; set; }

        public string? Name { get; set; }

        [DataverseLookup("primarycontactid", "contacts")]
        public Guid? PrimaryContactId { get; set; }

        [DataverseLookup("new_regionid", "new_regions", NavigationProperty = "new_RegionId")]
        public Guid RegionId { get; set; }

        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [DataverseLookup("ownerid", "systemusers")]
        public Guid? OwnerId { get; set; }

        [JsonPropertyName("_parentaccountid_value")]
        public Guid? ParentAccountId { get; set; }

        [JsonPropertyName("contact_customer_accounts")]
        public List<MappedContact>? Contacts { get; set; }

        [JsonPropertyName("primarycontactid")]
        public MappedContact? PrimaryContact { get; set; }

        [JsonIgnore]
        public string? Scratch { get; set; }

        [JsonPropertyName("parentcustomerid_account@odata.bind")]
        public string? WriteOnlyBind { get; set; }
    }

    [DataverseEntity("contact", "contacts")]
    private class MappedContact
    {
        public string? FullName { get; set; }

        [DataverseLookup("parentcustomerid", "accounts", NavigationProperty = "parentcustomerid_account")]
        public Guid? ParentAccountId { get; set; }

        [JsonPropertyName("account_primary_contact")]
        public List<MappedAccount>? PrimaryFor { get; set; }
    }

    // No [EntitySet]: used with an explicit entity set name.
    private class Unmapped
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private class BadLookup
    {
        [DataverseLookup("primarycontactid", "contacts")]
        public string? PrimaryContactId { get; set; }
    }

    private class ActionRequest
    {
        public int Status { get; set; }
        public string? Note { get; set; }
    }

    private sealed class FakeTransport(string response = "{\"value\":[]}") : IDataverseTransport
    {
        public string BaseUrl => MappingModelTests.BaseUrl;
        public List<DataverseRequest> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];

        public async Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? request.Payload?.ToJsonString() : await request.Content.ReadAsStringAsync(ct));
            return response.Length == 0 ? DataverseResponse.Empty() : DataverseResponse.FromJson(JsonNode.Parse(response));
        }
    }

    private static string Decoded(DataverseRequest request) => Uri.UnescapeDataString(request.Url);

    // ────────── Lookups ──────────

    [Fact]
    public async Task Lookup_IsSelectedAndFilteredByItsValueName()
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport);

        await client.Query<MappedAccount>()
            .Select(a => new { a.Name, a.PrimaryContactId })
            .Where(a => a.PrimaryContactId == ContactId)
            .GetListAsync(TestContext.Current.CancellationToken);

        var url = Decoded(transport.Requests.Single());
        Assert.Contains("$select=name,_primarycontactid_value", url);
        Assert.Contains($"$filter=_primarycontactid_value eq '{ContactId}'", url);
    }

    [Fact]
    public async Task Lookup_IsReadFromItsValueName()
    {
        var transport = new FakeTransport($"{{\"value\":[{{\"name\":\"A\",\"_primarycontactid_value\":\"{ContactId}\"}}]}}");
        var client = new DataverseClient(transport);

        var account = Assert.Single(await client.Query<MappedAccount>().GetListAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ContactId, account.PrimaryContactId);
    }

    [Fact]
    public void SetFrom_WritesLookupsAsBinds_AndSkipsUnsetOnes()
    {
        var client = new DataverseClient(new FakeTransport());

        var request = client.Entity<MappedAccount>()
            .SetFrom(new MappedAccount { Name = "A", PrimaryContactId = ContactId, RegionId = Guid.Empty })
            .ToCreateRequest();

        var payload = request.Payload!;
        Assert.Equal($"/contacts({ContactId})", (string?)payload["primarycontactid@odata.bind"]);
        Assert.False(payload.ContainsKey("_primarycontactid_value"));
        Assert.False(payload.ContainsKey("new_RegionId@odata.bind")); // Guid.Empty is "not set"
    }

    [Fact]
    public void SetFrom_UsesTheCustomNavigationProperty()
    {
        var client = new DataverseClient(new FakeTransport());
        var regionId = Guid.NewGuid();

        var payload = client.Entity<MappedAccount>().SetFrom(new MappedAccount { Name = "A", RegionId = regionId }).ToCreateRequest().Payload!;

        Assert.Equal($"/new_regions({regionId})", (string?)payload["new_RegionId@odata.bind"]);
    }

    [Fact]
    public void SetFrom_OnUpdate_LeavesOutReadOnlyLookups()
    {
        var client = new DataverseClient(new FakeTransport());

        var payload = client.Entity<MappedAccount>(AccountId)
            .SetFrom(new MappedAccount { Name = "A", OwnerId = Guid.NewGuid() })
            .ToUpdateRequest().Payload!;

        Assert.False(payload.ContainsKey("ownerid@odata.bind"));
        Assert.Equal("A", (string?)payload["name"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Set_LookupByExpression_BindsOrClears(bool hasValue)
    {
        var client = new DataverseClient(new FakeTransport());

        var payload = client.Entity<MappedAccount>(AccountId)
            .Set(a => a.PrimaryContactId, hasValue ? ContactId : null)
            .ToUpdateRequest().Payload!;

        Assert.True(payload.ContainsKey("primarycontactid@odata.bind"));
        Assert.Equal(hasValue ? $"/contacts({ContactId})" : null, (string?)payload["primarycontactid@odata.bind"]);
    }

    [Fact]
    public void Set_NavigationProperty_Throws()
    {
        var client = new DataverseClient(new FakeTransport());

        Assert.Throws<ArgumentException>(() => client.Entity<MappedAccount>().Set(a => a.Contacts, []));
    }

    [Fact]
    public void Lookup_OnANonGuidProperty_Throws()
    {
        var client = new DataverseClient(new FakeTransport());

        var ex = Assert.Throws<InvalidOperationException>(
            () => client.Entity<BadLookup>(entitySet: "bad").SetFrom(new BadLookup { PrimaryContactId = "x" }));
        Assert.Contains("requires a Guid", ex.Message);
    }

    // ────────── Navigation properties ──────────

    [Fact]
    public void SetFrom_ARetrievedRecordWithExpandedRelations_DoesNotWriteThem()
    {
        var client = new DataverseClient(new FakeTransport());
        var retrieved = new MappedAccount
        {
            Name = "A",
            Contacts = [new MappedContact { FullName = "c" }],
            PrimaryContact = new MappedContact { FullName = "p" }
        };

        var payload = client.Entity<MappedAccount>(AccountId).SetFrom(retrieved).ToUpdateRequest().Payload!;

        Assert.Equal(["name"], payload.Select(p => p.Key));
    }

    [Fact]
    public void Navigation_IsStillReadFromAnExpandedResponse()
    {
        var json = new JsonObject
        {
            ["name"] = "A",
            ["contact_customer_accounts"] = new JsonArray(new JsonObject { ["fullname"] = "c" })
        };

        var account = DataverseClient.DeserializeEntity<MappedAccount>(json)!;

        Assert.Equal("c", Assert.Single(account.Contacts!).FullName);
    }

    [Fact]
    public async Task DeepInsert_ChildLookupsAreBinds_AndTheirNavigationIsOmitted()
    {
        var transport = new FakeTransport("{}");
        var client = new DataverseClient(transport);

        await client.DeepInsert(new MappedAccount { Name = "Parent" })
            .WithRelated(new[] { new MappedContact { FullName = "c", ParentAccountId = AccountId, PrimaryFor = [new MappedAccount()] } }, "contact_customer_accounts")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        var child = transport.Requests.Single().Payload!["contact_customer_accounts"]![0]!.AsObject();
        Assert.Equal($"/accounts({AccountId})", (string?)child["parentcustomerid_account@odata.bind"]);
        Assert.False(child.ContainsKey("account_primary_contact"));
    }

    [Fact]
    public async Task Batch_AddCreate_WritesLookupsAsBinds()
    {
        var transport = new FakeTransport("");
        var client = new DataverseClient(transport);

        await client.Batch().AddCreate(new MappedAccount { Name = "A", PrimaryContactId = ContactId }).ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Contains($"\"primarycontactid@odata.bind\":\"/contacts({ContactId})\"", transport.Bodies.Single());
    }

    // ────────── SelectAll ──────────

    [Fact]
    public async Task SelectAll_SelectsColumnsAndLookups_Only()
    {
        var transport = new FakeTransport("{}");
        var client = new DataverseClient(transport);

        await client.Entity<MappedAccount>(AccountId).SelectAll().RetrieveAsync(TestContext.Current.CancellationToken);

        var select = Decoded(transport.Requests.Single()).Split("$select=")[1].Split(',');
        Assert.Equal(
            ["accountid", "name", "_primarycontactid_value", "_new_regionid_value", "_ownerid_value", "_parentaccountid_value"],
            select);
    }

    // ────────── Entity logical names (FetchXML) ──────────

    [Fact]
    public void FetchBuilder_UsesTheEntityAndLookupLogicalNames()
    {
        var client = new DataverseClient(new FakeTransport());

        var fetchXml = client.FetchBuilder<MappedAccount>()
            .AddAttributes(a => new { a.Name, a.PrimaryContactId, a.ParentAccountId })
            .AddFilter(a => a.PrimaryContactId == ContactId)
            .AddOrder(a => a.ParentAccountId)
            .BuildFetchXml();

        Assert.Contains("<entity name=\"account\">", fetchXml);
        Assert.Contains("<attribute name=\"primarycontactid\" />", fetchXml);
        Assert.Contains("<attribute name=\"parentaccountid\" />", fetchXml);
        Assert.Contains($"<condition attribute=\"primarycontactid\" operator=\"eq\" value=\"{ContactId}\" />", fetchXml);
        Assert.Contains("<order attribute=\"parentaccountid\" />", fetchXml);
        Assert.DoesNotContain("_value", fetchXml);
    }

    [Fact]
    public void FetchBuilder_WithoutAMappedLogicalName_LowercasesTheTypeName()
    {
        var client = new DataverseClient(new FakeTransport());

        Assert.Contains("<entity name=\"unmapped\"", client.FetchBuilder<Unmapped>().BuildFetchXml());
    }

    [Fact]
    public async Task DataverseEntity_ProvidesTheEntitySet()
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport);

        await client.Query<MappedAccount>().GetListAsync(TestContext.Current.CancellationToken);

        Assert.Equal("accounts", transport.Requests.Single().Url);
    }

    [Fact]
    public async Task QueryFunction_OnALookup_UsesTheColumnLogicalName()
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport);

        await client.Query<MappedAccount>()
            .Where(a => DataverseFunctions.EqualUserId(a.OwnerId) || DataverseFunctions.Under(a.ParentAccountId, AccountId))
            .GetListAsync(TestContext.Current.CancellationToken);

        // Dataverse rejects PropertyName='_ownerid_value' ("entity doesn't contain attribute").
        var filter = Decoded(transport.Requests.Single());
        Assert.Contains("EqualUserId(PropertyName='ownerid')", filter);
        Assert.Contains($"Under(PropertyName='parentaccountid',PropertyValue='{AccountId}')", filter);
    }

    // ────────── Payload naming ──────────

    [Fact]
    public void SetFrom_OnATypeWithoutAnEntitySet_UsesTheSameNamesAsTheUrl()
    {
        var client = new DataverseClient(new FakeTransport());

        var payload = client.Entity<Unmapped>(entitySet: "unmappeds").SetFrom(new Unmapped { Name = "x", Count = 2 }).ToCreateRequest().Payload!;

        Assert.Equal("{\"name\":\"x\",\"count\":2}", payload.ToJsonString());
    }

    [Fact]
    public void ActionRequests_KeepTheirParameterNames()
    {
        var client = new DataverseClient(new FakeTransport());

        var request = client.Action<ActionRequest, object>("DoIt").WithRequest(new ActionRequest { Status = 1 }).ToRequest();

        Assert.Equal("{\"Status\":1}", request.Payload!.ToJsonString());
    }
}
