using System.Reflection;
using System.Text.Json.Serialization;
using Dataverse.Fluent.Schema.Metadata;

namespace Dataverse.Fluent.Schema.Validation;

/// <summary>Compares one model with its table's metadata and collects the findings.</summary>
internal sealed class ModelChecker
{
    private const string FormattedValue = "OData.Community.Display.V1.FormattedValue";

    private readonly SchemaReader _reader;
    private readonly SchemaValidationOptions _options;
    private readonly Type _model;
    private readonly List<SchemaFinding> _findings = [];

    // Columns already reported as missing or unselectable, so the live $select check does not report them again.
    private readonly HashSet<string> _knownBad = new(StringComparer.Ordinal);

    public ModelChecker(SchemaReader reader, SchemaValidationOptions options, Type model)
    {
        _reader = reader;
        _options = options;
        _model = model;
    }

    public async Task<IReadOnlyList<SchemaFinding>> CheckAsync(CancellationToken ct)
    {
        DataverseEntityMapping mapping;
        try
        {
            mapping = DataverseEntityMapping.For(_model);
        }
        catch (InvalidOperationException ex)
        {
            Add(SchemaCodes.InvalidMapping, SchemaSeverity.Error, null, ex.Message);
            return _findings;
        }
        catch (Exception ex) when (ex is TypeLoadException or IOException or BadImageFormatException or ArgumentException or NotSupportedException)
        {
            // An attribute that rejects its arguments, or a property type that cannot be loaded. The client fails on
            // the model the same way, and the other models are still checked.
            Add(SchemaCodes.InvalidMapping, SchemaSeverity.Error, null, $"{_model.Name} cannot be read ({ex.GetType().Name}): {ex.Message}");
            return _findings;
        }

        if (!mapping.IsMapped)
        {
            Add(SchemaCodes.InvalidMapping, SchemaSeverity.Error, null,
                $"{_model.Name} is not mapped to a table: it has no [DataverseEntity] or [EntitySet] attribute, and no registered resolver maps it.");
            return _findings;
        }

        var table = await ResolveTableAsync(mapping, ct).ConfigureAwait(false);
        if (table is null)
            return _findings;

        foreach (var member in mapping.Members)
        {
            switch (member.Kind)
            {
                case DataverseMemberKind.Column:
                    await CheckColumnAsync(table, member, ct).ConfigureAwait(false);
                    break;
                case DataverseMemberKind.Lookup:
                    await CheckLookupAsync(table, member, ct).ConfigureAwait(false);
                    break;
                case DataverseMemberKind.PolymorphicLookup:
                    await CheckPolymorphicLookupAsync(table, member, ct).ConfigureAwait(false);
                    break;
                case DataverseMemberKind.Navigation:
                    await CheckNavigationAsync(table, member, ct).ConfigureAwait(false);
                    break;
            }
        }

        if (_options.CheckSelect)
            await new SelectCheck(_reader, this).RunAsync(table, mapping, _knownBad, ct).ConfigureAwait(false);

        return _findings;
    }

    // ───────── Table ─────────

    private async Task<TableSchema?> ResolveTableAsync(DataverseEntityMapping mapping, CancellationToken ct)
    {
        if (mapping.LogicalName is { } logicalName)
        {
            var table = await _reader.GetTableAsync(logicalName, ct).ConfigureAwait(false);
            if (table is null)
            {
                var all = await _reader.GetAllTablesAsync(ct).ConfigureAwait(false);
                Add(SchemaCodes.TableNotFound, SchemaSeverity.Error, null, $"Table '{logicalName}' does not exist.",
                    NameSuggestions.DidYouMean(logicalName, all.Select(t => t.LogicalName)));
                return null;
            }

            if (!string.Equals(table.EntitySetName, mapping.EntitySet, StringComparison.Ordinal))
            {
                Add(SchemaCodes.EntitySetMismatch, SchemaSeverity.Error, null,
                    $"The entity set of '{logicalName}' is '{table.EntitySetName}', not '{mapping.EntitySet}'.",
                    fix: SchemaFix.SetEntitySet(table.EntitySetName));
            }

            return table;
        }

        var entitySet = mapping.EntitySet!;
        var found = await _reader.FindLogicalNameAsync(entitySet, ct).ConfigureAwait(false);
        if (found is null)
        {
            var all = await _reader.GetAllTablesAsync(ct).ConfigureAwait(false);
            Add(SchemaCodes.TableNotFound, SchemaSeverity.Error, null, $"No table has the entity set '{entitySet}'.",
                NameSuggestions.DidYouMean(entitySet, all.Select(t => t.EntitySetName)));
            return null;
        }

        return await _reader.GetTableAsync(found, ct).ConfigureAwait(false);
    }

