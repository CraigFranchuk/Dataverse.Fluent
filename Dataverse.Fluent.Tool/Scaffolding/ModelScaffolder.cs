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

/// <summary>What a scaffold run did to a project's models.</summary>
internal sealed record ScaffoldResult(IReadOnlyList<SourceFile> Files, IReadOnlyList<ScaffoldChange> Changes, IReadOnlyList<ScaffoldMiss> Misses, int Models)
{
    public IEnumerable<SourceFile> Changed => Files.Where(f => f.Changes.Count > 0);
}

/// <summary>
/// Fills in the models of a project from the environment's metadata. A model is a class with
/// <c>[DataverseEntity]</c> or <c>[EntitySet]</c>; it does not have to compile yet. Each property without a mapping
/// is matched to a column by name and gets its mapping attribute, its read-only marker and, when its type cannot hold
/// the column, the type that can. A property that is already mapped only has its type and read-only marker checked.
/// </summary>
internal sealed class ModelScaffolder(IDataverseTransport transport, bool nullableEnabled, bool enums)
{
    private readonly List<ScaffoldChange> _changes = [];
    private readonly List<ScaffoldMiss> _misses = [];

    public async Task<ScaffoldResult> ScaffoldAsync(IReadOnlyList<SourceFile> files, IReadOnlyList<string> typeNames, CancellationToken ct)
    {
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

        var declared = files
            .SelectMany(f => f.Tree.GetRoot(ct).DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            .Select(t => t.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (file, type) in models)
            await ScaffoldModelAsync(file, type, declared, ct).ConfigureAwait(false);

        return new ScaffoldResult(files, _changes, _misses, models.Count);
    }

    private static bool Matches(TypeDeclarationSyntax type, string name)
        => type.Identifier.ValueText == name || SourceFixer.FullName(type) == name;

    // The attribute that maps the type to a table, with the table's logical name or entity set.
    private static (AttributeSyntax Attribute, string? LogicalName, string? EntitySet)? Table(TypeDeclarationSyntax type)
    {
        if (SourceFixer.FindAttribute(type.AttributeLists, "DataverseEntity") is { } entity && SourceFixer.StringArgument(entity, 0) is { Length: > 0 } logicalName)
            return (entity, logicalName, SourceFixer.StringArgument(entity, 1));
        if (SourceFixer.FindAttribute(type.AttributeLists, "EntitySet") is { } set && SourceFixer.StringArgument(set, 0) is { Length: > 0 } entitySet)
            return (set, null, entitySet);
        return null;
    }

    private sealed record Candidate(PropertyDeclarationSyntax Property, string Name, bool Mapped, Type? Type);

    private async Task ScaffoldModelAsync(SourceFile file, TypeDeclarationSyntax type, HashSet<string> declared, CancellationToken ct)
    {
        var (attribute, logicalName, entitySet) = Table(type)!.Value;
        var candidates = type.Members.OfType<PropertyDeclarationSyntax>().Select(Classify).OfType<Candidate>().ToList();

        var model = await DataverseModelScaffolder.ScaffoldAsync(transport, new ScaffoldRequest
        {
            LogicalName = logicalName,
            EntitySet = logicalName is null ? entitySet : null,
            Properties = [.. candidates.Select(c => new ScaffoldProperty(c.Name, c.Type))],
            IncludeChoiceOptions = enums
        }, ct).ConfigureAwait(false);

        var className = type.Identifier.ValueText;
        if (!model.TableFound)
        {
            _misses.Add(new ScaffoldMiss(className, model.Message!, model.Suggestion));
            return;
        }

        if (logicalName is not null && !string.Equals(entitySet, model.EntitySet, StringComparison.Ordinal))
            SetEntitySet(file, attribute, className, model.EntitySet!);

        var qualifier = SourceFixer.Qualifier(attribute);
        var newEnums = new StringBuilder();
        for (var i = 0; i < candidates.Count; i++)
            ScaffoldProperty(file, type, candidates[i], model.Properties[i], qualifier, declared, newEnums);

        if (newEnums.Length > 0)
            SourceFixer.Change(file, new TextSpan(type.Span.End, 0), newEnums.ToString());
    }

    // The properties scaffolding looks at: public instance auto-properties that are not ignored and not already a
    // lookup (the fix command corrects those).
    private static Candidate? Classify(PropertyDeclarationSyntax property)
    {
        if (!property.Modifiers.Any(SyntaxKind.PublicKeyword) || property.Modifiers.Any(SyntaxKind.StaticKeyword) || property.AccessorList is null)
            return null;

        var attributes = property.AttributeLists.SelectMany(l => l.Attributes).ToList();
        if (attributes.Any(a => SourceFixer.IsNamed(a, "JsonIgnore") || SourceFixer.IsNamed(a, "DataverseLookup") || SourceFixer.IsNamed(a, "DataverseLookupTarget")))
            return null;

        var type = KnownType(property.Type);
        var mapping = attributes.FirstOrDefault(a => SourceFixer.IsNamed(a, "JsonPropertyName") || SourceFixer.IsNamed(a, "DataverseColumn"));
        if (mapping is null)
            return new Candidate(property, property.Identifier.ValueText, Mapped: false, type);

        // Mapped already: only a plain column name can be checked. Annotations, lookups by their value name and
        // navigation properties are left to validate.
        return SourceFixer.StringArgument(mapping, 0) is { Length: > 0 } name && !name.Contains('@', StringComparison.Ordinal) && !name.StartsWith('_')
            ? new Candidate(property, name, Mapped: true, type)
            : null;
    }

    private void SetEntitySet(SourceFile file, AttributeSyntax attribute, string className, string entitySet)
    {
        var arguments = attribute.ArgumentList!.Arguments;
        string? problem;
        if (arguments.Count == 1)
            problem = SourceFixer.Change(file, new TextSpan(arguments[0].Span.End, 0), $", {SourceFixer.Literal(entitySet)}");
        else if (arguments[1].Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            problem = SourceFixer.Change(file, literal.Span, SourceFixer.Literal(entitySet));
        else
            problem = "the entity set is not a string literal";

        if (problem is null)
            _changes.Add(new ScaffoldChange(file.Path, className, $"entity set \"{entitySet}\"", TypeChanged: false));
        else
            _misses.Add(new ScaffoldMiss(className, $"The entity set is \"{entitySet}\", but it could not be written: {problem}.", null));
    }

    private void ScaffoldProperty(SourceFile file, TypeDeclarationSyntax type, Candidate candidate, ScaffoldedProperty scaffolded,
        string qualifier, HashSet<string> declared, StringBuilder newEnums)
    {
        var property = candidate.Property;
        var location = $"{type.Identifier.ValueText}.{property.Identifier.ValueText}";
        if (scaffolded.Kind == ScaffoldedPropertyKind.NotFound)
        {
            // A mapped property that is not a column is a navigation property, or a mistake validate reports.
            if (!candidate.Mapped)
                _misses.Add(new ScaffoldMiss(location, scaffolded.Message!, scaffolded.Suggestion));
            return;
        }

        var lines = new List<string>();
        var notes = new List<string>();

        if (scaffolded.ReadOnlyFor != DataverseOperation.None && SourceFixer.FindAttribute(property.AttributeLists, "DataverseReadOnly") is null)
        {
            var operation = scaffolded.ReadOnlyFor == DataverseOperation.Update ? nameof(DataverseOperation.Update) : nameof(DataverseOperation.CreateAndUpdate);
            lines.Add($"[{qualifier}DataverseReadOnly({qualifier}DataverseOperation.{operation})]");
            notes.Add(scaffolded.ReadOnlyFor == DataverseOperation.Update ? "read-only on update" : "read-only");
        }

        if (!candidate.Mapped)
        {
            lines.AddRange(ModelText.MappingAttributes(scaffolded, qualifier));
            notes.Insert(0, scaffolded.Kind == ScaffoldedPropertyKind.Column
                ? $"mapped to '{scaffolded.LogicalName}'"
                : $"mapped to the lookup '{scaffolded.LogicalName}' ({string.Join(", ", scaffolded.Targets.Select(t => t.LogicalName))})");
        }

        var newType = NewType(file, type, candidate, scaffolded, qualifier, declared, newEnums);
        if (lines.Count == 0 && newType is null)
            return;

        // The edits of one property apply together or not at all.
        var before = file.Changes.Count;
        string? problem = null;
        if (lines.Count > 0)
        {
            var first = property.GetFirstToken();
            var line = file.Text.Lines.GetLineFromPosition(first.SpanStart);
            var indentation = SourceFixer.Indentation(file, first.Parent!);
            var newline = SourceFixer.NewLine(file, line);
            problem = SourceFixer.Change(file, new TextSpan(line.Start, 0), string.Concat(lines.Select(l => indentation + l + newline)));
        }

        if (problem is null && newType is not null)
        {
            problem = SourceFixer.Change(file, property.Type.Span, newType);
            notes.Add($"type {property.Type} -> {newType}");
        }

        if (problem is not null)
        {
            file.Changes.RemoveRange(before, file.Changes.Count - before);
            _misses.Add(new ScaffoldMiss(location, $"It could not be written: {problem}", null));
            return;
        }

        _changes.Add(new ScaffoldChange(file.Path, location, string.Join("; ", notes), newType is not null));
    }

    // The type to write in place of the property's, or null to keep it.
    private string? NewType(SourceFile file, TypeDeclarationSyntax type, Candidate candidate, ScaffoldedProperty scaffolded, string qualifier,
        HashSet<string> declared, StringBuilder newEnums)
    {
        // A type the tool does not know (an enum, a strongly typed ID) is the model's choice.
        if (candidate.Type is null || scaffolded.ClrType is null)
            return null;

        if (enums && scaffolded.IsChoice && scaffolded.Options.Count > 0)
        {
            var name = type.Identifier.ValueText + candidate.Property.Identifier.ValueText;
            if (declared.Add(name))
                newEnums.Append(ModelText.Enum(name, scaffolded.Options, SourceFixer.Indentation(file, type), SourceFixer.NewLine(file, file.Text.Lines[0]), leadingBlankLine: true));
            return name + "?";
        }

        return scaffolded.TypeFits == false ? ModelText.TypeName(scaffolded, qualifier, nullableEnabled) : null;
    }

    /// <summary>The CLR type a type name stands for, when it is one the type rules know; otherwise <see langword="null"/>.</summary>
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
    public static string TypeName(ScaffoldedProperty property, string qualifier, bool nullableEnabled)
    {
        var name = property.ClrType == nameof(DataverseReference) ? qualifier + property.ClrType : property.ClrType!;
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
