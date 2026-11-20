using Dataverse.Fluent.Builders;
using Xunit;

namespace Dataverse.Fluent.Tests.Builders
{
    public class FetchXmlFilterVisitorTests
    {
        [Fact]
        public void ToFetchXmlFilter_WithEqualityCondition_GeneratesCorrectXml()
        {
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => a.StateCode == 0);

            Assert.Contains("attribute=\"statecode\"", filter);
            Assert.Contains("operator=\"eq\"", filter);
            Assert.Contains("value=\"0\"", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_WithGreaterThanCondition_GeneratesCorrectXml()
        {
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => a.Revenue > 100000);

            Assert.Contains("attribute=\"revenue\"", filter);
            Assert.Contains("operator=\"gt\"", filter);
            Assert.Contains("value=\"100000\"", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_WithLessThanOrEqualCondition_GeneratesCorrectXml()
        {
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => a.NumberOfEmployees <= 500);

            Assert.Contains("attribute=\"numberofemployees\"", filter);
            Assert.Contains("operator=\"le\"", filter);
            Assert.Contains("value=\"500\"", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_WithStringContains_GeneratesLikeCondition()
        {
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => a.Name.Contains("Test"));

            Assert.Contains("attribute=\"name\"", filter);
            Assert.Contains("operator=\"like\"", filter);
            Assert.Contains("%Test%", filter);
        }

        [Theory]
        [InlineData("a_b", "%a[_]b%")]
        [InlineData("50%", "%50[%]%")]
        [InlineData("[x]", "%[[]x]%")]
        public void ToFetchXmlFilter_WithStringContains_EscapesLikeWildcards(string text, string expected)
        {
            // like treats %, _ and [ as wildcards; the text must match literally, as string.Contains does.
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => a.Name.Contains(text));

            Assert.Contains($"value=\"{expected}\"", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_WithStartsWith_EscapesWildcardsAndXml()
        {
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => a.Name.StartsWith("R&D_"));

            Assert.Contains("value=\"R&amp;D[_]%\"", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_WithStringStartsWith_GeneratesLikeCondition()
        {
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => a.Name.StartsWith("Contoso"));

            Assert.Contains("attribute=\"name\"", filter);
            Assert.Contains("operator=\"like\"", filter);
            Assert.Contains("Contoso%", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_WithStringEndsWith_GeneratesLikeCondition()
        {
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => a.Name.EndsWith("Corp"));

            Assert.Contains("attribute=\"name\"", filter);
            Assert.Contains("operator=\"like\"", filter);
            Assert.Contains("%Corp", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_WithAndCondition_GeneratesCorrectXml()
        {
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => a.Revenue > 100000 && a.StateCode == 0);

            Assert.Contains("<filter type=\"and\">", filter);
            Assert.Contains("attribute=\"revenue\"", filter);
            Assert.Contains("attribute=\"statecode\"", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_WithOrCondition_GeneratesCorrectXml()
        {
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => a.StateCode == 0 || a.StateCode == 1);

            Assert.Contains("<filter type=\"or\">", filter);
            Assert.Contains("attribute=\"statecode\"", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_WithNullComparison_GeneratesNullOperator()
        {
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => a.AccountNumber == null);

            Assert.Contains("attribute=\"accountnumber\"", filter);
            Assert.Contains("operator=\"null\"", filter);
            Assert.DoesNotContain("value=", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_WithNotNullComparison_GeneratesNotNullOperator()
        {
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => a.AccountNumber != null);

            Assert.Contains("attribute=\"accountnumber\"", filter);
            Assert.Contains("operator=\"not-null\"", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_WithGuidComparison_GeneratesCorrectValue()
        {
            var testGuid = Guid.NewGuid();
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => a.AccountId == testGuid);

            Assert.Contains("attribute=\"accountid\"", filter);
            Assert.Contains("operator=\"eq\"", filter);
            Assert.Contains(testGuid.ToString(), filter);
        }

        [Fact]
        public void ToFetchXmlFilter_WithBooleanComparison_GeneratesCorrectValue()
        {
            Assert.Equal("<condition attribute=\"donotemail\" operator=\"eq\" value=\"1\" />",
                new FetchXmlFilterVisitor().ToFetchXmlFilter((FlagAccount a) => a.DoNotEmail == true));
            Assert.Equal("<condition attribute=\"donotemail\" operator=\"eq\" value=\"0\" />",
                new FetchXmlFilterVisitor().ToFetchXmlFilter((FlagAccount a) => a.DoNotEmail == false));
        }

        // Dataverse ignores a filter holding only an attribute name (it returned every row, checked 2026-11-20), so a
        // boolean used as a condition is always compared with true.
        [Fact]
        public void ToFetchXmlFilter_BareBoolean_IsAnEqualsTrueCondition()
        {
            Assert.Equal("<condition attribute=\"donotemail\" operator=\"eq\" value=\"1\" />",
                new FetchXmlFilterVisitor().ToFetchXmlFilter((FlagAccount a) => a.DoNotEmail));
            Assert.Equal("<condition attribute=\"donotemail\" operator=\"ne\" value=\"1\" />",
                new FetchXmlFilterVisitor().ToFetchXmlFilter((FlagAccount a) => !a.DoNotEmail));
            Assert.Equal("<filter type=\"and\"><condition attribute=\"numberofemployees\" operator=\"gt\" value=\"1\" /><condition attribute=\"creditonhold\" operator=\"eq\" value=\"1\" /></filter>",
                new FetchXmlFilterVisitor().ToFetchXmlFilter((FlagAccount a) => a.Employees > 1 && a.CreditOnHold!.Value));
        }

        private sealed class FlagAccount
        {
            [System.Text.Json.Serialization.JsonPropertyName("donotemail")]
            public bool DoNotEmail { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("creditonhold")]
            public bool? CreditOnHold { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("numberofemployees")]
            public int Employees { get; set; }
        }

        [Fact]
        public void ToFetchXmlFilter_EscapesXmlCharacters()
        {
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => a.Name == "<Test & \"Company\">");

            Assert.Contains("&lt;Test &amp; &quot;Company&quot;&gt;", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_WithNotOnAndCondition_SwapsToOr()
        {
            // De Morgan: NOT(revenue > 100000 AND stateCode == 0)
            //          → revenue <= 100000 OR stateCode != 0
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter(
                (TestAccount a) => !(a.Revenue > 100000 && a.StateCode == 0));

            Assert.Contains("<filter type=\"or\">", filter);
            Assert.DoesNotContain("<filter type=\"and\">", filter);
            Assert.Contains("attribute=\"revenue\" operator=\"le\"", filter);
            Assert.Contains("attribute=\"statecode\" operator=\"ne\"", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_WithNotOnOrCondition_SwapsToAnd()
        {
            // De Morgan: NOT(stateCode == 0 OR stateCode == 1)
            //          → stateCode != 0 AND stateCode != 1
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter(
                (TestAccount a) => !(a.StateCode == 0 || a.StateCode == 1));

            Assert.Contains("<filter type=\"and\">", filter);
            Assert.DoesNotContain("<filter type=\"or\">", filter);
            Assert.Contains("operator=\"ne\"", filter);
            Assert.DoesNotContain("operator=\"eq\"", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_WithNotOnSimpleCondition_NegatesOperator()
        {
            // NOT(stateCode == 0) → stateCode != 0
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter(
                (TestAccount a) => !(a.StateCode == 0));

            Assert.Contains("attribute=\"statecode\"", filter);
            Assert.Contains("operator=\"ne\"", filter);
            Assert.Contains("value=\"0\"", filter);
        }

        // ────────── Collection Contains → in operator ──────────

        [Fact]
        public void ToFetchXmlFilter_ArrayContains_GeneratesInCondition()
        {
            var statuses = new[] { 0, 1 };
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => statuses.Contains(a.StateCode));

            Assert.Contains("attribute=\"statecode\"", filter);
            Assert.Contains("operator=\"in\"", filter);
            Assert.Contains("<value>0</value>", filter);
            Assert.Contains("<value>1</value>", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_ListContains_GeneratesInCondition()
        {
            var names = new List<string> { "Alpha", "Beta", "Gamma" };
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => names.Contains(a.Name));

            Assert.Contains("attribute=\"name\"", filter);
            Assert.Contains("operator=\"in\"", filter);
            Assert.Contains("<value>Alpha</value>", filter);
            Assert.Contains("<value>Beta</value>", filter);
            Assert.Contains("<value>Gamma</value>", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_EnumerableContainsExtension_GeneratesInCondition()
        {
            var ids = new[] { Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222") };
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => ids.Contains(a.AccountId));

            Assert.Contains("attribute=\"accountid\"", filter);
            Assert.Contains("operator=\"in\"", filter);
            Assert.Contains("<value>11111111-1111-1111-1111-111111111111</value>", filter);
            Assert.Contains("<value>22222222-2222-2222-2222-222222222222</value>", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_ContainsWithXmlCharacters_EscapesValues()
        {
            var names = new List<string> { "A&B", "<Corp>" };
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter((TestAccount a) => names.Contains(a.Name));

            Assert.Contains("<value>A&amp;B</value>", filter);
            Assert.Contains("<value>&lt;Corp&gt;</value>", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_ContainsCombinedWithOtherConditions_GeneratesCorrectXml()
        {
            var statuses = new[] { 0, 1 };
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter(
                (TestAccount a) => statuses.Contains(a.StateCode) && a.Revenue > 1000);

            Assert.Contains("<filter type=\"and\">", filter);
            Assert.Contains("operator=\"in\"", filter);
            Assert.Contains("attribute=\"revenue\" operator=\"gt\"", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_NotContains_GeneratesNotInCondition()
        {
            var statuses = new[] { 0, 1 };
            var visitor = new FetchXmlFilterVisitor();
            var filter = visitor.ToFetchXmlFilter(
                (TestAccount a) => !statuses.Contains(a.StateCode));

            Assert.Contains("attribute=\"statecode\"", filter);
            Assert.Contains("operator=\"not-in\"", filter);
            Assert.Contains("<value>0</value>", filter);
            Assert.Contains("<value>1</value>", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_ContainsWithEmptyCollection_ThrowsArgumentException()
        {
            var statuses = Array.Empty<int>();
            var visitor = new FetchXmlFilterVisitor();

            Assert.Throws<ArgumentException>(() =>
                visitor.ToFetchXmlFilter((TestAccount a) => statuses.Contains(a.StateCode)));
        }

        // ────────── Inline array literals ──────────

        [Fact]
        public void ToFetchXmlFilter_InlineArrayContains_GeneratesInCondition()
        {
            var filter = new FetchXmlFilterVisitor().ToFetchXmlFilter((TestAccount a) => new[] { "Alpha", "A&B" }.Contains(a.Name));

            Assert.Equal("<condition attribute=\"name\" operator=\"in\"><value>Alpha</value><value>A&amp;B</value></condition>", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_InlineArrayWithCapturedElement_GeneratesInCondition()
        {
            var active = 0;

            var filter = new FetchXmlFilterVisitor().ToFetchXmlFilter((TestAccount a) => new[] { active, 2 }.Contains(a.StateCode));

            Assert.Equal("<condition attribute=\"statecode\" operator=\"in\"><value>0</value><value>2</value></condition>", filter);
        }

        // ────────── Culture independence ──────────

        [Theory]
        [InlineData("de-DE")] // decimal comma
        [InlineData("th-TH")] // Buddhist calendar
        public void ToFetchXmlFilter_NumbersAndDates_UseTheInvariantCulture(string culture)
        {
            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
                var visitor = new FetchXmlFilterVisitor();
                var since = new DateTime(2026, 10, 26, 14, 30, 0, DateTimeKind.Utc);

                Assert.Contains("value=\"1500.5\"", visitor.ToFetchXmlFilter((TestAccount a) => a.Revenue > 1500.5m));
                Assert.Contains("value=\"2026-10-26T14:30:00Z\"", new FetchXmlFilterVisitor().ToFetchXmlFilter((TestAccount a) => a.CreatedOn > since));
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void ToFetchXmlFilter_DataverseFunction_IsNotSupported()
        {
            Assert.Throws<NotSupportedException>(() =>
                new FetchXmlFilterVisitor().ToFetchXmlFilter((TestAccount a) => DataverseFunctions.EqualUserId(a.AccountId)));
        }

        // A value on the left was sent as the attribute name (attribute="42"), which Dataverse rejects.
        [Theory]
        [InlineData("eq")]
        [InlineData("lt")]
        [InlineData("ge")]
        public void ToFetchXmlFilter_ValueOnTheLeft_PutsTheAttributeFirst(string expected)
        {
            var visitor = new FetchXmlFilterVisitor();
            var filter = expected switch
            {
                // 42 == x is x == 42; 10 > x is x < 10; 10 <= x is x >= 10
                "eq" => visitor.ToFetchXmlFilter((TestAccount a) => 42 == a.NumberOfEmployees),
                "lt" => visitor.ToFetchXmlFilter((TestAccount a) => 10 > a.NumberOfEmployees),
                _ => visitor.ToFetchXmlFilter((TestAccount a) => 10 <= a.NumberOfEmployees),
            };

            var value = expected == "eq" ? "42" : "10";
            Assert.Equal($"<condition attribute=\"numberofemployees\" operator=\"{expected}\" value=\"{value}\" />", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_NullOnTheLeft_IsANullCondition()
        {
            var filter = new FetchXmlFilterVisitor().ToFetchXmlFilter((TestAccount a) => null == a.AccountNumber);

            Assert.Equal("<condition attribute=\"accountnumber\" operator=\"null\" />", filter);
        }

        [Fact]
        public void ToFetchXmlFilter_ContainsOnANullableValue_IsAnInCondition()
        {
            var ids = new List<int> { 1, 2 };

            var filter = new FetchXmlFilterVisitor().ToFetchXmlFilter((NullableAccount a) => ids.Contains(a.Employees!.Value));

            Assert.Equal("<condition attribute=\"numberofemployees\" operator=\"in\"><value>1</value><value>2</value></condition>", filter);
        }

        private class NullableAccount
        {
            [System.Text.Json.Serialization.JsonPropertyName("numberofemployees")]
            public int? Employees { get; set; }
        }
    }
}