    // ───────── Columns ─────────

    private async Task CheckColumnAsync(TableSchema table, DataverseMemberMapping member, CancellationToken ct)
    {
        var name = member.Name;
        if (name.Contains('@', StringComparison.Ordinal))
        {
            CheckAnnotation(table, member);
            return;
        }

        // A lookup read by its value name without [DataverseLookup].
        if (IsValueName(name, out var lookupName))
        {
            await CheckLookupValueColumnAsync(table, member, lookupName, ct).ConfigureAwait(false);
            return;
        }

        var column = table.Column(name);
        if (column is null)
        {
            _knownBad.Add(name);
            Add(SchemaCodes.ColumnNotFound, SchemaSeverity.Error, member.Property, $"Column '{name}' does not exist on {table.LogicalName}.",
                NameSuggestions.DidYouMean(name, table.Columns.Values.Where(c => !table.IsCompanion(c)).Select(c => c.LogicalName)));
            return;
        }

        if (table.IsCompanion(column))
        {
            _knownBad.Add(name);
            Add(SchemaCodes.CompanionColumn, SchemaSeverity.Error, member.Property,
                $"'{name}' is the display value of '{column.AttributeOf}', which the Web API returns as an annotation, not as a column.",
                CompanionSuggestion(table, column));
            return;
        }

        if (column.IsLookup)
        {
            // The live check would report it again only when no navigation property has the same name (see
            // SchemaContractTests; checked 2026-11-20).
            _knownBad.Add(name);
            var single = column.Targets.Count == 1 ? (await _reader.GetEntitySetsAsync(column.Targets, ct).ConfigureAwait(false))[column.Targets[0]] : null;
            Add(SchemaCodes.LookupMappedAsColumn, SchemaSeverity.Error, member.Property,
                $"'{name}' is a lookup, which Dataverse returns as '_{name}_value', so the property is never filled.",
                single is not null
                    ? $"Map it with [DataverseLookup(\"{name}\", \"{single}\")]."
                    : $"Map it as a DataverseReference with [DataverseLookup(\"{name}\")] and a [DataverseLookupTarget] for each of {string.Join(", ", column.Targets)}.");
            return;
        }

        if (!HasConverter(member.Property) && ClrTypeRules.Check(column, member.Property.PropertyType) is { } problem)
            Add(problem.Code, problem.Severity, member.Property, problem.Message, problem.Suggestion);

        if (_options.CheckChoiceEnums && column.IsChoice && Underlying(member.Property.PropertyType) is { IsEnum: true } enumType && !HasConverter(member.Property))
            await CheckEnumAsync(table, column, member, enumType, ct).ConfigureAwait(false);

        CheckWritable(column, member);
    }

