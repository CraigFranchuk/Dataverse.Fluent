using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Shared, cached <see cref="JsonSerializerOptions"/> instances used across builders.
/// <see cref="JsonSerializerOptions"/> is expensive to construct and thread-safe once built,
/// so a single static instance avoids repeated allocations on every deserialization call.
/// All instances use <see cref="DataverseColumnResolver"/> so that JSON property names match
/// the names used when building URLs.
/// </summary>
/// <remarks>
/// <see cref="JsonSerializerOptions"/> caches type metadata for its lifetime, so the instances are
/// rebuilt whenever the registered <see cref="IDataverseNameResolver"/> set changes.
/// </remarks>
internal static class JsonDefaults
{
    private static volatile Snapshot _current = new();

    static JsonDefaults()
    {
        DataverseTypeMapping.ResolversChanged += () => _current = new Snapshot();
    }

    /// <summary>Reads responses (records, collections, action results).</summary>
    internal static JsonSerializerOptions CaseInsensitive => _current.CaseInsensitive;

    /// <summary>Writes a non-entity object without its null members (action request parameters).</summary>
    internal static JsonSerializerOptions IgnoreNull => _current.IgnoreNull;

    /// <summary>
    /// Writes an entity as a create or update payload: null members omitted, lookups as binds, navigation
    /// properties skipped, column names lowercase unless mapped (see <see cref="DataverseColumnResolver.EntityWrite"/>).
    /// </summary>
    internal static JsonSerializerOptions EntityPayload => _current.EntityPayload;

    /// <summary>Writes individual values and finished JSON payloads.</summary>
    internal static JsonSerializerOptions PayloadSerialization => _current.PayloadSerialization;

    /// <summary>
    /// Writes a record as Dataverse returns it, so <see cref="CaseInsensitive"/> reads it back (see
    /// <see cref="DataverseColumnResolver.Response"/>). Nulls are written, as Dataverse does.
    /// </summary>
    internal static JsonSerializerOptions Response => _current.Response;

    private sealed class Snapshot
    {
        public readonly JsonSerializerOptions CaseInsensitive = new()
        {
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = DataverseColumnResolver.Read,
            PropertyNamingPolicy = DataverseColumnResolver.ProvisionalNames
        };

        public readonly JsonSerializerOptions IgnoreNull = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = DataverseColumnResolver.Read,
            PropertyNamingPolicy = DataverseColumnResolver.ProvisionalNames
        };

        public readonly JsonSerializerOptions EntityPayload = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = DataverseColumnResolver.EntityWrite,
            PropertyNamingPolicy = DataverseColumnResolver.ProvisionalNames
        };

        public readonly JsonSerializerOptions PayloadSerialization = new()
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = DataverseColumnResolver.Read,
            PropertyNamingPolicy = DataverseColumnResolver.ProvisionalNames
        };

        public readonly JsonSerializerOptions Response = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = DataverseColumnResolver.Response,
            PropertyNamingPolicy = DataverseColumnResolver.ProvisionalNames
        };
    }
}
