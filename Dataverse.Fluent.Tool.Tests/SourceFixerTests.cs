using System.Text;
using Dataverse.Fluent.Schema;
using Dataverse.Fluent.Tool.Fixing;
using Dataverse.Fluent.Tool.Tests.Fixtures;

namespace Dataverse.Fluent.Tool.Tests.Fixtures
{
    // Stand-ins for the models in the source the tests edit: the fixer finds a model's declaration by its full name.
    public class Incident
    {
        public Guid? IncidentId { get; set; }
        public string? TicketNumber { get; set; }
        public DataverseReference? CustomerId { get; set; }
        public Guid? RegionId { get; set; }
        public Guid? PrimaryContactId { get; set; }
        public Guid? SubjectId { get; set; }
        public DateTime? CreatedOn { get; set; }
    }

    public class Outer
    {
        public class Inner
        {
            public string? Name { get; set; }
        }
    }

    public class Split
    {
        public string? First { get; set; }
        public string? Second { get; set; }
    }

    public class Unmapped
    {
        public string? Name { get; set; }
    }
}

namespace Elsewhere
{
    public class Plain
    {
        public string? Name { get; set; }
    }
}

namespace Dataverse.Fluent.Tool.Tests
{
    /// <summary>The fixer turns each kind of <see cref="SchemaFix"/> into a minimal edit of the model's attributes.</summary>
    public class SourceFixerTests
    {
        private const string Path = "Incident.cs";

        private static SchemaFinding Finding<T>(string? property, SchemaFix fix)
            => new("DVS000", SchemaSeverity.Warning, typeof(T), property is null ? null : typeof(T).GetProperty(property), "m", fix: fix);

        private static (string Text, FixPlan Plan) Fix(string source, params SchemaFinding[] findings)
        {
            var fixer = SourceFixer.FromSource((Path, source));
            var plan = fixer.Plan(findings);
            return (plan.Files[Path].Edited().ToString(), plan);
        }

        // Raw strings take the line endings of this file, which depend on the checkout; the tests use LF.
        private static string N(string text) => text.ReplaceLineEndings("\n");

        private static readonly string Model = N(ModelSource);

        private const string ModelSource = """
            using System.Text.Json.Serialization;
            using Dataverse.Fluent;

            namespace Dataverse.Fluent.Tool.Tests.Fixtures;

            [DataverseEntity("incident", "incident")]
            public class Incident
            {
                /// <summary>The ID.</summary>
                [JsonPropertyName("incidentid")]
                public Guid? IncidentId { get; set; }

                [DataverseReadOnly]
                [JsonPropertyName("ticketnumber")]
                public string? TicketNumber { get; set; }

                [DataverseLookup("customerid")]
                [DataverseLookupTarget("account", "account", "customerid")]
                public DataverseReference? CustomerId { get; set; }

                [DataverseLookup("new_regionid", "new_region")]
                public Guid? RegionId { get; set; }

                [DataverseLookup("primarycontactid", Sets.Contacts, NavigationProperty = "PrimaryContactId")]
                public Guid? PrimaryContactId { get; set; }

                [JsonPropertyName("_subjectid_value")]
                public Guid? SubjectId { get; set; }

                public DateTime? CreatedOn { get; set; }
            }
            """;

        [Fact]
        public void SetReadOnly_AddsTheMarker_AboveTheFirstAttribute_AfterTheDocComment()
        {
            var (text, plan) = Fix(Model, Finding<Incident>(nameof(Incident.IncidentId), SchemaFix.SetReadOnly(DataverseOperation.Update)));

            Assert.Contains(N("""
                    /// <summary>The ID.</summary>
                    [DataverseReadOnly(DataverseOperation.Update)]
                    [JsonPropertyName("incidentid")]
                    public Guid? IncidentId { get; set; }
                """), text);
            Assert.True(Assert.Single(plan.Outcomes).Applied);
        }

        [Fact]
        public void SetReadOnly_ChangesAnExistingMarker()
        {
            var (text, _) = Fix(Model, Finding<Incident>(nameof(Incident.TicketNumber), SchemaFix.SetReadOnly(DataverseOperation.CreateAndUpdate)));

            Assert.Contains("    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]\n    [JsonPropertyName(\"ticketnumber\")]", text);
        }

        [Fact]
        public void SetReadOnly_OnAPropertyWithNoAttributes_GoesAboveIt()
        {
            var (text, _) = Fix(Model, Finding<Incident>(nameof(Incident.CreatedOn), SchemaFix.SetReadOnly(DataverseOperation.CreateAndUpdate)));

            Assert.Contains("    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]\n    public DateTime? CreatedOn", text);
        }

