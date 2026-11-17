using System.Net;
using System.Text;
using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent.Tests;

/// <summary>
/// Tests that a cancellation token reaches the HTTP layer and that cancellation surfaces as
/// <see cref="OperationCanceledException"/>, before, during, and between requests.
/// </summary>
public class CancellationTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";

    private static DataverseClient CreateClient(HttpMessageHandler handler)
        => new(BaseUrl, () => Task.FromResult("test-token"), new HttpClient(handler));

    private static CancellationToken Cancelled() => new(canceled: true);

    // Each operation's builder is configured so that, without cancellation, it would send a request.
    public static TheoryData<string> Operations =>
    [
        "Query.GetListAsync", "Query.CountAsync", "Entity.RetrieveAsync", "Entity.CreateAsync",
        "Entity.UpdateAsync", "Entity.DeleteAsync", "Batch.ExecuteAsync", "DeepInsert.ExecuteAsync",
        "Annotation.CreateAsync", "Fetch.ExecuteAsync", "Delta.ExecuteAsync", "Associate.AssociateAsync",
    ];

    private static Task Run(DataverseClient client, string operation, CancellationToken ct) => operation switch
    {
        "Query.GetListAsync" => client.Query<TestAccount>().GetListAsync(ct),
        "Query.CountAsync" => client.Query<TestAccount>().CountAsync(ct),
        "Entity.RetrieveAsync" => client.Entity<TestAccount>(Guid.NewGuid()).RetrieveAsync(ct),
        "Entity.CreateAsync" => client.Entity<TestAccount>().Set("name", "x").CreateAsync(ct),
        "Entity.UpdateAsync" => client.Entity<TestAccount>(Guid.NewGuid()).Set("name", "x").UpdateAsync(ct),
        "Entity.DeleteAsync" => client.Entity<TestAccount>(Guid.NewGuid()).DeleteAsync(ct),
        "Batch.ExecuteAsync" => client.Batch().AddCreate(new TestAccount { Name = "x" }).ExecuteAsync(ct),
        "DeepInsert.ExecuteAsync" => client.DeepInsert(new TestAccount { Name = "x" }).ExecuteAsync(ct),
        "Annotation.CreateAsync" => client.Annotation().Regarding<TestAccount>(Guid.NewGuid()).WithSubject("x").CreateAsync(ct),
        "Fetch.ExecuteAsync" => client.Fetch<TestAccount>("<fetch><entity name='account' /></fetch>").ExecuteAsync(ct),
        "Delta.ExecuteAsync" => client.Delta<TestAccount>().ExecuteAsync(ct),
        "Associate.AssociateAsync" => client.Associate().From<TestAccount>(Guid.NewGuid()).Via("nav").To<TestContact>(Guid.NewGuid()).AssociateAsync(ct),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task AlreadyCancelledToken_Throws_AndSendsNothing(string operation)
    {
        var handler = new TestHttpMessageHandler();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(CreateClient(handler), operation, Cancelled()));

        Assert.Empty(handler.Requests);
    }

    // Holds every request until it is cancelled.
    private sealed class HangingHandler : HttpMessageHandler
    {
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new UnreachableException();
        }
    }

    private sealed class UnreachableException : Exception;

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task CancellingAnInFlightRequest_Throws(string operation)
    {
        var handler = new HangingHandler();
        using var cts = new CancellationTokenSource();

        var task = Run(CreateClient(handler), operation, cts.Token);
        while (Volatile.Read(ref handler.Calls) == 0 && !task.IsCompleted)
            await Task.Delay(5);
        cts.Cancel();

        // The guard keeps a regression (a request that ignores the token) from hanging the run.
        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(task, finished);
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.IsNotType<TimeoutException>(ex.InnerException); // cancelled, not an HttpClient timeout
        Assert.Equal(1, handler.Calls);
    }

    // ────────── Cancelling between pages ──────────

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task QueryStreamAsync_CancelledBetweenPages_StopsBeforeTheNextRequest()
    {
        var handler = new TestHttpMessageHandler();
        handler.AddResponse(_ => true, Json($$"""
            { "value": [ { "name": "A" }, { "name": "B" } ], "@odata.nextLink": "{{BaseUrl}}/testaccounts?$skiptoken=2" }
            """));
        using var cts = new CancellationTokenSource();
        var seen = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var account in CreateClient(handler).Query<TestAccount>().StreamAsync(2, cts.Token))
            {
                seen.Add(account.Name);
                cts.Cancel();
            }
        });

        Assert.Equal(["A", "B"], seen); // the page already received is still yielded
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DeltaStreamAsync_CancelledBetweenPages_StopsBeforeTheNextRequest()
    {
        var handler = new TestHttpMessageHandler();
        handler.AddResponse(_ => true, Json($$"""
            { "value": [ { "name": "A" } ], "@odata.nextLink": "{{BaseUrl}}/testaccounts?$skiptoken=2" }
            """));
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in CreateClient(handler).Delta<TestAccount>().StreamAsync(1, cts.Token))
                cts.Cancel();
        });

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchStreamAsync_CancelledBetweenPages_StopsBeforeTheNextRequest()
    {
        var handler = new TestHttpMessageHandler();
        handler.AddResponse(_ => true, Json("""
            { "value": [ { "name": "A" } ], "@Microsoft.Dynamics.CRM.morerecords": true }
            """));
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in CreateClient(handler).Fetch<TestAccount>("<fetch><entity name='account' /></fetch>").StreamAsync(cts.Token))
                cts.Cancel();
        });

        Assert.Single(handler.Requests);
    }
}
