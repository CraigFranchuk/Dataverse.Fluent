using System.Linq.Expressions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Dataverse.Fluent.Builders;

namespace Dataverse.Fluent.Tests.Builders;

public class ODataExpressionVisitorTests
{
    private enum AccountStatus
    {
        Active = 0,
        Inactive = 1,
        Suspended = 2
    }

    private class TestEntity
    {
        public int Age { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public decimal Salary { get; set; }
        public Guid Id { get; set; }
        public DateTime CreatedDate { get; set; }

        [JsonPropertyName("custom_field")]
        public string CustomField { get; set; } = string.Empty;

        [JsonPropertyName("statecode")]
        public AccountStatus Status { get; set; }

        [JsonPropertyName("revenue")]
        public decimal? Revenue { get; set; }

        [JsonPropertyName("numberofemployees")]
        public int? NumberOfEmployees { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("ownerid")]
        public Guid OwnerId { get; set; }

        [JsonPropertyName("primarycontact")]
        public RelatedContact PrimaryContact { get; set; } = new();
    }

    private class RelatedContact
    {
        [JsonPropertyName("lastname")]
        public string LastName { get; set; } = string.Empty;

        public int Age { get; set; }
    }

    [Fact]
    public void Filter_GreaterThan_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Age > 18);
        