        [Fact]
        public void SetEntitySet_ChangesTheEntityAttribute()
        {
            var (text, _) = Fix(Model, Finding<Incident>(null, SchemaFix.SetEntitySet("incidents")));

            Assert.Contains("[DataverseEntity(\"incident\", \"incidents\")]", text);
        }

        [Fact]
        public void LookupFixes_ChangeOnlyTheLiterals()
        {
            var (text, _) = Fix(Model,
                Finding<Incident>(nameof(Incident.RegionId), SchemaFix.SetLookupTargetEntitySet("new_regions")),
                Finding<Incident>(nameof(Incident.RegionId), SchemaFix.SetLookupNavigationProperty("new_RegionId")));

            Assert.Contains("[DataverseLookup(\"new_regionid\", \"new_regions\", NavigationProperty = \"new_RegionId\")]", text);
        }

        [Fact]
        public void SetLookupNavigationProperty_ReplacesAnExistingValue()
        {
            var (text, _) = Fix(Model, Finding<Incident>(nameof(Incident.PrimaryContactId), SchemaFix.SetLookupNavigationProperty("primarycontactid")));

            Assert.Contains("NavigationProperty = \"primarycontactid\")]", text);
        }

        [Fact]
        public void AnArgumentThatIsNotALiteral_IsLeftForAManualChange()
        {
            var (text, plan) = Fix(Model, Finding<Incident>(nameof(Incident.PrimaryContactId), SchemaFix.SetLookupTargetEntitySet("contacts")));

            Assert.Equal(Model, text);
            Assert.Equal("argument 2 of [DataverseLookup] is not a string literal.", Assert.Single(plan.Outcomes).NotAppliedBecause);
        }

        [Fact]
        public void AddLookupTarget_GoesAfterTheLastTarget_InTheOrderTheFixesCame()
        {
            var (text, _) = Fix(Model,
                Finding<Incident>(nameof(Incident.CustomerId), SchemaFix.AddLookupTarget("contact", "contacts", "customerid_contact")),
                Finding<Incident>(nameof(Incident.CustomerId), SchemaFix.AddLookupTarget("lead", "leads", "customerid_lead")));

            Assert.Contains(N("""
                    [DataverseLookupTarget("account", "account", "customerid")]
                    [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
                    [DataverseLookupTarget("lead", "leads", "customerid_lead")]
                    public DataverseReference? CustomerId { get; set; }
                """), text);
        }

        [Fact]
        public void AddLookupTarget_StaysOnTheProperty_WhenItsAttributesShareItsLine()
        {
            var source = Model.Replace(
                "[DataverseLookup(\"customerid\")]\n    [DataverseLookupTarget(\"account\", \"account\", \"customerid\")]\n    public",
                "[DataverseLookup(\"customerid\"), DataverseLookupTarget(\"account\", \"account\", \"customerid\")] public",
                StringComparison.Ordinal);
            Assert.NotEqual(Model, source);

            var (text, plan) = Fix(source,
                Finding<Incident>(nameof(Incident.CustomerId), SchemaFix.AddLookupTarget("contact", "contacts", "customerid_contact")),
                Finding<Incident>(nameof(Incident.CustomerId), SchemaFix.AddLookupTarget("lead", "leads", "customerid_lead")));

            Assert.All(plan.Outcomes, o => Assert.True(o.Applied));
            Assert.Contains(
                "    [DataverseLookup(\"customerid\"), DataverseLookupTarget(\"account\", \"account\", \"customerid\")]"
                + " [DataverseLookupTarget(\"contact\", \"contacts\", \"customerid_contact\")]"
                + " [DataverseLookupTarget(\"lead\", \"leads\", \"customerid_lead\")]"
                + " public DataverseReference? CustomerId { get; set; }\n\n    [DataverseLookup(\"new_regionid\"",
                text);
        }

        [Fact]
        public void SetLookupTarget_ReplacesTheTargetsEntitySetAndNavigationProperty()
        {
            var (text, _) = Fix(Model, Finding<Incident>(nameof(Incident.CustomerId), SchemaFix.SetLookupTarget("account", "accounts", "customerid_account")));

            Assert.Contains("[DataverseLookupTarget(\"account\", \"accounts\", \"customerid_account\")]", text);
        }

        [Fact]
        public void SetLookupTarget_AndAddLookupTarget_OnOneProperty_BothApply()
        {
            var (text, plan) = Fix(Model,
                Finding<Incident>(nameof(Incident.CustomerId), SchemaFix.SetLookupTarget("account", "accounts", "customerid_account")),
                Finding<Incident>(nameof(Incident.CustomerId), SchemaFix.AddLookupTarget("contact", "contacts", "customerid_contact")));

            Assert.All(plan.Outcomes, o => Assert.True(o.Applied));
            Assert.Contains(N("""
                    [DataverseLookupTarget("account", "accounts", "customerid_account")]
                    [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
                """), text);
        }

