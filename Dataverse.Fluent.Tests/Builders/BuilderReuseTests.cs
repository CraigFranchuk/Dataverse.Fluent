using System.Net;
using System.Text;

namespace Dataverse.Fluent.Tests.Builders;

/// <summary>
/// Tests that every builder is single-use: once a terminal method runs, any further terminal call
/// throws <see cref="InvalidOperationException"/> without sending a request.
/// </summary>
public class BuilderReuseTests
{
    // Answers every request with a body each terminal method can read.
    private sealed class StubHandler : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            var path = request.RequestUri!.AbsolutePath;
            var (body, mediaType) = path.EndsWith("/$count") ? ("0", "text/plain")
                : path.EndsWith("/$batch") ? ("--batchresponse_1--", "multipart/mixed")
                : ("{\"value\":[]}", "application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType)
            });
        }
    }

    private static async Task Drain<T>(IAsyncEnumerable<T> source)
    {
        await foreach (var _ in source) { }
    }

    public static TheoryData<string> Terminals =>
    [
        "Query.GetListAsync", "Query.GetPagedAsync", "Query.GetAllAsync", "Query.StreamAsync",
        "Query.CountAsync", "Query.GetListWithCountAsync",
        "Entity.RetrieveAsync", "Entity.RetrieveWithMetadataAsync", "Entity.CreateAsync",
        "Entity.UpdateAsync", "Entity.UpsertAsync", "Entity.DeleteAsync",
        "Aggregate.GetListAsync", "Batch.ExecuteAsync", "DeepInsert.ExecuteAsync",
        "Annotation.CreateAsync", "Annotation.UpdateAsync", "Annotation.DeleteAsync",
        "Annotation.RetrieveAsync", "Annotation.DownloadFileAsync",
        "Fetch.ExecuteAsync", "Fetch.StreamAsync", "Function.ExecuteAsync", "Action.ExecuteAsync",
        "Associate.AssociateAsync", "Associate.DisassociateAsync", "Delta.ExecuteAsync", "Delta.StreamAsync",
    ];

    // Returns a call to the named terminal method on ONE builder instance, so calling it twice reuses it.
    private static Func<Task> Terminal(DataverseClient client, string name)
    {
        var id = Guid.NewGuid();
        switch (name)
        {
            case "Query.GetListAsync": { var b = client.Query<TestAccount>(); return () => b.GetListAsync(); }
            case "Query.GetPagedAsync": { var b = client.Query<TestAccount>(); return () => b.GetPagedAsync(); }
            case "Query.GetAllAsync": { var b = client.Query<TestAccount>(); return () => b.GetAllAsync(); }
            case "Query.StreamAsync": { var b = client.Query<TestAccount>(); return () => Drain(b.StreamAsync()); }
            case "Query.CountAsync": { var b = client.Query<TestAccount>(); return () => b.CountAsync(); }
            case "Query.GetListWithCountAsync": { var b = client.Query<TestAccount>(); return () => b.GetListWithCountAsync(); }
            case "Entity.RetrieveAsync": { var b = client.Entity<TestAccount>(id); return () => b.RetrieveAsync(); }
            case "Entity.RetrieveWithMetadataAsync": { var b = client.Entity<TestAccount>(id); return () => b.RetrieveWithMetadataAsync(); }
            case "Entity.CreateAsync": { var b = client.Entity<TestAccount>().Set("name", "x"); return () => b.CreateAsync(); }
            case "Entity.UpdateAsync": { var b = client.Entity<TestAccount>(id).Set("name", "x"); return () => b.UpdateAsync(); }
            case "Entity.UpsertAsync": { var b = client.Entity<TestAccount>(id).Set("name", "x"); return () => b.UpsertAsync(); }
            case "Entity.DeleteAsync": { var b = client.Entity<TestAccount>(id); return () => b.DeleteAsync(); }
            case "Aggregate.GetListAsync": { var b = client.Aggregate<TestAccount>().Count("n"); return () => b.GetListAsync<TestAccount>(); }
            case "Batch.ExecuteAsync": { var b = client.Batch().AddCreate(new TestAccount { Name = "x" }); return () => b.ExecuteAsync(); }
            case "DeepInsert.ExecuteAsync": { var b = client.DeepInsert(new TestAccount { Name = "x" }); return () => b.ExecuteAsync(); }
            case "Annotation.CreateAsync": { var b = client.Annotation().Regarding<TestAccount>(id).WithSubject("x"); return () => b.CreateAsync(); }
            case "Annotation.UpdateAsync": { var b = client.Annotation(id).WithSubject("x"); return () => b.UpdateAsync(); }
            case "Annotation.DeleteAsync": { var b = client.Annotation(id); return () => b.DeleteAsync(); }
            case "Annotation.RetrieveAsync": { var b = client.Annotation(id); return () => b.RetrieveAsync(); }
            case "Annotation.DownloadFileAsync": { var b = client.Annotation(id); return () => b.DownloadFileAsync(); }
            case "Fetch.ExecuteAsync": { var b = client.FetchBuilder<TestAccount>(); return () => b.ExecuteAsync(); }
            case "Fetch.StreamAsync": { var b = client.FetchBuilder<TestAccount>(); return () => Drain(b.StreamAsync()); }
            case "Function.ExecuteAsync": { var b = client.Function<object>("WhoAmI"); return () => b.ExecuteAsync(); }
            case "Action.ExecuteAsync": { var b = client.Action<object, object>("DoSomething"); return () => b.ExecuteAsync(); }
            case "Associate.AssociateAsync": { var b = client.Associate().From<TestAccount>(id).Via("nav").To<TestContact>(id); return () => b.AssociateAsync(); }
            case "Associate.DisassociateAsync": { var b = client.Associate().From<TestAccount>(id).Via("nav").To<TestContact>(id); return () => b.DisassociateAsync(); }
            case "Delta.ExecuteAsync": { var b = client.Delta<TestAccount>(); return () => b.ExecuteAsync(); }
            case "Delta.StreamAsync": { var b = client.Delta<TestAccount>(); return () => Drain(b.StreamAsync()); }
            default: throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }
    }

    [Theory]
    [MemberData(nameof(Terminals))]
    public async Task SecondTerminalCall_Throws_AndSendsNoRequest(string terminal)
    {
        var handler = new StubHandler();
        var call = Terminal(new DataverseClient("https://test.crm.dynamics.com/api/data/v9.2", () => Task.FromResult("t"), new HttpClient(handler)), terminal);

        await call();
        var callsAfterFirst = handler.Calls;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(call);
        Assert.Contains("already been executed", ex.Message);
        Assert.Equal(callsAfterFirst, handler.Calls);
    }

    [Fact]
    public async Task DifferentTerminalOnTheSameBuilder_Throws()
    {
        var client = new DataverseClient("https://test.crm.dynamics.com/api/data/v9.2", () => Task.FromResult("t"), new HttpClient(new StubHandler()));

        var entity = client.Entity<TestAccount>(Guid.NewGuid());
        await entity.RetrieveAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => entity.DeleteAsync());

        var query = client.Query<TestAccount>();
        await query.GetListAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => query.CountAsync());
    }

    [Fact]
    public async Task ConcurrentTerminalCalls_ExactlyOneRuns()
    {
        var handler = new StubHandler();
        var query = new DataverseClient("https://test.crm.dynamics.com/api/data/v9.2", () => Task.FromResult("t"), new HttpClient(handler))
            .Query<TestAccount>();
        using var start = new ManualResetEventSlim();

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();
                try { await query.GetListAsync(); return true; }
                catch (InvalidOperationException) { return false; }
            }))
            .ToArray();
        start.Set();
        var outcomes = await Task.WhenAll(tasks);

        Assert.Equal(1, outcomes.Count(ran => ran));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetNextPageAsync_DoesNotConsumeTheBuilder()
    {
        // Paging follows nextLinks on the builder that produced the first page, repeatedly.
        var handler = new TestHttpMessageHandler();
        var client = new DataverseClient("https://test.crm.dynamics.com/api/data/v9.2", () => Task.FromResult("t"), new HttpClient(handler));
        const string next = "https://test.crm.dynamics.com/api/data/v9.2/testaccounts?$skiptoken=x";
        foreach (var _ in Enumerable.Range(0, 3))
            handler.AddJsonResponse(_ => true, new Dictionary<string, object> { ["value"] = Array.Empty<object>(), ["@odata.nextLink"] = next });

        var query = client.Query<TestAccount>().PageSize(10);
        var page = await query.GetPagedAsync();
        page = await query.GetNextPageAsync(page);
        page = await query.GetNextPageAsync(page);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(next, page.NextLink);
    }
}
