using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Serializes a POCO into the JSON body of a write: columns mapped by the name resolvers, <see langword="null"/>
/// properties left out, lookups written as <c>@odata.bind</c>, and properties marked <see cref="DataverseReadOnlyAttribute"/>
/// for the operation removed. Shared by the batch and bulk builders so that both write a record the same way.
/// </summary>
internal static class EntityPayload
{
    /// <summary>
    /// Serializes <paramref name="entity"/>, removing the properties that are read-only for any of
    /// <paramref name="operations"/>.
    /// </summary>
    /// <remarks>
    /// Operations that can update a record pass both Create and Update: a property marked read-only for Create only
    /// (e.g. statecode) must not send its default value over an existing record.
    /// </remarks>
    internal static JsonNode Serialize<T>(T entity, params DataverseOperation[] operations) where T : class
    {
        var serialized = JsonSerializer.SerializeToNode(entity, JsonDefaults.EntityPayload);
        if (serialized is not JsonObject jsonObject)
            return serialized ?? new JsonObject();

        foreach (var operation in operations)
            PropertyFilter.RemoveReadOnlyProperties<T>(jsonObject, operation);

        return jsonObject;
    }
}
