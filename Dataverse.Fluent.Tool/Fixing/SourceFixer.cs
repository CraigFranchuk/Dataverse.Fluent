using System.Text;
using Dataverse.Fluent.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Dataverse.Fluent.Tool.Fixing;

/// <summary>What happened to one finding's fix.</summary>
internal sealed record FixOutcome(SchemaFinding Finding, string? File, string? NotAppliedBecause)
{
    public bool Applied => NotAppliedBecause is null;
}

/// <summary>The edited files and the outcome of each fix.</summary>
internal sealed record FixPlan(IReadOnlyDictionary<string, SourceFile> Files, IReadOnlyList<FixOutcome> Outcomes)
{
    public IEnumerable<SourceFile> Changed => Files.Values.Where(f => f.Changes.Count > 0);
}

/// <summary>A source file, as read, and the edits planned for it.</summary>
internal sealed class SourceFile(string path, SourceText text, SyntaxTree tree)
{
    public string Path { get; } = path;

    public SourceText Text { get; } = text;

    public SyntaxTree Tree { get; } = tree;

    public List<TextChange> Changes { get; } = [];

    /// <summary>The file with the planned edits. Insertions at the same position keep the order they were planned in.</summary>
    public SourceText Edited()
    {
        var merged = Changes
            .Select((c, i) => (Change: c, Order: i))
            .GroupBy(c => c.Change.Span)
            .Select(g => new TextChange(g.Key, string.Concat(g.OrderBy(c => c.Order).Select(c => c.Change.NewText))))
            .OrderBy(c => c.Span.Start)
            .ThenBy(c => c.Span.Length)
            .ToList();
        return Text.WithChanges(merged);
    }

    /// <summary>
    /// Whether an edit of <paramref name="span"/> would collide with one already planned. Insertions at the same position
    /// do not collide (they are applied in order), nor does an insertion at either end of a replacement.
    /// </summary>
    public bool Overlaps(TextSpan span) => Changes.Any(c =>
        c.Span.Length > 0 && span.Length > 0 ? c.Span.OverlapsWith(span)
        : c.Span.Length > 0 ? c.Span.Start < span.Start && span.Start < c.Span.End
        : span.Length > 0 && span.Start < c.Span.Start && c.Span.Start < span.End);
}

/// <summary>
/// Turns <see cref="SchemaFix"/>es into edits of the models' attributes. Only string-literal arguments are changed and
/// new attributes go on their own line with the indentation of the line they follow, so a fix never reformats code.
/// </summary>
internal sealed class SourceFixer
{
    private const string DataverseNamespace = "Dataverse.Fluent";

    private readonly Dictionary<string, SourceFile> _files;
    private readonly bool _globalUsing;