        Assert.Equal("age gt 18", result);
    }

    [Fact]
    public void Filter_GreaterThanOrEqual_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Age >= 21);
        
        Assert.Equal("age ge 21", result);
    }

    [Fact]
    public void Filter_LessThan_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Age < 65);
        
        Assert.Equal("age lt 65", result);
    }

    [Fact]
    public void Filter_LessThanOrEqual_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Age <= 60);
        
        Assert.Equal("age le 60", result);
    }

    [Fact]
    public void Filter_Equal_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Age == 30);
        
        Assert.Equal("age eq 30", result);
    }

    [Fact]
    public void Filter_NotEqual_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Age != 0);
        
        Assert.Equal("age ne 0", result);
    }

    [Fact]
    public void Filter_StringEqual_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Name == "John");
        
        Assert.Equal("name eq 'John'", result);
    }

    [Fact]
    public void Filter_StringWithApostrophe_EscapesCorrectly()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Name == "O'Brien");
        
        Assert.Equal("name eq 'O''Brien'", result);
    }

    [Fact]
    public void Filter_Boolean_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.IsActive == true);

        Assert.Equal("isactive eq true", result);
    }

    [Fact]
    public void Filter_BooleanFalse_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.IsActive == false);

        Assert.Equal("isactive eq false", result);
    }

    [Fact]
    public void Filter_Decimal_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Salary > 50000.50m);
        
        Assert.Equal("salary gt 50000.50", result);
    }

    [Fact]
    public void Filter_Guid_GeneratesCorrectODataFilter()
    {
        var testGuid = Guid.Parse("12345678-1234-1234-1234-123456789012");
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Id == testGuid);

        Assert.Equal("id eq '12345678-1234-1234-1234-123456789012'", result);
    }

    [Fact]
    public void Filter_Decimal_UsesInvariantCulture_RegardlessOfCurrentCulture()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // de-DE uses ',' as the decimal separator, which would corrupt the OData filter
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            var visitor = new ODataExpressionVisitor();
            var result = visitor.ToODataFilter((TestEntity x) => x.Salary > 50000.50m);

            Assert.Equal("salary gt 50000.50", result);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Filter_DateTimeLocal_IsConvertedToUtc()
    {
        // A Local DateTime should be converted to its UTC equivalent before formatting.
        var localKind = DateTime.SpecifyKind(new DateTime(2024, 1, 1, 12, 0, 0), DateTimeKind.Local);
        var expectedUtc = localKind.ToUniversalTime();

        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.CreatedDate > localKind);

        Assert.Equal($"createddate gt {expectedUtc:yyyy-MM-ddTHH:mm:ssZ}", result);
    }

    [Fact]
    public void Filter_DateTimeUtc_EmittedAsUtc()
    {
        var utc = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.CreatedDate > utc);

        Assert.Equal("createddate gt 2024-01-01T10:00:00Z", result);
    }

    [Fact]
    public void Filter_DateTimeUnspecified_TreatedAsUtc()
    {
        var unspecified = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Unspecified);

        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.CreatedDate > unspecified);

        Assert.Equal("createddate gt 2024-01-01T10:00:00Z", result);
    }

    [Fact]
    public void Filter_AndAlso_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Age > 18 && x.IsActive == true);

        Assert.Equal("age gt 18 and isactive eq true", result);
    }

    [Fact]
    public void Filter_OrElse_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Age < 18 || x.Age > 65);
        
        Assert.Equal("age lt 18 or age gt 65", result);
    }

    [Fact]
    public void Filter_ComplexCondition_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => 
            (x.Age >= 18 && x.Age <= 65) && x.IsActive == true);

        Assert.Equal("age ge 18 and age le 65 and isactive eq true", result);
    }

    [Fact]
    public void Filter_Not_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => !x.IsActive);

        Assert.Equal("not (isactive)", result);
    }

    [Fact]
    public void Filter_StringContains_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Name.Contains("Smith"));
        
        Assert.Equal("contains(name, 'Smith')", result);
    }

    [Fact]
    public void Filter_StringStartsWith_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Name.StartsWith("John"));
        
        Assert.Equal("startswith(name, 'John')", result);
    }

    [Fact]
    public void Filter_StringEndsWith_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Name.EndsWith("son"));
        
        Assert.Equal("endswith(name, 'son')", result);
    }

    [Fact]
    public void Filter_JsonPropertyName_UsesCustomName()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.CustomField == "test");
        
        Assert.Equal("custom_field eq 'test'", result);
    }

    [Fact]
    public void Filter_VariableCapture_GeneratesCorrectODataFilter()
    {
        var minAge = 18;
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Age > minAge);
        
        Assert.Equal("age gt 18", result);
    }

    [Fact]
    public void Filter_ComplexVariableCapture_GeneratesCorrectODataFilter()
    {
        var criteria = new { MinAge = 18, MaxAge = 65, Status = "Active" };
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => 
            x.Age >= criteria.MinAge && x.Age <= criteria.MaxAge);
        
        Assert.Equal("age ge 18 and age le 65", result);
    }

    [Fact]
    public void Filter_NullComparison_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Name == null);
        
        Assert.Equal("name eq null", result);
    }

    [Fact]
    public void Filter_NullExpression_ThrowsArgumentNullException()
    {
        var visitor = new ODataExpressionVisitor();
        
        Assert.Throws<ArgumentNullException>(() => 
            visitor.ToODataFilter((System.Linq.Expressions.Expression<Func<TestEntity, bool>>)null!));
    }

    [Fact]
    public void Filter_UnsupportedOperator_ThrowsNotSupportedException()
    {
        var visitor = new ODataExpressionVisitor();
        
        Assert.Throws<NotSupportedException>(() => 
            visitor.ToODataFilter((TestEntity x) => x.Age ^ 2));
    }

    [Fact]
    public void Filter_DateTime_GeneratesCorrectODataFilter()
    {
        var testDate = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.CreatedDate >= testDate);

        Assert.Equal("createddate ge 2024-01-15T10:30:00Z", result);
    }

    [Fact]
    public void Filter_MultipleStringConditions_GeneratesCorrectODataFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => 
            x.Name.StartsWith("J") && x.Name.EndsWith("n") && x.Name.Contains("oh"));

        Assert.Equal("startswith(name, 'J') and endswith(name, 'n') and contains(name, 'oh')", result);
    }

    // ────────── Nullable<T>.HasValue ──────────

    [Fact]
    public void Filter_NullableHasValue_GeneratesNeNull()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Revenue.HasValue);

        Assert.Equal("revenue ne null", result);
    }

    [Fact]
    public void Filter_NullableNotHasValue_GeneratesNotNeNull()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => !x.Revenue.HasValue);

        Assert.Equal("not (revenue ne null)", result);
    }

    [Fact]
    public void Filter_NullableHasValue_CombinedWithOtherCondition()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Revenue.HasValue && x.IsActive == true);

        Assert.Equal("revenue ne null and isactive eq true", result);
    }

    // ────────── Nullable<T>.Value ──────────

    [Fact]
    public void Filter_NullableValue_UnwrapsToPropertyName()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Revenue.Value > 1000m);

        Assert.Equal("revenue gt 1000", result);
    }

    [Fact]
    public void Filter_NullableIntValue_ComparesCorrectly()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.NumberOfEmployees.Value >= 50);

        Assert.Equal("numberofemployees ge 50", result);
    }

    // ────────── Null checks (reference types) ──────────

    [Fact]
    public void Filter_StringNotNull_GeneratesNeNull()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Name != null);

        Assert.Equal("name ne null", result);
    }

    [Fact]
    public void Filter_NullableStringEqNull_GeneratesEqNull()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Description == null);

        Assert.Equal("description eq null", result);
    }

    [Fact]
    public void Filter_NullableDecimalEqNull_GeneratesEqNull()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Revenue == null);

        Assert.Equal("revenue eq null", result);
    }

    [Fact]
    public void Filter_NullableDecimalNeNull_GeneratesNeNull()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Revenue != null);

        Assert.Equal("revenue ne null", result);
    }

    // ────────── Enum support ──────────

    [Fact]
    public void Filter_EnumEquality_ResolvesToIntegerValue()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Status == AccountStatus.Active);

        Assert.Equal("statecode eq 0", result);
    }

    [Fact]
    public void Filter_EnumNotEqual_ResolvesToIntegerValue()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Status != AccountStatus.Inactive);

        Assert.Equal("statecode ne 1", result);
    }

    [Fact]
    public void Filter_EnumFromVariable_ResolvesToIntegerValue()
    {
        var targetStatus = AccountStatus.Suspended;
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Status == targetStatus);

        Assert.Equal("statecode eq 2", result);
    }

    // ────────── string.IsNullOrEmpty ──────────

    [Fact]
    public void Filter_StringIsNullOrEmpty_GeneratesCompoundFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => string.IsNullOrEmpty(x.Name));

        Assert.Equal("(name eq null or name eq '')", result);
    }

    [Fact]
    public void Filter_NotStringIsNullOrEmpty_GeneratesNegatedFilter()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => !string.IsNullOrEmpty(x.Name));

        Assert.Equal("not ((name eq null or name eq ''))", result);
    }

    [Fact]
    public void Filter_StringIsNullOrEmpty_WithJsonPropertyName()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => string.IsNullOrEmpty(x.CustomField));

        Assert.Equal("(custom_field eq null or custom_field eq '')", result);
    }

    // ────────── Collection Contains → Microsoft.Dynamics.CRM.In (Dataverse rejects the OData 'in' operator) ──────────

    [Fact]
    public void Filter_ArrayContains_GeneratesInFunction()
    {
        var statuses = new[] { 0, 1 };
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => statuses.Contains(x.Age));

        Assert.Equal("Microsoft.Dynamics.CRM.In(PropertyName='age',PropertyValues=[\"0\",\"1\"])", result);
    }

    [Fact]
    public void Filter_ListContains_GeneratesInFunction()
    {
        var names = new List<string> { "Alpha", "Beta", "Gamma" };
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => names.Contains(x.Name));

        Assert.Equal("Microsoft.Dynamics.CRM.In(PropertyName='name',PropertyValues=[\"Alpha\",\"Beta\",\"Gamma\"])", result);
    }

    [Fact]
    public void Filter_EnumerableContainsExtension_GeneratesInFunction()
    {
        var ids = new[] { Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222") };
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => ids.Contains(x.Id));

        Assert.Equal("Microsoft.Dynamics.CRM.In(PropertyName='id',PropertyValues=[\"11111111-1111-1111-1111-111111111111\",\"22222222-2222-2222-2222-222222222222\"])", result);
    }

    [Fact]
    public void Filter_ContainsWithEnumValues_GeneratesInWithIntegers()
    {
        var allowedStatuses = new[] { AccountStatus.Active, AccountStatus.Suspended };
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => allowedStatuses.Contains(x.Status));

        Assert.Equal("Microsoft.Dynamics.CRM.In(PropertyName='statecode',PropertyValues=[\"0\",\"2\"])", result);
    }

    // ────────── Constructs Dataverse rejects (HTTP 501) fail before the request is sent ──────────

    // Each construct, and the reason its error gives.
    private static readonly Dictionary<string, (string Reason, Expression<Func<TestEntity, bool>> Filter)> UnsupportedFilters = new()
    {
        ["ToLower"] = ("tolower", x => x.Name.ToLower() == "john"),
        ["ToLowerInvariant"] = ("tolowerinvariant", x => x.Name.ToLowerInvariant() == "john"),
        ["ToUpper"] = ("toupper", x => x.Name.ToUpper() == "JOHN"),
        ["ToUpperInvariant"] = ("toupperinvariant", x => x.Name.ToUpperInvariant() == "JOHN"),
        ["Trim"] = ("Contains, StartsWith and EndsWith", x => x.Name.Trim() == "John"),
        ["Substring"] = ("Contains, StartsWith and EndsWith", x => x.Name.Substring(0, 3) == "Joh"),
        ["IndexOf"] = ("Contains, StartsWith and EndsWith", x => x.Name.IndexOf("oh") > 0),
        ["Length"] = ("length()", x => x.Name.Length > 10),
        ["Concat"] = ("concat()", x => string.Concat(x.Name, x.CustomField) == "test"),
        ["Add"] = ("arithmetic", x => x.Age + 5 > 25),
        ["Subtract"] = ("arithmetic", x => x.Age - 5 < 15),
        ["Multiply"] = ("arithmetic", x => x.Salary * 2 > 100000),
        ["Divide"] = ("arithmetic", x => x.Salary / 12 > 5000),
        ["Modulo"] = ("arithmetic", x => x.Age % 2 == 0),
    };

    public static TheoryData<string> UnsupportedFilterNames => [.. UnsupportedFilters.Keys];

    [Theory]
    [MemberData(nameof(UnsupportedFilterNames))]
    public void Filter_ThatDataverseRejects_ThrowsNotSupported_WithTheReason(string construct)
    {
        var (reason, filter) = UnsupportedFilters[construct];

        var ex = Assert.Throws<NotSupportedException>(() => new ODataExpressionVisitor().ToODataFilter(filter));

        Assert.Contains(reason, ex.Message);
    }

    [Fact]
    public void Filter_ArithmeticOnCapturedValues_IsComputedOnTheClient()
    {
        var limit = 10;

        Assert.Equal("age gt 9", new ODataExpressionVisitor().ToODataFilter((TestEntity x) => x.Age > limit - 1));
    }

    [Theory]
    [InlineData("a_b", "contains(name, 'a[_]b')")]
    [InlineData("50%", "contains(name, '50[%]')")]
    [InlineData("[x]", "contains(name, '[[]x]')")]
    [InlineData("O'Neil", "contains(name, 'O''Neil')")]
    public void Filter_StringFunctions_EscapeLikeWildcards(string text, string expected)
    {
        Assert.Equal(expected, new ODataExpressionVisitor().ToODataFilter((TestEntity x) => x.Name.Contains(text)));
    }

    [Fact]
    public void Filter_StartsWithAndEndsWith_EscapeLikeWildcards()
    {
        Assert.Equal("startswith(name, 'a[_]')", new ODataExpressionVisitor().ToODataFilter((TestEntity x) => x.Name.StartsWith("a_")));
        Assert.Equal("endswith(name, '[%]')", new ODataExpressionVisitor().ToODataFilter((TestEntity x) => x.Name.EndsWith("%")));
    }

    [Fact]
    public void Filter_ListContains_WithANullValue_Throws()
    {
        var names = new List<string?> { "a", null };

        Assert.Throws<NotSupportedException>(() => new ODataExpressionVisitor().ToODataFilter((TestEntity x) => names.Contains(x.Name)));
    }

    // ────────── Combined complex filters ──────────

    [Fact]
    public void Filter_NullableHasValueAndEnum_CombinesCorrectly()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) =>
            x.Revenue.HasValue && x.Status == AccountStatus.Active);

        Assert.Equal("revenue ne null and statecode eq 0", result);
    }

    [Fact]
    public void Filter_StringIsNullOrEmptyAndContains_CombinesCorrectly()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) =>
            !string.IsNullOrEmpty(x.Name) && x.Name.Contains("Corp"));

        Assert.Equal("not ((name eq null or name eq '')) and contains(name, 'Corp')", result);
    }

    [Fact]
    public void Filter_InOperatorAndNullCheck_CombinesCorrectly()
    {
        var validStatuses = new[] { AccountStatus.Active, AccountStatus.Suspended };
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) =>
            validStatuses.Contains(x.Status) && x.Revenue != null);

        Assert.Equal("Microsoft.Dynamics.CRM.In(PropertyName='statecode',PropertyValues=[\"0\",\"2\"]) and revenue ne null", result);
    }

    // ────────── DateTimeOffset literals ──────────

    [Fact]
    public void Filter_DateTimeOffset_EmittedUnquoted()
    {
        var dto = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.CreatedDate > dto.DateTime);

        Assert.Equal("createddate gt 2024-01-01T10:00:00Z", result);
    }

    // ────────── Navigation / lookup path filters ──────────

    [Fact]
    public void Filter_NavigationProperty_GeneratesSlashPath()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.PrimaryContact.LastName == "Smith");

        Assert.Equal("primarycontact/lastname eq 'Smith'", result);
    }

    [Fact]
    public void Filter_NavigationProperty_WithNumericComparison()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.PrimaryContact.Age > 18);

        Assert.Equal("primarycontact/age gt 18", result);
    }

    [Fact]
    public void Filter_NavigationProperty_CombinedWithRootCondition()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) =>
            x.IsActive == true && x.PrimaryContact.LastName == "Smith");

        Assert.Equal("isactive eq true and primarycontact/lastname eq 'Smith'", result);
    }

    // ────────── Dataverse query functions ──────────

    [Fact]
    public void Filter_EqualUserId_GeneratesCrmFunction()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => DataverseFunctions.EqualUserId(x.OwnerId));

        Assert.Equal("Microsoft.Dynamics.CRM.EqualUserId(PropertyName='ownerid')", result);
    }

    [Fact]
    public void Filter_Under_GeneratesCrmFunctionWithGuidValue()
    {
        var parentId = Guid.Parse("12345678-1234-1234-1234-123456789012");
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => DataverseFunctions.Under(x.Id, parentId));

        Assert.Equal("Microsoft.Dynamics.CRM.Under(PropertyName='id',PropertyValue='12345678-1234-1234-1234-123456789012')", result);
    }

    [Fact]
    public void Filter_LastXDays_GeneratesCrmFunctionWithIntValue()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => DataverseFunctions.LastXDays(x.CreatedDate, 30));

        Assert.Equal("Microsoft.Dynamics.CRM.LastXDays(PropertyName='createddate',PropertyValue='30')", result);
    }

    [Fact]
    public void Filter_In_GeneratesCrmFunctionWithPropertyValues()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => DataverseFunctions.In(x.Age, 1, 2, 3));

        Assert.Equal("Microsoft.Dynamics.CRM.In(PropertyName='age',PropertyValues=[\"1\",\"2\",\"3\"])", result);
    }

    [Fact]
    public void Filter_DataverseFunction_CombinedWithStandardCondition()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) =>
            DataverseFunctions.EqualUserId(x.OwnerId) && x.IsActive == true);

        Assert.Equal("Microsoft.Dynamics.CRM.EqualUserId(PropertyName='ownerid') and isactive eq true", result);
    }

    [Fact]
    public void DataverseFunctions_InvokedDirectly_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => DataverseFunctions.EqualUserId(Guid.NewGuid()));
    }

    // ────────── Operator precedence ──────────
    // OData 'and' binds tighter than 'or', so grouping
    // expressed structurally in the C# expression tree must be re-emitted as parentheses.

    [Fact]
    public void Precedence_OrInsideAnd_OnRight_IsParenthesized()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Age == 0 && (x.Name == "x" || x.Name == "y"));

        Assert.Equal("age eq 0 and (name eq 'x' or name eq 'y')", result);
    }

    [Fact]
    public void Precedence_OrInsideAnd_OnLeft_IsParenthesized()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => (x.Name == "x" || x.Name == "y") && x.Age == 0);

        Assert.Equal("(name eq 'x' or name eq 'y') and age eq 0", result);
    }

    [Fact]
    public void Precedence_AndInsideOr_NeedsNoParentheses()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Age == 0 && x.IsActive || x.Name == "y");

        Assert.Equal("age eq 0 and isactive or name eq 'y'", result);
    }

    [Fact]
    public void Precedence_ChainedAnds_StayFlat()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => x.Age > 1 && x.Age < 10 && x.IsActive);

        Assert.Equal("age gt 1 and age lt 10 and isactive", result);
    }

    [Fact]
    public void Precedence_NestedOrAndOr_IsParenthesizedOnBothSides()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) =>
            (x.Age == 1 || x.Age == 2) && (x.Name == "a" || x.Name == "b"));

        Assert.Equal("(age eq 1 or age eq 2) and (name eq 'a' or name eq 'b')", result);
    }

    [Fact]
    public void Precedence_HasValueInsideOr_StaysFlat()
    {
        var visitor = new ODataExpressionVisitor();
        var result = visitor.ToODataFilter((TestEntity x) => !x.Revenue.HasValue || x.Revenue > 5);

        Assert.Equal("not (revenue ne null) or revenue gt 5", result);
    }

    // ────────── Every DataverseFunctions marker ──────────

    // Builds x => DataverseFunctions.<function>((object)x.OwnerId[, value(s)]) — the shape the compiler emits.
    private static string TranslateFunction(string function, params object[] values)
    {
        var x = System.Linq.Expressions.Expression.Parameter(typeof(TestEntity), "x");
        var method = typeof(DataverseFunctions).GetMethod(function)!;
        var parameters = method.GetParameters();
        var args = new List<System.Linq.Expressions.Expression>
        {
            System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Property(x, nameof(TestEntity.OwnerId)), typeof(object))
        };
        if (parameters.Length == 2)
        {
            args.Add(parameters[1].ParameterType.IsArray
                ? System.Linq.Expressions.Expression.NewArrayInit(typeof(object),
                    values.Select(v => System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Constant(v), typeof(object))))
                : System.Linq.Expressions.Expression.Constant(values[0]));
        }

        var lambda = System.Linq.Expressions.Expression.Lambda<Func<TestEntity, bool>>(
            System.Linq.Expressions.Expression.Call(method, args), x);
        return new ODataExpressionVisitor().ToODataFilter(lambda);
    }

    [Theory]
    [InlineData("EqualUserId")]
    [InlineData("NotEqualUserId")]
    [InlineData("EqualUserTeams")]
    [InlineData("EqualUserOrUserTeams")]
    [InlineData("EqualBusinessId")]
    [InlineData("NotEqualBusinessId")]
    [InlineData("Today")]
    [InlineData("Yesterday")]
    [InlineData("Tomorrow")]
    [InlineData("ThisWeek")]
    [InlineData("ThisMonth")]
    [InlineData("ThisYear")]
    public void DataverseFunction_WithoutValue_TranslatesToCrmFunction(string function)
    {
        Assert.Equal($"Microsoft.Dynamics.CRM.{function}(PropertyName='ownerid')", TranslateFunction(function));
    }

    [Theory]
    [InlineData("Above")]
    [InlineData("AboveOrEqual")]
    [InlineData("Under")]
    [InlineData("UnderOrEqual")]
    [InlineData("NotUnder")]
    public void HierarchyFunction_TranslatesWithGuidValue(string function)
    {
        var id = Guid.Parse("12345678-1234-1234-1234-123456789012");

        Assert.Equal($"Microsoft.Dynamics.CRM.{function}(PropertyName='ownerid',PropertyValue='{id}')", TranslateFunction(function, id));
    }

    [Theory]
    [InlineData("LastXDays")]
    [InlineData("NextXDays")]
    [InlineData("OlderThanXDays")]
    [InlineData("OlderThanXMonths")]
    [InlineData("OlderThanXYears")]
    public void RelativeDateFunction_TranslatesWithCount(string function)
    {
        Assert.Equal($"Microsoft.Dynamics.CRM.{function}(PropertyName='ownerid',PropertyValue='7')", TranslateFunction(function, 7));
    }

    [Theory]
    [InlineData("In")]
    [InlineData("NotIn")]
    [InlineData("ContainValues")]
    [InlineData("DoesNotContainValues")]
    public void MultiValueFunction_TranslatesWithPropertyValues(string function)
    {
        Assert.Equal($"Microsoft.Dynamics.CRM.{function}(PropertyName='ownerid',PropertyValues=[\"a\",\"2\"])", TranslateFunction(function, "a", 2));
    }

    [Fact]
    public void In_WithEnumValues_SendsTheChoiceIntegers()
    {
        var result = new ODataExpressionVisitor().ToODataFilter((TestEntity x) =>
            DataverseFunctions.In(x.Status, AccountStatus.Active, AccountStatus.Suspended));

        Assert.Equal("Microsoft.Dynamics.CRM.In(PropertyName='statecode',PropertyValues=[\"0\",\"2\"])", result);
    }

    [Fact]
    public void In_WithAnObjectArrayVariable_SendsItsElements()
    {
        object[] names = ["Alpha", "Beta"];

        var result = new ODataExpressionVisitor().ToODataFilter((TestEntity x) => DataverseFunctions.In(x.Name, names));

        Assert.Equal("Microsoft.Dynamics.CRM.In(PropertyName='name',PropertyValues=[\"Alpha\",\"Beta\"])", result);
    }

    [Fact]
    public void In_WithAValueTypeArrayVariable_SendsItsElements()
    {
        // An int[] is not an object[], so the compiler wraps it: In(x.Age, new object[] { ages }).
        int[] ages = [30, 40];

        var result = new ODataExpressionVisitor().ToODataFilter((TestEntity x) => DataverseFunctions.In(x.Age, ages));

        Assert.Equal("Microsoft.Dynamics.CRM.In(PropertyName='age',PropertyValues=[\"30\",\"40\"])", result);
    }

    [Fact]
    public void In_WithAnApostropheInAValue_SendsADoubleQuotedJsonArray()
    {
        var result = new ODataExpressionVisitor().ToODataFilter((TestEntity x) => DataverseFunctions.In(x.Name, "O'Neil"));

        Assert.Equal("Microsoft.Dynamics.CRM.In(PropertyName='name',PropertyValues=[\"O'Neil\"])", result);
    }

    // ────────── Inline array literals ──────────

    [Fact]
    public void Filter_InlineStringArrayContains_GeneratesInFunction()
    {
        var result = new ODataExpressionVisitor().ToODataFilter((TestEntity x) => new[] { "Alpha", "O'Neil" }.Contains(x.Name));

        Assert.Equal("Microsoft.Dynamics.CRM.In(PropertyName='name',PropertyValues=[\"Alpha\",\"O'Neil\"])", result);
    }

    [Fact]
    public void Filter_InlineIntArrayWithCapturedElement_GeneratesInFunction()
    {
        var minimum = 18;

        var result = new ODataExpressionVisitor().ToODataFilter((TestEntity x) => new[] { minimum, 65 }.Contains(x.Age));

        Assert.Equal("Microsoft.Dynamics.CRM.In(PropertyName='age',PropertyValues=[\"18\",\"65\"])", result);
    }

    [Fact]
    public void Filter_InlineEmptyArrayContains_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ODataExpressionVisitor().ToODataFilter((TestEntity x) => Array.Empty<string>().Contains(x.Name)));
    }

    // Dataverse rejects a value on the left: "The right side of the 'Equal' operator must be a constant value".
    [Fact]
    public void Filter_ValueOnTheLeft_IsTurnedAround()
    {
        var visitor = new ODataExpressionVisitor();

        Assert.Equal("age eq 42", visitor.ToODataFilter((TestEntity x) => 42 == x.Age));
        Assert.Equal("age lt 10", new ODataExpressionVisitor().ToODataFilter((TestEntity x) => 10 > x.Age));
        Assert.Equal("age ge 10", new ODataExpressionVisitor().ToODataFilter((TestEntity x) => 10 <= x.Age));
        Assert.Equal("description eq null", new ODataExpressionVisitor().ToODataFilter((TestEntity x) => null == x.Description));
        Assert.Equal("age gt 1 and age lt 9", new ODataExpressionVisitor().ToODataFilter((TestEntity x) => 1 < x.Age && 9 > x.Age));
    }

    [Fact]
    public void Filter_ContainsOnANullableValue_UnwrapsTheColumn()
    {
        var counts = new List<int> { 1, 2 };

        var result = new ODataExpressionVisitor().ToODataFilter((TestEntity x) => counts.Contains(x.NumberOfEmployees!.Value));

        Assert.Equal("Microsoft.Dynamics.CRM.In(PropertyName='numberofemployees',PropertyValues=[\"1\",\"2\"])", result);
    }
}
