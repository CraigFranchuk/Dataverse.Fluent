namespace Dataverse.Fluent.Tests
{
    internal static class HttpRequestMessageExtensions
    {
        /// <summary>
        /// The request URL with percent-encoding removed, for asserting on the logical OData query
        /// (query option values such as $select, $filter and $expand are sent percent-encoded).
        /// </summary>
        public static string DecodedUrl(this HttpRequestMessage request)
            => Uri.UnescapeDataString(request.RequestUri!.ToString());
    }
}
