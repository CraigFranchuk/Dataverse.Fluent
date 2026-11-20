using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.Schema.Metadata;

/// <summary>
/// Reads table metadata from <c>EntityDefinitions</c> with <c>GET</c> requests only, caching every answer for the
/// lifetime of one validation run.
/// </summary>
internal sealed class SchemaReader : IDisposable
{
    // Logical names are lowercase letters, digits and underscores, so a few dozen fit comfortably in one filter.
    private const int NamesPerFilter = 25;

    private readonly IDataverseTransport _transport;
    private readonly SemaphoreSlim _throttle;
    private readonly ConcurrentDictionary<string, Lazy<Task<TableSchema?>>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlySet<int>?>>> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string?> _entitySets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string?> _logicalNamesBySet = new(StringComparer.Ordinal);
    private Lazy<Task<IReadOnlyList<TableName>>>? _allTables;

    public SchemaReader(IDataverseTransport transport, int maxDegreeOfParallelism)
    {
        _transport = transport;
        _throttle = new SemaphoreSlim(Math.Max(1, maxDegreeOfParallelism));
    }

    public void Dispose() => _throttle.Dispose();

    /// <summary>The table with this logical name, or <see langword="null"/> when it does not exist.</summary>
    public Task<TableSchema?> GetTableAsync(string logicalName, CancellationToken ct)
        => _tables.GetOrAdd(logicalName, name => new Lazy<Task<TableSchema?>>(() => ReadTableAsync(name, ct))).Value;

    /// <summary>The logical name of the table with this entity set, or <see langword="null"/>.</summary>
    public async Task<string?> FindLogicalNameAsync(string entitySet, CancellationToken ct)
    {
        if (_logicalNamesBySet.TryGetValue(entitySet, out var cached))
            return cached;

        var response = await GetAsync($"EntityDefinitions?$select=LogicalName,EntitySetName&$filter=EntitySetName eq '{Quote(entitySet)}'", ct).ConfigureAwait(false);
        var found = Names(response).FirstOrDefault(n => string.Equals(n.EntitySetName, entitySet, StringComparison.Ordinal));
        if (found is not null)
            _entitySets[found.LogicalName] = found.EntitySetName;

        return _logicalNamesBySet[entitySet] = found?.LogicalName;
    }

