namespace Dataverse.Fluent.Models;

/// <summary>
/// Represents a single page of query results from the Dataverse Web API,
/// including pagination metadata for manual page-by-page traversal.
/// </summary>
/// <typeparam name="T">The POCO type for the entities.</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// The entities on this page.
    /// </summary>
    public List<T> Items { get; }

    /// <summary>
    /// The <c>@odata.nextLink</c> URL for fetching the next page,
    /// or <c>null</c> if this is the last page.
    /// </summary>
    public string? NextLink { get; }

    /// <summary>
    /// The total record count returned by the server when <c>$count=true</c> was requested.
    /// May be <c>null</c> if count was not requested or the server did not include it.
    /// </summary>
    public long? TotalCount { get; }

    /// <summary>
    /// Indicates whether additional pages are available.
    /// </summary>
    public bool HasMorePages => NextLink is not null;

    /// <summary>
    /// The page size (<c>Prefer: odata.maxpagesize</c>) used to request this page, if one was set.
    /// Dataverse requires the same page size on every subsequent page request.
    /// </summary>
    public int? PageSize { get; }

    /// <summary>
    /// Initializes a new <see cref="PagedResult{T}"/>.
    /// </summary>
    public PagedResult(List<T> items, string? nextLink, long? totalCount, int? pageSize = null)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        NextLink = nextLink;
        TotalCount = totalCount;
        PageSize = pageSize;
    }
}
