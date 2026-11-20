using Dataverse.Fluent.Tool.Fixing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Dataverse.Fluent.Tool.Scaffolding;

/// <summary>Whether a type name can be given to a new type where a model is declared.</summary>
internal enum TypeNameState
{
    /// <summary>Nothing has the name.</summary>
    Free,

    /// <summary>An enum has the name.</summary>
    Enum,

    /// <summary>Another kind of type has the name.</summary>
    Taken
}

/// <summary>
/// What the compiler knows about a project's source: which attribute a name stands for, the value of a constant, the
/// type behind a type name, and what a name resolves to where a model is declared.
/// </summary>
/// <remarks>
/// The source is compiled against the framework the tool runs on and the tool's own Dataverse.Fluent, so nothing is
/// restored or built, and the project's packages are not there. A name that does not resolve (a package's type, or code
/// that does not compile yet) is answered from the syntax instead, as it is written.
/// </remarks>
internal sealed class ModelSemantics
{
    /// <summary>The namespaces a project with implicit usings imports in every file.</summary>
    public static readonly IReadOnlyList<string> ImplicitUsings =
        ["System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Threading", "System.Threading.Tasks"];

    private const string DataverseNamespace = "Dataverse.Fluent";
    private const string JsonNamespace = "System.Text.Json.Serialization";

    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(LoadReferences);

    private readonly CSharpCompilation _compilation;
    private readonly Dictionary<SyntaxTree, SemanticModel> _models = [];

    private ModelSemantics(CSharpCompilation compilation) => _compilation = compilation;

