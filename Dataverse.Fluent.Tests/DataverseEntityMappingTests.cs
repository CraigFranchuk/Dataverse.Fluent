using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Tests;

/// <summary>
/// <see cref="DataverseEntityMapping"/> describes a POCO as the builders read and write it.
/// </summary>
public class DataverseEntityMappingTests
{
    [DataverseEntity("incident", "incidents")]
    private class Case
    {
        [DataverseReadOnly(DataverseOperation.Create)]
        [JsonPropertyName("incidentid")]
        public Guid IncidentId { get; set; }

        public string? Title { get; set; }

        [DataverseColumn("ticketnumber")]
        [DataverseReadOnly(DataverseOperation.Update)]
        public string? Number { get; set; }

        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("createdon")]
        public DateTime? CreatedOn { get; set; }

        [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
        [JsonPropertyName("createdon@OData.Community.Display.V1.FormattedValue")]
        public string? CreatedOnText { get; set; }

        [DataverseLookup("primarycontactid", "contacts")]
        public Guid? PrimaryContactId { get; set; }

        [JsonPropertyName("_parentcaseid_value")]
        public Guid? ParentCaseId { get; set; }

        [DataverseLookup("customerid")]
        [DataverseLookupTarget("account", "accounts", "customerid_account")]
        [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
        public DataverseReference? CustomerId { get; set; }

        [JsonPropertyName("primarycontactid")]
        public Person? PrimaryContact { get; set; }

        [JsonPropertyName("incident_activity_parties")]
        public List<Person>? Parties { get; set; }

        [JsonIgnore]
        public string? Scratch { get; set; }
    }

    [EntitySet("contacts")]
    private class Person
    {
        [JsonPropertyName("fullname")]
        public string? FullName { get; set; }
    }

    private class NotMapped
    {
        public string? Name { get; set; }
    }

    private class BadLookup
    {
        [DataverseLookup("primarycontactid", "contacts")]
        public string? PrimaryContactId { get; set; }
    }

    private static DataverseMemberMapping Member<T>(string property)
        => DataverseEntityMapping.For<T>().Members.Single(m => m.Property.Name == property);

    [Fact]
    public void Table_IsTheLogicalNameAndEntitySet()
    {
        var mapping = DataverseEntityMapping.For<Case>();

        Assert.Equal(typeof(Case), mapping.Type);
        Assert.Equal("incident", mapping.LogicalName);
        Assert.Equal("incidents", mapping.EntitySet);
        Assert.True(mapping.IsMapped);
    }

    [Fact]
    public void Table_WithEntitySetOnly_HasNoLogicalName()
    {
        var mapping = DataverseEntityMapping.For<Person>();

        Assert.Equal("contacts", mapping.EntitySet);
        Assert.Null(mapping.LogicalName);
    }

    [Fact]
    public void UnmappedType_IsNotMapped()
    {
        var mapping = DataverseEntityMapping.For<NotMapped>();

        Assert.False(mapping.IsMapped);
        Assert.Null(mapping.EntitySet);
        Assert.Equal("name", Assert.Single(mapping.Members).Name);
    }

    [Fact]
    public void Members_AreInDeclarationOrder()
    {
        var names = DataverseEntityMapping.For<Case>().Members.Select(m => m.Property.Name);

        Assert.Equal(typeof(Case).GetProperties().Select(p => p.Name), names);
    }

    [Theory]
    [InlineData(nameof(Case.Title), DataverseMemberKind.Column, "title", "title")]
    [InlineData(nameof(Case.Number), DataverseMemberKind.Column, "ticketnumber", "ticketnumber")]
    [InlineData(nameof(Case.CreatedOnText), DataverseMemberKind.Column, "createdon@OData.Community.Display.V1.FormattedValue", "createdon@OData.Community.Display.V1.FormattedValue")]
    [InlineData(nameof(Case.PrimaryContactId), DataverseMemberKind.Lookup, "_primarycontactid_value", "primarycontactid")]
    [InlineData(nameof(Case.ParentCaseId), DataverseMemberKind.Column, "_parentcaseid_value", "parentcaseid")]
    [InlineData(nameof(Case.CustomerId), DataverseMemberKind.PolymorphicLookup, "_customerid_value", "customerid")]
    [InlineData(nameof(Case.PrimaryContact), DataverseMemberKind.Navigation, "primarycontactid", "primarycontactid")]
    [InlineData(nameof(Case.Parties), DataverseMemberKind.Navigation, "incident_activity_parties", "incident_activity_parties")]
    [InlineData(nameof(Case.Scratch), DataverseMemberKind.Ignored, "scratch", "scratch")]
    public void Member_KindAndNames(string property, DataverseMemberKind kind, string name, string logicalName)
    {
        var member = Member<Case>(property);

        Assert.Equal(kind, member.Kind);
        Assert.Equal(name, member.Name);
        Assert.Equal(logicalName, member.LogicalName);
    }

    [Fact]
    public void Lookups_CarryTheirAttributes()
    {
        var lookup = Member<Case>(nameof(Case.PrimaryContactId));
        var polymorphic = Member<Case>(nameof(Case.CustomerId));

        Assert.Equal("contacts", lookup.Lookup!.TargetEntitySet);
        Assert.Empty(lookup.Targets);
        Assert.Null(polymorphic.Lookup!.TargetEntitySet);
        Assert.Equal(["account", "contact"], polymorphic.Targets.Select(t => t.LogicalName));
        Assert.Null(Member<Case>(nameof(Case.Title)).Lookup);
    }

    [Fact]
    public void Navigation_HasItsRelatedType()
    {
        var single = Member<Case>(nameof(Case.PrimaryContact));
        var collection = Member<Case>(nameof(Case.Parties));

        Assert.Equal(typeof(Person), single.RelatedType);
        Assert.False(single.IsCollection);
        Assert.Equal(typeof(Person), collection.RelatedType);
        Assert.True(collection.IsCollection);
        Assert.Null(Member<Case>(nameof(Case.Title)).RelatedType);
    }

    [Theory]
    [InlineData(nameof(Case.IncidentId), DataverseOperation.Create)]
    [InlineData(nameof(Case.Number), DataverseOperation.Update)]
    [InlineData(nameof(Case.CreatedOn), DataverseOperation.CreateAndUpdate)]
    [InlineData(nameof(Case.Title), DataverseOperation.None)]
    public void ReadOnlyFor_IsTheMarker(string property, DataverseOperation expected)
        => Assert.Equal(expected, Member<Case>(property).ReadOnlyFor);

    [Fact]
    public void IsWrittenOnCreateAndUpdate_MatchWhatSetFromSends()
    {
        // Every member set to a value, so SetFrom sends each property it can.
        var record = new Case
        {
            IncidentId = Guid.NewGuid(),
            Title = "t",
            Number = "CAS-1",
            CreatedOn = DateTime.UtcNow,
            CreatedOnText = "today",
            PrimaryContactId = Guid.NewGuid(),
            ParentCaseId = Guid.NewGuid(),
            CustomerId = new DataverseReference("account", Guid.NewGuid()),
            PrimaryContact = new Person { FullName = "p" },
            Parties = [new Person()],
            Scratch = "s"
        };
        var client = new DataverseClient(new NullTransport());
        var create = client.Entity<Case>().SetFrom(record).ToCreateRequest().Payload!;
        var update = client.Entity<Case>(Guid.NewGuid()).SetFrom(record).ToUpdateRequest().Payload!;

        foreach (var member in DataverseEntityMapping.For<Case>().Members)
        {
            Assert.True(member.IsWrittenOnCreate == Sends(create, member), $"{member.Property.Name} on create");
            Assert.True(member.IsWrittenOnUpdate == Sends(update, member), $"{member.Property.Name} on update");
        }
    }

    // The payload names a member is written under: its name, or the bind of a lookup.
    private static bool Sends(JsonObject payload, DataverseMemberMapping member) => member.Kind switch
    {
        DataverseMemberKind.Lookup => payload.ContainsKey($"{member.Lookup!.NavigationProperty}@odata.bind"),
        DataverseMemberKind.PolymorphicLookup => member.Targets.Any(t => payload.ContainsKey($"{t.NavigationProperty}@odata.bind")),
        _ => payload.ContainsKey(member.Name)
    };

    [Fact]
    public void InvalidMapping_Throws()
        => Assert.Throws<InvalidOperationException>(() => DataverseEntityMapping.For<BadLookup>());

    private class ResolvedByConvention
    {
        public string? Name { get; set; }
    }

    // The registry is process-wide; the resolver only answers for ResolvedByConvention, so registering it once (and
    // never clearing it) cannot affect other tests running in parallel.
    private static readonly Lazy<bool> ResolverRegistered = new(() =>
    {
        DataverseTypeMapping.RegisterResolver(new PrefixResolver());
        return true;
    });

    [Fact]
    public void CustomResolver_IsHonoured()
    {
        Assert.True(ResolverRegistered.Value);

        var mapping = DataverseEntityMapping.For<ResolvedByConvention>();

        Assert.Equal("things", mapping.EntitySet);
        Assert.Equal("thing", mapping.LogicalName);
        Assert.Equal("new_name", Assert.Single(mapping.Members).Name);
    }

    private sealed class PrefixResolver : IDataverseNameResolver
    {
        public string? ResolvePropertyName(MemberInfo member)
            => member.DeclaringType == typeof(ResolvedByConvention) ? "new_" + member.Name.ToLowerInvariant() : null;

        public string? ResolveEntitySetName(Type type) => type == typeof(ResolvedByConvention) ? "things" : null;

        public string? ResolveEntityLogicalName(Type type) => type == typeof(ResolvedByConvention) ? "thing" : null;
    }

    private sealed class NullTransport : IDataverseTransport
    {
        public string BaseUrl => "https://org.crm.dynamics.com/api/data/v9.2";

        public Task<DataverseResponse> SendAsync(DataverseRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
