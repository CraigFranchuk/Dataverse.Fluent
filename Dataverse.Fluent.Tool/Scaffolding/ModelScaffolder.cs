using System.Globalization;
using System.Text;
using Dataverse.Fluent.Schema;
using Dataverse.Fluent.Tool.Fixing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Dataverse.Fluent.Tool.Scaffolding;

/// <summary>One property (or model) that scaffolding changed, and how.</summary>
internal sealed record ScaffoldChange(string File, string Location, string Description, bool TypeChanged);

/// <summary>A model or property that scaffolding could not match to the environment.</summary>
internal sealed record ScaffoldMiss(string Location, string Message, string? Suggestion);

/// <summary>A model or property that scaffolding left as it is for <c>validate</c> to check, and why.</summary>
internal sealed record ScaffoldSkip(string Location, string Reason);

/// <summary>What a scaffold run did to a project's models.</summary>
internal sealed record ScaffoldResult(
    IReadOnlyList<SourceFile> Files,
    IReadOnlyList<ScaffoldChange> Changes,
    IReadOnlyList<ScaffoldMiss> Misses,
    IReadOnlyList<ScaffoldSkip> Skipped,
    int Models)
{
    public IEnumerable<SourceFile> Changed => Files.Where(f => f.Changes.Count > 0);
}

