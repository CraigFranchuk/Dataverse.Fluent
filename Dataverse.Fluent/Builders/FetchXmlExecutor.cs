using System.Runtime.CompilerServices;
using Dataverse.Fluent.Models;
using System.Xml.Linq;

namespace Dataverse.Fluent.Builders;

/// <summary>
/// Handles execution of FetchXML queries against Dataverse Web API.
/// Supports pagination, deserialization, and resilient error handling.
/// </summary>
/// <typeparam name="T">The POCO type to deserialize results into.</typeparam>
internal class FetchXmlExecutor<T>
{
    // Annotations Dataverse only returns when requested via Prefer: odata.include-annotations.
    private const string PagingCookieAnnotation = "Microsoft.Dynamics.CRM.fetchxmlpagingcookie";
    private const string MoreRecordsAnnotation = "Microsoft.Dynamics.CRM.morerecords";

    private readonly IDataverseTransport _client;

    public FetchXmlExecutor(IDataverseTransport client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Executes FetchXML and returns all results as a list.
    /// </summary>
    public async Task<List<T>> ExecuteAsync(string fetchXml, FetchXmlOptions options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var results = new List<T>();

        await foreach (var entity in StreamAsync(fetchXml, options, ct).ConfigureAwait(false))
        {
            results.Add(entity);
        }

        return results;
    }

    /// <summary>
    /// Streams FetchXML results with automatic pagination.
    /// </summary>
    /// <remarks>
    /// Follows the Dataverse paging-cookie protocol: each page sets the <c>page</c> and <c>count</c>
    /// attributes, and when <c>@Microsoft.Dynamics.CRM.morerecords</c> is true, the <c>pagingcookie</c>
    /// attribute of the returned <c>@Microsoft.Dynamics.CRM.fetchxmlpagingcookie</c> element is
    /// URL-decoded twice and set as the next request's <c>paging-cookie</c>. When no cookie is returned,
    /// paging falls back to simple page numbers. Queries with <c>top</c>, an explicit <c>page</c>, or
    /// <c>aggregate="true"</c> are executed as a single request.
    /// </remarks>
    public async IAsyncEnumerable<T> StreamAsync(
        string fetchXml,
        FetchXmlOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var fetch = XElement.Parse(fetchXml);
        if (options.NoLock)
        {
            fetch.SetAttributeValue("no-lock", "true");
        }
        var headers = BuildHeaders(options);

        if (!SupportsAutoPaging(fetch))
        {
            var single = await ExecuteSinglePageAsync(fetch, headers, ct).ConfigureAwait(false);
            foreach (var entity in single.GetItems())
            {
                yield return entity;
            }
            yield break;
        }

        // An explicit count attribute on the query is honored as the page size.
        if (fetch.Attribute("count") is null)
        {
            fetch.SetAttributeValue("count", Math.Clamp(options.MaxPageSize, 1, ODataPaging.MaxPageSize));
        }

        var page = 1;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            fetch.SetAttributeValue("page", page);

            var response = await ExecuteSinglePageAsync(fetch, headers, ct).ConfigureAwait(false);
            var entities = response.GetItems();
            foreach (var entity in entities)
            {
                yield return entity;
            }

            // An empty page that claims more records would otherwise loop forever.
            if (!response.MoreRecords || entities.Count == 0)
            {
                yield break;
            }

            // Without a cookie Dataverse falls back to simple paging (page numbers only).
            fetch.SetAttributeValue("paging-cookie", ExtractPagingCookie(response.FetchXmlPagingCookie));
            page++;
        }
    }

    private static bool SupportsAutoPaging(XElement fetch)
    {
        if (fetch.Attribute("top") is not null || fetch.Attribute("page") is not null)
            return false;

        return !string.Equals((string?)fetch.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase);
    }

    // The annotation value is an XML element such as
    //   <cookie pagenumber="2" pagingcookie="%253ccookie%2520page..." istracking="False" />
    // whose pagingcookie attribute is URL-encoded twice. XElement handles XML-encoding the decoded
    // value when it is set as the fetch element's paging-cookie attribute.
    internal static string? ExtractPagingCookie(string? cookieXml)
    {
        if (string.IsNullOrEmpty(cookieXml))
            return null;

        var encoded = (string?)XElement.Parse(cookieXml).Attribute("pagingcookie");
        if (string.IsNullOrEmpty(encoded))
            return null;

        return Uri.UnescapeDataString(Uri.UnescapeDataString(encoded));
    }

    private static Dictionary<string, string> BuildHeaders(FetchXmlOptions options)
    {
        return new Dictionary<string, string>
        {
            ["Prefer"] = ODataPaging.IncludeAnnotationsPreference(typeof(T), options.IncludeFormattedValues, PagingCookieAnnotation, MoreRecordsAnnotation)!
        };
    }

    private async Task<ODataCollectionResponse<T>> ExecuteSinglePageAsync(XElement fetch, Dictionary<string, string> headers, CancellationToken ct)
    {
        var encodedFetchXml = Uri.EscapeDataString(fetch.ToString(SaveOptions.DisableFormatting));
        var url = $"{ODataPropertyNameCache.GetEntitySetName<T>()}?fetchXml={encodedFetchXml}";

        return await RequestSender.ExecuteAsync<ODataCollectionResponse<T>>(_client, HttpMethod.Get, url, headers, ct).ConfigureAwait(false)
            ?? new ODataCollectionResponse<T>();
    }
}
