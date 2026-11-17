using System.Globalization;
using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent.Tests.Builders;

public class AlternateKeyFormatterTests
{
    private enum AccountCategory
    {
        Preferred = 1,
        Standard = 2
    }

    public static TheoryData<object?, string> Literals => new()
    {
        { null, "null" },
        { "ACC-001", "'ACC-001'" },
        { "O'Brien & Co", "'O''Brien%20%26%20Co'" },
        { 42, "42" },
        { 42L, "42" },
        { 1.5m, "1.5" },
        { 2.25d, "2.25" },
        { true, "true" },
        { false, "false" },
        { Guid.Parse("12345678-1234-1234-1234-123456789012"), "12345678-1234-1234-1234-123456789012" },
        // Choice (option set) keys are sent as their integer value, not the enum member name.
        { AccountCategory.Standard, "2" },
        // Date/time literals are unquoted ISO 8601, matching the OData $filter formatting.
        { new DateTime(2026, 10, 26, 14, 30, 5, DateTimeKind.Utc), "2026-10-26T14:30:05Z" },
        { new DateTime(2026, 10, 26, 14, 30, 5, DateTimeKind.Unspecified), "2026-10-26T14:30:05Z" },
        { new DateTimeOffset(2026, 10, 26, 19, 30, 5, TimeSpan.FromHours(5)), "2026-10-26T14:30:05Z" },
        { new DateOnly(2026, 10, 26), "2026-10-26" },
    };

    [Theory]
    [MemberData(nameof(Literals))]
    public void FormatValue_EmitsODataLiteral(object? value, string expected)
    {
        Assert.Equal(expected, AlternateKeyFormatter.FormatValue(value));
    }

    [Fact]
    public void FormatValue_LocalDateTime_IsConvertedToUtc()
    {
        var utc = new DateTime(2026, 10, 26, 14, 30, 5, DateTimeKind.Utc);

        Assert.Equal("2026-10-26T14:30:05Z", AlternateKeyFormatter.FormatValue(utc.ToLocalTime()));
    }

    [Fact]
    public void FormatValue_UsesInvariantCulture_RegardlessOfCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("1.5", AlternateKeyFormatter.FormatValue(1.5m));
            Assert.Equal("2026-10-26T14:30:05Z",
                AlternateKeyFormatter.FormatValue(new DateTime(2026, 10, 26, 14, 30, 5, DateTimeKind.Utc)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void FormatCompositeKey_FormatsEachValueByType()
    {
        var keys = new Dictionary<string, object>
        {
            ["accountnumber"] = "ACC-001",
            ["category"] = AccountCategory.Preferred,
            ["opendate"] = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };

        Assert.Equal("accountnumber='ACC-001',category=1,opendate=2026-01-02T00:00:00Z",
            AlternateKeyFormatter.FormatCompositeKey(keys));
    }
}
