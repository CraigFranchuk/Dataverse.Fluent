using System.Text.Json;
using Dataverse.Fluent.Builders;
using Xunit;

namespace Dataverse.Fluent.Tests
{
    public class FetchQueryExecutorTests
    {
        private readonly TestHttpMessageHandler _handler;
        private readonly DataverseClient _client;

        public FetchQueryExecutorTests()
        {
            _handler = new TestHttpMessageHandler();
            var httpClient = new HttpClient(_handler);
            _client = new DataverseClient(
                "https://test.api.crm.dynamics.com/api/data/v9.2",
                () => Task.FromResult("test-token"),
                httpClient
            );
        }

        [Fact]
        public async Task Fetch_WithRawFetchXml_ExecutesSuccessfully()
        {
            var fetchXml = @"
                <fetch top='10'>
                    <entity name='account'>
                        <attribute name='name' />
                        <attribute name='revenue' />
                    </entity>
                </fetch>";

            var expectedAccounts = new[]
            {
                new TestAccount { AccountId = Guid.NewGuid(), Name = "Test Account", Revenue = 50000 }
            };

            var responseJson = new
            {
                value = expectedAccounts
            };

            _handler.AddJsonResponse(
                req => req.RequestUri!.ToString().Contains("testaccounts") && req.Method == HttpMethod.Get,
                responseJson
            );

            var results = await _client.Fetch<TestAccount>(fetchXml).ExecuteAsync();

            Assert.Single(results);
            Assert.Equal("Test Account", results[0].Name);
        }

        [Fact]
        public async Task Fetch_WithOptions_AppliesOptionsCorrectly()
        {
            var fetchXml = @"
                <fetch>
                    <entity name='account'>
                        <all-attributes />
                    </entity>
                </fetch>";

            var expectedAccounts = new[]
            {
                new TestAccount { AccountId = Guid.NewGuid(), Name = "Test Account", Revenue = 50000 }
            };

            var responseJson = new
            {
                value = expectedAccounts
            };

            _handler.AddJsonResponse(
                req => req.RequestUri!.ToString().Contains("testaccounts") 
                    && req.Method == HttpMethod.Get
                    && req.Headers.TryGetValues("Prefer", out var preferValues)
                    && preferValues.Any(v => v.Contains("OData.Community.Display.V1.FormattedValue")),
                responseJson
            );

            var results = await _client.Fetch<TestAccount>(fetchXml)
                .WithOptions(opts => opts.IncludeFormattedValues = true)
                .ExecuteAsync();

            Assert.Single(results);
        }

        [Fact]
        public async Task Fetch_WithStreamAsync_StreamsResults()
        {
            var fetchXml = @"
                <fetch>
                    <entity name='account'>
                        <all-attributes />
                    </entity>
                </fetch>";

            var expectedAccounts = new[]
            {
                new TestAccount { AccountId = Guid.NewGuid(), Name = "Account 1", Revenue = 10000 },
                new TestAccount { AccountId = Guid.NewGuid(), Name = "Account 2", Revenue = 20000 }
            };

            var responseJson = new
            {
                value = expectedAccounts
            };

            _handler.AddJsonResponse(
                req => req.RequestUri!.ToString().Contains("testaccounts") && req.Method == HttpMethod.Get,
                responseJson
            );

            var results = new List<TestAccount>();
            await foreach (var account in _client.Fetch<TestAccount>(fetchXml).StreamAsync())
            {
                results.Add(account);
            }

            Assert.Equal(2, results.Count);
        }

        // ────────── Paging ──────────

        private const string PagingCookieAnnotation = "@Microsoft.Dynamics.CRM.fetchxmlpagingcookie";
        private const string MoreRecordsAnnotation = "@Microsoft.Dynamics.CRM.morerecords";

        private static System.Xml.Linq.XElement GetFetch(HttpRequestMessage request)
        {
            var query = request.RequestUri!.Query;
            var encoded = query[(query.IndexOf("fetchXml=", StringComparison.Ordinal) + "fetchXml=".Length)..];
            var end = encoded.IndexOf('&');
            return System.Xml.Linq.XElement.Parse(Uri.UnescapeDataString(end >= 0 ? encoded[..end] : encoded));
        }

