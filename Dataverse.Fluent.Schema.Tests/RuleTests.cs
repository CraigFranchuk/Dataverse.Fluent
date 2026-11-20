using System.Text.Json.Serialization;
using Dataverse.Fluent.Schema.Metadata;
using Dataverse.Fluent.Schema.Validation;

namespace Dataverse.Fluent.Schema.Tests;

/// <summary>The type rules, name suggestions, metadata parsing, report and model discovery.</summary>
public class RuleTests
{
    public enum IntEnum { A = 1 }

    public enum ByteEnum : byte { A = 1 }

    private static ColumnSchema Column(string type, string? behavior = null, string? typeName = null)
        => new("c", type, typeName ?? type + "Type", null, true, true, true) { DateTimeBehavior = behavior };

    public static TheoryData<string, string?, Type> Fits => new()
    {
        { "String", null, typeof(string) },
        { "Memo", null, typeof(string) },
        { "EntityName", null, typeof(string) },
        { "Boolean", null, typeof(bool?) },
        { "Uniqueidentifier", null, typeof(Guid) },
        { "Integer", null, typeof(int?) },
        { "Integer", null, typeof(long) },
        { "Picklist", null, typeof(int?) },
        { "Picklist", null, typeof(IntEnum?) },
        { "State", null, typeof(int) },
        { "Status", null, typeof(IntEnum) },
        { "BigInt", null, typeof(long?) },
        { "Decimal", null, typeof(decimal?) },
        { "Money", null, typeof(decimal) },
        { "Double", null, typeof(double?) },
        { "Double", null, typeof(decimal?) },
        { "DateTime", "DateOnly", typeof(DateOnly?) },
        { "DateTime", "UserLocal", typeof(DateTime?) },
        { "DateTime", "TimeZoneIndependent", typeof(DateTimeOffset) },
        { "Lookup", null, typeof(Guid?) },
        { "Customer", null, typeof(DataverseReference) },
        { "Owner", null, typeof(Guid) },
        { "SomethingNew", null, typeof(object) }
    };

    [Theory]
    [MemberData(nameof(Fits))]
    public void TypesThatFit_HaveNoProblem(string attributeType, string? behavior, Type type)
        => Assert.Null(ClrTypeRules.Check(Column(attributeType, behavior), type));

    public static TheoryData<string, string?, Type, string, string> Problems => new()
    {
        { "String", null, typeof(int?), SchemaCodes.IncompatibleType, "Use string." },
        { "Boolean", null, typeof(string), SchemaCodes.IncompatibleType, "Use bool?." },
        { "Uniqueidentifier", null, typeof(string), SchemaCodes.LossyType, "Use Guid?." },
        { "Integer", null, typeof(short?), SchemaCodes.LossyType, "Use int?." },
        { "Picklist", null, typeof(string), SchemaCodes.IncompatibleType, "Use int? or an enum." },
        { "Picklist", null, typeof(ByteEnum), SchemaCodes.LossyType, "Use an enum based on int." },
        { "BigInt", null, typeof(int?), SchemaCodes.LossyType, "Use long?." },
        { "Money", null, typeof(int), SchemaCodes.IncompatibleType, "Use decimal?." },
        { "Money", null, typeof(double?), SchemaCodes.LossyType, "Use decimal?." },
        { "Double", null, typeof(float), SchemaCodes.LossyType, "Use double?." },
        { "DateTime", "DateOnly", typeof(DateTime?), SchemaCodes.IncompatibleType, "Use DateOnly?." },
        { "DateTime", "DateOnly", typeof(DateTimeOffset), SchemaCodes.IncompatibleType, "Use DateOnly?." },
        { "DateTime", "UserLocal", typeof(DateOnly?), SchemaCodes.IncompatibleType, "Use DateTime?." },
        { "Lookup", null, typeof(string), SchemaCodes.IncompatibleType, "Use Guid?." }
    };

    [Theory]
    [MemberData(nameof(Problems))]
    public void TypesThatDoNotFit_AreReported(string attributeType, string? behavior, Type type, string code, string suggestion)
    {
        var problem = ClrTypeRules.Check(Column(attributeType, behavior), type);

        Assert.NotNull(problem);
        Assert.Equal((code, suggestion), (problem.Code, problem.Suggestion));
        Assert.Equal(code == SchemaCodes.IncompatibleType ? SchemaSeverity.Error : SchemaSeverity.Warning, problem.Severity);
    }

    [Fact]
    public void MultiSelectChoices_AreText()
    {
        var column = Column("Virtual", typeName: "MultiSelectPicklistType");

        Assert.Null(ClrTypeRules.Check(column, typeof(string)));
        Assert.Equal(SchemaCodes.IncompatibleType, ClrTypeRules.Check(column, typeof(List<int>))!.Code);
    }

    [Theory]
    [InlineData(typeof(int?), "int?")]
    [InlineData(typeof(DateTime), "DateTime")]
    [InlineData(typeof(List<string>), "List<string>")]
    [InlineData(typeof(byte[]), "byte[]")]
    [InlineData(typeof(IntEnum?), "IntEnum?")]
    public void Types_AreNamedAsCSharpWritesThem(Type type, string expected)
        => Assert.Equal(expected, ClrTypeRules.Describe(type));

