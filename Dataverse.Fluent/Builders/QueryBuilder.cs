using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Dataverse.Fluent.Builders.Expressions;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Fluent builder for Dataverse queries (RetrieveMultiple via Web API).
/// Supports OData-style querying with optional strong typing to POCOs.
/// </summary>
/// <typeparam name="T">The POCO type to deserialize results into. Use [JsonPropertyName("logicalname")] attributes on properties for mapping.</typeparam>
/// <remarks>
/// This type is not thread-safe. Each instance should be used for a single operation and not shared across threads.
/// Terminal methods (e.g., <see cref="GetListAsync"/>, <see cref="GetPagedAsync"/>, <see cref="GetAllAsync"/>,
/// <see cref="StreamAsync"/>) mark the builder as consumed; subsequent calls will throw
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class QueryBuilder<T>
{
    private readonly IDataverseTransport _client;
    private readonly string _entitySet;

    private readonly ODataQueryOptions _options = new();
    private int? _top;
    private int? _pageSize;
    private bool _includeFormattedValues;
    private bool _includeCount;
    private SingleUseGuard _guard;

    internal QueryBuilder(IDataverseTransport client, string? entitySet = null)
    {
        _client = client;

        _entitySet = entitySet ?? ODataPropertyNameCache.GetEntitySetName<T>();
    }

    /// <summary>
    /// Adds $select clause.
    /// </summary>
    public QueryBuilder<T> Select(params string[] properties)
    {
        if (properties?.Length > 0)
        {
            _options.Select.AddRange(properties);
        }
        return this;
    }

    /// <summary>
    /// Adds $select clause using a lambda expression that defines which properties to include.
    /// </summary>
    /// <typeparam name="TProjection">The projection type (typically an anonymous type with selected properties).</typeparam>
    /// <param name="selector">Lambda expression defining the properties to select (e.g., e => new { e.Name, e.CreatedOn }).</param>
    public QueryBuilder<T> Select<TProjection>(Expression<Func<T, TProjection>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var propertyNames = MemberPathExtractor.GetPropertyNames(selector);

        if (propertyNames.Count > 0)
        {
            _options.Select.AddRange(propertyNames);
        }

        return this;
    }

    /// <summary>
    /// Adds a raw $expand clause.
    /// </summary>
    /// <param name="expand">Raw OData expand string (e.g., "contact_customer_accounts($select=firstname,lastname)").</param>
    public QueryBuilder<T> Expand(string expand)
    {
        if (!string.IsNullOrWhiteSpace(expand))
        {
            _options.Expand.Add(expand);
        }
        return this;
    }

    /// <summary>
    /// Adds an $expand clause for a navigation property with nested query options configured via a fluent builder.
    /// </summary>
    /// <typeparam name="TRelated">The related entity type being expanded.</typeparam>
    /// <param name="navigationProperty">The navigation property name (e.g., "contact_customer_accounts").</param>
    /// <param name="configure">Optional action to configure nested $select, $filter, $orderby, $top on the expansion.</param>
    public QueryBuilder<T> Expand<TRelated>(string navigationProperty, Action<ExpandBuilder<T, TRelated>>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(navigationProperty))
            throw new ArgumentException("Navigation property cannot be null or whitespace.", nameof(navigationProperty));

        var builder = new ExpandBuilder<T, TRelated>(navigationProperty);
        configure?.Invoke(builder);
        _options.Expand.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Adds $filter clause (raw OData filter string).
    /// </summary>
    public QueryBuilder<T> Where(string filter)
    {
        if (!string.IsNullOrWhiteSpace(filter))
        {
            _options.Filter.Add(filter);
        }
        return this;
    }

    /// <summary>
    /// Adds $filter clause using a lambda expression that is converted to OData syntax.
    /// </summary>
    /// <param name="predicate">Lambda expression defining the filter condition (e.g., x => x.Age > 18).</param>
    public QueryBuilder<T> Where(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var visitor = new ODataExpressionVisitor();
        var odataFilter = visitor.ToODataFilter(predicate);

        if (!string.IsNullOrWhiteSpace(odataFilter))
        {
            _options.Filter.Add(odataFilter);
        }

        return this;
    }

    /// <summary>
    /// Adds $orderby clause (raw OData orderby string).
    /// </summary>
    public QueryBuilder<T> OrderBy(string orderBy)
    {
        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            _options.OrderBy.Add(orderBy);
        }
        return this;
    }

    /// <summary>
    /// Adds $orderby clause using a lambda expression.
    /// Supports both single properties (e.g., x => x.Name) and multiple properties (e.g., x => new { x.Name, x.Revenue }).
    /// </summary>
    /// <typeparam name="TKey">The property or projection type.</typeparam>
    /// <param name="keySelector">Lambda expression defining the property or properties to order by.</param>
    /// <param name="descending">Whether to sort in descending order (default: false = ascending).</param>
    public QueryBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, bool descending = false)
    {
        ArgumentNullException.ThrowIfNull(keySelector);

        var propertyNames = MemberPathExtractor.GetPropertyNames(keySelector);

        if (propertyNames.Count > 0)
        {
            var direction = descending ? " desc" : " asc";
            foreach (var name in propertyNames)
                _options.OrderBy.Add(name + direction);
        }

        return this;
    }

    /// <summary>
    /// Limits the total number of rows returned (<c>$top</c>).
    /// </summary>
    /// <remarks>
    /// <c>$top</c> is a limit, not a page size: Dataverse does not page a <c>$top</c> query. Use
    /// <see cref="PageSize"/> to page through results. With <see cref="StreamAsync"/> and
    /// <see cref="GetAllAsync"/>, <c>Top</c> caps the total number of rows yielded across pages.
    /// </remarks>
    public QueryBuilder<T> Top(int top)
    {
        _top = top > 0 ? top : throw new ArgumentOutOfRangeException(nameof(top));
        return this;
    }

    /// <summary>
    /// Sets the number of rows per page (<c>Prefer: odata.maxpagesize</c>) for <see cref="GetPagedAsync"/>,
    /// <see cref="GetListAsync"/>, and <see cref="GetNextPageAsync(string, CancellationToken)"/>.
    /// </summary>
    /// <param name="pageSize">Rows per page, from 1 to 5,000 (the Dataverse maximum).</param>
    /// <remarks>
    /// Dataverse ignores <c>$top</c> when a page size is requested, so <see cref="Top"/> and
    /// <see cref="PageSize"/> cannot be combined on single-page requests.
    /// </remarks>
    public QueryBuilder<T> PageSize(int pageSize)
    {
        ThrowIfPageSizeOutOfRange(pageSize);
        _pageSize = pageSize;
        return this;
    }

    /// <summary>
    /// Not supported: the Dataverse Web API does not support the <c>$skip</c> query option.
    /// </summary>
    [Obsolete("Dataverse does not support $skip. Page with PageSize() and GetPagedAsync()/GetNextPageAsync(), or use StreamAsync().", error: true)]
    public QueryBuilder<T> Skip(int skip)
        => throw new NotSupportedException("Dataverse does not support the $skip query option.");

    /// <summary>
    /// Requests formatted values via Prefer header (adds @OData.Community.Display.V1.FormattedValue annotations).
    /// </summary>
    public QueryBuilder<T> IncludeFormattedValues(bool include = true)
    {
        _includeFormattedValues = include;
        return this;
    }

    /// <summary>
    /// Requests @odata.count in response.
    /// </summary>
    public QueryBuilder<T> Count(bool include = true)
    {
        _includeCount = include;
        return this;
    }

    /// <summary>
    /// Executes the query and returns a single page of results.
    /// </summary>
    public async Task<List<T>> GetListAsync(CancellationToken ct = default)
    {
        _guard.Use();
        ThrowIfTopAndPageSizeCombined();
        var url = BuildUrl(includeTop: true, includeCount: true);
        var page = await ExecuteWithPreferences(url, _pageSize, ct).ConfigureAwait(false);

        return page.GetItems();
    }

    /// <summary>
    /// Executes the query and returns a single page of results with pagination metadata.
    /// Pass the returned page to <see cref="GetNextPageAsync(PagedResult{T}, CancellationToken)"/> to fetch subsequent pages.
    /// </summary>
    /// <example>
    /// <code>
    /// var query = client.Query&lt;Account&gt;()
    ///     .Select(a =&gt; new { a.Name })
    ///     .PageSize(50)
    ///     .Count();
    ///
    /// var page = await query.GetPagedAsync();
    /// Console.WriteLine($"Got {page.Items.Count} of {page.TotalCount}");
    ///
    /// while (page.HasMorePages)
    /// {
    ///     page = await query.GetNextPageAsync(page);
    /// }
    /// </code>
    /// </example>
    public async Task<PagedResult<T>> GetPagedAsync(CancellationToken ct = default)
    {
        _guard.Use();
        ThrowIfTopAndPageSizeCombined();
        var url = BuildUrl(includeTop: true, includeCount: true);
        var page = await ExecuteWithPreferences(url, _pageSize, ct).ConfigureAwait(false);

        return ToPagedResult(page, _pageSize);
    }

    /// <summary>
    /// Fetches the next page of results using a <c>@odata.nextLink</c> URL
    /// obtained from a previous <see cref="PagedResult{T}"/>.
    /// </summary>
    /// <param name="nextLink">The <c>@odata.nextLink</c> URL from a previous page.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// This method does not mark the builder as consumed because it is intended to be called
    /// repeatedly after <see cref="GetPagedAsync"/> to follow pagination links. It only reads
    /// immutable builder state (Prefer headers, including the <see cref="PageSize"/>, which Dataverse
    /// requires on every page request) and does not mutate the builder.
    /// </remarks>
    public async Task<PagedResult<T>> GetNextPageAsync(string nextLink, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nextLink);

        var page = await ExecuteWithPreferences(nextLink, _pageSize, ct).ConfigureAwait(false);

        return ToPagedResult(page, _pageSize);
    }

    /// <summary>
    /// Fetches the page following <paramref name="previousPage"/>, re-sending the page size it was
    /// requested with (Dataverse requires the same <c>odata.maxpagesize</c> on every page).
    /// </summary>
    /// <param name="previousPage">A page returned by <see cref="GetPagedAsync"/> or a previous call to this method.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="previousPage"/> is the last page.</exception>
    public async Task<PagedResult<T>> GetNextPageAsync(PagedResult<T> previousPage, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(previousPage);
        if (previousPage.NextLink is null)
            throw new InvalidOperationException("The previous page is the last page; there is no next page to fetch.");

        var pageSize = previousPage.PageSize ?? _pageSize;
        var page = await ExecuteWithPreferences(previousPage.NextLink, pageSize, ct).ConfigureAwait(false);

        return ToPagedResult(page, pageSize);
    }

    /// <summary>
    /// Fetches all pages automatically and returns the complete list of results.
    /// Follows <c>@odata.nextLink</c> until all pages are consumed.
    /// </summary>
    /// <remarks>
    /// Dataverse limits individual responses to 5,000 records. This method automatically
    /// follows pagination links to retrieve the entire result set (capped by <see cref="Top"/>, if set).
    /// For large datasets, prefer <see cref="StreamAsync"/> to reduce memory pressure.
    /// </remarks>
    /// <param name="pageSize">Records per page (max 5,000). Defaults to 5,000 for maximum throughput.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<List<T>> GetAllAsync(int pageSize = 5000, CancellationToken ct = default)
    {
        _guard.Use();
        ThrowIfPageSizeOutOfRange(pageSize);
        var results = new List<T>();

        await foreach (var entity in StreamCore(pageSize, ct).ConfigureAwait(false))
        {
            results.Add(entity);
        }

        return results;
    }

    /// <summary>
    /// Streams all results asynchronously with automatic pagination (low memory usage).
    /// </summary>
    /// <param name="pageSize">Records per page (max 5,000), sent as <c>Prefer: odata.maxpagesize</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>If <see cref="Top"/> is set, streaming stops after that many rows.</remarks>
    public async IAsyncEnumerable<T> StreamAsync(
        int pageSize = 2000,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        _guard.Use();
        ThrowIfPageSizeOutOfRange(pageSize);

        await foreach (var entity in StreamCore(pageSize, ct).ConfigureAwait(false))
        {
            yield return entity;
        }
    }

    /// <summary>
    /// Executes a <c>$count</c> request and returns the number of matching records, up to 5,000.
    /// Applies any configured <c>$filter</c> clause. Other options ($select, $top, $orderby) are ignored.
    /// </summary>
    /// <remarks>
    /// Dataverse caps <c>$count</c> at 5,000: a result of 5,000 means "5,000 or more". To count a larger
    /// set, use <see cref="IDataverseClient.Aggregate{T}"/> with <c>Count(alias)</c>, or a FetchXML aggregate.
    /// </remarks>
    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        _guard.Use();
        var countUrl = _entitySet + "/$count";
        if (ODataQueryOptions.CombineFilters(_options.Filter) is { } filter)
            countUrl += "?$filter=" + Uri.EscapeDataString(filter);

        // $count returns a bare number as text/plain rather than JSON.
        var request = new DataverseRequest(HttpMethod.Get, countUrl) { Headers = { ["Accept"] = "text/plain" } };

        using var response = await _client.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.ReadStringAsync(ct).ConfigureAwait(false);
        return long.Parse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Executes the query and returns results together with the total record count.
    /// Equivalent to <see cref="GetListAsync"/> but also surfaces <c>@odata.count</c>.
    /// </summary>
    public async Task<(List<T> Items, long? TotalCount)> GetListWithCountAsync(CancellationToken ct = default)
    {
        _guard.Use();
        ThrowIfTopAndPageSizeCombined();
        var url = BuildUrl(includeTop: true, includeCount: true);
        var page = await ExecuteWithPreferences(url, _pageSize, ct).ConfigureAwait(false);
        return (page.GetItems(), page.Count);
    }

    private async IAsyncEnumerable<T> StreamCore(
        int pageSize,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Page size goes in Prefer: odata.maxpagesize (re-sent on every page, as Dataverse requires).
        // $top is deliberately not sent: it is a total limit that disables paging, and Dataverse
        // ignores it alongside maxpagesize. Top() is therefore enforced client-side.
        var effectivePageSize = pageSize;
        if (_top.HasValue)
            effectivePageSize = Math.Min(effectivePageSize, _top.Value);

        // $count is not requested either: the stream has nowhere to surface it.
        string? nextLink = BuildUrl(includeTop: false, includeCount: false);
        var remaining = _top ?? int.MaxValue;
        var followed = new HashSet<string>(StringComparer.Ordinal);

        while (!string.IsNullOrEmpty(nextLink))
        {
            // ExecuteRequestAsync handles relative or full absolute URLs seamlessly
            var page = await ExecuteWithPreferences(nextLink, effectivePageSize, ct).ConfigureAwait(false);

            foreach (var entity in page.GetItems())
            {
                yield return entity;
                if (--remaining == 0)
                    yield break;
            }

            nextLink = page.NextLink;
            if (!string.IsNullOrEmpty(nextLink))
                ODataPaging.TrackNextLink(followed, nextLink);
        }
    }

    private static void ThrowIfPageSizeOutOfRange(int pageSize)
    {
        if (pageSize <= 0 || pageSize > ODataPaging.MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, $"Page size must be between 1 and {ODataPaging.MaxPageSize}.");
    }

    // Dataverse ignores $top when odata.maxpagesize is present, so the combination would silently
    // return a full page instead of the requested limit.
    private void ThrowIfTopAndPageSizeCombined()
    {
        if (_top.HasValue && _pageSize.HasValue)
            throw new InvalidOperationException(
                "Top() and PageSize() cannot be combined on a single-page request: Dataverse ignores $top when a page size is requested. " +
                "Use Top() to limit the results, PageSize() to page through them, or StreamAsync()/GetAllAsync() to page with an overall limit.");
    }

    // Builds the relative URL portion (without base)
    private string BuildUrl(bool includeTop, bool includeCount)
    {
        var parts = new List<string>(8);

        _options.AppendTo(parts);

        if (includeCount && _includeCount)
            parts.Add("$count=true");

        if (includeTop && _top.HasValue)
            parts.Add($"$top={_top.Value}");

        return parts.Count == 0 ? _entitySet : _entitySet + "?" + string.Join("&", parts);
    }

    // Executes one page with optional Prefer headers, deserializing the response stream straight into
    // the typed envelope.
    private async Task<ODataCollectionResponse<T>> ExecuteWithPreferences(string url, int? pageSize, CancellationToken ct)
    {
        var headers = BuildPreferHeaders(pageSize);
        return await _client.ExecuteRequestAsync<ODataCollectionResponse<T>>(HttpMethod.Get, url, headers: headers, ct: ct).ConfigureAwait(false)
            ?? new ODataCollectionResponse<T>();
    }

    private Dictionary<string, string>? BuildPreferHeaders(int? pageSize)
    {
        var preferValues = new List<string>(2);
        if (_includeFormattedValues)
            preferValues.Add(ODataPaging.FormattedValuesPreference);
        if (pageSize.HasValue)
            preferValues.Add(ODataPaging.MaxPageSizePreference(pageSize.Value));

        return preferValues.Count > 0
            ? new Dictionary<string, string> { ["Prefer"] = string.Join(",", preferValues) }
            : null;
    }

    private static PagedResult<T> ToPagedResult(ODataCollectionResponse<T> response, int? pageSize)
        => new(response.GetItems(), response.NextLink, response.Count, pageSize);
}