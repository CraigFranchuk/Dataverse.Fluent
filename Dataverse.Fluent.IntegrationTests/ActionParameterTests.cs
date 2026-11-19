using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Dataverse.Fluent.IntegrationTests.Models;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.IntegrationTests;

// Action parameters and a bound action against live Dataverse. A 2026-11-18 probe found that Dataverse reads an enum
// action parameter only by member name ("ReadAccess", or "ReadAccess, WriteAccess" for flags) and rejects the number,
// which ActionBuilder sent until 1.0.2. It also found that a zone-less date in a JSON body is read as UTC whatever the
// user's time zone, so a DateTime of any kind is written correctly.
// Records created here are deleted by the fixture when the run ends.
[Collection("Dataverse Integration Tests")]
public class ActionParameterTests : IClassFixture<DataverseTestFixture>
{
    private readonly DataverseTestFixture _fixture;

    public ActionParameterTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Members as in Dataverse's Microsoft.Dynamics.CRM.AccessRights enum.
    [Flags]
    public enum AccessRights
    {
        None = 0,
        ReadAccess = 1,
        WriteAccess = 2,
        AppendAccess = 4,
    }

    public class SharedLinkRequest
    {
        [JsonPropertyName("Target")] public JsonObject? Target { get; set; }
        [JsonPropertyName("SharedRights")] public AccessRights SharedRights { get; set; }
    }

    public class SharedLinkResponse
    {
        [JsonPropertyName("Response")] public string? Response { get; set; }
    }

    private async Task<JsonObject> NewAccountTargetAsync()
    {
        var id = (await _fixture.Client.Entity<Account>().Set(a => a.Name, $"Action params {Guid.NewGuid()}").ReturnRepresentation().CreateAsync(Ct)).Entity!.AccountId;
        return new JsonObject { ["accountid"] = id, ["@odata.type"] = "Microsoft.Dynamics.CRM.account" };
    }

    [Theory]
    [InlineData(AccessRights.ReadAccess)]
    [InlineData(AccessRights.ReadAccess | AccessRights.WriteAccess)]
    public async Task AnEnumParameter_IsAccepted(AccessRights rights)
    {
        var target = await NewAccountTargetAsync();

        var link = await _fixture.Client.Action<SharedLinkRequest, SharedLinkResponse>("GenerateSharedLink")
            .WithParameter(p => p.Target, target)
            .WithParameter(p => p.SharedRights, rights)
            .ExecuteAsync(Ct);

        Assert.StartsWith("shareLink=", link!.Response);
        // Not revoked: RevokeSharedLink fails here with "The system managed team can't be deleted", a Dataverse error
        // unrelated to the parameters. The link goes when the fixture deletes the account.
    }

    [Fact]
    public async Task AnEnumProperty_OfWithRequest_IsAccepted()
    {
        var target = await NewAccountTargetAsync();

        var link = await _fixture.Client.Action<SharedLinkRequest, SharedLinkResponse>("GenerateSharedLink")
            .WithRequest(new SharedLinkRequest { Target = target, SharedRights = AccessRights.ReadAccess | AccessRights.AppendAccess })
            .ExecuteAsync(Ct);

        Assert.StartsWith("shareLink=", link!.Response);
    }

    [Fact]
    public async Task AnEnumSentAsANumber_IsRejectedByDataverse()
    {
        // Pins why ActionBuilder writes names: the raw number fails.
        var target = await NewAccountTargetAsync();

        var ex = await Assert.ThrowsAsync<DataverseException>(() => _fixture.Client.Transport.ExecuteRequestAsync(
            HttpMethod.Post, "GenerateSharedLink", new JsonObject { ["Target"] = target, ["SharedRights"] = 1 }, ct: Ct));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.StatusCode);
    }

    // ───────── Bound action ─────────

    [EntitySet("queues")]
    public class Queue
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("queueid")] public Guid QueueId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    }

    [EntitySet("tasks")]
    public class TaskActivity
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("activityid")] public Guid ActivityId { get; set; }
        [JsonPropertyName("subject")] public string Subject { get; set; } = string.Empty;
    }

    [EntitySet("queueitems")]
    public class QueueItem
    {
        [JsonPropertyName("queueitemid")] public Guid QueueItemId { get; set; }
        [JsonPropertyName("_queueid_value")] public Guid? QueueId { get; set; }
        [JsonPropertyName("_objectid_value")] public Guid? ObjectId { get; set; }
    }

    public class AddToQueueRequest
    {
        [JsonPropertyName("Target")] public JsonObject? Target { get; set; }
    }

    public class AddToQueueResponse
    {
        [JsonPropertyName("QueueItemId")] public Guid QueueItemId { get; set; }
    }

    [Fact]
    public async Task ABoundAction_RunsOnTheRecord_AndReturnsItsResponse()
    {
        var queueId = (await _fixture.Client.Entity<Queue>().Set(q => q.Name, $"Action test {Guid.NewGuid()}").ReturnRepresentation().CreateAsync(Ct)).Entity!.QueueId;
        var taskId = (await _fixture.Client.Entity<TaskActivity>().Set(t => t.Subject, $"Action test {Guid.NewGuid()}").ReturnRepresentation().CreateAsync(Ct)).Entity!.ActivityId;

        try
        {
            var response = await _fixture.Client.ForEntity<Queue>(queueId)
                .Action<AddToQueueRequest, AddToQueueResponse>("AddToQueue")
                .WithParameter(p => p.Target, new JsonObject { ["activityid"] = taskId, ["@odata.type"] = "Microsoft.Dynamics.CRM.task" })
                .ExecuteAsync(Ct);

            Assert.NotEqual(Guid.Empty, response!.QueueItemId);
            var item = await _fixture.Client.Entity<QueueItem>(response.QueueItemId).RetrieveAsync(Ct);
            Assert.Equal(queueId, item!.QueueId);
            Assert.Equal(taskId, item.ObjectId);
        }
        finally
        {
            // The task first: deleting it removes its queue item, and a queue holding an item cannot be deleted.
            await _fixture.Client.Entity<TaskActivity>(taskId).DeleteAsync(CancellationToken.None);
            await _fixture.Client.Entity<Queue>(queueId).DeleteAsync(CancellationToken.None);
        }
    }
}
