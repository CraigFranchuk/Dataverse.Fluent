using System.Text.RegularExpressions;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Parses record IDs from the <c>OData-EntityId</c> / <c>Location</c> URLs Dataverse returns for
/// created records, e.g. <c>https://org.crm.dynamics.com/api/data/v9.2/accounts(00000000-0000-0000-0000-000000000001)</c>.
/// </summary>
internal static partial class ODataEntityId
{
    internal const string HeaderName = "OData-EntityId";

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidRegex();

    /// <summary>
    /// Extracts the record ID from an entity URL, or a bare GUID. Only the key segment (the text after
    /// the last '(') is searched, so composite keys such as elastic-table
    /// <c>accounts(accountid=...,partitionid='p1')</c> resolve to the primary key.
    /// </summary>
    internal static Guid? TryParse(string? entityIdUrl)
    {
        if (string.IsNullOrWhiteSpace(entityIdUrl))
            return null;

        var trimmed = entityIdUrl.Trim();
        if (Guid.TryParse(trimmed, out var direct))
            return direct;

        var keyStart = trimmed.LastIndexOf('(');
        var keySegment = keyStart >= 0 ? trimmed[(keyStart + 1)..] : trimmed;
        var match = GuidRegex().Match(keySegment);
        return match.Success && Guid.TryParse(match.Value, out var id) ? id : null;
    }

    /// <summary>
    /// Reads the <c>OData-EntityId</c> header (preferred) or <c>Location</c> header from a response.
    /// </summary>
    internal static string? GetEntityUrl(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues(HeaderName, out var values))
        {
            var value = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return response.Headers.Location?.ToString();
    }
}