        [Fact]
        public void MapAsLookup_ReplacesTheValueNameMapping()
        {
            var (text, _) = Fix(Model, Finding<Incident>(nameof(Incident.SubjectId), SchemaFix.MapAsLookup("subjectid", "subjects", "subjectid")));

            Assert.Contains("    [DataverseLookup(\"subjectid\", \"subjects\")]\n    public Guid? SubjectId", text);
            Assert.DoesNotContain("_subjectid_value", text);
        }

        [Fact]
        public void MapAsLookup_OfALookupThatCannotBeUpdated_AlsoMarksItReadOnly()
        {
            var (text, plan) = Fix(Model, Finding<Incident>(nameof(Incident.SubjectId),
                SchemaFix.MapAsLookup("subjectid", "subjects", "subjectid", DataverseOperation.Update)));

            Assert.True(Assert.Single(plan.Outcomes).Applied);
            Assert.Contains(
                "    [DataverseReadOnly(DataverseOperation.Update)]\n    [DataverseLookup(\"subjectid\", \"subjects\")]\n    public Guid? SubjectId",
                text);
        }

        [Fact]
        public void ConflictingFixes_ApplyTheFirst()
        {
            var (text, plan) = Fix(Model,
                Finding<Incident>(nameof(Incident.RegionId), SchemaFix.SetLookupTargetEntitySet("new_regions")),
                Finding<Incident>(nameof(Incident.RegionId), SchemaFix.SetLookupTargetEntitySet("regions")));

            Assert.Contains("\"new_regions\"", text);
            Assert.Equal("it conflicts with another fix to the same code.", plan.Outcomes[1].NotAppliedBecause);
        }

        [Fact]
        public void AModelNotInTheSource_IsReported()
        {
            var (_, plan) = Fix(Model, Finding<Unmapped>(nameof(Unmapped.Name), SchemaFix.SetReadOnly(DataverseOperation.Update)));

            Assert.Equal("Unmapped is not declared in the project's source.", Assert.Single(plan.Outcomes).NotAppliedBecause);
        }

        [Fact]
        public void NestedTypes_InBlockNamespaces_AreFound()
        {
            const string source = """
                namespace Dataverse.Fluent.Tool.Tests
                {
                    namespace Fixtures
                    {
                        public class Outer
                        {
                            public class Inner
                            {
                                [System.Text.Json.Serialization.JsonPropertyName("name")]
                                public string? Name { get; set; }
                            }
                        }
                    }
                }
                """;

            var (text, _) = Fix(N(source), Finding<Outer.Inner>(nameof(Outer.Inner.Name), SchemaFix.SetReadOnly(DataverseOperation.Create)));

            // The namespace is inside Dataverse.Fluent, so the attribute resolves unqualified.
            Assert.Contains(N("""
                                [DataverseReadOnly(DataverseOperation.Create)]
                                [System.Text.Json.Serialization.JsonPropertyName("name")]
                """), text);
        }

        [Fact]
        public void OutsideTheNamespace_WithoutAUsing_NewCodeIsFullyQualified()
        {
            const string source = """
                namespace Elsewhere;

                public class Plain
                {
                    [System.Text.Json.Serialization.JsonPropertyName("name")]
                    public string? Name { get; set; }
                }
                """;

            var (text, _) = Fix(N(source), Finding<Elsewhere.Plain>(nameof(Elsewhere.Plain.Name), SchemaFix.SetReadOnly(DataverseOperation.Create)));

            Assert.Contains("    [Dataverse.Fluent.DataverseReadOnly(Dataverse.Fluent.DataverseOperation.Create)]\n    [System.Text.Json", text);
        }

        [Fact]
        public void SetReadOnly_GoesRightBeforeTheProperty_WhenItSharesItsLine()
        {
            const string source = "namespace Elsewhere;\npublic class Plain { public string? Name { get; set; } }\n";

            var (text, _) = Fix(source, Finding<Elsewhere.Plain>(nameof(Elsewhere.Plain.Name), SchemaFix.SetReadOnly(DataverseOperation.Create)));

            Assert.Contains("public class Plain { [Dataverse.Fluent.DataverseReadOnly(Dataverse.Fluent.DataverseOperation.Create)] public string? Name { get; set; } }", text);
        }

