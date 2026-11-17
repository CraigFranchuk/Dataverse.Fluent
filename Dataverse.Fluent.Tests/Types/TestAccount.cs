using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Tests
{
    [EntitySet("testaccounts")]
    internal class TestAccount
    {
        [JsonPropertyName("accountid")]
        public Guid AccountId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("accountnumber")]
        public string? AccountNumber { get; set; }

        [JsonPropertyName("revenue")]
        public decimal Revenue { get; set; }

        [JsonPropertyName("numberofemployees")]
        public int NumberOfEmployees { get; set; }

        [JsonPropertyName("createdon")]
        public DateTime CreatedOn { get; set; }

        [JsonPropertyName("statecode")]
        public int StateCode { get; set; }
    }
}
