using System.Text.Json.Nodes;

namespace Dataverse.Fluent.IntegrationTests;

// Function parameters of each type, against real Dataverse functions, asserting the values that come back.
// A 2026-11-18 probe found GUIDs and dates were sent quoted ("Edm.String cannot be converted to Edm.Guid") and enums as
// integers ("Edm.Int32 cannot be converted to Microsoft.Dynamics.CRM.EndpointAccessType"): all three always failed.
[Collection("Dataverse Integration Tests")]
public class FunctionParameterTests : IClassFixture<DataverseTestFixture>
{
    // Named as Dataverse names them: an enum parameter is sent as Microsoft.Dynamics.CRM.{type}'{member}'.
    [Flags]
    public enum EntityFilters { Entity = 1, Attributes = 2, Privileges = 4, Relationships = 8 }

    public enum EndpointAccessType { Default = 0, Internet = 1, Intranet = 2 }

    private const int PacificTimeZoneCode = 4;

    private readonly DataverseTestFixture _fixture;

    public FunctionParameterTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AStringAndAnInt_ArePassed()
    {
        // Matches the standard name ("Pacific Standard Time"), not the display name.
        var result = await _fixture.Client.Function<JsonObject>("GetTimeZoneCodeByLocalizedName")
            .WithParameter("LocalizedStandardName", "Pacific Standard Time")
            .WithParameter("LocaleId", 1033)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PacificTimeZoneCode, (int)result!["TimeZoneCode"]!);
    }

    [Fact]
    public async Task AStringWithSpecialCharacters_ArrivesUnchanged()
    {
        // FetchXmlToQueryExpression echoes the condition value back, so the text Dataverse received can be compared.
        const string text = "A & B + C % D # E ? F 'G' (H) é";
        var fetchXml = $"<fetch><entity name='account'><filter><condition attribute='name' operator='eq' value='{System.Security.SecurityElement.Escape(text)}' /></filter></entity></fetch>";

        var result = await _fixture.Client.Function<JsonObject>("FetchXmlToQueryExpression")
            .WithParameter("FetchXml", fetchXml)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        var value = result!["Query"]!["Criteria"]!["Conditions"]![0]!["Values"]![0]!["Value"];
        Assert.Equal(text, (string)value!);
    }

    public static TheoryData<string> Instants => ["DateTime Utc", "DateTime Local", "DateTime Unspecified", "DateTimeOffset -08:00"];

    [Theory]
    [MemberData(nameof(Instants))]
    public async Task ADateParameter_IsTheRightInstant(string kind)
    {
        // 2026-11-18 20:00 UTC is 12:00 in Pacific standard time.
        var utc = new DateTime(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);
        // Each arm is cast to object: otherwise C# converts every arm to DateTimeOffset, which reads an
        // unspecified-kind DateTime as local time.
        var value = kind switch
        {
            "DateTime Utc" => (object)utc,
            "DateTime Local" => (object)utc.ToLocalTime(),
            "DateTime Unspecified" => (object)DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), // taken as UTC
            _ => (object)new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.FromHours(-8))
        };

        var result = await _fixture.Client.Function<JsonObject>("LocalTimeFromUtcTime")
            .WithParameter("TimeZoneCode", PacificTimeZoneCode)
            .WithParameter("UtcTime", value)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("2026-11-18T12:00:00", (string)result!["LocalTime"]!);
    }

    [Fact]
    public async Task AStringCollection_IsPassedAsACollection()
    {
        var result = await _fixture.Client.Function<JsonObject>("RetrieveTotalRecordCount")
            .WithParameter("EntityNames", new[] { "account", "contact" })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        var keys = result!["EntityRecordCountCollection"]!["Keys"]!.AsArray().Select(k => (string)k!).ToList();
        Assert.Equal(["account", "contact"], keys.Order());
    }

    [Fact]
    public async Task AGuidParameter_IdentifiesTheRecord()
    {
        var ct = TestContext.Current.CancellationToken;
        var definition = await _fixture.Client.Transport.ExecuteRequestAsync(HttpMethod.Get,
            "EntityDefinitions(LogicalName='account')?$select=MetadataId", ct: ct);
        var metadataId = Guid.Parse((string)definition["MetadataId"]!);

        // LogicalName is null, so Dataverse can only find the table by the GUID.
        var result = await _fixture.Client.Function<JsonObject>("RetrieveEntity")
            .WithParameter("EntityFilters", EntityFilters.Entity)
            .WithParameter("LogicalName", null)
            .WithParameter("MetadataId", metadataId)
            .WithParameter("RetrieveAsIfPublished", false)
            .ExecuteAsync(ct);

        Assert.Equal("account", (string)result!["EntityMetadata"]!["LogicalName"]!);
    }

    [Fact]
    public async Task AFlagsEnumParameter_SendsEveryFlag()
    {
        var result = await _fixture.Client.Function<JsonObject>("RetrieveEntity")
            .WithParameter("EntityFilters", EntityFilters.Entity | EntityFilters.Attributes)
            .WithParameter("LogicalName", "account")
            .WithParameter("MetadataId", Guid.Empty)
            .WithParameter("RetrieveAsIfPublished", false)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // The Attributes flag is what makes Dataverse include the columns.
        Assert.NotEmpty(result!["EntityMetadata"]!["Attributes"]!.AsArray());
    }

    [Fact]
    public async Task AnEnumParameter_IsSentAsTheDataverseEnumMember()
    {
        var result = await _fixture.Client.Function<JsonObject>("RetrieveCurrentOrganization")
            .WithParameter("AccessType", EndpointAccessType.Default)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, Guid.Parse((string)result!["Detail"]!["OrganizationId"]!));
    }
}
