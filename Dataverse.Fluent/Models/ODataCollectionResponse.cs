using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Models;

/// <summary>
/// The envelope of a Dataverse collection response (OData query or FetchXML), deserialized directly
/// from the response stream so items never pass through a JSON DOM.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
internal sealed class ODataCollectionResponse<T>
{
    [JsonPropertyName("value")]
    public List<T> Value { get; set; } = [];

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; set; }

    [JsonPropertyName("@odata.count")]
    public long? Count { get; set; }

    /// <summary>FetchXML: whether more pages exist (only returned when requested via Prefer).</summary>
    [JsonPropertyName("@Microsoft.Dynamics.CRM.morerecords")]
    public bool MoreRecords { get; set; }

    /// <summary>FetchXML: the paging cookie element (only returned when requested via Prefer).</summary>
    [JsonPropertyName("@Microsoft.Dynamics.CRM.fetchxmlpagingcookie")]
    public string? FetchXmlPagingCookie { get; set; }

    /// <summary>Returns the items, skipping JSON <c>null</c> entries.</summary>
    public List<T> GetItems()
    {
        Value.RemoveAll(item => item is null);
        return Value;
    }
}
