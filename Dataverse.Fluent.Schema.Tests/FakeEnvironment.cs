using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dataverse.Fluent.Models;
using Dataverse.Fluent.Testing;

namespace Dataverse.Fluent.Schema.Tests;

/// <summary>
/// A fake environment that answers the validator's metadata requests in the shape <c>EntityDefinitions</c> returns
/// them (recorded from a live environment on 2026-11-20), and its <c>$select</c> check: 404 for a random ID, or 400
/// naming the first column the table rejects.
/// </summary>
internal sealed partial class FakeEnvironment
{
    private readonly Dictionary<string, FakeTable> _tables = new(StringComparer.OrdinalIgnoreCase);

    public FakeEnvironment()
    {
        Transport.On(r => r.Method == HttpMethod.Get && r.Path.StartsWith("EntityDefinitions", StringComparison.Ordinal)).Respond(Metadata);
        Transport.On(r => r.Method == HttpMethod.Get && !r.Path.StartsWith("EntityDefinitions", StringComparison.Ordinal)).Respond(Retrieve);
    }

    public FakeDataverseTransport Transport { get; } = new();

    /// <summary>Responds to the record retrieves of the $select check; defaults to "not found".</summary>
    public Func<FakeRequest, DataverseResponse>? RetrieveOverride { get; set; }

    public FakeTable this[string logicalName] => _tables[logicalName];

    public FakeTable Table(string logicalName, string entitySet)
    {
        var table = new FakeTable(logicalName, entitySet);
        _tables[logicalName] = table;
        return table;
    }

    public Task<SchemaReport> ValidateAsync(params Type[] models) => ValidateAsync(new SchemaValidationOptions(), models);

    public Task<SchemaReport> ValidateAsync(SchemaValidationOptions options, params Type[] models)
        => DataverseSchemaValidator.ValidateAsync(Transport, models, options, TestContext.Current.CancellationToken);

    /// <summary>A standard environment: the tables most tests point lookups at.</summary>
    public static FakeEnvironment WithStandardTables()
    {
        var environment = new FakeEnvironment();
        foreach (var (logical, set) in new[]
        {
            ("account", "accounts"), ("contact", "contacts"), ("systemuser", "systemusers"), ("team", "teams"),
            ("task", "tasks"), ("knowledgearticle", "knowledgearticles"), ("new_region", "new_regions")
        })
        {
            environment.Table(logical, set).Column($"{logical}id", "Uniqueidentifier", update: false).Column("name", "String");
        }

        return environment;
    }

    private DataverseResponse Metadata(FakeRequest request)
    {
        var path = request.Path;
        var match = TablePath().Match(path);
        if (!match.Success)
            return Respond(Names(request.GetQueryOption("$filter")));

        if (!_tables.TryGetValue(match.Groups[1].Value, out var table))
            throw Error(HttpStatusCode.NotFound, $"EntityMetadata With Id = LogicalName='{match.Groups[1].Value}' does not exist.");

        return match.Groups[2].Value switch
        {
            "" => Respond(table.Definition()),
            "/Attributes/Microsoft.Dynamics.CRM.LookupAttributeMetadata" => Respond(table.Lookups()),
            "/Attributes/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata" => Respond(table.Dates()),
            var cast when cast.EndsWith("AttributeMetadata", StringComparison.Ordinal) => Respond(table.Options(ColumnInFilter(request.GetQueryOption("$filter")))),
            _ => throw new InvalidOperationException($"Unexpected metadata request: {request.Url}")
        };
    }

    private JsonObject Names(string? filter)
    {
        var tables = _tables.Values.AsEnumerable();
        if (filter is not null)
        {
            var logicalNames = FilterValues("LogicalName", filter);
            var entitySets = FilterValues("EntitySetName", filter);
            tables = tables.Where(t => logicalNames.Contains(t.LogicalName) || entitySets.Contains(t.EntitySet));
        }

        return new JsonObject
        {
            ["value"] = new JsonArray([.. tables.Select(t => (JsonNode)new JsonObject { ["LogicalName"] = t.LogicalName, ["EntitySetName"] = t.EntitySet })])
        };
    }

    private DataverseResponse Retrieve(FakeRequest request)
    {
        if (RetrieveOverride is { } custom)
            return custom(request);

        var table = _tables.Values.Single(t => request.Path.StartsWith(t.EntitySet + "(", StringComparison.Ordinal));
        var selected = request.GetQueryOption("$select")?.Split(',') ?? [];
        var rejected = selected.FirstOrDefault(table.RejectedInSelect.Contains);
        if (rejected is not null)
            throw Error(HttpStatusCode.BadRequest, $"Could not find a property named '{rejected}' on type 'Microsoft.Dynamics.CRM.{table.LogicalName}'.");

        throw Error(HttpStatusCode.NotFound, $"Entity '{table.LogicalName}' With Id = {Guid.NewGuid()} Does Not Exist");
    }

