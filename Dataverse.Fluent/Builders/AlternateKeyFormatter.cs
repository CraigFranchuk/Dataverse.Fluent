namespace Dataverse.Fluent.Builders;

/// <summary>
/// Formats alternate-key values into OData-compliant URL key segments.
/// </summary>
/// <remarks>
/// String values are wrapped in single-quote delimiters (the OData string-literal delimiters,
/// which are valid unencoded in a URL path). Any single quote inside the value is doubled per the
/// OData literal-escaping rule, and characters that would otherwise corrupt URL/OData path parsing
/// (e.g., <c>%</c>, space, <c>&amp;</c>, <c>+</c>, <c>#</c>, <c>?</c> and <c>/</c>) are percent-encoded.
/// Path-safe characters such as <c>@</c> are intentionally left unencoded.
/// </remarks>
internal static class AlternateKeyFormatter
{
    /// <summary>
    /// Formats a single alternate-key value for use inside an OData key predicate.
    /// </summary>
    /// <param name="value">The key value. <see langword="null"/> is rendered as the OData <c>null</c> literal.</param>
    /// <returns>The encoded, OData-compliant key value.</returns>
    public static string FormatValue(object? value)
    {
        // Strings are escaped as a whole literal; every other type is formatted as in $filter (dates in UTC,
        // enums by integer value) with GUIDs left bare, then percent-encoded for the URL path.
        return value is string s
            ? $"'{EscapeStringLiteral(s)}'"
            : PercentEncodeReserved(ODataLiteral.Format(value, quoteGuids: false));
    }

    // Escapes the content of a quoted OData string literal: double embedded single quotes
    // (OData rule), then percent-encode characters that would break URL/path parsing.
    private static string EscapeStringLiteral(string value)
        => PercentEncodeReserved(value.Replace("'", "''"));

    private static string PercentEncodeReserved(string value)
    {
        // Encode '%' first so subsequently inserted escape sequences are not re-encoded.
        return value
            .Replace("%", "%25")
            .Replace(" ", "%20")
            .Replace("#", "%23")
            .Replace("&", "%26")
            .Replace("+", "%2B")
            .Replace("/", "%2F")
            .Replace("?", "%3F");
    }

    /// <summary>
    /// Formats a single key name/value pair (e.g., <c>keyname='value'</c>) with the value encoded.
    /// </summary>
    public static string FormatKeyValue(string keyName, object? value)
        => $"{keyName}={FormatValue(value)}";

    /// <summary>
    /// Formats a composite alternate key into a comma-separated OData key predicate body.
    /// </summary>
    public static string FormatCompositeKey(IEnumerable<KeyValuePair<string, object>> alternateKeys)
        => string.Join(",", alternateKeys.Select(kvp => FormatKeyValue(kvp.Key, kvp.Value)));
}
