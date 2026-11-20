using Dataverse.Fluent.Schema.Metadata;

namespace Dataverse.Fluent.Schema.Validation;

/// <summary>A property type that does not fit its column.</summary>
internal sealed record TypeProblem(string Code, SchemaSeverity Severity, string Message, string? Suggestion);

/// <summary>
/// Which CLR types can hold a column's values. The column types map to these Web API types (checked against an
/// environment's <c>$metadata</c> on 2026-11-20): String, Memo and EntityName → Edm.String; Integer, Picklist, State
/// and Status → Edm.Int32; BigInt → Edm.Int64; Decimal and Money → Edm.Decimal; Double → Edm.Double; Boolean →
/// Edm.Boolean; Uniqueidentifier and lookups → Edm.Guid; DateTime → Edm.Date for the DateOnly behaviour and
/// Edm.DateTimeOffset otherwise (including UserLocal columns with the date-only format); multi-select choices →
/// Edm.String; images → Edm.Binary.
/// </summary>
internal static class ClrTypeRules
{
    /// <summary>
    /// The problem with holding <paramref name="column"/> in <paramref name="propertyType"/>, or <see langword="null"/>
    /// when it fits or the column's type is one the rules do not know.
    /// </summary>
    public static TypeProblem? Check(ColumnSchema column, Type propertyType)
    {
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        var typeName = Describe(propertyType);

        if (column.IsMultiSelectChoice)
        {
            return type == typeof(string) ? null
                : Incompatible($"'{column.LogicalName}' is a multi-select choice, which the Web API reads and writes as text such as \"1,3\"; {typeName} cannot hold it.", "string");
        }

        switch (column.AttributeType)
        {
            case "String" or "Memo" or "EntityName":
                return type == typeof(string) ? null : Incompatible($"'{column.LogicalName}' is a {column.AttributeType} column; {typeName} cannot hold text.", "string");

            case "Boolean":
                return type == typeof(bool) ? null : Incompatible($"'{column.LogicalName}' is a Boolean column; {typeName} cannot hold it.", "bool?");

            case "Uniqueidentifier":
                if (type == typeof(Guid))
                    return null;
                return type == typeof(string)
                    ? Lossy($"'{column.LogicalName}' is a uniqueidentifier column held as text.", "Guid?")
                    : Incompatible($"'{column.LogicalName}' is a uniqueidentifier column; {typeName} cannot hold it.", "Guid?");

            case "Integer" or "Picklist" or "State" or "Status":
                return CheckInteger(column, type, typeName);

            case "BigInt":
                if (type == typeof(long) || type == typeof(decimal))
                    return null;
                return IsInteger(type) || type == typeof(double) || type == typeof(float)
                    ? Lossy($"'{column.LogicalName}' is a BigInt column; {typeName} cannot hold every 64-bit value exactly.", "long?")
                    : Incompatible($"'{column.LogicalName}' is a BigInt column; {typeName} cannot hold it.", "long?");

            case "Decimal" or "Money":
                if (type == typeof(decimal))
                    return null;
                if (type == typeof(double) || type == typeof(float))
                    return Lossy($"'{column.LogicalName}' is a {column.AttributeType} column; {typeName} rounds decimal values.", "decimal?");
                return Incompatible($"'{column.LogicalName}' is a {column.AttributeType} column; {typeName} cannot hold fractional values.", "decimal?");

            case "Double":
                if (type == typeof(double) || type == typeof(decimal))
                    return null;
                return type == typeof(float)
                    ? Lossy($"'{column.LogicalName}' is a Double column; float loses precision.", "double?")
                    : Incompatible($"'{column.LogicalName}' is a Double column; {typeName} cannot hold it.", "double?");

            case "DateTime":
                return CheckDateTime(column, type, typeName);

            case "Lookup" or "Customer" or "Owner":
                return type == typeof(Guid) || type == typeof(DataverseReference) ? null
                    : Incompatible($"'{column.LogicalName}' is a lookup, read as an ID; {typeName} cannot hold it.", "Guid?");

            case "Virtual" when column.AttributeTypeName == "ImageType":
                return type == typeof(byte[]) || type == typeof(string) ? null
                    : Incompatible($"'{column.LogicalName}' is an image column, read as base64 data; {typeName} cannot hold it.", "byte[]");

            default:
                return null;
        }
    }

    private static TypeProblem? CheckInteger(ColumnSchema column, Type type, string typeName)
    {
        var kind = column.IsChoice ? "choice" : "whole number";
        if (type.IsEnum)
        {
            var underlying = Enum.GetUnderlyingType(type);
            return underlying == typeof(int) || underlying == typeof(long) ? null
                : Lossy($"'{column.LogicalName}' is a {kind} column; {typeName} has a {underlying.Name} underlying type, which cannot hold every value.", "an enum based on int");
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(decimal) || type == typeof(double))
            return null;
        if (type == typeof(short) || type == typeof(byte) || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong))
            return Lossy($"'{column.LogicalName}' is a {kind} column; {typeName} cannot hold every 32-bit value.", "int?");
        return Incompatible($"'{column.LogicalName}' is a {kind} column, read as a number; {typeName} cannot hold it.", column.IsChoice ? "int? or an enum" : "int?");
    }

    private static TypeProblem? CheckDateTime(ColumnSchema column, Type type, string typeName)
    {
        if (column.DateTimeBehavior == "DateOnly")
        {
            if (type == typeof(DateOnly))
                return null;
            return type == typeof(DateTime) || type == typeof(DateTimeOffset)
                ? Incompatible($"'{column.LogicalName}' is a date-only column (Edm.Date); Dataverse rejects a date and time for it on write (HTTP 400).", "DateOnly?")
                : Incompatible($"'{column.LogicalName}' is a date-only column (Edm.Date); {typeName} cannot hold it.", "DateOnly?");
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
            return null;
        return type == typeof(DateOnly)
            ? Incompatible($"'{column.LogicalName}' is a date and time column (Edm.DateTimeOffset, {column.DateTimeBehavior ?? "UserLocal"} behaviour), not date-only; DateOnly cannot read it.", "DateTime?")
            : Incompatible($"'{column.LogicalName}' is a date and time column; {typeName} cannot hold it.", "DateTime?");
    }

    private static bool IsInteger(Type type)
        => type == typeof(int) || type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ushort) || type == typeof(sbyte);

    private static TypeProblem Incompatible(string message, string suggested)
        => new(SchemaCodes.IncompatibleType, SchemaSeverity.Error, message, $"Use {suggested}.");

    private static TypeProblem Lossy(string message, string suggested)
        => new(SchemaCodes.LossyType, SchemaSeverity.Warning, message, $"Use {suggested}.");

    /// <summary>A type as C# writes it: <c>int?</c>, <c>DateTime</c>, <c>List&lt;string&gt;</c>.</summary>
    public static string Describe(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return Describe(underlying) + "?";
        if (type.IsArray)
            return Describe(type.GetElementType()!) + "[]";
        if (type.IsGenericType)
            return $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>";

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => "bool",
            TypeCode.Byte => "byte",
            TypeCode.SByte => "sbyte",
            TypeCode.Int16 => "short",
            TypeCode.UInt16 => "ushort",
            TypeCode.Int32 when !type.IsEnum => "int",
            TypeCode.UInt32 when !type.IsEnum => "uint",
            TypeCode.Int64 when !type.IsEnum => "long",
            TypeCode.UInt64 when !type.IsEnum => "ulong",
            TypeCode.Single => "float",
            TypeCode.Double => "double",
            TypeCode.Decimal => "decimal",
            TypeCode.String => "string",
            _ => type.Name
        };
    }
}