/// <summary>
/// Fills in the models of a project from the environment's metadata. A model is a class with
/// <c>[DataverseEntity]</c> or <c>[EntitySet]</c>; it does not have to compile yet. Each property without a mapping
/// is matched to a column by name and gets its mapping attribute, its read-only marker and, when its type cannot hold
/// the column, the type that can. A property that is already mapped only has its type and read-only marker checked.
/// </summary>
/// <param name="transport">The environment to read metadata from.</param>
/// <param name="nullableEnabled">Whether the project has nullable reference types on.</param>
/// <param name="enums">Whether to write an enum for each choice column.</param>
/// <param name="globalUsings">The project's global usings that are not in its source, such as its implicit usings.</param>
/// <param name="skip">Properties to leave alone, as <c>Model.Property</c> (the model's simple or full name).</param>
internal sealed class ModelScaffolder(
    IDataverseTransport transport,
    bool nullableEnabled,
    bool enums,
    IReadOnlyList<string>? globalUsings = null,
    IReadOnlyCollection<string>? skip = null)
{
    private readonly List<ScaffoldChange> _changes = [];
    private readonly List<ScaffoldMiss> _misses = [];
    private readonly List<ScaffoldSkip> _skipped = [];
    private readonly HashSet<string> _skipUsed = new(StringComparer.Ordinal);

    // The full names of the enums this run writes: a second one must not take the same name.
    private readonly HashSet<string> _newEnums = new(StringComparer.Ordinal);

    private ModelSemantics _semantics = null!;
    private PropertyClassifier _classifier = null!;

    public async Task<ScaffoldResult> ScaffoldAsync(IReadOnlyList<SourceFile> files, IReadOnlyList<string> typeNames, CancellationToken ct)
    {
        _semantics = ModelSemantics.Create(files, globalUsings ?? [], nullableEnabled);
        _classifier = new PropertyClassifier(_semantics);

        var models = files
            .SelectMany(f => f.Tree.GetRoot(ct).DescendantNodes().OfType<TypeDeclarationSyntax>().Select(t => (File: f, Type: t)))
            .Where(m => Table(m.Type) is not null)
            .ToList();

        foreach (var name in typeNames)
        {
            if (!models.Any(m => Matches(m.Type, name)))
                throw new ToolException($"No model named '{name}' with [DataverseEntity] or [EntitySet] in the project's source.");
        }

        if (typeNames.Count > 0)
            models = [.. models.Where(m => typeNames.Any(n => Matches(m.Type, n)))];

        foreach (var (file, type) in models)
            await ScaffoldModelAsync(file, type, ct).ConfigureAwait(false);

        // A mistyped name would skip nothing, and the property it was meant for would fail the run.
        var unknown = (skip ?? []).Where(s => !_skipUsed.Contains(s)).ToList();
        if (unknown.Count > 0)
        {
            throw new ToolException(
                $"--skip: {string.Join(", ", unknown.Select(s => $"'{s}'"))} {(unknown.Count == 1 ? "is not a property" : "are not properties")} " +
                "of a model that was read. Name a property as Model.Property.");
        }

        return new ScaffoldResult(files, _changes, _misses, _skipped, models.Count);
    }

    private static bool Matches(TypeDeclarationSyntax type, string name)
        => type.Identifier.ValueText == name || SourceFixer.FullName(type) == name;

    // The attribute that maps the type to a table, with the table's logical name or entity set.
    private (AttributeSyntax Attribute, string? LogicalName, string? EntitySet)? Table(TypeDeclarationSyntax type)
    {
        if (_semantics.FindAttribute(type.AttributeLists, "DataverseEntity") is { } entity && _semantics.StringArgument(entity, 0) is { Length: > 0 } logicalName)
            return (entity, logicalName, _semantics.StringArgument(entity, 1));
        if (_semantics.FindAttribute(type.AttributeLists, "EntitySet") is { } set && _semantics.StringArgument(set, 0) is { Length: > 0 } entitySet)
            return (set, null, entitySet);
        return null;
    }

    private async Task ScaffoldModelAsync(SourceFile file, TypeDeclarationSyntax type, CancellationToken ct)
    {
        var (attribute, logicalName, entitySet) = Table(type)!.Value;
        var className = type.Identifier.ValueText;

        var candidates = new List<ClassifiedProperty>();
        var left = new List<ScaffoldSkip>();
        foreach (var property in type.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (IsSkipped(type, property))
                continue;

            var classified = _classifier.Classify(property);
            if (classified.Role is PropertyRole.Unmapped or PropertyRole.Mapped)
                candidates.Add(classified);
            else if (classified.Reason is { } reason)
                left.Add(new ScaffoldSkip($"{className}.{property.Identifier.ValueText}", reason));
        }

        var model = await DataverseModelScaffolder.ScaffoldAsync(transport, new ScaffoldRequest
        {
            LogicalName = logicalName,
            EntitySet = logicalName is null ? entitySet : null,
            Properties = [.. candidates.Select(c => new ScaffoldProperty(c.Name!, c.Type))],
            IncludeChoiceOptions = enums
        }, ct).ConfigureAwait(false);

        if (!model.TableFound)
        {
            _misses.Add(new ScaffoldMiss(className, model.Message!, model.Suggestion));
            return;
        }

        if (logicalName is not null && !string.Equals(entitySet, model.EntitySet, StringComparison.Ordinal))
            SetEntitySet(file, attribute, className, entitySet, model.EntitySet!);

        _skipped.AddRange(left);

        var qualifier = SourceFixer.Qualifier(attribute);
        var newEnums = new StringBuilder();
        for (var i = 0; i < candidates.Count; i++)
            ScaffoldProperty(file, type, candidates[i], model.Properties[i], qualifier, newEnums);

        if (newEnums.Length > 0)
            SourceFixer.Change(file, new TextSpan(type.Span.End, 0), newEnums.ToString());
    }

    // Whether --skip names the property, by the model's simple or full name.
    private bool IsSkipped(TypeDeclarationSyntax type, PropertyDeclarationSyntax property)
    {
        if (skip is null || skip.Count == 0)
            return false;

        var name = property.Identifier.ValueText;
        var matched = skip.Where(s => s == $"{type.Identifier.ValueText}.{name}" || s == $"{SourceFixer.FullName(type)}.{name}").ToList();
        _skipUsed.UnionWith(matched);
        return matched.Count > 0;
    }

    // Writes the table's entity set where it is missing or a string that differs. One given as a constant is not
    // rewritten: a wrong value is reported, and one that cannot be read is left to validate.
    private void SetEntitySet(SourceFile file, AttributeSyntax attribute, string className, string? written, string entitySet)
    {
        var arguments = attribute.ArgumentList!.Arguments;
        string? problem;
        if (arguments.Count == 1)
        {
            problem = SourceFixer.Change(file, new TextSpan(arguments[0].Span.End, 0), $", {SourceFixer.Literal(entitySet)}");
        }
        else if (arguments[1].Expression.IsKind(SyntaxKind.StringLiteralExpression))
        {
            problem = SourceFixer.Change(file, arguments[1].Expression.Span, SourceFixer.Literal(entitySet));
        }
        else if (written is null)
        {
            _skipped.Add(new ScaffoldSkip(className, $"its entity set, {arguments[1].Expression}, is not a constant the tool can read"));
            return;
        }
        else
        {
            _misses.Add(new ScaffoldMiss(className, $"The entity set is \"{entitySet}\", but {arguments[1].Expression} is \"{written}\".", "Change the constant, or write the entity set as a string."));
            return;
        }

        if (problem is null)
            _changes.Add(new ScaffoldChange(file.Path, className, $"entity set \"{entitySet}\"", TypeChanged: false));
        else
            _misses.Add(new ScaffoldMiss(className, $"The entity set is \"{entitySet}\", but it could not be written: {problem}.", null));
    }

    private void ScaffoldProperty(SourceFile file, TypeDeclarationSyntax type, ClassifiedProperty candidate, ScaffoldedProperty scaffolded,
        string qualifier, StringBuilder newEnums)
    {
        var property = candidate.Property;
        var mapped = candidate.Role == PropertyRole.Mapped;
        var location = $"{type.Identifier.ValueText}.{property.Identifier.ValueText}";
        if (scaffolded.Kind == ScaffoldedPropertyKind.NotFound)
        {
            // A mapped property that is not a column is a navigation property, or a mistake validate reports.
            if (!mapped)
                _misses.Add(new ScaffoldMiss(location, scaffolded.Message!, scaffolded.Suggestion));
            return;
        }

        // A name mapped as a plain column that is a lookup here is a navigation property named like its lookup, or a
        // mistake: neither is scaffolding's to rewrite, and validate reports the mistake.
        if (mapped && scaffolded.Kind != ScaffoldedPropertyKind.Column)
        {
            _skipped.Add(new ScaffoldSkip(location, $"it is mapped to '{candidate.Name}' as a column, and that is a lookup on the table"));
            return;
        }

        var lines = new List<string>();
        var notes = new List<string>();

        if (scaffolded.ReadOnlyFor != DataverseOperation.None && _semantics.FindAttribute(property.AttributeLists, "DataverseReadOnly") is null)
        {
            var operation = scaffolded.ReadOnlyFor == DataverseOperation.Update ? nameof(DataverseOperation.Update) : nameof(DataverseOperation.CreateAndUpdate);
            lines.Add($"[{qualifier}DataverseReadOnly({qualifier}DataverseOperation.{operation})]");
            notes.Add(scaffolded.ReadOnlyFor == DataverseOperation.Update ? "read-only on update" : "read-only");
        }

        if (!mapped)
        {
            lines.AddRange(ModelText.MappingAttributes(scaffolded, qualifier));
            notes.Insert(0, scaffolded.Kind == ScaffoldedPropertyKind.Column
                ? $"mapped to '{scaffolded.LogicalName}'"
                : $"mapped to the lookup '{scaffolded.LogicalName}' ({string.Join(", ", scaffolded.Targets.Select(t => t.LogicalName))})");
        }

        var newType = NewType(file, type, candidate, scaffolded, qualifier, newEnums);
        if (lines.Count == 0 && newType is null)
            return;

        // The edits of one property apply together or not at all.
        var before = (Changes: file.Changes.Count, Expected: file.Expected.Count);
        string? problem = null;
        if (lines.Count > 0)
            problem = SourceFixer.InsertLinesBefore(file, property, lines);

        if (problem is null && newType is not null)
        {
            problem = SourceFixer.Change(file, property.Type.Span, newType);
            notes.Add($"type {property.Type} -> {newType}");
        }

        if (problem is not null)
        {
            file.Changes.RemoveRange(before.Changes, file.Changes.Count - before.Changes);
            file.Expected.RemoveRange(before.Expected, file.Expected.Count - before.Expected);
            _misses.Add(new ScaffoldMiss(location, $"It could not be written: {problem}", null));
            return;
        }

        if (newType is not null)
            file.Expect(property, type: newType);

        _changes.Add(new ScaffoldChange(file.Path, location, string.Join("; ", notes), newType is not null));
    }

    // The type to write in place of the property's, or null to keep it.
    private string? NewType(SourceFile file, TypeDeclarationSyntax type, ClassifiedProperty candidate, ScaffoldedProperty scaffolded, string qualifier,
        StringBuilder newEnums)
    {
        // A type the tool does not know (an enum, a strongly typed ID) is the model's choice.
        if (candidate.Type is null || scaffolded.ClrType is null)
            return null;

        var property = candidate.Property;
        if (enums && scaffolded.IsChoice && scaffolded.Options.Count > 0)
        {
            // The enum is written next to the model, so its name must be free there: in the source, and in what the
            // source imports.
            var name = type.Identifier.ValueText + property.Identifier.ValueText;
            var modelName = SourceFixer.FullName(type);
            var fullName = modelName[..^type.Identifier.ValueText.Length] + name;
            var state = _newEnums.Contains(fullName) ? TypeNameState.Taken : _semantics.TypeNameState(property.Type, name);
            if (state == TypeNameState.Free)
            {
                _newEnums.Add(fullName);
                newEnums.Append(ModelText.Enum(name, scaffolded.Options, SourceFixer.Indentation(file, type), SourceFixer.NewLine(file, file.Text.Lines[0]), leadingBlankLine: true));
                return name + "?";
            }

            // An enum of that name is there already (an earlier run wrote it): the property takes it. Something else
            // has the name, so the property keeps a plain type.
            if (state == TypeNameState.Enum)
                return name + "?";
        }

        if (scaffolded.TypeFits != false)
            return null;

        var system = scaffolded.ClrType is nameof(Guid) or nameof(DateTime) or nameof(DateOnly) ? _semantics.SystemQualifier(property.Type, scaffolded.ClrType) : "";
        return ModelText.TypeName(scaffolded, qualifier, nullableEnabled, system);
    }

    /// <summary>
    /// The CLR type a type name stands for as it is written, when it is one the type rules know; otherwise
    /// <see langword="null"/>. <see cref="ModelSemantics.KnownType"/> asks the compiler first.
    /// </summary>
    internal static Type? KnownType(TypeSyntax syntax)
    {
        if (syntax is NullableTypeSyntax nullable)
            syntax = nullable.ElementType;

        if (syntax is ArrayTypeSyntax array)
            return array.ElementType is PredefinedTypeSyntax element && element.Keyword.IsKind(SyntaxKind.ByteKeyword) && array.RankSpecifiers.Count == 1 ? typeof(byte[]) : null;

        var name = syntax switch
        {
            PredefinedTypeSyntax predefined => predefined.Keyword.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax { Right: IdentifierNameSyntax right } => right.Identifier.ValueText,
            _ => null
        };

        return name switch
        {
            "string" or "String" => typeof(string),
            "bool" or "Boolean" => typeof(bool),
            "int" or "Int32" => typeof(int),
            "long" or "Int64" => typeof(long),
            "short" or "Int16" => typeof(short),
            "byte" or "Byte" => typeof(byte),
            "decimal" or "Decimal" => typeof(decimal),
            "double" or "Double" => typeof(double),
            "float" or "Single" => typeof(float),
            "Guid" => typeof(Guid),
            "DateTime" => typeof(DateTime),
            "DateTimeOffset" => typeof(DateTimeOffset),
            "DateOnly" => typeof(DateOnly),
            "DataverseReference" => typeof(DataverseReference),
            _ => null
        };
    }
}