    public static DataverseException Error(HttpStatusCode status, string message, string code = "0x80060888")
        => DataverseException.FromErrorContent(new JsonObject { ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString(), status);

    private static DataverseResponse Respond(JsonObject body) => DataverseResponse.FromJson(body);

    private static HashSet<string> FilterValues(string property, string filter)
        => FilterValue().Matches(filter).Where(m => m.Groups[1].Value == property).Select(m => m.Groups[2].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string ColumnInFilter(string? filter)
        => FilterValue().Match(filter ?? string.Empty) is { Success: true } m ? m.Groups[2].Value : throw new InvalidOperationException("Option requests filter by column.");

    [GeneratedRegex(@"^EntityDefinitions\(LogicalName='([^']+)'\)(.*)$")]
    private static partial Regex TablePath();

    [GeneratedRegex(@"(\w+) eq '([^']*)'")]
    private static partial Regex FilterValue();
}

/// <summary>One table of a <see cref="FakeEnvironment"/>.</summary>
internal sealed class FakeTable(string logicalName, string entitySet)
{
    private readonly List<JsonObject> _attributes = [];
    private readonly Dictionary<string, string[]> _targets = [];
    private readonly Dictionary<string, string> _behaviors = [];
    private readonly Dictionary<string, int[]> _options = [];
    private readonly List<JsonObject> _manyToOne = [];
    private readonly List<JsonObject> _oneToMany = [];
    private readonly List<JsonObject> _manyToMany = [];

    public string LogicalName { get; } = logicalName;

    public string EntitySet { get; } = entitySet;

    /// <summary>Columns the $select check rejects, as Dataverse does for a column it cannot select.</summary>
    public HashSet<string> RejectedInSelect { get; } = new(StringComparer.Ordinal);

    public FakeTable Column(string name, string type, bool create = true, bool update = true, bool read = true, string? of = null, string? typeName = null)
    {
        _attributes.Add(new JsonObject
        {
            ["LogicalName"] = name,
            ["AttributeType"] = type,
            ["AttributeTypeName"] = new JsonObject { ["Value"] = typeName ?? type + "Type" },
            ["AttributeOf"] = of,
            ["IsValidForRead"] = read,
            ["IsValidForCreate"] = create,
            ["IsValidForUpdate"] = update
        });
        return this;
    }

    public FakeTable DateTime(string name, string behavior, bool create = true, bool update = true)
    {
        _behaviors[name] = behavior;
        return Column(name, "DateTime", create, update);
    }

    public FakeTable Choice(string name, string type = "Picklist", params int[] options)
    {
        _options[name] = options;
        Column(name, type, create: type != "State");
        return Column(name + "name", "Virtual", create: false, update: false, of: name, typeName: "VirtualType");
    }

    /// <summary>A lookup, its relationship to each target, and its name column.</summary>
    public FakeTable Lookup(string name, string type, params (string Target, string Navigation)[] targets)
    {
        _targets[name] = [.. targets.Select(t => t.Target)];
        foreach (var (target, navigation) in targets)
            ManyToOne(name, target, navigation);
        Column(name, type);
        return Column(name + "name", "String", create: false, update: false, of: name);
    }

    /// <summary>The owner lookup: a user or a team, through one relationship to the abstract owner table.</summary>
    public FakeTable Owner(string name = "ownerid")
    {
        _targets[name] = ["systemuser", "team"];
        ManyToOne(name, "owner", name);
        Column(name, "Owner");
        return Column(name + "name", "String", create: false, update: false, of: name);
    }

    /// <summary>Makes a column one Dataverse sets itself: valid for neither create nor update.</summary>
    public FakeTable NotWritable(string name)
    {
        var attribute = _attributes.Single(a => (string?)a["LogicalName"] == name);
        attribute["IsValidForCreate"] = false;
        attribute["IsValidForUpdate"] = false;
        return this;
    }

    public FakeTable ManyToOne(string attribute, string referenced, string navigation)
    {
        _manyToOne.Add(new JsonObject { ["ReferencingAttribute"] = attribute, ["ReferencedEntity"] = referenced, ["ReferencingEntityNavigationPropertyName"] = navigation });
        return this;
    }

    public FakeTable OneToMany(string navigation, string referencing)
    {
        _oneToMany.Add(new JsonObject { ["ReferencingEntity"] = referencing, ["ReferencedEntityNavigationPropertyName"] = navigation });
        return this;
    }

    public FakeTable ManyToMany(string navigation, string other)
    {
        _manyToMany.Add(new JsonObject
        {
            ["Entity1LogicalName"] = LogicalName,
            ["Entity2LogicalName"] = other,
            ["Entity1NavigationPropertyName"] = navigation,
            ["Entity2NavigationPropertyName"] = navigation
        });
        return this;
    }

    public JsonObject Definition() => new()
    {
        ["LogicalName"] = LogicalName,
        ["EntitySetName"] = EntitySet,
        ["Attributes"] = Copy(_attributes),
        ["ManyToOneRelationships"] = Copy(_manyToOne),
        ["OneToManyRelationships"] = Copy(_oneToMany),
        ["ManyToManyRelationships"] = Copy(_manyToMany)
    };

    public JsonObject Lookups() => Value(_targets.Select(t => new JsonObject
    {
        ["LogicalName"] = t.Key,
        ["Targets"] = new JsonArray([.. t.Value.Select(v => (JsonNode)v)])
    }));

    public JsonObject Dates() => Value(_behaviors.Select(b => new JsonObject
    {
        ["LogicalName"] = b.Key,
        ["Format"] = b.Value == "DateOnly" ? "DateOnly" : "DateAndTime",
        ["DateTimeBehavior"] = new JsonObject { ["Value"] = b.Value }
    }));

    public JsonObject Options(string column) => Value(_options.Where(o => o.Key == column).Select(o => new JsonObject
    {
        ["LogicalName"] = o.Key,
        ["OptionSet"] = new JsonObject
        {
            ["Options"] = new JsonArray([.. o.Value.Select(v => (JsonNode)new JsonObject { ["Value"] = v, ["Label"] = new JsonObject() })])
        }
    }));

    private static JsonObject Value(IEnumerable<JsonObject> items) => new() { ["value"] = new JsonArray([.. items]) };

    private static JsonArray Copy(List<JsonObject> items) => new([.. items.Select(i => i.DeepClone())]);
}
