using System.Text.Json;
using Dataverse.Fluent.Builders;
using Xunit;

namespace Dataverse.Fluent.Tests.Builders
{
    public class FetchBuilderTests
    {
        private readonly TestHttpMessageHandler _handler;
        private readonly DataverseClient _client;

        public FetchBuilderTests()
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
        public void BuildFetchXml_WithSingleAttribute_GeneratesCorrectXml()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            builder.AddAttribute(a => a.Name);

            var fetchXml = builder.BuildFetchXml();

            Assert.Contains("<entity name=\"testaccount\">", fetchXml);
            Assert.Contains("<attribute name=\"name\" />", fetchXml);
        }

        [Fact]
        public void BuildFetchXml_WithMultipleAttributes_GeneratesCorrectXml()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            builder.AddAttributes(a => new { a.Name, a.Revenue, a.CreatedOn });

            var fetchXml = builder.BuildFetchXml();

            Assert.Contains("<attribute name=\"name\" />", fetchXml);
            Assert.Contains("<attribute name=\"revenue\" />", fetchXml);
            Assert.Contains("<attribute name=\"createdon\" />", fetchXml);
        }

        [Fact]
        public void BuildFetchXml_WithAllAttributes_GeneratesCorrectXml()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            builder.AddAllAttributes();

            var fetchXml = builder.BuildFetchXml();

            Assert.Contains("<all-attributes />", fetchXml);
        }

        [Fact]
        public void BuildFetchXml_WithFilter_GeneratesCorrectXml()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            builder.AddAllAttributes()
                   .AddFilter(a => a.Revenue > 100000);

            var fetchXml = builder.BuildFetchXml();

            Assert.Contains("<filter type=\"and\">", fetchXml);
            Assert.Contains("<condition attribute=\"revenue\" operator=\"gt\" value=\"100000\"", fetchXml);
        }

        [Fact]
        public void BuildFetchXml_WithMultipleFilters_GeneratesCorrectXml()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            builder.AddAllAttributes()
                   .AddFilter(a => a.Revenue > 100000)
                   .AddFilter(a => a.StateCode == 0);

            var fetchXml = builder.BuildFetchXml();

            Assert.Contains("<filter type=\"and\">", fetchXml);
            Assert.Contains("attribute=\"revenue\" operator=\"gt\"", fetchXml);
            Assert.Contains("attribute=\"statecode\" operator=\"eq\"", fetchXml);
        }

        [Fact]
        public void BuildFetchXml_WithComplexFilter_GeneratesCorrectXml()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            builder.AddAllAttributes()
                   .AddFilter(a => a.Revenue > 100000 && a.StateCode == 0);

            var fetchXml = builder.BuildFetchXml();

            Assert.Contains("<filter", fetchXml);
            Assert.Contains("attribute=\"revenue\"", fetchXml);
            Assert.Contains("attribute=\"statecode\"", fetchXml);
        }

        [Fact]
        public void BuildFetchXml_WithStringContains_GeneratesLikeCondition()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            builder.AddAllAttributes()
                   .AddFilter(a => a.Name.Contains("Test"));

            var fetchXml = builder.BuildFetchXml();

            Assert.Contains("operator=\"like\"", fetchXml);
            Assert.Contains("%Test%", fetchXml);
        }

        [Fact]
        public void BuildFetchXml_WithStringStartsWith_GeneratesLikeCondition()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            builder.AddAllAttributes()
                   .AddFilter(a => a.Name.StartsWith("Test"));

            var fetchXml = builder.BuildFetchXml();

            Assert.Contains("operator=\"like\"", fetchXml);
            Assert.Contains("Test%", fetchXml);
        }

        [Fact]
        public void BuildFetchXml_WithOrder_GeneratesCorrectXml()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            builder.AddAllAttributes()
                   .AddOrder(a => a.Name);

            var fetchXml = builder.BuildFetchXml();

            Assert.Contains("<order attribute=\"name\"", fetchXml);
            Assert.DoesNotContain("descending=\"true\"", fetchXml);
        }

        [Fact]
        public void BuildFetchXml_WithOrderDescending_GeneratesCorrectXml()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            builder.AddAllAttributes()
                   .AddOrder(a => a.Revenue, descending: true);

            var fetchXml = builder.BuildFetchXml();

            Assert.Contains("<order attribute=\"revenue\" descending=\"true\"", fetchXml);
        }

        [Fact]
        public void BuildFetchXml_WithTop_GeneratesCorrectXml()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            builder.AddAllAttributes()
                   .Top(10);

            var fetchXml = builder.BuildFetchXml();

            Assert.Contains("<fetch top=\"10\"", fetchXml);
        }

        [Fact]
        public void BuildFetchXml_WithDistinct_GeneratesCorrectXml()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            builder.AddAllAttributes()
                   .Distinct();

            var fetchXml = builder.BuildFetchXml();

            Assert.Contains("distinct=\"true\"", fetchXml);
        }

        [Fact]
        public void BuildFetchXml_WithNoLock_GeneratesCorrectXml()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            builder.AddAllAttributes()
                   .NoLock();

            var fetchXml = builder.BuildFetchXml();

            Assert.Contains("no-lock=\"true\"", fetchXml);
        }

        [Fact]
        public void BuildFetchXml_WithPage_GeneratesCorrectXml()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            builder.AddAllAttributes()
                   .Page(2, 100);

            var fetchXml = builder.BuildFetchXml();

            Assert.Contains("page=\"2\"", fetchXml);
            Assert.Contains("count=\"100\"", fetchXml);
        }

        [Fact]
        public async Task ExecuteAsync_WithValidResponse_ReturnsDeserializedEntities()
        {
            var expectedAccounts = new[]
            {
                new TestAccount { AccountId = Guid.NewGuid(), Name = "Test Account 1", Revenue = 50000 },
                new TestAccount { AccountId = Guid.NewGuid(), Name = "Test Account 2", Revenue = 75000 }
            };

            var responseJson = new
            {
                value = expectedAccounts
            };

            _handler.AddJsonResponse(
                req => req.RequestUri!.ToString().Contains("testaccounts") && req.Method == HttpMethod.Get,
                responseJson
            );

            var builder = _client.FetchBuilder<TestAccount>();
            var results = await builder.AddAllAttributes().ExecuteAsync();

            Assert.Equal(2, results.Count);
            Assert.Equal("Test Account 1", results[0].Name);
            Assert.Equal("Test Account 2", results[1].Name);
        }

        [Fact]
        public async Task StreamAsync_WithMultiplePages_ReturnsAllEntities()
        {
            _handler.AddResponse(
                req => req.RequestUri!.ToString().Contains("testaccounts") && !req.RequestUri.ToString().Contains("page2cookie"),
                new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        value = new[]
                        {
                            new { accountid = Guid.NewGuid(), name = "Account 1", revenue = 10000 },
                            new { accountid = Guid.NewGuid(), name = "Account 2", revenue = 20000 }
                        }
                    }), System.Text.Encoding.UTF8, "application/json")
                }
            );

            _handler.AddResponse(
                req => req.RequestUri!.ToString().Contains("testaccounts"),
                new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        value = new[]
                        {
                            new { accountid = Guid.NewGuid(), name = "Account 3", revenue = 30000 }
                        }
                    }), System.Text.Encoding.UTF8, "application/json")
                }
            );

            var builder = _client.FetchBuilder<TestAccount>();
            var results = new List<TestAccount>();

            await foreach (var account in builder.AddAllAttributes().StreamAsync())
            {
                results.Add(account);
            }

            Assert.True(results.Count >= 1);
        }

        [Fact]
        public void BuildFetchXml_CompleteExample_GeneratesValidXml()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            var fetchXml = builder
                .AddAttributes(a => new { a.Name, a.Revenue, a.CreatedOn })
                .AddFilter(a => a.Revenue > 100000 && a.StateCode == 0)
                .AddOrder(a => a.CreatedOn, descending: true)
                .Top(50)
                .NoLock()
                .BuildFetchXml();

            Assert.Contains("<fetch", fetchXml);
            Assert.Contains("top=\"50\"", fetchXml);
            Assert.Contains("no-lock=\"true\"", fetchXml);
            Assert.Contains("<entity name=\"testaccount\">", fetchXml);
            Assert.Contains("<attribute name=\"name\"", fetchXml);
            Assert.Contains("<attribute name=\"revenue\"", fetchXml);
            Assert.Contains("<attribute name=\"createdon\"", fetchXml);
            Assert.Contains("<filter", fetchXml);
            Assert.Contains("<order attribute=\"createdon\" descending=\"true\"", fetchXml);
        }

        [Fact]
        public void BuildFetchXml_WithTop_DoesNotIncludePagination()
        {
            var builder = _client.FetchBuilder<TestAccount>();
            var fetchXml = builder
                .AddAllAttributes()
                .Top(10)
                .BuildFetchXml();

            // Should have 'top' attribute
            Assert.Contains("top=\"10\"", fetchXml);
            
            // Should NOT have pagination attributes (page/count)
            Assert.DoesNotContain("page=", fetchXml);
            Assert.DoesNotContain("count=", fetchXml);
        }

        [Fact]
        public async Task ExecuteAsync_WithTop_DoesNotApplyAutoPagination()
        {
            var expectedAccounts = new[]
            {
                new TestAccount { AccountId = Guid.NewGuid(), Name = "Test Account 1", Revenue = 50000 }
            };

            var responseJson = new
            {
                value = expectedAccounts
            };

            _handler.AddJsonResponse(
                req => {
                    var url = req.RequestUri!.ToString();
                    // Verify URL contains 'top' in the FetchXML but not 'page' or 'count'
                    var decodedUrl = Uri.UnescapeDataString(url);
                    return url.Contains("testaccounts") 
                        && decodedUrl.Contains("top=") 
                        && !decodedUrl.Contains("page=") 
                        && !decodedUrl.Contains("count=");
                },
                responseJson
            );

            var builder = _client.FetchBuilder<TestAccount>();
            var results = await builder
                .AddAllAttributes()
                .Top(10)
                .ExecuteAsync();

            Assert.Single(results);
            Assert.Equal("Test Account 1", results[0].Name);
        }
    }
}