    private async Task CheckLookupValueColumnAsync(TableSchema table, DataverseMemberMapping member, string lookupName, CancellationToken ct)
    {
        var column = table.Column(lookupName);
        if (column is null || !column.IsLookup)
        {
            _knownBad.Add(member.Name);
            Add(SchemaCodes.LookupNotFound, SchemaSeverity.Error, member.Property,
                column is null
                    ? $"'{member.Name}' reads the lookup '{lookupName}', which does not exist on {table.LogicalName}."
                    : $"'{member.Name}' reads a lookup, but '{lookupName}' is a {column.Description} column.",
                column is null ? NameSuggestions.DidYouMean(lookupName, table.Columns.Values.Where(c => c.IsLookup).Select(c => c.LogicalName)) : null);
            return;
        }

        if (!HasConverter(member.Property) && ClrTypeRules.Check(column, member.Property.PropertyType) is { } problem)
            Add(problem.Code, problem.Severity, member.Property, problem.Message, problem.Suggestion);

        // Written under its value name, which Dataverse rejects with HTTP 400 ("use navigation properties instead";
        // checked 2026-11-20). [DataverseLookup] reads the same name and writes a bind.
        if (member.IsWrittenOnCreate || member.IsWrittenOnUpdate)
        {
            SchemaFix? fix = null;
            if (!column.IsValidForCreate && !column.IsValidForUpdate)
            {
                // A lookup Dataverse sets itself (createdby, say) cannot be written as a bind either: only read it.
                fix = SchemaFix.SetReadOnly(DataverseOperation.CreateAndUpdate);
            }
            // [DataverseLookup] with an entity set maps a Guid; a DataverseReference needs targets instead.
            else if (column.Targets.Count == 1 && Underlying(member.Property.PropertyType) == typeof(Guid)
                && table.NavigationPropertyFor(lookupName, column.Targets[0]) is { } navigation
                && (await _reader.GetEntitySetsAsync(column.Targets, ct).ConfigureAwait(false))[column.Targets[0]] is { } entitySet)
            {
                // A lookup that can be set on create only is not sent on update either, so the fix is complete in one go.
                fix = SchemaFix.MapAsLookup(lookupName, entitySet, navigation,
                    !column.IsValidForUpdate && member.IsWrittenOnUpdate ? DataverseOperation.Update : DataverseOperation.None);
            }

            Add(SchemaCodes.LookupValueWritten, SchemaSeverity.Error, member.Property,
                $"SetFrom writes '{member.Name}', which Dataverse rejects (HTTP 400): a lookup is written as a bind.",
                fix is null ? "Map it with [DataverseLookup], or mark it [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]." : null,
                fix);
        }
    }

    private void CheckAnnotation(TableSchema table, DataverseMemberMapping member)
    {
        var name = member.Name;
        var at = name.IndexOf('@', StringComparison.Ordinal);
        var target = name[..at];
        var annotation = name[(at + 1)..];

        // A write-only bind, e.g. "parentcustomerid_account@odata.bind": the navigation property must exist.
        if (string.Equals(annotation, "odata.bind", StringComparison.OrdinalIgnoreCase))
        {
            if (!table.ManyToOne.Any(r => string.Equals(r.NavigationProperty, target, StringComparison.Ordinal)))
            {
                Add(SchemaCodes.NavigationNotFound, SchemaSeverity.Error, member.Property, $"Navigation property '{target}' does not exist on {table.LogicalName}.",
                    NameSuggestions.DidYouMean(target, table.ManyToOne.Select(r => r.NavigationProperty)));
            }

            return;
        }

        // A record annotation such as "@odata.etag" annotates no column.
        var columnName = IsValueName(target, out var lookupName) ? lookupName : target;
        var column = target.Length == 0 ? null : table.Column(columnName);
        var isNavigation = table.OneToMany.Any(r => r.NavigationProperty == target) || table.ManyToMany.Any(r => r.NavigationProperty == target);
        if (target.Length > 0 && column is null && !isNavigation)
        {
            Add(SchemaCodes.ColumnNotFound, SchemaSeverity.Error, member.Property, $"'{name}' annotates '{target}', which does not exist on {table.LogicalName}.",
                NameSuggestions.DidYouMean(columnName, table.Columns.Keys));
            return;
        }

        if (column is not null && column.IsLookup && target == columnName)
        {
            Add(SchemaCodes.LookupMappedAsColumn, SchemaSeverity.Error, member.Property,
                $"'{target}' is a lookup, whose annotations Dataverse returns on '_{target}_value', so the property is never filled.",
                $"Use '_{target}_value@{annotation}'.");
            return;
        }

        if (member.IsWrittenOnCreate || member.IsWrittenOnUpdate)
        {
            Add(SchemaCodes.AnnotationWritten, SchemaSeverity.Info, member.Property,
                $"SetFrom sends the annotation '{name}', which Dataverse ignores.",
                fix: SchemaFix.SetReadOnly(DataverseOperation.CreateAndUpdate));
        }
    }