/// <summary>The C# text scaffolding writes: attributes, type names, enums, and a whole new model.</summary>
internal static class ModelText
{
    /// <summary>The attributes that map a property, one per line.</summary>
    public static IEnumerable<string> MappingAttributes(ScaffoldedProperty property, string qualifier)
    {
        var name = SourceFixer.Literal(property.LogicalName!);
        switch (property.Kind)
        {
            case ScaffoldedPropertyKind.Column:
                yield return $"[{qualifier}DataverseColumn({name})]";
                break;

            case ScaffoldedPropertyKind.Lookup:
                var target = property.Targets[0];
                var navigation = target.NavigationProperty == property.LogicalName ? "" : $", NavigationProperty = {SourceFixer.Literal(target.NavigationProperty)}";
                yield return $"[{qualifier}DataverseLookup({name}, {SourceFixer.Literal(target.EntitySet)}{navigation})]";
                break;

            case ScaffoldedPropertyKind.PolymorphicLookup:
                yield return $"[{qualifier}DataverseLookup({name})]";
                foreach (var t in property.Targets)
                    yield return $"[{qualifier}DataverseLookupTarget({SourceFixer.Literal(t.LogicalName)}, {SourceFixer.Literal(t.EntitySet)}, {SourceFixer.Literal(t.NavigationProperty)})]";
                break;
        }
    }