        [Fact]
        public void AnEditThatBreaksTheSource_OrMissesItsProperty_IsCaughtBeforeItIsWritten()
        {
            const string source = "public class Plain\n{\n    public string? Name { get; set; }\n}\n";
            SourceFile File() => SourceFile.Parse("Plain.cs", source);
            static Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax Name(SourceFile file)
                => file.Tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax>().Single();

            // The edit the fixer plans holds.
            var good = File();
            Assert.Null(SourceFixer.InsertLinesBefore(good, Name(good), ["[Dataverse.Fluent.DataverseReadOnly]"]));
            Assert.Null(good.Verify());

            // The attribute lands on the class instead of the property.
            var misplaced = File();
            misplaced.Changes.Add(new Microsoft.CodeAnalysis.Text.TextChange(new Microsoft.CodeAnalysis.Text.TextSpan(0, 0), "[DataverseReadOnly]\n"));
            misplaced.Expect(Name(misplaced), attribute: "DataverseReadOnly");
            Assert.Equal("[DataverseReadOnly] would not be on Plain.Name", misplaced.Verify());

            // The type is not the one planned.
            var retyped = File();
            retyped.Expect(Name(retyped), type: "int?");
            Assert.Equal("Plain.Name would not be a int?", retyped.Verify());

            // The edit leaves a file that does not parse.
            var broken = File();
            broken.Changes.Add(new Microsoft.CodeAnalysis.Text.TextChange(Name(broken).Type.Span, "string? ["));
            Assert.StartsWith("it would not parse (line 3: ", broken.Verify());

            // A file that did not parse before is not held to more than it was.
            var unfinished = SourceFile.Parse("Plain.cs", source + "public class");
            Assert.Null(SourceFixer.InsertLinesBefore(unfinished, Name(unfinished), ["[DataverseReadOnly]"]));
            Assert.Null(unfinished.Verify());
        }

        [Fact]
        public void APartialClass_IsEditedWhereThePropertyIsDeclared()
        {
            var fixer = SourceFixer.FromSource(
                ("A.cs", "using Dataverse.Fluent;\nnamespace Dataverse.Fluent.Tool.Tests.Fixtures;\n[EntitySet(\"splits\")]\npublic partial class Split\n{\n    public string? First { get; set; }\n}\n"),
                ("B.cs", "namespace Dataverse.Fluent.Tool.Tests.Fixtures;\npublic partial class Split\n{\n    public string? Second { get; set; }\n}\n"));

            var plan = fixer.Plan([Finding<Split>(nameof(Split.Second), SchemaFix.SetReadOnly(DataverseOperation.Update))]);

            Assert.Empty(plan.Files["A.cs"].Changes);
            // The class's [EntitySet] is unqualified, so the new attribute is too.
            Assert.Contains("    [DataverseReadOnly(DataverseOperation.Update)]\n    public string? Second", plan.Files["B.cs"].Edited().ToString());
        }

        [Fact]
        public void QualifiedAttributes_AreCopied()
        {
            const string source = """
                namespace Dataverse.Fluent.Tool.Tests.Fixtures;
                public class Incident
                {
                    [global::Dataverse.Fluent.DataverseLookup("customerid")]
                    [global::Dataverse.Fluent.DataverseLookupTarget("account", "accounts", "customerid_account")]
                    public object? CustomerId { get; set; }
                }
                """;

            var (text, _) = Fix(N(source),
                Finding<Incident>(nameof(Incident.CustomerId), SchemaFix.AddLookupTarget("contact", "contacts", "customerid_contact")),
                Finding<Incident>(nameof(Incident.CustomerId), SchemaFix.SetReadOnly(DataverseOperation.Update)));

            Assert.Contains("[global::Dataverse.Fluent.DataverseLookupTarget(\"contact\", \"contacts\", \"customerid_contact\")]", text);
            Assert.Contains("[global::Dataverse.Fluent.DataverseReadOnly(global::Dataverse.Fluent.DataverseOperation.Update)]", text);
        }

        [Fact]
        public void InsertedLines_UseTheFilesLineEndings()
        {
            var crlf = Model.ReplaceLineEndings("\r\n");

            var (text, _) = Fix(crlf, Finding<Incident>(nameof(Incident.CustomerId), SchemaFix.AddLookupTarget("contact", "contacts", "customerid_contact")));

            Assert.Contains("\"customerid\")]\r\n    [DataverseLookupTarget(\"contact\", \"contacts\", \"customerid_contact\")]\r\n    public", text);
            Assert.DoesNotContain("\r\r", text);
            Assert.Equal(text.Split("\r\n").Length - 1, text.Count(c => c == '\n'));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Write_KeepsTheFilesEncoding(bool bom)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fixer-{Guid.NewGuid():N}.cs");
            try
            {
                File.WriteAllText(path, Model + "\n// café\n", new UTF8Encoding(bom));
                var fixer = SourceFixer.Load([path]);
                var plan = fixer.Plan([Finding<Incident>(nameof(Incident.IncidentId), SchemaFix.SetReadOnly(DataverseOperation.Update))]);

                SourceFixer.Write(plan);

                var bytes = File.ReadAllBytes(path);
                Assert.Equal(bom, bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
                var written = File.ReadAllText(path);
                Assert.Contains("[DataverseReadOnly(DataverseOperation.Update)]", written);
                Assert.Contains("café", written);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