    /// <summary>The entity set of each table, with <see langword="null"/> for a table that does not exist.</summary>
    public async Task<IReadOnlyDictionary<string, string?>> GetEntitySetsAsync(IEnumerable<string> logicalNames, CancellationToken ct)
    {
        var wanted = logicalNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var missing = wanted.Where(n => !_entitySets.ContainsKey(n)).ToList();

        foreach (var chunk in missing.Chunk(NamesPerFilter))
        {
            var filter = string.Join(" or ", chunk.Select(n => $"LogicalName eq '{Quote(n)}'"));
            var response = await GetAsync($"EntityDefinitions?$select=LogicalName,EntitySetName&$filter={filter}", ct).ConfigureAwait(false);
            var found = Names(response).ToDictionary(n => n.LogicalName, n => n.EntitySetName, StringComparer.OrdinalIgnoreCase);
            foreach (var name in chunk)
                _entitySets[name] = found.GetValueOrDefault(name);
        }

        return wanted.ToDictionary(n => n, n => _entitySets.GetValueOrDefault(n), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every table's logical name and entity set, for suggestions when a table is not found.</summary>
    public Task<IReadOnlyList<TableName>> GetAllTablesAsync(CancellationToken ct)
    {
        var all = _allTables ??= new Lazy<Task<IReadOnlyList<TableName>>>(async () =>
            Names(await GetAsync("EntityDefinitions?$select=LogicalName,EntitySetName", ct).ConfigureAwait(false)).ToList());
        return all.Value;
    }

    /// <summary>The option values of a choice column, or <see langword="null"/> when they cannot be read.</summary>
    public Task<IReadOnlySet<int>?> GetOptionValuesAsync(TableSchema table, ColumnSchema column, CancellationToken ct)
        => _options.GetOrAdd($"{table.LogicalName}.{column.LogicalName}",
            _ => new Lazy<Task<IReadOnlySet<int>?>>(() => ReadOptionValuesAsync(table.LogicalName, column, ct))).Value;

    /// <summary>Sends a <c>GET</c>, at most <c>MaxDegreeOfParallelism</c> at a time.</summary>
    public async Task<JsonObject> GetAsync(string url, CancellationToken ct)
    {
        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await _transport.ExecuteRequestAsync(HttpMethod.Get, url, ct: ct).ConfigureAwait(false);
        }
        finally
        {
            _throttle.Release();
        }
    }

    private async Task<TableSchema?> ReadTableAsync(string logicalName, CancellationToken ct)
    {
        var path = $"EntityDefinitions(LogicalName='{Quote(logicalName)}')";
        var definitionTask = GetAsync(path + "?$select=LogicalName,EntitySetName" +
            "&$expand=Attributes($select=LogicalName,AttributeType,AttributeTypeName,AttributeOf,IsValidForRead,IsValidForCreate,IsValidForUpdate)," +
            "ManyToOneRelationships($select=ReferencingAttribute,ReferencedEntity,ReferencingEntityNavigationPropertyName)," +
            "OneToManyRelationships($select=ReferencingEntity,ReferencedEntityNavigationPropertyName)," +
            "ManyToManyRelationships($select=Entity1LogicalName,Entity2LogicalName,Entity1NavigationPropertyName,Entity2NavigationPropertyName)", ct);

        JsonObject definition;
        try
        {
            definition = await definitionTask.ConfigureAwait(false);
        }
        catch (DataverseException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var lookupsTask = GetAsync(path + "/Attributes/Microsoft.Dynamics.CRM.LookupAttributeMetadata?$select=LogicalName,Targets", ct);
        var datesTask = GetAsync(path + "/Attributes/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata?$select=LogicalName,DateTimeBehavior", ct);
        var lookups = await lookupsTask.ConfigureAwait(false);
        var dates = await datesTask.ConfigureAwait(false);

        var table = Parse(definition, lookups, dates);
        _entitySets[table.LogicalName] = table.EntitySetName;
        _logicalNamesBySet[table.EntitySetName] = table.LogicalName;
        return table;
    }

    /// <summary>Builds a <see cref="TableSchema"/> from the three <c>EntityDefinitions</c> responses.</summary>
    internal static TableSchema Parse(JsonObject definition, JsonObject lookups, JsonObject dates)
    {
        var logicalName = (string)definition["LogicalName"]!;

        var targets = Values(lookups).ToDictionary(
            a => (string)a["LogicalName"]!,
            a => (IReadOnlyList<string>)(a["Targets"]?.AsArray().Select(t => (string)t!).ToList() ?? []),
            StringComparer.Ordinal);
        var behaviors = Values(dates).ToDictionary(
            a => (string)a["LogicalName"]!,
            a => (string?)a["DateTimeBehavior"]?["Value"],
            StringComparer.Ordinal);

        var columns = new Dictionary<string, ColumnSchema>(StringComparer.Ordinal);
        foreach (var attribute in Array(definition, "Attributes"))
        {
            var name = (string)attribute["LogicalName"]!;
            columns[name] = new ColumnSchema(
                name,
                (string?)attribute["AttributeType"] ?? "Unknown",
                (string?)attribute["AttributeTypeName"]?["Value"],
                (string?)attribute["AttributeOf"],
                (bool?)attribute["IsValidForRead"] != false,
                (bool?)attribute["IsValidForCreate"] != false,
                (bool?)attribute["IsValidForUpdate"] != false)
            {
                DateTimeBehavior = behaviors.GetValueOrDefault(name),
                Targets = targets.GetValueOrDefault(name) ?? []
            };
        }

        var manyToOne = Array(definition, "ManyToOneRelationships")
            .Select(r => new ManyToOneSchema((string)r["ReferencingAttribute"]!, (string)r["ReferencedEntity"]!, (string)r["ReferencingEntityNavigationPropertyName"]!))
            .ToList();
        var oneToMany = Array(definition, "OneToManyRelationships")
            .Select(r => new OneToManySchema((string)r["ReferencingEntity"]!, (string)r["ReferencedEntityNavigationPropertyName"]!))
            .ToList();

        // A many-to-many relationship is read from each side through that side's navigation property; a relationship
        // of a table with itself has both.
        var manyToMany = new List<ManyToManySchema>();
        foreach (var r in Array(definition, "ManyToManyRelationships"))
        {
            var entity1 = (string)r["Entity1LogicalName"]!;
            var entity2 = (string)r["Entity2LogicalName"]!;
            if (string.Equals(entity1, logicalName, StringComparison.OrdinalIgnoreCase))
                manyToMany.Add(new ManyToManySchema(entity2, (string)r["Entity1NavigationPropertyName"]!));
            if (string.Equals(entity2, logicalName, StringComparison.OrdinalIgnoreCase))
                manyToMany.Add(new ManyToManySchema(entity1, (string)r["Entity2NavigationPropertyName"]!));
        }

        return new TableSchema(logicalName, (string)definition["EntitySetName"]!, columns, manyToOne, oneToMany, manyToMany);
    }

    private async Task<IReadOnlySet<int>?> ReadOptionValuesAsync(string table, ColumnSchema column, CancellationToken ct)
    {
        var cast = column.AttributeType switch
        {
            "State" => "StateAttributeMetadata",
            "Status" => "StatusAttributeMetadata",
            _ => "PicklistAttributeMetadata"
        };

        var response = await GetAsync($"EntityDefinitions(LogicalName='{Quote(table)}')/Attributes/Microsoft.Dynamics.CRM.{cast}" +
            $"?$select=LogicalName&$filter=LogicalName eq '{Quote(column.LogicalName)}'&$expand=OptionSet($select=Options)", ct).ConfigureAwait(false);

        var options = Values(response).FirstOrDefault()?["OptionSet"]?["Options"]?.AsArray();
        return options?.Select(o => (int)o!["Value"]!).ToHashSet();
    }

    private static IEnumerable<TableName> Names(JsonObject response)
        => Values(response).Select(t => new TableName((string)t["LogicalName"]!, (string?)t["EntitySetName"] ?? string.Empty));

    private static IEnumerable<JsonObject> Values(JsonObject response) => Array(response, "value");

    private static IEnumerable<JsonObject> Array(JsonObject owner, string property)
        => owner[property]?.AsArray().OfType<JsonObject>() ?? [];

    // OData doubles a quote inside a string literal.
    private static string Quote(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
