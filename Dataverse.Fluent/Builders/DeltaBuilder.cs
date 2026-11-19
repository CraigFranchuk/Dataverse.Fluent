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
    private bool _includeFormattedValues;
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
        
        var json = await RequestSender.ExecuteJsonAsync(_client, HttpMethod.Get, url, null, headers, ct).ConfigureAwait(false);

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

            var json = await RequestSender.ExecuteJsonAsync(_client, HttpMethod.Get, nextLink, null, headers, ct).ConfigureAwait(false);
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

        // Initial delta query: the entity set and $select, the only query option Dataverse accepts with change tracking.
        var parts = new List<string>(1);
        _options.AppendTo(parts);

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

    // A deleted record in a delta response, or null for a created or changed one. Dataverse sends a deletion as
    // {"@odata.context": "...#competitors/$deletedEntity", "id": "<guid>", "reason": "deleted"} (checked 2026-11-18);
    // the OData 4.01 form {"@removed": {"reason": ...}, "@id": "..."} is read too.
    private static EntityReference? TryReadDeleted(JsonObject obj)
    {
        if (obj.TryGetPropertyValue("@removed", out _))
            return DataverseClient.DeserializeEntity<EntityReference>(obj);

        if (obj.TryGetPropertyValue("@odata.context", out var context)
            && context is JsonValue contextValue && contextValue.TryGetValue<string>(out var contextText)
            && contextText.EndsWith("/$deletedEntity", StringComparison.Ordinal))
        {
            return new EntityReference
            {
                Id = obj.TryGetPropertyValue("id", out var id) && id is JsonValue idValue ? idValue.ToString() : null,
                Removed = new DeletedReference
                {
                    Reason = obj.TryGetPropertyValue("reason", out var reason) && reason is JsonValue reasonValue ? reasonValue.ToString() : null
                }
            };
        }

        return null;
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
                    if (TryReadDeleted(obj) is { } deletedRef)
                    {
                        results.Deleted.Add(deletedRef);
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

        return results;
    }
}
