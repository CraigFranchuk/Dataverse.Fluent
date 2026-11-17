namespace Dataverse.Fluent.Builders;

/// <summary>
/// Shared constants and helpers for Dataverse OData paging.
/// </summary>
/// <remarks>
/// Dataverse pages results with the <c>Prefer: odata.maxpagesize</c> header and <c>@odata.nextLink</c>.
/// <c>$top</c> limits the total number of rows and must not be used as a page size, and <c>$skip</c>
/// is not supported at all.
/// </remarks>
internal static class ODataPaging
{
    /// <summary>The largest page size Dataverse honors for standard tables.</summary>
    internal const int MaxPageSize = 5000;

    internal const string FormattedValuesPreference = "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"";

    internal static string MaxPageSizePreference(int pageSize) => $"odata.maxpagesize={pageSize}";

    /// <summary>
    /// Records a <c>@odata.nextLink</c> about to be followed. Throws when it was already followed: a
    /// server that links back to a page already read would otherwise make paging loop forever.
    /// </summary>
    internal static void TrackNextLink(HashSet<string> followed, string nextLink)
    {
        if (!followed.Add(nextLink))
            throw new InvalidOperationException(
                $"Dataverse returned an @odata.nextLink that was already followed ({nextLink}); paging stopped to avoid an infinite loop.");
    }
}
