using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Tests
{
    [EntitySet("testcontacts")]
    internal class TestContact
    {
        [JsonPropertyName("contactid")]
        public Guid ContactId { get; set; }

        [JsonPropertyName("firstname")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("lastname")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("emailaddress1")]
        public string? EmailAddress { get; set; }

        [JsonPropertyName("jobtitle")]
        public string? JobTitle { get; set; }

        [JsonPropertyName("statecode")]
        public int StateCode { get; set; }
    }
}