    /// <summary>Compiles the files, with the project's global usings (<c>System</c>, <c>static System.Math</c>, <c>Json = System.Text.Json</c>).</summary>
    public static ModelSemantics Create(IReadOnlyList<SourceFile> files, IReadOnlyList<string> globalUsings, bool nullableEnabled)
    {
        var trees = files.Select(f => f.Tree).Distinct().ToList();
        if (globalUsings.Count > 0)
            trees.Add(CSharpSyntaxTree.ParseText(string.Concat(globalUsings.Select(u => $"global using {u};\n"))));

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: nullableEnabled ? NullableContextOptions.Enable : NullableContextOptions.Disable);
        return new ModelSemantics(CSharpCompilation.Create("scaffold", trees, References.Value, options));
    }

    // The framework's assemblies and Dataverse.Fluent, from the assemblies the tool itself runs on.
    private static IReadOnlyList<MetadataReference> LoadReferences()
    {
        var platform = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => Path.GetFileNameWithoutExtension(p) is var name
                && (name.StartsWith("System.", StringComparison.Ordinal) || name is "System" or "mscorlib" or "netstandard"));

        return [.. platform
            .Append(typeof(DataverseReference).Assembly.Location)
            .Where(p => p.Length > 0 && File.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => MetadataReference.CreateFromFile(p))];
    }

    private SemanticModel Model(SyntaxNode node)
    {
        if (!_models.TryGetValue(node.SyntaxTree, out var model))
            _models[node.SyntaxTree] = model = _compilation.GetSemanticModel(node.SyntaxTree);
        return model;
    }

    // ───────── Attributes and constants ─────────

    /// <summary>
    /// Whether the attribute is the Dataverse.Fluent or System.Text.Json attribute called <paramref name="name"/>
    /// ("DataverseColumn", "JsonIgnore"): by its type when that resolves, by its name as written otherwise.
    /// </summary>
    public bool IsAttribute(AttributeSyntax attribute, string name)
    {
        var model = Model(attribute);
        var symbol = model.GetSymbolInfo(attribute);
        var type = (symbol.Symbol ?? symbol.CandidateSymbols.FirstOrDefault())?.ContainingType ?? model.GetTypeInfo(attribute).Type as INamedTypeSymbol;
        if (type is null || type.TypeKind == TypeKind.Error)
            return SourceFixer.IsNamed(attribute, name);

        var @namespace = name.StartsWith("Json", StringComparison.Ordinal) ? JsonNamespace : DataverseNamespace;
        return type.Name == name + "Attribute" && type.ContainingNamespace.ToDisplayString() == @namespace;
    }

    /// <summary>The first attribute that <see cref="IsAttribute"/> called <paramref name="name"/>.</summary>
    public AttributeSyntax? FindAttribute(SyntaxList<AttributeListSyntax> lists, string name)
        => lists.SelectMany(l => l.Attributes).FirstOrDefault(a => IsAttribute(a, name));

    /// <summary>The string an expression stands for: a literal, a constant, or a <c>nameof</c>.</summary>
    public string? String(ExpressionSyntax expression)
        => Model(expression).GetConstantValue(expression) is { HasValue: true, Value: string value } ? value
            : expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression) ? literal.Token.ValueText
            : null;

    /// <summary>The number an expression stands for, such as an enum member.</summary>
    public int? Int32(ExpressionSyntax expression)
        => Model(expression).GetConstantValue(expression) is { HasValue: true, Value: int value } ? value : null;

    /// <summary>The string given as the attribute's positional argument <paramref name="position"/>.</summary>
    public string? StringArgument(AttributeSyntax attribute, int position)
    {
        var arguments = attribute.ArgumentList?.Arguments.Where(a => a.NameEquals is null && a.NameColon is null).ToList() ?? [];
        return position < arguments.Count ? String(arguments[position].Expression) : null;
    }

    // ───────── Types ─────────

    /// <summary>The CLR type a property is declared with, when it is one the type rules know; otherwise <see langword="null"/>.</summary>
    public Type? KnownType(TypeSyntax syntax)
    {
        var symbol = Model(syntax).GetTypeInfo(syntax).Type;
        if (symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            symbol = nullable.TypeArguments[0];

        if (symbol is IArrayTypeSymbol array)
            return array is { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte } ? typeof(byte[]) : null;

        // Not resolved: the code does not compile yet, or the namespace is not imported. The name is what there is.
        if (symbol is null || symbol.TypeKind == TypeKind.Error)
            return ModelScaffolder.KnownType(syntax);

        return symbol.SpecialType switch
        {
            SpecialType.System_String => typeof(string),
            SpecialType.System_Boolean => typeof(bool),
            SpecialType.System_Int32 => typeof(int),
            SpecialType.System_Int64 => typeof(long),
            SpecialType.System_Int16 => typeof(short),
            SpecialType.System_Byte => typeof(byte),
            SpecialType.System_Decimal => typeof(decimal),
            SpecialType.System_Double => typeof(double),
            SpecialType.System_Single => typeof(float),
            SpecialType.System_DateTime => typeof(DateTime),
            _ => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) switch
            {
                "System.Guid" => typeof(Guid),
                "System.DateTimeOffset" => typeof(DateTimeOffset),
                "System.DateOnly" => typeof(DateOnly),
                "Dataverse.Fluent.DataverseReference" => typeof(DataverseReference),
                _ => null
            }
        };
    }

    /// <summary>
    /// Whether a property holds related records: its type, or the element type of its collection, is a class mapped
    /// with <c>[DataverseEntity]</c> or <c>[EntitySet]</c>. A type mapped by a resolver cannot be told from here.
    /// </summary>
    public bool IsNavigation(TypeSyntax syntax)
    {
        var symbol = Model(syntax).GetTypeInfo(syntax).Type;
        var element = symbol switch
        {
            IArrayTypeSymbol array => array.ElementType,
            INamedTypeSymbol { SpecialType: SpecialType.None } named
                when named.AllInterfaces.Append(named).FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T) is { } enumerable
                => enumerable.TypeArguments[0],
            _ => symbol
        };

        return element is INamedTypeSymbol { TypeKind: TypeKind.Class } record
            && record.GetAttributes().Any(a => a.AttributeClass?.Name is "DataverseEntityAttribute" or "DataverseEntity" or "EntitySetAttribute" or "EntitySet");
    }

    // ───────── Names where a model is declared ─────────

    /// <summary>"System." when a type of the System namespace does not resolve by its name alone at <paramref name="at"/>; otherwise "".</summary>
    public string SystemQualifier(SyntaxNode at, string typeName)
        => Model(at).LookupNamespacesAndTypes(at.SpanStart, name: typeName)
            .Any(s => s is INamedTypeSymbol && s.ContainingNamespace?.ToDisplayString() == "System") ? "" : "System.";

    /// <summary>What <paramref name="name"/> resolves to at <paramref name="at"/>, in the source or in what it imports.</summary>
    public TypeNameState TypeNameState(SyntaxNode at, string name)
    {
        var symbols = Model(at).LookupNamespacesAndTypes(at.SpanStart, name: name);
        return symbols.IsEmpty ? Scaffolding.TypeNameState.Free
            : symbols.Any(s => s is INamedTypeSymbol { TypeKind: TypeKind.Enum }) ? Scaffolding.TypeNameState.Enum
            : Scaffolding.TypeNameState.Taken;
    }
}

/// <summary>What scaffolding does with a property of a model.</summary>
internal enum PropertyRole
{
    /// <summary>Not part of the model: not public, static, or without a getter.</summary>
    None,

    /// <summary>Excluded with <c>[JsonIgnore]</c>.</summary>
    Ignored,

