using System.Net;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Tests.Builders;

public class BulkBuilderTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";

    // ────────── CreateMultiple ──────────

    [Fact]
    public async Task Create_PostsCreateMultiple_WithTypedTargets()
    {
        var transport = new FakeTransport();
        var contactId = Guid.NewGuid();

        var result = await new DataverseClient(transport).Bulk<BulkAccount>().CreateAsync(
        [
            new BulkAccount { Name = "One", Revenue = 10m, PrimaryContactId = contactId },
            new BulkAccount { Name = "Two" }
        ], TestContext.Current.CancellationToken);

        var request = Assert.Single(transport.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("accounts/Microsoft.Dynamics.CRM.CreateMultiple", request.Url);

        var targets = Targets(request);
        Assert.Equal(2, targets.Count);
        Assert.Equal("Microsoft.Dynamics.CRM.account", (string?)targets[0]["@odata.type"]);
        Assert.Equal("@odata.type", targets[0].First().Key);
        Assert.Equal("One", (string?)targets[0]["name"]);
        Assert.Equal(10m, (decimal?)targets[0]["revenue"]);
        Assert.Equal($"/contacts({contactId})", (string?)targets[0]["primarycontactid@odata.bind"]);
        // Null properties are left out; read-only ones are never written.
        Assert.False(targets[1].ContainsKey("revenue"));
        Assert.False(targets[0].ContainsKey("accountid"));
        Assert.False(targets[0].ContainsKey("createdon"));

        Assert.True(result.AllSucceeded);
        Assert.Equal(transport.CreatedIds, result.Ids);
    }

    [Fact]
    public async Task Create_SplitsTheRecordsIntoChunks_AndReturnsIdsInInputOrder()
    {
        var transport = new FakeTransport();

        var result = await new DataverseClient(transport).Bulk<BulkAccount>()
            .ChunkSize(2)
            .CreateAsync(Accounts(5), TestContext.Current.CancellationToken);

        Assert.Equal([2, 2, 1], transport.Requests.Select(r => Targets(r).Count));
        Assert.Equal(["Account 0", "Account 1"], Targets(transport.Requests[0]).Select(t => (string?)t["name"]));
        Assert.Equal(["Account 4"], Targets(transport.Requests[2]).Select(t => (string?)t["name"]));

        Assert.Equal([0, 2, 4], result.Chunks.Select(c => c.StartIndex));
        Assert.Equal([2, 2, 1], result.Chunks.Select(c => c.Count));
        Assert.Equal(5, result.SucceededCount);
        Assert.Equal(transport.CreatedIds, result.Ids);
        Assert.Equal(transport.CreatedIds.Take(2), result.Chunks[0].Ids);
    }

    [Fact]
    public async Task NoRecords_SendsNothing()
    {
        var transport = new FakeTransport();

        var result = await new DataverseClient(transport).Bulk<BulkAccount>().CreateAsync([], TestContext.Current.CancellationToken);

        Assert.Empty(transport.Requests);
        Assert.Empty(result.Chunks);
        Assert.True(result.AllSucceeded);
        result.ThrowIfAnyFailed();
    }

    [Fact]
    public async Task ExplicitNames_OverrideTheMappedOnes()
    {
        var transport = new FakeTransport();

        await new DataverseClient(transport).Bulk<BulkAccount>("new_records", "new_record")
            .CreateAsync([new BulkAccount { Name = "x" }], TestContext.Current.CancellationToken);

        var request = Assert.Single(transport.Requests);
        Assert.Equal("new_records/Microsoft.Dynamics.CRM.CreateMultiple", request.Url);
        Assert.Equal("Microsoft.Dynamics.CRM.new_record", (string?)Targets(request)[0]["@odata.type"]);
    }

    // ────────── UpdateMultiple ──────────

    [Fact]
    public async Task Update_PostsUpdateMultiple_WithThePrimaryKeyInEachTarget()
    {
        var transport = new FakeTransport();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var result = await new DataverseClient(transport).Bulk<BulkAccount>().UpdateAsync(
        [
            new BulkAccount { AccountId = first, Revenue = 5m },
            new BulkAccount { AccountId = second, Name = "Renamed" }
        ], a => a.AccountId, TestContext.Current.CancellationToken);

        var request = Assert.Single(transport.Requests);
        Assert.Equal("accounts/Microsoft.Dynamics.CRM.UpdateMultiple", request.Url);
        var targets = Targets(request);
        // The key is written although it is read-only for create, and only the set columns are sent.
        Assert.Equal(first, (Guid?)targets[0]["accountid"]);
        Assert.Equal(5m, (decimal?)targets[0]["revenue"]);
        Assert.False(targets[0].ContainsKey("name"));
        Assert.Equal(second, (Guid?)targets[1]["accountid"]);
        Assert.Equal("Microsoft.Dynamics.CRM.account", (string?)targets[1]["@odata.type"]);

        Assert.True(result.AllSucceeded);
        Assert.Empty(result.Ids);
    }

    [Fact]
    public async Task Update_LeavesOutCreateOnlyColumns_AsSetFromDoes()
    {
        // A create-only column such as statecode must not overwrite an existing record.
        var transport = new FakeTransport();

        await new DataverseClient(transport).Bulk<BulkAccount>().UpdateAsync(
            [new BulkAccount { AccountId = Guid.NewGuid(), Name = "x", StateCode = 1 }],
            a => a.AccountId, TestContext.Current.CancellationToken);

        var target = Targets(Assert.Single(transport.Requests))[0];
        Assert.False(target.ContainsKey("statecode"));
        Assert.False(target.ContainsKey("createdon"));
        Assert.Equal("x", (string?)target["name"]);
    }

    [Fact]
    public async Task Update_AcceptsANullablePrimaryKeyProperty()
    {
        var transport = new FakeTransport();
        var id = Guid.NewGuid();

        await new DataverseClient(transport).Bulk<NullableKeyRecord>()
            .UpdateAsync([new NullableKeyRecord { Id = id, Name = "x" }], r => r.Id, TestContext.Current.CancellationToken);

        Assert.Equal(id, (Guid?)Targets(Assert.Single(transport.Requests))[0]["new_recordid"]);
    }

    [Fact]
    public async Task Update_ARecordWithoutAKey_IsRejectedBeforeAnythingIsSent()
    {
        var transport = new FakeTransport();
        var builder = new DataverseClient(transport).Bulk<BulkAccount>();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => builder.UpdateAsync(
            [new BulkAccount { AccountId = Guid.NewGuid() }, new BulkAccount { Name = "no id" }],
            a => a.AccountId, TestContext.Current.CancellationToken));

        Assert.Contains("index 1", ex.Message);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task Update_DuplicateKeys_AreRejected()
    {
        // UpdateMultiple silently applies only the first record with a given key.
        var transport = new FakeTransport();
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => new DataverseClient(transport).Bulk<BulkAccount>()
            .ChunkSize(1)
            .UpdateAsync([new BulkAccount { AccountId = id }, new BulkAccount { AccountId = Guid.NewGuid() }, new BulkAccount { AccountId = id }],
                a => a.AccountId, TestContext.Current.CancellationToken));

        Assert.Contains("index 0 and 2", ex.Message);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task Update_AKeySelectorThatIsNotAProperty_IsRejected()
    {
        var builder = new DataverseClient(new FakeTransport()).Bulk<BulkAccount>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            builder.UpdateAsync([new BulkAccount()], a => Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    // ────────── UpsertMultiple ──────────

    [Fact]
    public async Task Upsert_ByPrimaryKey_SendsTheKey_AndSkipsCreateOnlyColumns()
    {
        var transport = new FakeTransport();
        var id = Guid.NewGuid();

        await new DataverseClient(transport).Bulk<BulkAccount>()
            .UpsertAsync([new BulkAccount { AccountId = id, Name = "x", StateCode = 0 }], a => a.AccountId, TestContext.Current.CancellationToken);

        var request = Assert.Single(transport.Requests);
        Assert.Equal("accounts/Microsoft.Dynamics.CRM.UpsertMultiple", request.Url);
        var target = Targets(request)[0];
        Assert.Equal(id, (Guid?)target["accountid"]);
        // statecode is read-only for create, so an upsert, which may update, must not send its default.
        Assert.False(target.ContainsKey("statecode"));
    }

    [Fact]
    public async Task Upsert_ByAlternateKey_SendsTheKeyAsOdataId()
    {
        var transport = new FakeTransport();

        await new DataverseClient(transport).Bulk<BulkAccount>().UpsertByAlternateKeyAsync(
        [
            new BulkAccount { AccountNumber = "ACC-001", Name = "One" },
            new BulkAccount { AccountNumber = "O'Brien & Co", Name = "Two" }
        ], a => a.AccountNumber, TestContext.Current.CancellationToken);

        var targets = Targets(Assert.Single(transport.Requests));
        Assert.Equal("accounts(accountnumber='ACC-001')", (string?)targets[0]["@odata.id"]);
        Assert.Equal("accounts(accountnumber='O''Brien%20%26%20Co')", (string?)targets[1]["@odata.id"]);
        Assert.Equal("Two", (string?)targets[1]["name"]);
        Assert.False(targets[0].ContainsKey("accountid"));
    }

    [Fact]
    public async Task Upsert_ByCompositeAlternateKey_FormatsEveryColumn()
    {
        var transport = new FakeTransport();

        await new DataverseClient(transport).Bulk<BulkAccount>().UpsertByAlternateKeyAsync(
            [new BulkAccount { AccountNumber = "ACC-1", Revenue = 7m, Name = "x" }],
            a => new { a.AccountNumber, a.Revenue }, TestContext.Current.CancellationToken);

        Assert.Equal("accounts(accountnumber='ACC-1',revenue=7)", (string?)Targets(Assert.Single(transport.Requests))[0]["@odata.id"]);
    }

    [Fact]
    public async Task Upsert_ByAlternateKey_KeysDifferingOnlyInCase_AreDuplicates()
    {
        // Dataverse compares key values case-insensitively, so it would reject the request.
        var transport = new FakeTransport();

        await Assert.ThrowsAsync<ArgumentException>(() => new DataverseClient(transport).Bulk<BulkAccount>().UpsertByAlternateKeyAsync(
            [new BulkAccount { AccountNumber = "acc-1" }, new BulkAccount { AccountNumber = "ACC-1" }],
            a => a.AccountNumber, TestContext.Current.CancellationToken));

        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task Upsert_ByAlternateKey_ANullKey_IsRejected()
    {
        var builder = new DataverseClient(new FakeTransport()).Bulk<BulkAccount>();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => builder.UpsertByAlternateKeyAsync(
            [new BulkAccount { Name = "no key" }], a => a.AccountNumber, TestContext.Current.CancellationToken));

        Assert.Contains("accountnumber", ex.Message);
    }

    // ────────── Failures ──────────

    [Fact]
    public async Task AFailedChunk_StopsTheRemainingChunks_ByDefault()
    {
        var transport = new FakeTransport { FailOnRequest = 2 };

        var result = await new DataverseClient(transport).Bulk<BulkAccount>()
            .ChunkSize(2)
            .CreateAsync(Accounts(7), TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(
            [BulkChunkStatus.Succeeded, BulkChunkStatus.Failed, BulkChunkStatus.NotSent, BulkChunkStatus.NotSent],
            result.Chunks.Select(c => c.Status));
        Assert.False(result.AllSucceeded);
        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(2, result.Ids.Count);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(2, failure.StartIndex);
        Assert.IsType<DataverseException>(failure.Error);
        Assert.Empty(failure.Ids);
    }

    [Fact]
    public async Task ContinueOnError_SendsEveryChunk()
    {
        var transport = new FakeTransport { FailOnRequest = 2 };

        var result = await new DataverseClient(transport).Bulk<BulkAccount>()
            .ChunkSize(2)
            .ContinueOnError()
            .CreateAsync(Accounts(7), TestContext.Current.CancellationToken);

        Assert.Equal(4, transport.Requests.Count);
        Assert.Equal(
            [BulkChunkStatus.Succeeded, BulkChunkStatus.Failed, BulkChunkStatus.Succeeded, BulkChunkStatus.Succeeded],
            result.Chunks.Select(c => c.Status));
        Assert.Equal(5, result.SucceededCount);
    }

    [Fact]
    public async Task ThrowIfAnyFailed_ReportsTheFailedAndUnsentRecords()
    {
        var transport = new FakeTransport { FailOnRequest = 1 };
        var result = await new DataverseClient(transport).Bulk<BulkAccount>()
            .ChunkSize(3)
            .CreateAsync(Accounts(7), TestContext.Current.CancellationToken);

        var ex = Assert.Throws<BulkOperationException>(result.ThrowIfAnyFailed);

        Assert.Same(result, ex.Result);
        Assert.IsType<DataverseException>(Assert.Single(ex.InnerExceptions));
        Assert.StartsWith("7 of 7 records were not written: 1 chunk(s) failed and 2 chunk(s) were not sent.", ex.Message);
        Assert.Contains("records 0 to 2", ex.Message);
    }

    // ────────── Parallelism ──────────

    [Fact]
    public async Task MaxDegreeOfParallelism_SendsChunksConcurrently_UpToTheLimit()
    {
        var transport = new FakeTransport { Delay = TimeSpan.FromMilliseconds(30) };

        var result = await new DataverseClient(transport).Bulk<BulkAccount>()
            .ChunkSize(1)
            .MaxDegreeOfParallelism(3)
            .CreateAsync(Accounts(9), TestContext.Current.CancellationToken);

        Assert.True(result.AllSucceeded);
        Assert.Equal(9, transport.Requests.Count);
        Assert.Equal(3, transport.MaxConcurrency);
        // Results stay in input order whatever order the chunks complete in.
        Assert.Equal(Enumerable.Range(0, 9), result.Chunks.Select(c => c.StartIndex));
        Assert.Equal(9, result.Ids.Distinct().Count());
    }

    [Fact]
    public async Task ByDefault_ChunksAreSentOneAtATime()
    {
        var transport = new FakeTransport { Delay = TimeSpan.FromMilliseconds(5) };

        await new DataverseClient(transport).Bulk<BulkAccount>().ChunkSize(1).CreateAsync(Accounts(4), TestContext.Current.CancellationToken);

        Assert.Equal(1, transport.MaxConcurrency);
    }

    [Fact]
    public async Task Cancellation_Throws_AfterTheChunksInFlightComplete()
    {
        using var cts = new CancellationTokenSource();
        var transport = new FakeTransport { OnSend = count => { if (count == 2) cts.Cancel(); } };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DataverseClient(transport).Bulk<BulkAccount>()
            .ChunkSize(1)
            .CreateAsync(Accounts(5), cts.Token));

        Assert.Equal(2, transport.Requests.Count);
    }

    // ────────── Composition ──────────

    [Fact]
    public async Task RequestOptions_ApplyToEveryChunk()
    {
        var transport = new FakeTransport();

        await new DataverseClient(transport)
            .WithOptions(o => o.BypassBusinessLogic = BusinessLogicBypass.CustomSync)
            .Bulk<BulkAccount>()
            .ChunkSize(2)
            .CreateAsync(Accounts(3), TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.Requests.Count);
        Assert.All(transport.Requests, r => Assert.Equal("CustomSync", r.Headers["MSCRM.BypassBusinessLogicExecution"]));
    }

    [Fact]
    public async Task OverHttp_TheRequestIsJson_AndTheIdsAreRead()
    {
        var handler = new TestHttpMessageHandler();
        var id = Guid.NewGuid();
        handler.AddJsonResponse(_ => true, new { Ids = new[] { id } });
        using var client = new DataverseClient(BaseUrl, () => Task.FromResult("t"), new HttpClient(handler));

        var result = await client.Bulk<BulkAccount>().CreateAsync([new BulkAccount { Name = "x" }], TestContext.Current.CancellationToken);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal($"{BaseUrl}/accounts/Microsoft.Dynamics.CRM.CreateMultiple", sent.Uri.AbsoluteUri);
        Assert.Equal("Microsoft.Dynamics.CRM.account", sent.Json.GetProperty("Targets")[0].GetProperty("@odata.type").GetString());
        Assert.Equal([id], result.Ids);
    }

    // ────────── Builder rules ──────────

    [Fact]
    public async Task TheBuilder_IsSingleUse()
    {
        var builder = new DataverseClient(new FakeTransport()).Bulk<BulkAccount>();
        await builder.CreateAsync([new BulkAccount { Name = "x" }], TestContext.Current.CancellationToken);

        Assert.Throws<InvalidOperationException>(() => { _ = builder.CreateAsync([new BulkAccount { Name = "y" }], TestContext.Current.CancellationToken); });
    }

    [Fact]
    public void InvalidSettings_AreRejected()
    {
        var builder = new DataverseClient(new FakeTransport()).Bulk<BulkAccount>();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.ChunkSize(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.MaxDegreeOfParallelism(0));
        Assert.Throws<ArgumentException>(() => new DataverseClient(new FakeTransport()).Bulk<BulkAccount>(" "));
        Assert.Throws<ArgumentNullException>(() => ((IDataverseClient)null!).Bulk<BulkAccount>());
    }

    [Fact]
    public async Task NullRecords_AreRejected()
    {
        var builder = new DataverseClient(new FakeTransport()).Bulk<BulkAccount>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => builder.CreateAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => new DataverseClient(new FakeTransport()).Bulk<BulkAccount>()
            .CreateAsync([new BulkAccount(), null!], TestContext.Current.CancellationToken));
    }

    // ────────── Helpers ──────────

    private static List<BulkAccount> Accounts(int count)
        => [.. Enumerable.Range(0, count).Select(i => new BulkAccount { Name = $"Account {i}" })];

    private static List<JsonObject> Targets(DataverseRequest request)
        => [.. request.Payload!["Targets"]!.AsArray().Select(t => t!.AsObject())];

    [DataverseEntity("account", "accounts")]
    private sealed class BulkAccount
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("accountid")]
        public Guid AccountId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("accountnumber")]
        public string? AccountNumber { get; set; }

        [JsonPropertyName("revenue")]
        public decimal? Revenue { get; set; }

        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("statecode")]
        public int? StateCode { get; set; }

        [DataverseReadOnly]
        [JsonPropertyName("createdon")]
        public DateTime? CreatedOn { get; set; } = DateTime.UtcNow;

        [DataverseLookup("primarycontactid", "contacts")]
        public Guid? PrimaryContactId { get; set; }
    }

    [DataverseEntity("new_record", "new_records")]
    private sealed class NullableKeyRecord
    {
        [JsonPropertyName("new_recordid")]
        public Guid? Id { get; set; }

        [JsonPropertyName("new_name")]
        public string? Name { get; set; }
    }

    // Answers CreateMultiple with new IDs, and other messages with 204. Optionally fails the Nth request (1-based),
    // delays each response, and records the highest number of requests in flight at once.
    private sealed class FakeTransport : IDataverseTransport
    {
        private readonly Lock _lock = new();
        private int _inFlight;

        public string BaseUrl => BulkBuilderTests.BaseUrl;
        public List<DataverseRequest> Requests { get; } = [];
        public List<Guid> CreatedIds { get; } = [];
        public int? FailOnRequest { get; init; }
        public TimeSpan Delay { get; init; }
        public Action<int>? OnSend { get; init; }
        public int MaxConcurrency { get; private set; }

        public async Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
        {
            int number;
            lock (_lock)
            {
                Requests.Add(request);
                number = Requests.Count;
                MaxConcurrency = Math.Max(MaxConcurrency, ++_inFlight);
            }

            try
            {
                OnSend?.Invoke(number);
                if (Delay > TimeSpan.Zero)
                    await Task.Delay(Delay, CancellationToken.None);

                if (number == FailOnRequest)
                    throw DataverseException.FromErrorContent(
                        """{"error":{"code":"0x80040237","message":"A record failed validation."}}""", HttpStatusCode.BadRequest);

                if (!request.Url.EndsWith("CreateMultiple", StringComparison.Ordinal))
                    return DataverseResponse.Empty();

                var ids = new JsonArray();
                lock (_lock)
                {
                    // CreatedIds is in the order the requests completed, which matches the input only when the
                    // chunks are sent one at a time.
                    foreach (var _ in request.Payload!["Targets"]!.AsArray())
                    {
                        var id = Guid.NewGuid();
                        CreatedIds.Add(id);
                        ids.Add(id.ToString());
                    }
                }
                return DataverseResponse.FromJson(new JsonObject { ["Ids"] = ids });
            }
            finally
            {
                lock (_lock) _inFlight--;
            }
        }
    }
}
