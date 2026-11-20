using Dataverse.Fluent.Tool.Fixing;
using Dataverse.Fluent.Tool.Scaffolding;
using Dataverse.Fluent.Tool.Tests.Fixtures;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Dataverse.Fluent.Tool.Tests;

/// <summary>
/// The library decides what each property of a model is from the compiled type; the scaffolder decides again from the
/// source. These tests run both over the same class, <see cref="ParityModel"/>, so that one cannot drift from the other.
/// </summary>
public class ParityTests
{
    private static readonly Dictionary<string, PropertyRole> Roles = ClassifySource();

    private static readonly Dictionary<string, DataverseMemberKind> Kinds =
        DataverseEntityMapping.For<ParityModel>().Members.ToDictionary(m => m.Property.Name, m => m.Kind);

    // The model's source, embedded next to its compiled type, classified the way scaffold does.
    private static Dictionary<string, PropertyRole> ClassifySource()
    {
        using var stream = typeof(ParityModel).Assembly.GetManifestResourceStream("Fixtures.ParityModel.cs")
            ?? throw new InvalidOperationException("Fixtures/ParityModel.cs is not embedded in the test assembly.");
        using var reader = new StreamReader(stream);
        var file = SourceFile.Parse("ParityModel.cs", reader.ReadToEnd());

        var classifier = new PropertyClassifier(ModelSemantics.Create([file], ModelSemantics.ImplicitUsings, nullableEnabled: true));
        return file.Tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.ValueText == nameof(ParityModel))
            .Members.OfType<PropertyDeclarationSyntax>()
            .ToDictionary(p => p.Identifier.ValueText, p => classifier.Classify(p).Role);
    }

    [Fact]
    public void EveryProperty_IsWhatTheLibrarySaysItIs()
    {
        Assert.NotEmpty(Roles);
        Assert.All(Roles, pair =>
        {
            var (name, role) = pair;
            PropertyRole[] expected = Kinds.TryGetValue(name, out var kind)
                ? kind switch
                {
                    DataverseMemberKind.Ignored => [PropertyRole.Ignored],
                    DataverseMemberKind.Lookup or DataverseMemberKind.PolymorphicLookup => [PropertyRole.Lookup],
                    // Related records are validate's to check.
                    DataverseMemberKind.Navigation => [PropertyRole.Left],
                    // A column is matched, checked, or (an annotation, a computed value) left to validate.
                    _ => [PropertyRole.Unmapped, PropertyRole.Mapped, PropertyRole.Left]
                }
                : [PropertyRole.None];

            Assert.True(expected.Contains(role), $"{name}: the library says {(Kinds.ContainsKey(name) ? kind : "it is not part of the model")}, scaffold says {role}.");
        });
    }

    [Fact]
    public void TheLibrarySeesNoProperty_ThatTheSourceDoesNotDeclare()
        => Assert.Empty(Kinds.Keys.Except(Roles.Keys));

    // Which of the three a column is: the library has one kind for them, so the fixture's own are pinned here.
    [Theory]
    [InlineData(nameof(ParityModel.Unmapped), nameof(PropertyRole.Unmapped))]
    [InlineData(nameof(ParityModel.Backed), nameof(PropertyRole.Unmapped))]
    [InlineData(nameof(ParityModel.GetOnly), nameof(PropertyRole.Unmapped))]
    [InlineData(nameof(ParityModel.InitOnly), nameof(PropertyRole.Unmapped))]
    [InlineData(nameof(ParityModel.Virtual), nameof(PropertyRole.Unmapped))]
    [InlineData(nameof(ParityModel.PrivateSetter), nameof(PropertyRole.Unmapped))]
    [InlineData(nameof(ParityModel.IgnoredWhenNull), nameof(PropertyRole.Unmapped))]
    [InlineData(nameof(ParityModel.NeverIgnored), nameof(PropertyRole.Unmapped))]
    [InlineData(nameof(ParityModel.IgnoredWhenNullByConstant), nameof(PropertyRole.Unmapped))]
    [InlineData(nameof(ParityModel.MappedByJson), nameof(PropertyRole.Mapped))]
    [InlineData(nameof(ParityModel.MappedByColumn), nameof(PropertyRole.Mapped))]
    [InlineData(nameof(ParityModel.MappedByConstant), nameof(PropertyRole.Mapped))]
    [InlineData(nameof(ParityModel.LookupValue), nameof(PropertyRole.Left))]
    [InlineData(nameof(ParityModel.Annotation), nameof(PropertyRole.Left))]
    [InlineData(nameof(ParityModel.Computed), nameof(PropertyRole.Left))]
    [InlineData(nameof(ParityModel.ComputedWithBody), nameof(PropertyRole.Left))]
    public void AColumn_IsMatched_Checked_OrLeft(string property, string role)
    {
        Assert.Equal(DataverseMemberKind.Column, Kinds[property]);
        Assert.Equal(role, Roles[property].ToString());
    }
}