    /// <summary>Mapped with <c>[DataverseLookup]</c>: the <c>fix</c> command corrects those.</summary>
    Lookup,

    /// <summary>Not mapped yet: matched to a column by its name.</summary>
    Unmapped,

    /// <summary>Mapped to a plain column name: its type and read-only marker are checked.</summary>
    Mapped,

    /// <summary>Part of the model, but not scaffolding's to change: <c>validate</c> checks it.</summary>
    Left
}

/// <summary>A property and what scaffolding does with it.</summary>
/// <param name="Property">The property.</param>
/// <param name="Role">What scaffolding does with it.</param>
/// <param name="Name">The name to look up, for <see cref="PropertyRole.Unmapped"/> and <see cref="PropertyRole.Mapped"/>.</param>
/// <param name="Type">The property's type when the type rules know it.</param>
/// <param name="Reason">Why a <see cref="PropertyRole.Left"/> property is worth telling the user about; <see langword="null"/>
/// when leaving it is the ordinary case (a navigation property, an annotation).</param>
internal sealed record ClassifiedProperty(PropertyDeclarationSyntax Property, PropertyRole Role, string? Name = null, Type? Type = null, string? Reason = null);

/// <summary>
/// Sorts a model's properties the way the library does (<c>DataverseEntityMapping</c>), from source. The parity test
/// holds the two together.
/// </summary>
internal sealed class PropertyClassifier(ModelSemantics semantics)
{
    public ClassifiedProperty Classify(PropertyDeclarationSyntax property)
    {
        var accessors = property.AccessorList?.Accessors ?? default;
        var hasGetter = property.AccessorList is null || accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
        if (!property.Modifiers.Any(SyntaxKind.PublicKeyword) || property.Modifiers.Any(SyntaxKind.StaticKeyword) || !hasGetter)
            return new ClassifiedProperty(property, PropertyRole.None);

        var attributes = property.AttributeLists.SelectMany(l => l.Attributes).ToList();
        foreach (var ignore in attributes.Where(a => semantics.IsAttribute(a, "JsonIgnore")))
        {
            switch (IsAlways(ignore))
            {
                case true:
                    return new ClassifiedProperty(property, PropertyRole.Ignored);
                case null:
                    return new ClassifiedProperty(property, PropertyRole.Left, Reason: "its [JsonIgnore] condition could not be read");
            }
        }

        if (attributes.Any(a => semantics.IsAttribute(a, "DataverseLookup") || semantics.IsAttribute(a, "DataverseLookupTarget")))
            return new ClassifiedProperty(property, PropertyRole.Lookup);

        // An expression body, or a getter with a body and nothing to set the value: there is no column behind it.
        var computed = property.AccessorList is null
            || accessors.All(a => a.IsKind(SyntaxKind.GetAccessorDeclaration) && (a.Body is not null || a.ExpressionBody is not null));
        if (computed)
            return new ClassifiedProperty(property, PropertyRole.Left, Reason: "it is computed, and the client still sends it as a column unless it has [JsonIgnore]");

        if (semantics.IsNavigation(property.Type))
            return new ClassifiedProperty(property, PropertyRole.Left);

        var type = semantics.KnownType(property.Type);
        var mapping = attributes.FirstOrDefault(a => semantics.IsAttribute(a, "JsonPropertyName") || semantics.IsAttribute(a, "DataverseColumn"));
        if (mapping is null)
            return new ClassifiedProperty(property, PropertyRole.Unmapped, property.Identifier.ValueText, type);

        if (semantics.StringArgument(mapping, 0) is not { Length: > 0 } name)
            return new ClassifiedProperty(property, PropertyRole.Left, Reason: $"the name in its [{mapping.Name}] could not be read");

        // Annotations and lookups mapped by their value name are validate's to check.
        return name.Contains('@', StringComparison.Ordinal) || name.StartsWith('_')
            ? new ClassifiedProperty(property, PropertyRole.Left)
            : new ClassifiedProperty(property, PropertyRole.Mapped, name, type);
    }

    // [JsonIgnore] takes the property out of the model only when it always applies, as the library reads it: a
    // conditional one (WhenWritingNull, ...) is still a column. Null when the condition cannot be read.
    private bool? IsAlways(AttributeSyntax ignore)
    {
        var condition = ignore.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == "Condition");
        if (condition is null)
            return true;

        if (semantics.Int32(condition.Expression) is { } value)
            return value == (int)System.Text.Json.Serialization.JsonIgnoreCondition.Always;

        var written = condition.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null
        };
        return written switch
        {
            "Always" => true,
            "Never" or "WhenWritingDefault" or "WhenWritingNull" or "WhenWriting" or "WhenReading" => false,
            _ => null
        };
    }
}