    private SourceFixer(Dictionary<string, SourceFile> files)
    {
        _files = files;
        _globalUsing = files.Values.Any(f => f.Tree.GetCompilationUnitRoot().Usings
            .Any(u => u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword) && u.Alias is null && u.Name?.ToString() == DataverseNamespace));
    }

    public static SourceFixer Load(IEnumerable<string> paths)
    {
        var files = new Dictionary<string, SourceFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            SourceText text;
            using (var stream = File.OpenRead(path))
                text = SourceText.From(stream, throwIfBinaryDetected: true);
            files[path] = new SourceFile(path, text, CSharpSyntaxTree.ParseText(text, path: path));
        }

        return new SourceFixer(files);
    }

    public static SourceFixer FromSource(params (string Path, string Text)[] sources)
        => new(sources.ToDictionary(
            s => s.Path,
            s =>
            {
                var text = SourceText.From(s.Text, Encoding.UTF8);
                return new SourceFile(s.Path, text, CSharpSyntaxTree.ParseText(text, path: s.Path));
            },
            StringComparer.OrdinalIgnoreCase));

    public FixPlan Plan(IEnumerable<SchemaFinding> findings)
    {
        var outcomes = new List<FixOutcome>();
        foreach (var finding in findings.Where(f => f.Fix is not null))
        {
            var (file, reason) = Apply(finding);
            outcomes.Add(new FixOutcome(finding, file?.Path, reason));
        }

        return new FixPlan(_files, outcomes);
    }

    /// <summary>Writes each edited file in the encoding it was read in.</summary>
    public static void Write(FixPlan plan)
    {
        foreach (var file in plan.Changed)
        {
            var edited = file.Edited();
            var encoding = file.Text.Encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            using var writer = new StreamWriter(file.Path, append: false, encoding);
            edited.Write(writer);
        }
    }

    // ───────── Locating the model ─────────

    private (SourceFile? File, string? Reason) Apply(SchemaFinding finding)
    {
        var fix = finding.Fix!;
        var declarations = FindType(finding.Model);
        if (declarations.Count == 0)
            return (null, $"{finding.Model.Name} is not declared in the project's source.");

        if (fix.Kind == SchemaFixKind.SetEntitySet)
            return SetEntitySet(declarations, fix);

        if (finding.Property is null)
            return (null, "the finding names no property.");

        var found = declarations
            .SelectMany(d => d.Type.Members.OfType<PropertyDeclarationSyntax>().Select(p => (d.File, Property: p)))
            .FirstOrDefault(p => p.Property.Identifier.ValueText == finding.Property.Name);
        if (found.Property is null)
            return (null, $"{finding.Model.Name}.{finding.Property.Name} is not declared in the project's source.");

        var (file, property) = found;
        var reason = fix.Kind switch
        {
            SchemaFixKind.SetLookupTargetEntitySet => WithAttribute(file, property, "DataverseLookup", a => ReplaceStringArgument(file, a, 1, fix.EntitySet!)),
            SchemaFixKind.SetLookupNavigationProperty => WithAttribute(file, property, "DataverseLookup", a => SetNamedArgument(file, a, "NavigationProperty", fix.NavigationProperty!)),
            SchemaFixKind.AddLookupTarget => AddLookupTarget(file, property, fix),
            SchemaFixKind.SetLookupTarget => SetLookupTarget(file, property, fix),
            SchemaFixKind.SetReadOnly => SetReadOnly(file, property, fix.ReadOnlyFor),
            SchemaFixKind.MapAsLookup => MapAsLookup(file, property, fix),
            _ => $"the tool cannot apply a {fix.Kind} fix."
        };

        return (file, reason);
    }

    /// <summary>Every declaration of the type (several for a partial class), matched by namespace and nesting.</summary>
    private List<(SourceFile File, TypeDeclarationSyntax Type)> FindType(Type type)
    {
        var wanted = (type.FullName ?? type.Name).Replace('+', '.');
        var matches = new List<(SourceFile, TypeDeclarationSyntax)>();
        foreach (var file in _files.Values)
        {
            foreach (var declaration in file.Tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (declaration.Identifier.ValueText == type.Name && FullName(declaration) == wanted)
                    matches.Add((file, declaration));
            }
        }

        return matches;
    }

    private static string FullName(TypeDeclarationSyntax declaration)
    {
        var parts = new List<string> { declaration.Identifier.ValueText };
        for (var node = declaration.Parent; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case TypeDeclarationSyntax parent:
                    parts.Add(parent.Identifier.ValueText);
                    break;
                case BaseNamespaceDeclarationSyntax ns:
                    parts.Add(ns.Name.ToString());
                    break;
            }
        }

        parts.Reverse();
        return string.Join(".", parts);
    }

    // ───────── Fixes ─────────

    private static (SourceFile? File, string? Reason) SetEntitySet(List<(SourceFile File, TypeDeclarationSyntax Type)> declarations, SchemaFix fix)
    {
        foreach (var (file, type) in declarations)
        {
            if (FindAttribute(type.AttributeLists, "DataverseEntity") is { } entity)
                return (file, ReplaceStringArgument(file, entity, 1, fix.EntitySet!));
            if (FindAttribute(type.AttributeLists, "EntitySet") is { } entitySet)
                return (file, ReplaceStringArgument(file, entitySet, 0, fix.EntitySet!));
        }

        return (declarations[0].File, "the model has no [DataverseEntity] or [EntitySet] attribute in source (is it mapped by a resolver?).");
    }

    private static string? AddLookupTarget(SourceFile file, PropertyDeclarationSyntax property, SchemaFix fix)
    {
        var lookup = FindAttribute(property.AttributeLists, "DataverseLookup");
        if (lookup is null)
            return "the property has no [DataverseLookup] attribute in source.";

        // After the last target, or after the lookup when it has none yet.
        var anchor = property.AttributeLists.LastOrDefault(l => l.Attributes.Any(a => IsNamed(a, "DataverseLookupTarget")))
            ?? (AttributeListSyntax)lookup.Parent!;
        var text = $"{Qualifier(lookup)}DataverseLookupTarget({Literal(fix.TargetLogicalName!)}, {Literal(fix.EntitySet!)}, {Literal(fix.NavigationProperty!)})";
        return InsertLineAfter(file, anchor, $"[{text}]");
    }

    private static string? SetLookupTarget(SourceFile file, PropertyDeclarationSyntax property, SchemaFix fix)
    {
        var target = property.AttributeLists.SelectMany(l => l.Attributes)
            .Where(a => IsNamed(a, "DataverseLookupTarget"))
            .FirstOrDefault(a => StringArgument(a, 0) is { } name && string.Equals(name, fix.TargetLogicalName, StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return $"no [DataverseLookupTarget(\"{fix.TargetLogicalName}\", ...)] with a string literal table name was found.";
        if (StringArgument(target, 1) is null || StringArgument(target, 2) is null)
            return $"the entity set and navigation property of [{target.Name}(\"{fix.TargetLogicalName}\", ...)] are not both string literals.";

        // Both arguments are literals, so both edits apply (or neither, when they collide with another fix).
        return ReplaceStringArgument(file, target, 1, fix.EntitySet!) ?? ReplaceStringArgument(file, target, 2, fix.NavigationProperty!);
    }

    private string? SetReadOnly(SourceFile file, PropertyDeclarationSyntax property, DataverseOperation operations)
    {
        if (FindAttribute(property.AttributeLists, "DataverseReadOnly") is { } existing)
        {
            var value = $"{Qualifier(existing)}DataverseOperation.{OperationName(operations)}";
            if (existing.ArgumentList is { } arguments)
                return Change(file, arguments.Span, $"({value})");

            return Change(file, new TextSpan(existing.Span.End, 0), $"({value})");
        }

        var qualifier = QualifierFor(file, property);
        var attribute = $"[{qualifier}DataverseReadOnly({qualifier}DataverseOperation.{OperationName(operations)})]";
        return InsertLineBefore(file, property, attribute);
    }

    private string? MapAsLookup(SourceFile file, PropertyDeclarationSyntax property, SchemaFix fix)
    {
        var valueName = $"_{fix.LookupLogicalName}_value";
        var mapped = property.AttributeLists.SelectMany(l => l.Attributes)
            .FirstOrDefault(a => (IsNamed(a, "JsonPropertyName") || IsNamed(a, "DataverseColumn")) && StringArgument(a, 0) == valueName);
        if (mapped is null)
            return $"no [JsonPropertyName(\"{valueName}\")] or [DataverseColumn(\"{valueName}\")] with a string literal was found.";

        var navigation = fix.NavigationProperty == fix.LookupLogicalName ? "" : $", NavigationProperty = {Literal(fix.NavigationProperty!)}";
        var replacement = $"{QualifierFor(file, property)}DataverseLookup({Literal(fix.LookupLogicalName!)}, {Literal(fix.EntitySet!)}{navigation})";
        if (Change(file, mapped.Span, replacement) is { } conflict)
            return conflict;

        return fix.ReadOnlyFor == DataverseOperation.None ? null : SetReadOnly(file, property, fix.ReadOnlyFor);
    }

    private static string? WithAttribute(SourceFile file, PropertyDeclarationSyntax property, string name, Func<AttributeSyntax, string?> edit)
        => FindAttribute(property.AttributeLists, name) is { } attribute ? edit(attribute) : $"the property has no [{name}] attribute in source.";

    private static string? ReplaceStringArgument(SourceFile file, AttributeSyntax attribute, int position, string value)
    {
        var arguments = attribute.ArgumentList?.Arguments.Where(a => a.NameEquals is null && a.NameColon is null).ToList() ?? [];
        if (position >= arguments.Count)
            return $"[{attribute.Name}] has no argument {position + 1}.";
        if (arguments[position].Expression is not LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } literal)
            return $"argument {position + 1} of [{attribute.Name}] is not a string literal.";

        return Change(file, literal.Span, Literal(value));
    }

    private static string? SetNamedArgument(SourceFile file, AttributeSyntax attribute, string name, string value)
    {
        if (attribute.ArgumentList is not { } arguments)
            return $"[{attribute.Name}] has no argument list.";

        var named = arguments.Arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == name);
        if (named is null)
            return Change(file, new TextSpan(arguments.CloseParenToken.SpanStart, 0), $"{(arguments.Arguments.Count > 0 ? ", " : "")}{name} = {Literal(value)}");
        if (named.Expression is not LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } literal)
            return $"{name} of [{attribute.Name}] is not a string literal.";

        return Change(file, literal.Span, Literal(value));
    }

    // ───────── Text edits ─────────

    private static string? Change(SourceFile file, TextSpan span, string text)
    {
        if (file.Overlaps(span))
            return "it conflicts with another fix to the same code.";

        file.Changes.Add(new TextChange(span, text));
        return null;
    }

    /// <summary>
    /// Inserts <paramref name="code"/> on a new line after the line that holds <paramref name="node"/>, indented like it.
    /// When more code follows the node on its line (attributes written on the property's own line), the new line would
    /// land after that code, so <paramref name="code"/> goes right after the node instead.
    /// </summary>
    private static string? InsertLineAfter(SourceFile file, SyntaxNode node, string code)
    {
        var line = file.Text.Lines.GetLineFromPosition(node.Span.End);
        var next = node.GetLastToken().GetNextToken();
        if (!next.IsKind(SyntaxKind.None) && next.SpanStart < line.End)
            return Change(file, new TextSpan(node.Span.End, 0), " " + code);


        var newline = NewLine(file, line);
        if (line.EndIncludingLineBreak == line.End)
            return Change(file, new TextSpan(line.End, 0), newline + Indentation(file, node) + code);

        return Change(file, new TextSpan(line.EndIncludingLineBreak, 0), Indentation(file, node) + code + newline);
    }

    /// <summary>Inserts <paramref name="code"/> on a new line before the property's first attribute (or its declaration).</summary>
    private static string? InsertLineBefore(SourceFile file, PropertyDeclarationSyntax property, string code)
    {
        var first = property.GetFirstToken();
        var line = file.Text.Lines.GetLineFromPosition(first.SpanStart);
        return Change(file, new TextSpan(line.Start, 0), Indentation(file, first.Parent!) + code + NewLine(file, line));
    }

    private static string Indentation(SourceFile file, SyntaxNode node)
    {
        var line = file.Text.Lines.GetLineFromPosition(node.SpanStart);
        var text = file.Text.ToString(line.Span);
        return text[..(text.Length - text.TrimStart().Length)];
    }

    // The line break the file uses: the line's own, or the first line's when the line has none.
    private static string NewLine(SourceFile file, TextLine line)
    {
        if (line.EndIncludingLineBreak > line.End)
            return file.Text.ToString(TextSpan.FromBounds(line.End, line.EndIncludingLineBreak));

        var first = file.Text.Lines[0];
        return first.EndIncludingLineBreak > first.End ? file.Text.ToString(TextSpan.FromBounds(first.End, first.EndIncludingLineBreak)) : Environment.NewLine;
    }

    // ───────── Attributes ─────────

    private static AttributeSyntax? FindAttribute(SyntaxList<AttributeListSyntax> lists, string name)
        => lists.SelectMany(l => l.Attributes).FirstOrDefault(a => IsNamed(a, name));

    private static bool IsNamed(AttributeSyntax attribute, string name)
    {
        var simple = attribute.Name switch
        {
            QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
            SimpleNameSyntax s => s.Identifier.ValueText,
            _ => attribute.Name.ToString()
        };
        return simple == name || simple == name + "Attribute";
    }

    private static string? StringArgument(AttributeSyntax attribute, int position)
    {
        var arguments = attribute.ArgumentList?.Arguments.Where(a => a.NameEquals is null && a.NameColon is null).ToList() ?? [];
        return position < arguments.Count && arguments[position].Expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } literal
            ? literal.Token.ValueText
            : null;
    }

    /// <summary>The namespace qualifier an attribute is written with ("Dataverse.Fluent." or ""), to write new code the same way.</summary>
    private static string Qualifier(AttributeSyntax attribute)
        => attribute.Name is QualifiedNameSyntax q ? q.Left + "." : "";

    /// <summary>
    /// How to write a Dataverse.Fluent type in this property's file: like the Dataverse attributes already there, or
    /// unqualified when the namespace is imported, or fully qualified.
    /// </summary>
    private string QualifierFor(SourceFile file, PropertyDeclarationSyntax property)
    {
        var existing = property.AttributeLists.SelectMany(l => l.Attributes)
            .Concat(property.Ancestors().OfType<TypeDeclarationSyntax>().SelectMany(t => t.AttributeLists.SelectMany(l => l.Attributes)))
            .FirstOrDefault(a => a.Name.ToString().Contains("Dataverse", StringComparison.Ordinal) || IsNamed(a, "EntitySet"));
        if (existing is not null)
            return Qualifier(existing);

        var root = file.Tree.GetCompilationUnitRoot();
        var imported = _globalUsing
            || root.Usings.Concat(property.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().SelectMany(n => n.Usings)).Any(u => u.Alias is null && u.Name?.ToString() == DataverseNamespace)
            || property.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Any(n => n.Name.ToString() is var ns && (ns == DataverseNamespace || ns.StartsWith(DataverseNamespace + ".", StringComparison.Ordinal)));
        return imported ? "" : DataverseNamespace + ".";
    }

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    // CreateAndUpdate and All share a value, so ToString() may return either; the code base writes CreateAndUpdate.
    private static string OperationName(DataverseOperation operations) => operations switch
    {
        DataverseOperation.Create => nameof(DataverseOperation.Create),
        DataverseOperation.Update => nameof(DataverseOperation.Update),
        _ => nameof(DataverseOperation.CreateAndUpdate)
    };
}