    [Theory]
    [InlineData("closedatedon", "closedon")]
    [InlineData("satisfactioncode", "customersatisfactioncode")]
    [InlineData("Name", "name")]
    [InlineData("titel", "title")]
    public void Suggestions_FindTheIntendedName(string name, string expected)
        => Assert.Equal(expected, NameSuggestions.Closest(name, ["closedon", "customersatisfactioncode", "name", "title", "description", "statecode"]).First());

    [Fact]
    public void Suggestions_AreLeftOut_WhenNothingIsClose()
        => Assert.Null(NameSuggestions.DidYouMean("revenue", ["title", "description"]));

    [Fact]
    public void Suggestions_ListUpToThree()
        => Assert.Equal("Did you mean 'name1', 'name2' or 'name3'?", NameSuggestions.DidYouMean("name", ["name1", "name2", "name3", "name4"]));

    [Fact]
    public void Metadata_IsParsedFromTheEntityDefinitionsShape()
    {
        var table = new FakeTable("incident", "incidents")
            .Column("title", "String")
            .DateTime("duedate", "DateOnly")
            .Lookup("customerid", "Customer", ("account", "customerid_account"), ("contact", "customerid_contact"))
            .Owner()
            .ManyToMany("self_association", "incident");

        var schema = SchemaReader.Parse(table.Definition(), table.Lookups(), table.Dates());

        Assert.Equal(("incident", "incidents"), (schema.LogicalName, schema.EntitySetName));
        Assert.Equal("DateOnly", schema.Column("duedate")!.DateTimeBehavior);
        Assert.Equal(["account", "contact"], schema.Column("customerid")!.Targets);
        Assert.True(schema.IsCompanion(schema.Column("customeridname")!));
        Assert.False(schema.IsCompanion(schema.Column("title")!));
        Assert.Equal("customerid_contact", schema.NavigationPropertyFor("customerid", "contact"));
        Assert.Equal("ownerid", schema.NavigationPropertyFor("ownerid", "team"));
        // A relationship of a table with itself is read from both sides.
        Assert.Equal(2, schema.ManyToMany.Count);
    }

    [Fact]
    public void Report_ThrowIfAny_FailsAtTheGivenSeverity()
    {
        var warning = new SchemaFinding(SchemaCodes.SentOnUpdate, SchemaSeverity.Warning, typeof(RuleTests), null, "m", fix: SchemaFix.SetReadOnly(DataverseOperation.Update));
        var report = new SchemaReport([typeof(RuleTests)], [warning]);

        report.ThrowIfAny();
        var ex = Assert.Throws<SchemaValidationException>(() => report.ThrowIfAny(SchemaSeverity.Warning));
        Assert.Same(warning, Assert.Single(ex.Findings));
        Assert.Contains("WARNING RuleTests: DVS040 m Fix: mark it [DataverseReadOnly(DataverseOperation.Update)].", ex.Message);
        Assert.EndsWith("1 model checked: 0 errors, 1 warning, 0 info.", report.ToString());
    }

    [DataverseEntity("account", "accounts")]
    public class Model
    {
        public string? Name { get; set; }
    }

    [DataverseEntity("account", "accounts")]
    public abstract class AbstractModel
    {
        public string? Name { get; set; }
    }

    public class InvalidModel
    {
        [DataverseLookup("x")]
        public Guid? X { get; set; }
    }

    public class ActionRequest
    {
        [JsonPropertyName("Target")]
        public string? Target { get; set; }
    }

    [Fact]
    public void FindModels_ReturnsMappedAndInvalidClasses()
    {
        var models = DataverseSchemaValidator.FindModels(typeof(RuleTests).Assembly);

        Assert.Contains(typeof(Model), models);
        Assert.Contains(typeof(InvalidModel), models);
        Assert.Contains(typeof(ValidatorTests.Case), models);
        Assert.DoesNotContain(typeof(AbstractModel), models);
        Assert.DoesNotContain(typeof(ActionRequest), models);
        Assert.DoesNotContain(typeof(RuleTests), models);
    }

    [Fact]
    public void FindModels_SkipsCompilerGeneratedTypes()
        => Assert.All(DataverseSchemaValidator.FindModels(typeof(RuleTests).Assembly), t => Assert.DoesNotContain("<", t.Name, StringComparison.Ordinal));

    [Fact]
    public void Options_HaveSafeDefaults()
    {
        var options = new SchemaValidationOptions();

        Assert.True(options.CheckSelect);
        Assert.True(options.CheckChoiceEnums);
        Assert.Empty(options.IgnoreCodes);
        Assert.Equal(4, options.MaxDegreeOfParallelism);
    }

    [Fact]
    public void Fixes_DescribeTheChange()
    {
        Assert.Equal("set the entity set to \"accounts\"", SchemaFix.SetEntitySet("accounts").ToString());
        Assert.Equal("add [DataverseLookupTarget(\"contact\", \"contacts\", \"customerid_contact\")]",
            SchemaFix.AddLookupTarget("contact", "contacts", "customerid_contact").Description);
        Assert.Equal("map it with [DataverseLookup(\"primarycontactid\", \"contacts\")]",
            SchemaFix.MapAsLookup("primarycontactid", "contacts", "primarycontactid").Description);
    }
}
