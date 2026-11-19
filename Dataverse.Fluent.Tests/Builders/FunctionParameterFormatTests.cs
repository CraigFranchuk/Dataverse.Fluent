using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent.Tests.Builders;

// How function parameters are written. Each form was checked against Dataverse on 2026-11-18; the live tests are in
// Dataverse.Fluent.IntegrationTests/FunctionParameterTests.cs. Quoted GUIDs and dates, and enums as integers, are
// rejected ("Edm.String cannot be converted to Edm.Guid", "Edm.Int32 cannot be converted to ...EntityFilters").
public class FunctionParameterFormatTests
{
    [Flags]
    private enum EntityFilters { Entity = 1, Attributes = 2 }

    private enum EndpointAccessType { Default = 0, Internet = 1 }

    [Fact]
    public void Guid_IsUnquoted()
        => Assert.Equal("11111111-1111-1111-1111-111111111111",
            FunctionBuilder<object>.FormatLiteral(Guid.Parse("11111111-1111-1111-1111-111111111111")));

    [Fact]
    public void Dates_AreUnquotedUtc()
    {
        Assert.Equal("2026-11-18T20:00:00Z", FunctionBuilder<object>.FormatLiteral(new DateTime(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("2026-11-18T20:00:00Z", FunctionBuilder<object>.FormatLiteral(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.FromHours(-8))));
        Assert.Equal("2026-11-18", FunctionBuilder<object>.FormatLiteral(new DateOnly(2026, 11, 18)));
    }

    [Fact]
    public void Enum_IsTheQualifiedMember()
        => Assert.Equal("Microsoft.Dynamics.CRM.EndpointAccessType'Default'", FunctionBuilder<object>.FormatLiteral(EndpointAccessType.Default));

    [Fact]
    public void FlagsEnum_ListsEveryMember()
        => Assert.Equal("Microsoft.Dynamics.CRM.EntityFilters'Entity,Attributes'",
            FunctionBuilder<object>.FormatLiteral(EntityFilters.Entity | EntityFilters.Attributes));

    [Fact]
    public void AnEnumValueWithNoMember_IsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => FunctionBuilder<object>.FormatLiteral((EndpointAccessType)7));

        Assert.Contains("no member for the value 7", ex.Message);
    }

    [Fact]
    public void StringsAndCollections_AreUnchanged()
    {
        Assert.Equal("'O''Neil'", FunctionBuilder<object>.FormatLiteral("O'Neil"));
        Assert.Equal("[\"account\",\"contact\"]", FunctionBuilder<object>.FormatLiteral(new[] { "account", "contact" }));
    }
}