        private static object Page(string name, bool moreRecords, string? cookieXml)
        {
            var response = new Dictionary<string, object>
            {
                ["value"] = new[] { new { accountid = Guid.NewGuid(), name } }
            };
            if (moreRecords) response[MoreRecordsAnnotation] = true;
            if (cookieXml != null) response[PagingCookieAnnotation] = cookieXml;
            return response;
        }

        [Fact]
        public async Task Fetch_FollowsPagingCookie_AcrossPages()
        {
            // The annotation is an XML element whose pagingcookie attribute is URL-encoded twice.
            const string rawCookie = "<cookie page=\"1\"><accountid last=\"{AAAA}\" first=\"{BBBB}\" /></cookie>";
            var annotation = $"<cookie pagenumber=\"2\" pagingcookie=\"{Uri.EscapeDataString(Uri.EscapeDataString(rawCookie))}\" istracking=\"False\" />";

            var requests = new List<HttpRequestMessage>();
            _handler.AddJsonResponse(req => { requests.Add(req); return true; }, Page("Account 1", moreRecords: true, annotation));
            _handler.AddJsonResponse(req => { requests.Add(req); return true; }, Page("Account 2", moreRecords: false, cookieXml: null));

            var results = await _client.Fetch<TestAccount>("<fetch><entity name='account'><attribute name='name' /></entity></fetch>")
                .WithOptions(o => o.MaxPageSize = 1)
                .ExecuteAsync();

            Assert.Equal(new[] { "Account 1", "Account 2" }, results.Select(r => r.Name));

            var first = GetFetch(requests[0]);
            Assert.Equal("1", (string?)first.Attribute("page"));
            Assert.Equal("1", (string?)first.Attribute("count"));
            Assert.Null(first.Attribute("paging-cookie"));

            var second = GetFetch(requests[1]);
            Assert.Equal("2", (string?)second.Attribute("page"));
            Assert.Equal(rawCookie, (string?)second.Attribute("paging-cookie"));

            // The paging annotations are only returned when requested.
            Assert.All(requests, r =>
            {
                var prefer = string.Join(",", r.Headers.GetValues("Prefer"));
                Assert.Contains("Microsoft.Dynamics.CRM.fetchxmlpagingcookie", prefer);
                Assert.Contains("Microsoft.Dynamics.CRM.morerecords", prefer);
            });
        }

        [Fact]
        public async Task Fetch_WithMoreRecordsButNoCookie_FallsBackToSimplePaging()
        {
            var requests = new List<HttpRequestMessage>();
            _handler.AddJsonResponse(req => { requests.Add(req); return true; }, Page("Account 1", moreRecords: true, cookieXml: null));
            _handler.AddJsonResponse(req => { requests.Add(req); return true; }, Page("Account 2", moreRecords: false, cookieXml: null));

            var results = await _client.Fetch<TestAccount>("<fetch><entity name='account' /></fetch>").ExecuteAsync();

            Assert.Equal(2, results.Count);
            Assert.Equal("2", (string?)GetFetch(requests[1]).Attribute("page"));
            Assert.Null(GetFetch(requests[1]).Attribute("paging-cookie"));
        }

        [Fact]
        public async Task Fetch_WithFormattedValues_RequestsFormattedAndPagingAnnotations()
        {
            string? prefer = null;
            _handler.AddJsonResponse(req => { prefer = string.Join(",", req.Headers.GetValues("Prefer")); return true; },
                Page("Account 1", moreRecords: false, cookieXml: null));

            await _client.Fetch<TestAccount>("<fetch><entity name='account' /></fetch>", new Dataverse.Fluent.Builders.FetchXmlOptions { IncludeFormattedValues = true })
                .ExecuteAsync();

            Assert.Equal(
                "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue,Microsoft.Dynamics.CRM.fetchxmlpagingcookie,Microsoft.Dynamics.CRM.morerecords\"",
                prefer);
        }

