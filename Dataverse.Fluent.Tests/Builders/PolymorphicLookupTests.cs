using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent.Tests.Builders;

public class PolymorphicLookupTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";
    private const string LookupAnnotation = "Microsoft.Dynamics.CRM.lookuplogicalname";

    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeamId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ────────── DataverseReference ──────────

    [Fact]
    public void Reference_NormalizesTheLogicalName_AndComparesByValue()
    {
        var reference = new DataverseReference("Account", AccountId);

        Assert.Equal("account", reference.LogicalName);
        Assert.Equal(new DataverseReference("account", AccountId), reference);
        Assert.Equal($"account({AccountId})", reference.ToString());
    }

    [Fact]
    public void Reference_To_UsesTheMappedLogicalName()
    {
        Assert.Equal(new DataverseReference("account", AccountId), DataverseReference.To<PolyAccount>(AccountId));
        Assert.Throws<InvalidOperationException>(() => DataverseReference.To<Unmapped>(AccountId));
    }

    [Fact]
    public void Reference_RequiresATableAndAnId()
    {
        Assert.Throws<ArgumentException>(() => new DataverseReference(" ", AccountId));
        Assert.Throws<ArgumentException>(() => new DataverseReference("account", Guid.Empty));
    }

    // ────────── Writing ──────────

    [Fact]
    public async Task SetFrom_WritesTheBindOfTheTargetTheReferencePointsAt()
    {
        var transport = new FakeTransport();

        await new DataverseClient(transport).Entity<PolyContact>().SetFrom(new PolyContact
        {
            LastName = "Smith",
            ParentCustomerId = DataverseReference.To<PolyAccount>(AccountId),
            OwnerId = new DataverseReference("team", TeamId)
        }).CreateAsync(TestContext.Current.CancellationToken);

        var payload = Assert.Single(transport.Requests).Payload!;
        Assert.Equal($"/accounts({AccountId})", (string?)payload["parentcustomerid_account@odata.bind"]);
        Assert.False(payload.ContainsKey("parentcustomerid_contact@odata.bind"));
        // Both owner targets share one navigation property.
        Assert.Equal($"/teams({TeamId})", (string?)payload["ownerid@odata.bind"]);
        Assert.False(payload.ContainsKey("_parentcustomerid_value"));
        Assert.Equal("Smith", (string?)payload["lastname"]);
    }

    [Fact]
    public async Task SetFrom_AnUnsetReference_IsNotWritten()
    {
        var transport = new FakeTransport();

        await new DataverseClient(transport).Entity<PolyContact>()
            .SetFrom(new PolyContact { LastName = "Smith" })
            .CreateAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(Assert.Single(transport.Requests).Payload!, p => p.Key.Contains("@odata.bind", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetFrom_AReferenceToATableThatIsNotATarget_Throws()
    {
        var builder = new DataverseClient(new FakeTransport()).Entity<PolyContact>();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.SetFrom(new PolyContact { ParentCustomerId = new DataverseReference("lead", AccountId) }));

        Assert.Contains("'lead'", ex.Message);
        Assert.Contains("account, contact", ex.Message);
        await Task.CompletedTask;
    }

    [Fact]
    public void SetFrom_AReferenceReadWithoutItsTable_Throws()
    {
        var contact = Deserialize<PolyContact>($$"""{ "_parentcustomerid_value": "{{AccountId}}" }""");
        var builder = new DataverseClient(new FakeTransport()).Entity<PolyContact>();

        var ex = Assert.Throws<InvalidOperationException>(() => builder.SetFrom(contact));

        Assert.Contains(LookupAnnotation, ex.Message);
    }

    [Fact]
    public async Task Set_WritesTheBind_AndAReplacedValueLeavesNoEarlierBind()
    {
        var transport = new FakeTransport();
        var contactId = Guid.NewGuid();

        await new DataverseClient(transport).Entity<PolyContact>(Guid.NewGuid())
            .Set(c => c.ParentCustomerId, DataverseReference.To<PolyAccount>(AccountId))
            .Set(c => c.ParentCustomerId, new DataverseReference("contact", contactId))
            .UpdateAsync(TestContext.Current.CancellationToken);

        var payload = Assert.Single(transport.Requests).Payload!;
        Assert.Equal($"/contacts({contactId})", (string?)payload["parentcustomerid_contact@odata.bind"]);
        Assert.False(payload.ContainsKey("parentcustomerid_account@odata.bind"));
    }

    [Fact]
    public async Task Set_Null_ClearsTheLookupThroughOneNavigationProperty()
    {
        var transport = new FakeTransport();

        await new DataverseClient(transport).Entity<PolyContact>(Guid.NewGuid())
            .Set(c => c.ParentCustomerId, null)
            .UpdateAsync(TestContext.Current.CancellationToken);

        var payload = Assert.Single(transport.Requests).Payload!;
        Assert.True(payload.ContainsKey("parentcustomerid_account@odata.bind"));
        Assert.Null(payload["parentcustomerid_account@odata.bind"]);
        Assert.False(payload.ContainsKey("parentcustomerid_contact@odata.bind"));
    }

    [Fact]
    public async Task ReadOnly_RemovesEveryBindOfThePolymorphicLookup()
    {
        var transport = new FakeTransport();

        await new DataverseClient(transport).Entity<ReadOnlyCustomer>()
            .SetFrom(new ReadOnlyCustomer { Name = "x", CustomerId = DataverseReference.To<PolyAccount>(AccountId) })
            .CreateAsync(TestContext.Current.CancellationToken);

        var payload = Assert.Single(transport.Requests).Payload!;
        Assert.DoesNotContain(payload, p => p.Key.Contains("@odata.bind", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetFromExcept_ExcludesAPolymorphicLookup()
    {
        var transport = new FakeTransport();

        await new DataverseClient(transport).Entity<PolyContact>(Guid.NewGuid())
            .SetFromExcept(new PolyContact
            {
                LastName = "Smith",
                ParentCustomerId = DataverseReference.To<PolyAccount>(AccountId),
                OwnerId = new DataverseReference("team", TeamId)
            }, c => c.ParentCustomerId!)
            .UpdateAsync(TestContext.Current.CancellationToken);

        var payload = Assert.Single(transport.Requests).Payload!;
        Assert.False(payload.ContainsKey("parentcustomerid_account@odata.bind"));
        Assert.True(payload.ContainsKey("ownerid@odata.bind"));
    }

    [Fact]
    public async Task SetFromExcept_ExcludesASingleTableLookup()
    {
        var transport = new FakeTransport();

        await new DataverseClient(transport).Entity<PolyContact>(Guid.NewGuid())
            .SetFromExcept(new PolyContact { LastName = "Smith", PreferredUserId = Guid.NewGuid() }, c => c.PreferredUserId!)
            .UpdateAsync(TestContext.Current.CancellationToken);

        var payload = Assert.Single(transport.Requests).Payload!;
        Assert.False(payload.ContainsKey("preferredsystemuserid@odata.bind"));
        Assert.Equal("Smith", (string?)payload["lastname"]);
    }

    [Fact]
    public async Task SetFrom_ExcludingByLogicalName_ExcludesLookupBinds()
    {
        var transport = new FakeTransport();

        await new DataverseClient(transport).Entity<PolyContact>(Guid.NewGuid())
            .SetFrom(new PolyContact
            {
                LastName = "Smith",
                PreferredUserId = Guid.NewGuid(),
                ParentCustomerId = DataverseReference.To<PolyAccount>(AccountId)
            }, "preferredsystemuserid", "parentcustomerid")
            .UpdateAsync(TestContext.Current.CancellationToken);

        var payload = Assert.Single(transport.Requests).Payload!;
        Assert.DoesNotContain(payload, p => p.Key.Contains("@odata.bind", StringComparison.Ordinal));
        Assert.Equal("Smith", (string?)payload["lastname"]);
    }

    [Fact]
    public async Task Batch_AndBulk_WriteTheBind()
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport);
        var contact = new PolyContact { LastName = "Smith", ParentCustomerId = DataverseReference.To<PolyAccount>(AccountId) };

        await client.Batch().AddCreate(contact).ExecuteAsync(TestContext.Current.CancellationToken);
        await client.Bulk<PolyContact>().CreateAsync([contact], TestContext.Current.CancellationToken);

        var batchBody = transport.Bodies[0];
        Assert.Contains($"\"parentcustomerid_account@odata.bind\":\"/accounts({AccountId})\"", batchBody);
        var target = transport.Requests[1].Payload!["Targets"]![0]!;
        Assert.Equal($"/accounts({AccountId})", (string?)target["parentcustomerid_account@odata.bind"]);
    }

    // ────────── Reading ──────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Read_FillsTheReference_WhicheverPartComesFirst(bool annotationFirst)
    {
        var id = $"\"_parentcustomerid_value\": \"{AccountId}\"";
        var table = $"\"_parentcustomerid_value@{LookupAnnotation}\": \"account\"";
        var json = annotationFirst ? $"{{ {table}, {id} }}" : $"{{ {id}, {table} }}";

        var contact = Deserialize<PolyContact>(json);

        Assert.Equal(new DataverseReference("account", AccountId), contact.ParentCustomerId);
    }

    [Fact]
    public void Read_ANullLookup_IsNull()
    {
        var contact = Deserialize<PolyContact>("""{ "_parentcustomerid_value": null, "lastname": "Smith" }""");

        Assert.Null(contact.ParentCustomerId);
        Assert.Equal("Smith", contact.LastName);
    }

    [Fact]
    public void Read_WithoutTheAnnotation_KeepsTheIdWithoutATable()
    {
        var contact = Deserialize<PolyContact>($$"""{ "_parentcustomerid_value": "{{AccountId}}" }""");

        Assert.Equal(AccountId, contact.ParentCustomerId!.Id);
        Assert.Null(contact.ParentCustomerId.LogicalName);
    }

    [Fact]
    public async Task ARetrievedRecord_CanBeWrittenBack()
    {
        var contact = Deserialize<PolyContact>($$"""
            { "_ownerid_value": "{{TeamId}}", "_ownerid_value@{{LookupAnnotation}}": "team", "lastname": "Smith" }
            """);
        var transport = new FakeTransport();

        await new DataverseClient(transport).Entity<PolyContact>(Guid.NewGuid()).SetFrom(contact).UpdateAsync(TestContext.Current.CancellationToken);

        Assert.Equal($"/teams({TeamId})", (string?)Assert.Single(transport.Requests).Payload!["ownerid@odata.bind"]);
    }

    // ────────── The table annotation is requested ──────────

    [Fact]
    public async Task Query_RequestsTheTableAnnotation_AlongsideFormattedValues()
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport);
        var ct = TestContext.Current.CancellationToken;

        await client.Query<PolyContact>().GetListAsync(ct);
        await client.Query<PolyContact>().IncludeFormattedValues().PageSize(10).GetPagedAsync(ct);

        Assert.Equal($"odata.include-annotations=\"{LookupAnnotation}\"", transport.Requests[0].Headers["Prefer"]);
        Assert.Equal(
            $"odata.include-annotations=\"OData.Community.Display.V1.FormattedValue,{LookupAnnotation}\",odata.maxpagesize=10",
            transport.Requests[1].Headers["Prefer"]);
    }

    [Fact]
    public async Task Query_OfATypeWithoutPolymorphicLookups_RequestsNoAnnotation()
    {
        var transport = new FakeTransport();

        await new DataverseClient(transport).Query<TestAccount>().GetListAsync(TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(transport.Requests).Headers.ContainsKey("Prefer"));
    }

    [Fact]
    public async Task Query_OfATypeWhoseRelatedRecordsHavePolymorphicLookups_RequestsTheAnnotation()
    {
        var transport = new FakeTransport();

        await new DataverseClient(transport).Query<PolyAccount>().GetListAsync(TestContext.Current.CancellationToken);

        Assert.Contains(LookupAnnotation, Assert.Single(transport.Requests).Headers["Prefer"]);
    }

    [Fact]
    public async Task Retrieve_AndWritesThatReturnTheRecord_RequestTheAnnotation_OtherWritesDoNot()
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport);
        var ct = TestContext.Current.CancellationToken;

        await client.Entity<PolyContact>(Guid.NewGuid()).RetrieveAsync(ct);
        await client.Entity<PolyContact>(Guid.NewGuid()).Set(c => c.LastName, "x").ReturnRepresentation().UpdateAsync(ct);
        await client.Entity<PolyContact>(Guid.NewGuid()).Set(c => c.LastName, "x").UpdateAsync(ct);

        Assert.Contains(LookupAnnotation, transport.Requests[0].Headers["Prefer"]);
        Assert.Contains(LookupAnnotation, transport.Requests[1].Headers["Prefer"]);
        Assert.False(transport.Requests[2].Headers.ContainsKey("Prefer"));
    }

    [Fact]
    public async Task FetchXml_Delta_AndBatchRetrieve_RequestTheAnnotation()
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport);
        var ct = TestContext.Current.CancellationToken;

        await client.FetchBuilder<PolyContact>().ExecuteAsync(ct);
        await client.Delta<PolyContact>().ExecuteAsync(ct);
        await client.Batch().AddRetrieve<PolyContact>(Guid.NewGuid()).ExecuteAsync(ct);

        Assert.Contains(LookupAnnotation, transport.Requests[0].Headers["Prefer"]);
        Assert.Contains("Microsoft.Dynamics.CRM.fetchxmlpagingcookie", transport.Requests[0].Headers["Prefer"]);
        Assert.Contains(LookupAnnotation, transport.Requests[1].Headers["Prefer"]);
        Assert.Contains("odata.track-changes", transport.Requests[1].Headers["Prefer"]);
        var batchBody = transport.Bodies[2];
        Assert.Contains($"Prefer: odata.include-annotations=\"{LookupAnnotation}\"", batchBody);
    }

    // ────────── Selecting and filtering ──────────

    [Fact]
    public async Task Select_AndFilter_UseTheLookupValue()
    {
        var transport = new FakeTransport();
        var client = new DataverseClient(transport);
        var ct = TestContext.Current.CancellationToken;

        await client.Query<PolyContact>().Select(c => new { c.LastName, c.ParentCustomerId }).Where(c => c.ParentCustomerId!.Id == AccountId).GetListAsync(ct);
        await client.Query<PolyContact>().Where(c => c.ParentCustomerId == null).GetListAsync(ct);
        await client.Entity<PolyContact>(Guid.NewGuid()).SelectAll().RetrieveAsync(ct);

        var first = Uri.UnescapeDataString(transport.Requests[0].Url);
        Assert.Contains("$select=lastname,_parentcustomerid_value", first);
        Assert.Contains($"$filter=_parentcustomerid_value eq '{AccountId}'", first);
        Assert.Contains("$filter=_parentcustomerid_value eq null", Uri.UnescapeDataString(transport.Requests[1].Url));
        Assert.Contains("_ownerid_value", Uri.UnescapeDataString(transport.Requests[2].Url));
    }

    [Fact]
    public void Filter_OnTheTable_IsNotSupported()
    {
        var builder = new DataverseClient(new FakeTransport()).Query<PolyContact>();

        var ex = Assert.Throws<NotSupportedException>(() => builder.Where(c => c.ParentCustomerId!.LogicalName == "account"));

        Assert.Contains("ParentCustomerId.Id", ex.Message);
    }

    [Fact]
    public void FetchXmlFilter_UsesTheLookupAttribute()
    {
        var fetchXml = new DataverseClient(new FakeTransport()).FetchBuilder<PolyContact>()
            .AddFilter(c => c.ParentCustomerId!.Id == AccountId)
            .BuildFetchXml();

        Assert.Contains($"<condition attribute=\"parentcustomerid\" operator=\"eq\" value=\"{AccountId}\" />", fetchXml);
    }

    [Fact]
    public void FetchXmlFilter_WithTheIdOnTheRight_NamesTheLookupAttribute()
    {
        var fetchXml = new DataverseClient(new FakeTransport()).FetchBuilder<PolyContact>()
            .AddFilter(c => AccountId == c.ParentCustomerId!.Id)
            .BuildFetchXml();

        Assert.Contains($"<condition attribute=\"parentcustomerid\" operator=\"eq\" value=\"{AccountId}\" />", fetchXml);
    }

    // ────────── Mapping errors ──────────

    [Theory]
    [InlineData(typeof(ReferenceWithoutLookup), "needs [DataverseLookup")]
    [InlineData(typeof(ReferenceWithSingleTarget), "not a single target entity set")]
    [InlineData(typeof(ReferenceWithoutTargets), "at least one [DataverseLookupTarget")]
    [InlineData(typeof(ReferenceWithDuplicateTargets), "'account' more than once")]
    [InlineData(typeof(GuidWithTargets), "single-table lookup")]
    [InlineData(typeof(GuidWithoutTargetEntitySet), "single-table lookup")]
    public void Misconfigured_Lookups_AreRejected(Type type, string message)
    {
        var ex = Assert.ThrowsAny<Exception>(() => JsonSerializer.SerializeToNode(Activator.CreateInstance(type), JsonDefaults.EntityPayload));

        var invalid = ex as InvalidOperationException ?? ex.InnerException as InvalidOperationException;
        Assert.NotNull(invalid);
        Assert.Contains(message, invalid.Message);
    }

    // ────────── Helpers ──────────

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonDefaults.CaseInsensitive)!;

    [DataverseEntity("account", "accounts")]
    private sealed class PolyAccount
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("contact_customer_accounts")]
        public List<PolyContact>? Contacts { get; set; }
    }

    [DataverseEntity("contact", "contacts")]
    private sealed class PolyContact
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("contactid")]
        public Guid ContactId { get; set; }

        [JsonPropertyName("lastname")]
        public string? LastName { get; set; }

        [DataverseLookup("parentcustomerid")]
        [DataverseLookupTarget("account", "accounts", "parentcustomerid_account")]
        [DataverseLookupTarget("contact", "contacts", "parentcustomerid_contact")]
        public DataverseReference? ParentCustomerId { get; set; }

        [DataverseLookup("ownerid")]
        [DataverseLookupTarget("systemuser", "systemusers", "ownerid")]
        [DataverseLookupTarget("team", "teams", "ownerid")]
        public DataverseReference? OwnerId { get; set; }

        [DataverseLookup("preferredsystemuserid", "systemusers")]
        public Guid? PreferredUserId { get; set; }
    }

    [DataverseEntity("opportunity", "opportunities")]
    private sealed class ReadOnlyCustomer
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [DataverseReadOnly]
        [DataverseLookup("customerid")]
        [DataverseLookupTarget("account", "accounts", "customerid_account")]
        [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
        public DataverseReference? CustomerId { get; set; }
    }

    private sealed class Unmapped;

    [EntitySet("things")]
    private sealed class ReferenceWithoutLookup
    {
        public DataverseReference? Customer { get; set; } = new("account", Guid.NewGuid());
    }

    [EntitySet("things")]
    private sealed class ReferenceWithSingleTarget
    {
        [DataverseLookup("customerid", "accounts")]
        public DataverseReference? Customer { get; set; }
    }

    [EntitySet("things")]
    private sealed class ReferenceWithoutTargets
    {
        [DataverseLookup("customerid")]
        public DataverseReference? Customer { get; set; }
    }

    [EntitySet("things")]
    private sealed class ReferenceWithDuplicateTargets
    {
        [DataverseLookup("customerid")]
        [DataverseLookupTarget("account", "accounts", "customerid_account")]
        [DataverseLookupTarget("Account", "accounts", "customerid_account2")]
        public DataverseReference? Customer { get; set; }
    }

    [EntitySet("things")]
    private sealed class GuidWithTargets
    {
        [DataverseLookup("customerid", "accounts")]
        [DataverseLookupTarget("account", "accounts", "customerid_account")]
        public Guid? Customer { get; set; }
    }

    [EntitySet("things")]
    private sealed class GuidWithoutTargetEntitySet
    {
        [DataverseLookup("customerid")]
        public Guid? Customer { get; set; }
    }

    // Records each request, and its raw body (read at send time: the batch builder disposes it afterwards). GETs get
    // an empty collection (or a bare record for a retrieve), $batch an empty multipart body, other writes 204.
    private sealed class FakeTransport : IDataverseTransport
    {
        public string BaseUrl => PolymorphicLookupTests.BaseUrl;
        public List<DataverseRequest> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];

        public Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            Bodies.Add(request.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult());

            if (request.Url == "$batch")
            {
                return Task.FromResult(new DataverseResponse(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("--batch_response\r\n", Encoding.UTF8, "multipart/mixed")
                }));
            }

            if (request.Url.EndsWith("CreateMultiple", StringComparison.Ordinal))
                return Task.FromResult(DataverseResponse.FromJson(new JsonObject { ["Ids"] = new JsonArray(Guid.NewGuid().ToString()) }));

            if (request.Method == HttpMethod.Get)
            {
                var single = request.Url.Contains('(') && !request.Url.Contains("fetchXml", StringComparison.Ordinal) && !request.Url.Contains('?');
                return Task.FromResult(DataverseResponse.FromJson(single ? new JsonObject() : new JsonObject { ["value"] = new JsonArray() }));
            }

            return Task.FromResult(request.Headers.TryGetValue("Prefer", out var prefer) && prefer.Contains("return=representation")
                ? DataverseResponse.FromJson(new JsonObject())
                : DataverseResponse.Empty(entityUrl: $"{BaseUrl}/contacts({Guid.NewGuid()})"));
        }
    }
}
