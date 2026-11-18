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

    internal const string FormattedValueAnnotation = "OData.Community.Display.V1.FormattedValue";

    /// <summary>The annotation that says which table a lookup value points at.</summary>
    internal const string LookupLogicalNameAnnotation = "Microsoft.Dynamics.CRM.lookuplogicalname";

    internal const string FormattedValuesPreference = "odata.include-annotations=\"" + FormattedValueAnnotation + "\"";

    /// <summary>
    /// Returns the <c>odata.include-annotations</c> preference for reading records of <paramref name="entityType"/>,
    /// or <see langword="null"/> when none is needed: formatted values when requested, and the lookup table
    /// annotation when the type, or a related record it holds, has a polymorphic lookup.
    /// </summary>
    internal static string? IncludeAnnotationsPreference(Type? entityType, bool formattedValues, params string[] others)
    {
        var annotations = new List<string>(2 + others.Length);
        if (formattedValues)
            annotations.Add(FormattedValueAnnotation);
        if (entityType is not null && EntityTypeMetadata.For(entityType).ReadsLookupLogicalNames)
            annotations.Add(LookupLogicalNameAnnotation);
        annotations.AddRange(others);

        return annotations.Count == 0 ? null : $"odata.include-annotations=\"{string.Join(",", annotations)}\"";
    }

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