    /// <summary>The property type as a model declares it: nullable, since any column can be empty.</summary>
    public static string TypeName(ScaffoldedProperty property, string qualifier, bool nullableEnabled, string systemQualifier = "")
    {
        var name = property.ClrType switch
        {
            nameof(DataverseReference) => qualifier + property.ClrType,
            nameof(Guid) or nameof(DateTime) or nameof(DateOnly) => systemQualifier + property.ClrType,
            _ => property.ClrType!
        };
        return property.ClrTypeIsValueType || nullableEnabled ? name + "?" : name;
    }

    /// <summary>An enum with a member per option, named from the option's label.</summary>
    public static string Enum(string name, IReadOnlyList<ScaffoldedOption> options, string indentation, string newline, bool leadingBlankLine)
    {
        var text = new StringBuilder();
        if (leadingBlankLine)
            text.Append(newline).Append(newline);

        text.Append(indentation).Append("public enum ").Append(name).Append(newline).Append(indentation).Append('{').Append(newline);
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < options.Count; i++)
        {
            var member = Identifier(options[i].Label) ?? Fallback(options[i].Value);
            if (!used.Add(member))
            {
                member += Fallback(options[i].Value);
                used.Add(member);
            }

            text.Append(indentation).Append("    ").Append(member).Append(" = ").Append(options[i].Value.ToString(CultureInfo.InvariantCulture))
                .Append(i < options.Count - 1 ? "," : "").Append(newline);
        }

