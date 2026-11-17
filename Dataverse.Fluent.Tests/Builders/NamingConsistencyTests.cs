using System.Net;
using System.Reflection;

namespace Dataverse.Fluent.Tests.Builders;

/// <summary>
/// URL building ($select/$filter) and JSON payloads must agree on property names; Dataverse property
/// names are case-sensitive, so a mismatch makes writes fail and reads silently lose values.
/// </summary>
public class NamingConsistencyTests
{
    [EntitySet("plains")]
    private class PlainEntity
    {
        public string? FullName { get; set; }
    }

    // Stands in for a POCO produced by an external source generator: no library attributes.
    private class GeneratedEntity
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
    }

    // Action/function payload types are not entity types and keep default (PascalCase) naming.
    private class ActionRequest
    {
        public string? Target { get; set; }
    }

    private sealed class GeneratedResolver : IDataverseNameResolver
    {
        public string? ResolvePropertyName(MemberInfo member)
            => member.DeclaringType == typeof(GeneratedEntity)
                ? member.Name == nameof(GeneratedEntity.Id) ? "new_generatedid" : "new_" + member.Name.ToLowerInvariant()
                : null;

        public string? ResolveEntitySetName(Type type)
            => type == typeof(GeneratedEntity) ? "new_generateds" : null;
    }

    // The registry is process-wide; the resolver only answers for GeneratedEntity, so registering it
    // once cannot affect other tests.
    private static readonly Lazy<bool> ResolverRegistered = new(() =>
    {
        DataverseTypeMapping.RegisterResolver(new GeneratedResolver());
        return true;
    });

    private static (DataverseClient Client, List<(string Url, string Body)> Requests) CreateClient(params object[] responses)
    {
        var handler = new TestHttpMessageHandler();
        var requests = new List<(string, string)>();
        foreach (var response in responses)
        {
            handler.AddJsonResponse(req =>
            {
                requests.Add((req.DecodedUrl(), req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty));
                return true;
            }, response);
        }

        var client = new DataverseClient("https://test.crm.dynamics.com/api/data/v9.2", () => Task.FromResult("t"), new HttpClient(handler));
        return (client, requests);
    }

    [Fact]
    public async Task EntityWithoutAttributes_UsesLowercaseNamesInUrlAndPayload()
    {
        var (client, requests) = CreateClient(new { value = Array.Empty<object>() }, new { });

        await client.Query<PlainEntity>().Select(p => p.FullName).GetListAsync();
        await client.Entity<PlainEntity>().SetFrom(new PlainEntity { FullName = "x" }).CreateAsync();

        Assert.EndsWith("plains?$select=fullname", requests[0].Url);
        Assert.Equal("{\"fullname\":\"x\"}", requests[1].Body);
    }

    [Fact]
    public async Task CustomResolver_AppliesToUrlPayloadAndDeserialization()
    {
        _ = ResolverRegistered.Value;
        var id = Guid.NewGuid();
        var (client, requests) = CreateClient(
            new { value = new[] { new Dictionary<string, object> { ["new_generatedid"] = id, ["new_title"] = "read" } } },
            new { });

        var read = await client.Query<GeneratedEntity>().Select(g => new { g.Id, g.Title }).GetListAsync();
        await client.Entity<GeneratedEntity>().SetFrom(new GeneratedEntity { Id = id, Title = "t" }).CreateAsync();

        Assert.EndsWith("new_generateds?$select=new_generatedid,new_title", requests[0].Url);
        Assert.Equal(id, read[0].Id);
        Assert.Equal("read", read[0].Title);
        Assert.Equal($"{{\"new_generatedid\":\"{id}\",\"new_title\":\"t\"}}", requests[1].Body);
    }

    [Fact]
    public async Task ActionRequest_KeepsPascalCaseParameterNames()
    {
        var (client, requests) = CreateClient(new { });

        await client.Action<ActionRequest, object>("DoSomething").WithRequest(new ActionRequest { Target = "x" }).ExecuteAsync();

        Assert.Equal("{\"Target\":\"x\"}", requests[0].Body);
    }
}
