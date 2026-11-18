using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Fluent builder for delta (change tracking) queries in Dataverse.
/// Supports initial delta queries and subsequent polling with delta tokens.
/// </summary>
/// <typeparam name="T">The POCO type to deserialize results into.</typeparam>
/// <remarks>
/// This type is not thread-safe. Each instance should be used for a single operation and not shared across threads.
/// Terminal methods (e.g., <see cref="ExecuteAsync"/>, <see cref="StreamAsync"/>) mark the builder as consumed;
/// subsequent calls will throw <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class DeltaBuilder<T>
{
    private readonly IDataverseTransport _client;
    private readonly string _entitySet;

    private readonly ODataQueryOptions _options = new();
    private string? _deltaToken;
    private int? _top;
    private bool _includeFormattedValues;
    private bool _includeCount;
    private readonly Dictionary<string, string> _customHeaders = [];
    private SingleUseGuard _guard;

    internal DeltaBuilder(IDataverseTransport client, string? entitySet = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));

        _entitySet = entitySet ?? ODataPropertyNameCache.GetEntitySetName<T>();
    }

    /// <summary>
    /// Specifies a delta token from a previous query to retrieve only changes since that point.
    /// </summary>
    /// <param name="deltaToken">The delta token (from a previous DeltaResults.DeltaToken).</param>
    /// <returns>The current builder instance for chaining.</returns>
    public DeltaBuilder<T> Since(string deltaToken)
    {
        if (string.IsNullOrWhiteSpace(deltaToken))
        {
            throw new ArgumentException("Delta token cannot be null or empty.", nameof(deltaToken));
        }

        _deltaToken = deltaToken;
        return this;
    }

    /// <summary>
    /// Adds $select clause to specify which properties to retrieve.
    /// </summary>
    /// <param name="properties">Array of property names to select.</param>
    /// <returns>The current builder instance for chaining.</returns>
    public DeltaBuilder<T> Select(params string[] properties)
    {
        if (properties?.Length > 0)
        {
            _options.Select.AddRange(properties);
        }
        return this;
    }

    /// <summary>
    /// Adds $filter clause for the initial delta query (raw OData filter string).
    /// Note: Filters only apply to the initial query, not to subsequent delta token queries.
    /// </summary>
    /// <param name="filter">The OData filter expression.</param>
    /// <returns>The current builder instance for chaining.</returns>
    public DeltaBuilder<T> Where(string filter)
    {
        if (!string.IsNullOrWhiteSpace(filter))
        {
            _options.Filter.Add(filter);
        }
        return this;
    }

    /// <summary>
    /// Adds $orderby clause for the initial delta query (raw OData orderby string).
    /// </summary>
    /// <param name="orderBy">The OData orderby expression.</param>
    /// <returns>The current builder instance for chaining.</returns>
    public DeltaBuilder<T> OrderBy(string orderBy)
    {
        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            _options.OrderBy.Add(orderBy);
        }
        return this;
    }

    /// <summary>
    /// Adds $top clause to limit the number of results per page.
    /// Note: Dataverse does not support $top with change tracking enabled. This method is provided
    /// for API consistency but will be ignored in delta queries. Pagination is handled automatically
    /// via @odata.nextLink.
    /// </summary>
    /// <param name="top">Maximum number of records to retrieve.</param>
    /// <returns>The current builder instance for chaining.</returns>
    public DeltaBuilder<T> Top(int top)
    {
        // Note: $top is not supported by Dataverse when change tracking is enabled
        // This method is provided for API consistency but will be ignored in delta queries
        // Pagination is handled automatically by Dataverse via @odata.nextLink
        _top = top > 0 ? top : throw new ArgumentOutOfRangeException(nameof(top));
        return this;
    }

    /// <summary>
    /// Requests formatted values via Prefer header (adds @OData.Community.Display.V1.FormattedValue annotations).
    /// </summary>
    /// <param name="include">Whether to include formatted values (default: true).</param>
    /// <returns>The current builder instance for chaining.</returns>
    public DeltaBuilder<T> IncludeFormattedValues(bool include = true)
    {
        _includeFormattedValues = include;
        return this;
    }

    /// <summary>
    /// Requests @odata.count in the response.
    /// Note: Dataverse does not support $count with change tracking enabled. This method is provided
    /// for API consistency but will be ignored in delta queries.
    /// </summary>
    /// <param name="include">Whether to include count (default: true).</param>
    /// <returns>The current builder instance for chaining.</returns>
    public DeltaBuilder<T> Count(bool include = true)
    {
        // Note: $count is not supported by Dataverse when change tracking is enabled
        // This method is provided for API consistency but will be ignored in delta queries
        _includeCount = include;
        return this;
    }

    /// <summary>
    /// Adds custom headers to the request (e.g., for specific Prefer options).
    /// </summary>
    /// <param name="headers">Dictionary of header name-value pairs.</param>
    /// <returns>The current builder instance for chaining.</returns>
    public DeltaBuilder<T> WithHeaders(IDictionary<string, string> headers)
    {
        if (headers != null)
        {
            foreach (var header in headers)
            {
                _customHeaders[header.Key] = header.Value;
            }
        }
        return this;
    }

    /// <summary>
    /// Executes the delta query and returns results with changed entities, deleted references, and a new delta token.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>Delta results containing changes, deletions, and the next delta token.</returns>
    public async Task<DeltaResults<T>> ExecuteAsync(CancellationToken ct = default)
    {
        _guard.Use();
        var url = BuildUrl();
        var headers = BuildHeaders();
        
        var json = await _client.ExecuteRequestAsync(HttpMethod.Get, url, headers: headers, ct: ct).ConfigureAwait(false);

        return DeserializeDeltaResults(json);
    }

    /// <summary>
    /// Streams all delta results asynchronously with automatic pagination via @odata.nextLink.
    /// </summary>
    /// <param name="pageSize">Number of records per page (max 5,000), sent as <c>Prefer: odata.maxpagesize</c> on every page.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>Async enumerable of delta results pages.</returns>
    public async IAsyncEnumerable<DeltaResults<T>> StreamAsync(
        int pageSize = 2000,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _guard.Use();

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        // Page size is requested with Prefer: odata.maxpagesize (sent on every page, as Dataverse
        // requires); pages are then followed via @odata.nextLink.
        string? nextLink = BuildUrl();
        var headers = BuildHeaders(Math.Min(pageSize, ODataPaging.MaxPageSize));
        var followed = new HashSet<string>(StringComparer.Ordinal);

        while (!string.IsNullOrEmpty(nextLink))
        {
            ct.ThrowIfCancellationRequested();

            var json = await _client.ExecuteRequestAsync(HttpMethod.Get, nextLink, headers: headers, ct: ct).ConfigureAwait(false);
            var deltaResults = DeserializeDeltaResults(json);

            yield return deltaResults;

            // Follow pagination via @odata.nextLink (for same delta set)
            nextLink = deltaResults.NextLink;

            // If no more pages, we're done streaming this delta
            if (string.IsNullOrEmpty(nextLink))
            {
                break;
            }

            ODataPaging.TrackNextLink(followed, nextLink);
        }
    }

    // Builds the URL for the delta query
    private string BuildUrl(int? pageSize = null)
    {
        // If we have a delta token, use it directly (it's a full delta link)
        if (!string.IsNullOrWhiteSpace(_deltaToken))
        {
            // Delta token is typically a full @odata.deltaLink URL
            // Extract relative path if it's absolute
            return _deltaToken;
        }

        // Initial delta query: entity set + query options. Repeated Where() calls are combined with "and".
        var parts = new List<string>(4);
        _options.AppendTo(parts);

        // Note: $count and $top may be ignored by Dataverse when change tracking is enabled,
        // but they are passed through and the server decides. Pagination is via @odata.nextLink.
        if (_includeCount)
            parts.Add("$count=true");

        if (_top.HasValue)
            parts.Add($"$top={_top.Value}");

        var url = parts.Count == 0 ? _entitySet : _entitySet + "?" + string.Join("&", parts);

        // Change tracking is activated via the Prefer: odata.track-changes header
        // (set in BuildHeaders), not via a URL parameter.

        return url;
    }

    // Builds headers including Prefer header for formatted values
    private Dictionary<string, string>? BuildHeaders(int? maxPageSize = null)
    {
        var preferValues = new List<string>();

        if (ODataPaging.IncludeAnnotationsPreference(typeof(T), _includeFormattedValues) is { } annotations)
        {
            preferValues.Add(annotations);
        }

        // Add odata.track-changes preference for delta queries
        preferValues.Add("odata.track-changes");

        if (maxPageSize.HasValue)
        {
            preferValues.Add(ODataPaging.MaxPageSizePreference(maxPageSize.Value));
        }

        var headers = new Dictionary<string, string>(_customHeaders);

        if (preferValues.Count > 0)
        {
            // Merge with any existing Prefer header
            if (headers.TryGetValue("Prefer", out var existingPrefer))
            {
                headers["Prefer"] = string.Join(",", existingPrefer, string.Join(",", preferValues));
            }
            else
            {
                headers["Prefer"] = string.Join(",", preferValues);
            }
        }

        return headers.Count > 0 ? headers : null;
    }

    // Deserializes the response to DeltaResults<T>
    private static DeltaResults<T> DeserializeDeltaResults(JsonObject response)
    {
        var results = new DeltaResults<T>();

        // Deserialize changed/created entities from "value" array
        if (response.TryGetPropertyValue("value", out var valueNode) && valueNode is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is JsonObject obj)
                {
                    // Check if this is a deleted entity reference
                    if (obj.TryGetPropertyValue("@removed", out _))
                    {
                        var deletedRef = DataverseClient.DeserializeEntity<EntityReference>(obj);
                        if (deletedRef != null)
                        {
                            results.Deleted.Add(deletedRef);
                        }
                    }
                    else
                    {
                        // Regular entity (created or modified)
                        var entity = DataverseClient.DeserializeEntity<T>(obj);
                        if (entity != null)
                        {
                            results.Value.Add(entity);
                        }
                    }
                }
            }
        }

        // Extract delta token from @odata.deltaLink
        if (response.TryGetPropertyValue("@odata.deltaLink", out var deltaLinkNode))
        {
            results.DeltaToken = deltaLinkNode?.GetValue<string>();
        }

        // Extract next link for pagination within the same delta set
        if (response.TryGetPropertyValue("@odata.nextLink", out var nextLinkNode))
        {
            results.NextLink = nextLinkNode?.GetValue<string>();
        }

        // Extract count if requested
        if (response.TryGetPropertyValue("@odata.count", out var countNode))
        {
            results.Count = countNode?.GetValue<long>();
        }

        return results;
    }
}