        return text.Append(indentation).Append('}').ToString();

        static string Fallback(int value) => value < 0 ? $"OptionMinus{-(long)value}" : $"Option{value}";
    }

    /// <summary>
    /// A name in PascalCase from a label, schema name or logical name: letters and digits kept, each word capitalised
    /// ("In Progress" → InProgress, "new_RegionId" → NewRegionId). <see langword="null"/> when nothing usable is left.
    /// </summary>
    public static string? Identifier(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var name = new StringBuilder();
        var startOfWord = true;
        foreach (var c in text)
        {
            if (!char.IsLetterOrDigit(c))
            {
                startOfWord = true;
                continue;
            }

            name.Append(startOfWord ? char.ToUpperInvariant(c) : c);
            startOfWord = false;
        }

        if (name.Length == 0)
            return null;
        return char.IsDigit(name[0]) ? "_" + name : name.ToString();
    }

    /// <summary>A new model file for a table and the columns asked for.</summary>
    public static string NewModel(ScaffoldedModel model, string className, string? @namespace, IReadOnlyList<(ScaffoldedProperty Property, string Name)> properties, bool enums)
    {
        var lines = new List<string> { "#nullable enable", "using System;", "using Dataverse.Fluent;", "" };
        if (@namespace is not null)
            lines.AddRange([$"namespace {@namespace};", ""]);

        lines.Add($"[DataverseEntity({SourceFixer.Literal(model.LogicalName!)}, {SourceFixer.Literal(model.EntitySet!)})]");
        lines.Add($"public partial class {className}");
        lines.Add("{");

        var newEnums = new List<string>();
        for (var i = 0; i < properties.Count; i++)
        {
            var (property, name) = properties[i];
            if (i > 0)
                lines.Add("");

            if (property.ReadOnlyFor != DataverseOperation.None)
            {
                var operation = property.ReadOnlyFor == DataverseOperation.Update ? nameof(DataverseOperation.Update) : nameof(DataverseOperation.CreateAndUpdate);
                lines.Add($"    [DataverseReadOnly(DataverseOperation.{operation})]");
            }

            lines.AddRange(MappingAttributes(property, "").Select(a => "    " + a));

            string type;
            if (enums && property.IsChoice && property.Options.Count > 0)
            {
                type = className + name + "?";
                newEnums.Add(Enum(className + name, property.Options, "", "\n", leadingBlankLine: false));
            }
            else
            {
                // A column type the rules do not know is left for the author to type.
                type = property.ClrType is null ? "object?" : TypeName(property, "", nullableEnabled: true);
            }

            lines.Add($"    public {type} {name} {{ get; set; }}");
        }

        lines.Add("}");
        foreach (var @enum in newEnums)
            lines.AddRange(["", @enum]);

        return string.Join("\n", lines) + "\n";
    }
}
