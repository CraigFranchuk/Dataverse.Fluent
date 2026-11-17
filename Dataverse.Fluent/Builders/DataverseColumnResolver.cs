using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// A <see cref="IJsonTypeInfoResolver"/> that applies the same name resolution used for URL building
/// (<see cref="ODataPropertyNameCache"/>) to JSON serialization and deserialization, so that
/// <c>$select</c>/<c>$filter</c> and request payloads always agree on property names.
/// <para>
/// Resolution order: registered <see cref="IDataverseNameResolver"/> strategies →
/// <see cref="DataverseLookupAttribute"/> → <see cref="DataverseColumnAttribute"/> →
/// <see cref="JsonPropertyNameAttribute"/> → lowercase member name.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Read"/> is used for responses and for payloads that are not entities (action parameters): the
/// lowercase fallback applies only to entity types, so action request types with PascalCase parameter names
/// keep the default naming.
/// </para>
/// <para>
/// <see cref="EntityWrite"/> is used for create and update payloads. Every object is an entity there, so the
/// lowercase fallback always applies; lookups (<see cref="DataverseLookupAttribute"/>) are written as
/// <c>{navigation}@odata.bind</c>; and navigation properties (related records) are never written, so a
/// retrieved record with expanded relations can be sent back with <c>SetFrom</c>.
/// </para>
/// </remarks>
internal sealed class DataverseColumnResolver : DefaultJsonTypeInfoResolver
{
    internal static readonly DataverseColumnResolver Read = new(write: false);

    internal static readonly DataverseColumnResolver EntityWrite = new(write: true);

    private readonly bool _write;

    private DataverseColumnResolver(bool write)
    {
        _write = write;
    }

    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var typeInfo = base.GetTypeInfo(type, options);
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return typeInfo;

        if (_write)
            ConfigureForWrite(typeInfo);
        else
            ConfigureForRead(typeInfo, type);

        return typeInfo;
    }

    private static void ConfigureForRead(JsonTypeInfo typeInfo, Type type)
    {
        var isEntityType = DataverseTypeMapping.ResolveEntitySetName(type) != null;

        foreach (var property in typeInfo.Properties)
        {
            if (property.AttributeProvider is not MemberInfo member)
            {
                property.Name = ProvisionalNamingPolicy.Strip(property.Name);
                continue;
            }

            var resolved = DataverseTypeMapping.ResolvePropertyName(member);
            if (resolved != null)
                property.Name = resolved;
            else if (isEntityType)
                property.Name = member.Name.ToLowerInvariant();
            else
                property.Name = ProvisionalNamingPolicy.Strip(property.Name);
        }
    }

    private static void ConfigureForWrite(JsonTypeInfo typeInfo)
    {
        var metadata = EntityTypeMetadata.For(typeInfo.Type);

        foreach (var property in typeInfo.Properties)
        {
            if (property.AttributeProvider is not MemberInfo member)
            {
                property.Name = ProvisionalNamingPolicy.Strip(property.Name);
                continue;
            }

            var mapped = member is PropertyInfo ? metadata.Find(member) : null;
            switch (mapped?.Kind)
            {
                case EntityMemberKind.Navigation:
                case EntityMemberKind.Ignored:
                    property.Name = mapped.Name;
                    property.ShouldSerialize = static (_, _) => false;
                    break;

                case EntityMemberKind.Lookup:
                    property.Name = mapped.WriteName;
                    property.CustomConverter = property.PropertyType == typeof(Guid)
                        ? new LookupBindConverter(mapped.Lookup!.TargetEntitySet)
                        : new NullableLookupBindConverter(mapped.Lookup!.TargetEntitySet);
                    // An unset lookup is not written; clearing one is an explicit Set(x => x.Lookup, null).
                    property.ShouldSerialize = static (_, value) => value is Guid id && id != Guid.Empty;
                    break;

                default:
                    property.Name = mapped?.Name
                        ?? DataverseTypeMapping.ResolvePropertyName(member)
                        ?? member.Name.ToLowerInvariant();
                    break;
            }
        }
    }

    internal static string FormatBind(string entitySet, Guid id) => $"/{entitySet}({id})";

    /// <summary>
    /// The naming policy every option set uses. System.Text.Json rejects duplicate names (case-insensitively,
    /// with <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/>) while it builds a type's properties,
    /// before this resolver renames them. A lookup <c>PrimaryContactId</c> next to an expanded record mapped to
    /// <c>"primarycontactid"</c> would collide there although their final names differ (<c>_primarycontactid_value</c>).
    /// The policy gives unmapped CLR names a provisional prefix so they cannot collide; this resolver then sets
    /// every property's final name.
    /// </summary>
    internal static readonly ProvisionalNamingPolicy ProvisionalNames = new();

    internal sealed class ProvisionalNamingPolicy : JsonNamingPolicy
    {
        private const char Marker = '\u0001';

        public override string ConvertName(string name) => Marker + name;

        public static string Strip(string name) => name.Length > 0 && name[0] == Marker ? name[1..] : name;
    }

    // Writes a lookup as the bind reference "/{entityset}({id})". Write-only: responses carry _x_value instead.
    private sealed class LookupBindConverter(string entitySet) : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException("Lookup binds are written, not read.");

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
            => writer.WriteStringValue(FormatBind(entitySet, value));
    }

    private sealed class NullableLookupBindConverter(string entitySet) : JsonConverter<Guid?>
    {
        public override bool HandleNull => true;

        public override Guid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException("Lookup binds are written, not read.");

        public override void Write(Utf8JsonWriter writer, Guid? value, JsonSerializerOptions options)
        {
            if (value is { } id && id != Guid.Empty)
                writer.WriteStringValue(FormatBind(entitySet, id));
            else
                writer.WriteNullValue();
        }
    }
}
