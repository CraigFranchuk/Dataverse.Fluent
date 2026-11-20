using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Tool.Tests.Fixtures;

// One property of every shape. This file is compiled into the tests, where the library classifies the type, and is
// embedded as text, where the scaffolder classifies its source: ParityTests holds the two to the same answer. Add a
// property here when either side learns a new shape.
#pragma warning disable CA1822, CA1051, IDE0032, IDE0044, CS0649, CS0169, CA1823

[DataverseEntity("incident", "incidents")]
public class ParityModel
{
    private const JsonIgnoreCondition Always = JsonIgnoreCondition.Always;
    private const JsonIgnoreCondition WhenNull = JsonIgnoreCondition.WhenWritingNull;
    private const string TitleColumn = "title";

    private string? _backed;

    // ───── Columns ─────

    public string? Unmapped { get; set; }

    [JsonPropertyName("title")]
    public string? MappedByJson { get; set; }

    [DataverseColumn("description")]
    public string? MappedByColumn { get; set; }

    [DataverseColumn(TitleColumn)]
    public string? MappedByConstant { get; set; }

    [JsonPropertyName("_subjectid_value")]
    public Guid? LookupValue { get; set; }

    [JsonPropertyName("createdon@OData.Community.Display.V1.FormattedValue")]
    public string? Annotation { get; set; }

    public string? Backed
    {
        get => _backed;
        set => _backed = value;
    }

    public string? GetOnly { get; }

    public string? InitOnly { get; init; }

    public virtual string? Virtual { get; set; }

    public string? PrivateSetter { get; private set; }

    public string Computed => "computed";

    public string ComputedWithBody
    {
        get { return "computed"; }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IgnoredWhenNull { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? NeverIgnored { get; set; }

    [JsonIgnore(Condition = WhenNull)]
    public string? IgnoredWhenNullByConstant { get; set; }

    // ───── Ignored ─────

    [JsonIgnore]
    public string? Ignored { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public string? IgnoredAlways { get; set; }

    [JsonIgnore(Condition = Always)]
    public string? IgnoredByConstant { get; set; }

    [JsonIgnore]
    public string IgnoredComputed => "computed";

    [JsonIgnore]
    [DataverseColumn("title")]
    public string? IgnoredAndMapped { get; set; }

    // ───── Lookups ─────

    [DataverseLookup("primarycontactid", "contacts")]
    public Guid? Lookup { get; set; }

    [DataverseLookup("customerid")]
    [DataverseLookupTarget("account", "accounts", "customerid_account")]
    [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
    public DataverseReference? PolymorphicLookup { get; set; }

    // ───── Related records ─────

    public ParityRelated? Related { get; set; }

    public List<ParityRelated>? RelatedList { get; set; }

    public ParityRelated[]? RelatedArray { get; set; }

    [JsonPropertyName("Incident_Tasks")]
    public IReadOnlyCollection<ParityRelated>? RelatedMapped { get; set; }

    // ───── Not part of the model ─────

    public static string? Static { get; set; }

    internal string? Internal { get; set; }

    protected string? Protected { get; set; }

    private string? Private { get; set; }

    public string? SetOnly
    {
        set => _backed = value;
    }
}

[EntitySet("contacts")]
public class ParityRelated
{
    public string? Name { get; set; }
}