        [Theory]
        [InlineData("<fetch top='5'><entity name='account' /></fetch>")]
        [InlineData("<fetch page='3' count='10'><entity name='account' /></fetch>")]
        [InlineData("<fetch aggregate='true'><entity name='account'><attribute name='accountid' alias='c' aggregate='count' /></entity></fetch>")]
        public async Task Fetch_WithTopExplicitPageOrAggregate_ExecutesSingleRequestUnchanged(string fetchXml)
        {
            HttpRequestMessage? captured = null;
            // morerecords is ignored for these queries; a second request would fail (no response queued).
            _handler.AddJsonResponse(req => { captured = req; return true; }, Page("Account 1", moreRecords: true, cookieXml: null));

            var results = await _client.Fetch<TestAccount>(fetchXml).ExecuteAsync();

            Assert.Single(results);
            Assert.True(System.Xml.Linq.XNode.DeepEquals(System.Xml.Linq.XElement.Parse(fetchXml), GetFetch(captured!)));
        }

        [Fact]
        public void ExtractPagingCookie_DecodesPagingCookieAttributeTwice()
        {
            const string rawCookie = "<cookie page=\"1\"><name last=\"R&amp;D 100%\" first=\"A\" /></cookie>";
            var annotation = $"<cookie pagenumber=\"2\" pagingcookie=\"{Uri.EscapeDataString(Uri.EscapeDataString(rawCookie))}\" istracking=\"False\" />";

            var cookie = Dataverse.Fluent.Builders.FetchXmlExecutor<TestAccount>.ExtractPagingCookie(annotation);

            Assert.Equal(rawCookie, cookie);
        }

        // ────────── Options ──────────

        private const string SimpleFetch = "<fetch><entity name='account'><attribute name='name' /></entity></fetch>";

        [Fact]
        public async Task WithOptions_Object_AppliesNoLockPageSizeAndFormattedValues()
        {
            _handler.AddJsonResponse(_ => true, Page("A", moreRecords: false, cookieXml: null));

            await _client.Fetch<TestAccount>(SimpleFetch)
                .WithOptions(new FetchXmlOptions { NoLock = true, MaxPageSize = 25, IncludeFormattedValues = true })
                .ExecuteAsync();

            var request = Assert.Single(_handler.Requests);
            var fetch = System.Xml.Linq.XElement.Parse(request.QueryOption("fetchXml")!);
            Assert.Equal("true", (string?)fetch.Attribute("no-lock"));
            Assert.Equal("25", (string?)fetch.Attribute("count"));
            Assert.Contains("OData.Community.Display.V1.FormattedValue", request.Header("Prefer"));
        }

        [Fact]
        public async Task NoLockOption_IsAppliedToSinglePageQueriesToo()
        {
            _handler.AddJsonResponse(_ => true, Page("A", moreRecords: false, cookieXml: null));

            await _client.Fetch<TestAccount>("<fetch top='5'><entity name='account' /></fetch>", new FetchXmlOptions { NoLock = true })
                .ExecuteAsync();

            var fetch = System.Xml.Linq.XElement.Parse(Assert.Single(_handler.Requests).QueryOption("fetchXml")!);
            Assert.Equal("true", (string?)fetch.Attribute("no-lock"));
            Assert.Null(fetch.Attribute("count")); // top queries are sent unchanged otherwise
        }

        [Fact]
        public async Task WithOptions_Delegate_StartsFromTheCurrentOptions()
        {
            _handler.AddJsonResponse(_ => true, Page("A", moreRecords: false, cookieXml: null));

            await _client.Fetch<TestAccount>(SimpleFetch, new FetchXmlOptions { NoLock = true })
                .WithOptions(o => o.MaxPageSize = 10)
                .ExecuteAsync();

            var fetch = System.Xml.Linq.XElement.Parse(Assert.Single(_handler.Requests).QueryOption("fetchXml")!);
            Assert.Equal("true", (string?)fetch.Attribute("no-lock")); // kept from the earlier options
            Assert.Equal("10", (string?)fetch.Attribute("count"));
        }

        [Fact]
        public async Task WithOptions_ReturnsANewExecutor_LeavingTheOriginalUnchanged()
        {
            _handler.AddJsonResponse(_ => true, Page("A", moreRecords: false, cookieXml: null));
            var original = _client.Fetch<TestAccount>(SimpleFetch);

            _ = original.WithOptions(o => o.NoLock = true);
            await original.ExecuteAsync();

            var fetch = System.Xml.Linq.XElement.Parse(Assert.Single(_handler.Requests).QueryOption("fetchXml")!);
            Assert.Null(fetch.Attribute("no-lock"));
        }
    }
}
