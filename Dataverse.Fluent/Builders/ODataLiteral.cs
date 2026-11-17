using System.Globalization;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Formats CLR values as OData literals. Shared by <c>$filter</c> translation, alternate-key predicates
/// and function parameters so that a value is written the same way wherever it is used.
/// </summary>
/// <remarks>
/// All output is culture-invariant. Date/time values are written in UTC: a <see cref="DateTimeKind.Local"/>
/// <see cref="DateTime"/> and a <see cref="DateTimeOffset"/> are converted, and a
/// <see cref="DateTimeKind.Unspecified"/> <see cref="DateTime"/> is taken to be UTC already.
/// </remarks>
internal static class ODataLiteral
{
    /// <summary>
    /// Formats a value as an OData literal: strings are quoted (embedded quotes doubled), numbers,
    /// booleans, dates and enums (by their integer value) are not.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <param name="quoteGuids">
    /// Whether a <see cref="Guid"/> is wrapped in single quotes. Dataverse accepts quoted GUIDs in
    /// <c>$filter</c>; key predicates take them bare.
    /// </param>
    public static string Format(object? value, bool quoteGuids = true) => value switch
    {
        null => "null",
        string s => Quote(s),
        char c => Quote(c.ToString()),
        bool b => b ? "true" : "false",
        Guid g => quoteGuids ? $"'{g}'" : g.ToString(),
        // Choice (option set) values are matched by their integer value, not the enum member name.
        Enum e => Convert.ToString(
            Convert.ChangeType(e, Enum.GetUnderlyingType(e.GetType()), CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture)!,
        // OData v4 date/time literals are unquoted ISO 8601.
        DateTime dt => FormatDateTime(dt),
        DateTimeOffset dto => FormatDateTime(dto),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        sbyte or byte or short or ushort or int or uint or long or ulong or decimal or double or float
            => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        IFormattable f => Quote(f.ToString(null, CultureInfo.InvariantCulture)),
        _ => Quote(value.ToString() ?? string.Empty)
    };

    /// <summary>Formats a <see cref="DateTime"/> as <c>yyyy-MM-ddTHH:mm:ssZ</c> in UTC.</summary>
    public static string FormatDateTime(DateTime value)
        => (value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>Formats a <see cref="DateTimeOffset"/> as <c>yyyy-MM-ddTHH:mm:ssZ</c> in UTC.</summary>
    public static string FormatDateTime(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>Wraps text in single quotes, doubling any embedded single quote.</summary>
    public static string Quote(string text) => $"'{text.Replace("'", "''")}'";
}
