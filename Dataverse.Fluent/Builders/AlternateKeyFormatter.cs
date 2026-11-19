namespace Dataverse.Fluent.Builders;

/// <summary>
/// Where an alternate-key predicate is written, which decides how its values are encoded.
/// </summary>
internal enum KeyPlacement
{
    /// <summary>
    /// In the URL path, e.g. <c>accounts(accountnumber='A B')</c> addressing a record. Values are percent-encoded.
    /// </summary>
    UrlPath,

    /// <summary>
    /// In a JSON body, e.g. an <c>@odata.bind</c> value. Dataverse does not URL-decode it, so values are written as is.
    /// </summary>
    JsonBody
}

/// <summary>
/// Formats alternate-key values into OData key predicates.
/// </summary>
/// <remarks>
/// <para>
/// String values are wrapped in single quotes, and an embedded single quote is doubled. In the URL path, characters that
/// would corrupt URL parsing (<c>%</c>, space, <c>&amp;</c>, <c>+</c>, <c>#</c>, <c>?</c>, <c>/</c>) are percent-encoded.
/// In a JSON body nothing is percent-encoded: Dataverse reads the text as is, so <c>%20</c> there would be looked up
/// literally.
/// </para>
/// <para>
/// Some characters cannot be used in a key at all; this was confirmed against Dataverse on 2026-11-18. A value
/// containing them is rejected before anything is sent:
/// </para>
/// <list type="bullet">
/// <item>In the URL path: <c>/</c> and <c>\</c>, which Dataverse's OData parser rejects in any form. A key
/// containing <c>&amp;</c>, <c>?</c> or <c>+</c> can be addressed, but only inside <c>$batch</c>, because IIS rejects
/// them in a request path (see <see cref="KeyedRequestSender"/>).</item>
/// <item>In a JSON body: <c>/</c>, <c>\</c>, <c>#</c> and <c>?</c>.</item>
/// </list>
/// </remarks>
internal static class AlternateKeyFormatter
{
    private static readonly char[] UnaddressableInPath = ['/', '\\'];
    private static readonly char[] UnbindableInBody = ['/', '\\', '#', '?'];

    /// <summary>
    /// Formats a single alternate-key value for use inside an OData key predicate.
    /// </summary>
    /// <param name="value">The key value. <see langword="null"/> is rendered as the OData <c>null</c> literal.</param>
    /// <param name="placement">Where the predicate is written.</param>
    /// <returns>The encoded, OData-compliant key value.</returns>
    /// <exception cref="ArgumentException">A string value contains a character Dataverse cannot accept in a key there.</exception>
    public static string FormatValue(object? value, KeyPlacement placement = KeyPlacement.UrlPath)
    {
        if (value is string s)
        {
            ThrowIfUnusable(s, placement);
            var literal = s.Replace("'", "''");
            return placement == KeyPlacement.UrlPath ? $"'{PercentEncodeReserved(literal)}'" : $"'{literal}'";
        }

        // Every other type is formatted as in $filter (dates in UTC, enums by integer value) with GUIDs left bare.
        var formatted = ODataLiteral.Format(value, quoteGuids: false);
        return placement == KeyPlacement.UrlPath ? PercentEncodeReserved(formatted) : formatted;
    }

    /// <summary>
    /// Throws when a string key value contains a character Dataverse cannot accept in a key at
    /// <paramref name="placement"/>, so the caller learns it before a request is sent.
    /// </summary>
    /// <exception cref="ArgumentException">The value cannot be used as a key there.</exception>
    public static void ThrowIfUnusable(object? value, KeyPlacement placement = KeyPlacement.UrlPath)
    {
        if (value is not string s)
            return;

        if (placement == KeyPlacement.UrlPath && s.IndexOfAny(UnaddressableInPath) >= 0)
        {
            throw new ArgumentException(
                $"The alternate key value '{s}' cannot be addressed by key: Dataverse rejects '/' and '\\' in a key in a URL. " +
                "Use the record's ID, or find the record with a filter (Query<T>().Where(...)).", nameof(value));
        }

        if (placement == KeyPlacement.JsonBody && s.IndexOfAny(UnbindableInBody) >= 0)
        {
            throw new ArgumentException(
                $"The alternate key value '{s}' cannot be used to bind a lookup: Dataverse rejects '/', '\\', '#' and '?' in a key " +
                "in @odata.bind. Bind by the record's ID instead (SetLookup).", nameof(value));
        }
    }

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
    public static string FormatKeyValue(string keyName, object? value, KeyPlacement placement = KeyPlacement.UrlPath)
        => $"{keyName}={FormatValue(value, placement)}";

    /// <summary>
    /// Formats a composite alternate key into a comma-separated OData key predicate body.
    /// </summary>
    public static string FormatCompositeKey(IEnumerable<KeyValuePair<string, object>> alternateKeys, KeyPlacement placement = KeyPlacement.UrlPath)
        => string.Join(",", alternateKeys.Select(kvp => FormatKeyValue(kvp.Key, kvp.Value, placement)));
}