    // A column Dataverse sets or fixes itself is ignored when written (checked 2026-11-20), so SetFrom should not send it.
    private void CheckWritable(ColumnSchema column, DataverseMemberMapping member)
    {
        if (!column.IsValidForCreate && !column.IsValidForUpdate)
        {
            if (member.IsWrittenOnCreate || member.IsWrittenOnUpdate)
            {
                Add(SchemaCodes.NotWritable, SchemaSeverity.Warning, member.Property,
                    $"'{column.LogicalName}' cannot be written, but SetFrom sends it{(member.IsWrittenOnUpdate ? "" : " on create")}, where Dataverse ignores it.",
                    fix: SchemaFix.SetReadOnly(DataverseOperation.CreateAndUpdate));
            }
        }
        else if (!column.IsValidForUpdate && member.IsWrittenOnUpdate)
        {
            Add(SchemaCodes.SentOnUpdate, SchemaSeverity.Warning, member.Property,
                $"'{column.LogicalName}' cannot be updated, but SetFrom sends it on update, where Dataverse ignores it.",
                fix: SchemaFix.SetReadOnly(DataverseOperation.Update));
        }
    }

    private async Task CheckEnumAsync(TableSchema table, ColumnSchema column, DataverseMemberMapping member, Type enumType, CancellationToken ct)
    {
        if (enumType.IsDefined(typeof(FlagsAttribute), inherit: false))
            return;

        var options = await _reader.GetOptionValuesAsync(table, column, ct).ConfigureAwait(false);
        if (options is null)
            return;

        var members = Enum.GetValues(enumType).Cast<object>()
            .Select(v => (Name: Enum.GetName(enumType, v)!, Value: Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

        var unknown = members.Where(m => !options.Contains((int)m.Value)).ToList();
        if (unknown.Count > 0)
        {
            Add(SchemaCodes.EnumValueNotAnOption, SchemaSeverity.Warning, member.Property,
                $"{enumType.Name} has members that are not options of '{column.LogicalName}': {string.Join(", ", unknown.Select(m => $"{m.Name} = {m.Value}"))}.");
        }

        var unmapped = options.Where(o => members.All(m => m.Value != o)).Order().ToList();
        if (unmapped.Count > 0)
        {
            const int shown = 10;
            var list = string.Join(", ", unmapped.Take(shown)) + (unmapped.Count > shown ? $" and {unmapped.Count - shown} more" : "");
            Add(SchemaCodes.OptionWithoutEnumMember, SchemaSeverity.Info, member.Property,
                $"{unmapped.Count} option{(unmapped.Count == 1 ? "" : "s")} of '{column.LogicalName}' {(unmapped.Count == 1 ? "has" : "have")} no {enumType.Name} member: {list}.");
        }
    }

    // ───────── Lookups ─────────

    private async Task CheckLookupAsync(TableSchema table, DataverseMemberMapping member, CancellationToken ct)
    {
        var lookup = member.Lookup!;
        var column = FindLookupColumn(table, member);
        if (column is null)
            return;

        var sets = await _reader.GetEntitySetsAsync(column.Targets, ct).ConfigureAwait(false);
        if (column.Targets.Count > 1)
        {
            Add(SchemaCodes.PolymorphicColumnMappedAsSingleTable, SchemaSeverity.Warning, member.Property,
                $"'{lookup.LogicalName}' can point at {string.Join(", ", column.Targets)}, but is mapped to one table, so a record pointing at another reads its ID without the table.",
                $"Map it as a DataverseReference with [DataverseLookup(\"{lookup.LogicalName}\")] and a [DataverseLookupTarget] per table.");
        }

        var target = column.Targets.FirstOrDefault(t => string.Equals(sets[t], lookup.TargetEntitySet, StringComparison.Ordinal));
        if (target is null)
        {
            if (column.Targets.Count == 1 && sets[column.Targets[0]] is { } expected)
            {
                target = column.Targets[0];
                Add(SchemaCodes.LookupTargetEntitySetMismatch, SchemaSeverity.Error, member.Property,
                    $"'{lookup.LogicalName}' points at {target}, whose entity set is '{expected}', not '{lookup.TargetEntitySet}'.",
                    fix: SchemaFix.SetLookupTargetEntitySet(expected));
            }
            else
            {
                Add(SchemaCodes.LookupTargetEntitySetMismatch, SchemaSeverity.Error, member.Property,
                    $"'{lookup.TargetEntitySet}' is not the entity set of a table '{lookup.LogicalName}' can point at ({string.Join(", ", column.Targets.Select(t => $"{t}: {sets[t]}"))}).");
            }
        }

        if (target is not null && table.NavigationPropertyFor(lookup.LogicalName, target) is { } navigation
            && !string.Equals(navigation, lookup.NavigationProperty, StringComparison.Ordinal))
        {
            Add(SchemaCodes.LookupNavigationPropertyMismatch, SchemaSeverity.Error, member.Property,
                $"'{lookup.LogicalName}' is written through the navigation property '{navigation}', not '{lookup.NavigationProperty}'.",
                fix: SchemaFix.SetLookupNavigationProperty(navigation));
        }

        CheckWritable(column, member);
    }

    private async Task CheckPolymorphicLookupAsync(TableSchema table, DataverseMemberMapping member, CancellationToken ct)
    {
        var lookup = member.Lookup!;
        var column = FindLookupColumn(table, member);
        if (column is null)
            return;

        var sets = await _reader.GetEntitySetsAsync(column.Targets.Concat(member.Targets.Select(t => t.LogicalName)), ct).ConfigureAwait(false);
        var mapped = member.Targets.ToDictionary(t => t.LogicalName, StringComparer.OrdinalIgnoreCase);

        foreach (var target in column.Targets)
        {
            var entitySet = sets[target];
            var navigation = table.NavigationPropertyFor(lookup.LogicalName, target);
            if (!mapped.TryGetValue(target, out var declared))
            {
                Add(SchemaCodes.MissingLookupTarget, SchemaSeverity.Warning, member.Property,
                    $"'{lookup.LogicalName}' can also point at {target}; a retrieved record pointing at it cannot be written back.",
                    fix: entitySet is not null && navigation is not null ? SchemaFix.AddLookupTarget(target, entitySet, navigation) : null);
                continue;
            }

            var problems = new List<string>();
            if (entitySet is not null && !string.Equals(entitySet, declared.EntitySet, StringComparison.Ordinal))
                problems.Add($"its entity set is '{entitySet}', not '{declared.EntitySet}'");
            if (navigation is not null && !string.Equals(navigation, declared.NavigationProperty, StringComparison.Ordinal))
                problems.Add($"it is written through '{navigation}', not '{declared.NavigationProperty}'");

            if (problems.Count > 0)
            {
                Add(SchemaCodes.LookupTargetMismatch, SchemaSeverity.Error, member.Property,
                    $"Target {target} of '{lookup.LogicalName}': {string.Join(", and ", problems)}.",
                    fix: SchemaFix.SetLookupTarget(declared.LogicalName, entitySet ?? declared.EntitySet, navigation ?? declared.NavigationProperty));
            }
        }

        foreach (var declared in member.Targets.Where(t => !column.Targets.Contains(t.LogicalName, StringComparer.OrdinalIgnoreCase)))
        {
            Add(SchemaCodes.UnknownLookupTarget, SchemaSeverity.Error, member.Property,
                $"'{lookup.LogicalName}' cannot point at {declared.LogicalName}; it can point at {string.Join(", ", column.Targets)}.");
        }

        CheckWritable(column, member);
    }

    private ColumnSchema? FindLookupColumn(TableSchema table, DataverseMemberMapping member)
    {
        var name = member.Lookup!.LogicalName;
        var column = table.Column(name);
        if (column is not null && column.IsLookup)
            return column;

        _knownBad.Add(member.Name);
        Add(SchemaCodes.LookupNotFound, SchemaSeverity.Error, member.Property,
            column is null ? $"Lookup '{name}' does not exist on {table.LogicalName}." : $"'{name}' is a {column.Description} column, not a lookup.",
            column is null ? NameSuggestions.DidYouMean(name, table.Columns.Values.Where(c => c.IsLookup).Select(c => c.LogicalName)) : null);
        return null;
    }

    // ───────── Navigation properties ─────────

    private async Task CheckNavigationAsync(TableSchema table, DataverseMemberMapping member, CancellationToken ct)
    {
        var name = member.Name;
        string? related;
        if (member.IsCollection)
        {
            related = table.OneToMany.FirstOrDefault(r => r.NavigationProperty == name)?.ReferencingEntity
                ?? table.ManyToMany.FirstOrDefault(r => r.NavigationProperty == name)?.OtherEntity;
            if (related is null)
            {
                Add(SchemaCodes.CollectionNavigationNotFound, SchemaSeverity.Error, member.Property,
                    $"Collection navigation property '{name}' does not exist on {table.LogicalName}.",
                    NameSuggestions.DidYouMean(name, table.OneToMany.Select(r => r.NavigationProperty).Concat(table.ManyToMany.Select(r => r.NavigationProperty))));
                return;
            }
        }
        else
        {
            related = table.ManyToOne.FirstOrDefault(r => r.NavigationProperty == name)?.ReferencedEntity;
            if (related is null)
            {
                Add(SchemaCodes.NavigationNotFound, SchemaSeverity.Error, member.Property,
                    $"Navigation property '{name}' does not exist on {table.LogicalName}.",
                    NameSuggestions.DidYouMean(name, table.ManyToOne.Select(r => r.NavigationProperty)));
                return;
            }
        }

        // The owner relationship leads to the abstract owner table; the record read is a systemuser or a team.
        if (member.RelatedType is null || string.Equals(related, "owner", StringComparison.OrdinalIgnoreCase))
            return;

        var relatedTable = await RelatedTableAsync(member.RelatedType, ct).ConfigureAwait(false);
        if (relatedTable is not null && !string.Equals(relatedTable, related, StringComparison.OrdinalIgnoreCase))
        {
            Add(SchemaCodes.RelatedTypeMismatch, SchemaSeverity.Warning, member.Property,
                $"'{name}' leads to {related}, but {member.RelatedType.Name} maps {relatedTable}.");
        }
    }

    private async Task<string?> RelatedTableAsync(Type relatedType, CancellationToken ct)
    {
        DataverseEntityMapping mapping;
        try
        {
            mapping = DataverseEntityMapping.For(relatedType);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TypeLoadException or IOException or BadImageFormatException or ArgumentException or NotSupportedException)
        {
            return null; // Reported when the related model itself is validated.
        }

        return mapping.LogicalName
            ?? (mapping.EntitySet is { } set ? await _reader.FindLogicalNameAsync(set, ct).ConfigureAwait(false) : null);
    }

    // ───────── Helpers ─────────

    internal void Add(string code, SchemaSeverity severity, PropertyInfo? property, string message, string? suggestion = null, SchemaFix? fix = null)
    {
        if (_options.IgnoreCodes.Contains(code))
            return;

        // A property inherited from a base model is reported against the model that declares it.
        var model = property?.DeclaringType is { } declaring && declaring != _model && declaring.IsAssignableFrom(_model) ? declaring : _model;
        _findings.Add(new SchemaFinding(code, severity, model, property, message, suggestion, fix));
    }

    private static string CompanionSuggestion(TableSchema table, ColumnSchema companion)
    {
        var owner = table.Column(companion.AttributeOf!)!;
        if (owner.IsLookup)
        {
            return companion.AttributeType == "EntityName"
                ? $"Map '{owner.LogicalName}' as a DataverseReference, which carries the table, or read '_{owner.LogicalName}_value@Microsoft.Dynamics.CRM.lookuplogicalname'."
                : $"Read '_{owner.LogicalName}_value@{FormattedValue}' instead.";
        }

        return $"Read '{owner.LogicalName}@{FormattedValue}' instead.";
    }

    private static bool IsValueName(string name, out string logicalName)
    {
        if (name.Length > "__value".Length && name.StartsWith('_') && name.EndsWith("_value", StringComparison.Ordinal))
        {
            logicalName = name[1..^"_value".Length];
            return true;
        }

        logicalName = name;
        return false;
    }

    // A converter on the property or on its type decides what the property can hold, so the type rules do not apply.
    private static bool HasConverter(PropertyInfo property)
        => property.IsDefined(typeof(JsonConverterAttribute), inherit: true)
            || Underlying(property.PropertyType).IsDefined(typeof(JsonConverterAttribute), inherit: false);

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;
}
