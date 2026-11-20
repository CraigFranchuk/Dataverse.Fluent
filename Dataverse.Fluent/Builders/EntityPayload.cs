using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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

    // Options that write a value with one property's converter, built once per converter.
    private static readonly ConditionalWeakTable<JsonConverter, JsonSerializerOptions> ConverterOptions = new();

    /// <summary>
    /// Serializes <paramref name="value"/> as the <c>[JsonConverter]</c> on <paramref name="property"/> writes it, so
    /// <c>Set(x =&gt; x.Property, value)</c> sends what <c>SetFrom</c> sends. Returns <see langword="false"/> when the
    /// property has no converter of its own, or the value is <see langword="null"/>.
    /// </summary>
    internal static bool TrySerializeWithPropertyConverter(Type entityType, PropertyInfo property, object? value, out JsonNode? node)
    {
        node = null;
        if (value is null)
            return false;

        var info = JsonDefaults.EntityPayload.GetTypeInfo(entityType).Properties
            .FirstOrDefault(p => p.AttributeProvider is PropertyInfo candidate && candidate.Name == property.Name);
        if (info?.CustomConverter is not { } converter)
            return false;

        var options = ConverterOptions.GetValue(converter,
            static c => new JsonSerializerOptions(JsonDefaults.PayloadSerialization) { Converters = { c } });

        // Serialized as the property's type: a converter for DateTime? does not handle a boxed DateTime.
        node = JsonSerializer.SerializeToNode(value, info.PropertyType, options);
        return true;
    }
}
