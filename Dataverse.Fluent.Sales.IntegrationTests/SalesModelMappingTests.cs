using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Dataverse.Fluent.IntegrationTests;
using Dataverse.Fluent.Models;
using Dataverse.Fluent.Sales.Entities;

namespace Dataverse.Fluent.Sales.IntegrationTests;

/// <summary>
/// Checks every Sales model against the environment: each mapped column, lookup and relationship exists under the
/// name the model uses, and the columns the library selects are accepted. Until 1.1.1 two models mapped columns that
/// do not exist (quote.closedatedon, incident.satisfactioncode); a retrieve without $select hid that, because an
/// unknown property just stays null.
/// </summary>
[Collection("Dataverse Integration Tests")]
public class SalesModelMappingTests(DataverseTestFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<Type> Models =>
    [
        typeof(Account), typeof(Contact), typeof(Incident), typeof(Lead),
        typeof(Opportunity), typeof(OpportunityProduct), typeof(Quote), typeof(QuoteDetail)
    ];

    [Theory]
    [MemberData(nameof(Models))]
    public async Task Model_MapsColumnsLookupsAndRelationshipsThatExist(Type model)
    {
        var table = model.GetCustomAttribute<DataverseEntityAttribute>()!;
        var transport = fixture.Client.Transport;

        var definition = await transport.ExecuteRequestAsync(HttpMethod.Get,
            $"EntityDefinitions(LogicalName='{table.LogicalName}')?$select=EntitySetName" +
            "&$expand=Attributes($select=LogicalName,IsValidForCreate,IsValidForUpdate)," +
            "ManyToOneRelationships($select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity)," +
            "OneToManyRelationships($select=ReferencedEntityNavigationPropertyName)", ct: Ct);

        Assert.Equal(table.EntitySet, (string?)definition["EntitySetName"]);
        var columns = definition["Attributes"]!.AsArray().ToDictionary(
            a => (string)a!["LogicalName"]!,
            a => (Create: (bool?)a!["IsValidForCreate"] != false, Update: (bool?)a["IsValidForUpdate"] != false));
        var lookups = definition["ManyToOneRelationships"]!.AsArray().Select(r => r!.AsObject()).ToList();
        var collections = definition["OneToManyRelationships"]!.AsArray()
            .Select(r => (string?)r!["ReferencedEntityNavigationPropertyName"]).ToHashSet();

        // The tables each lookup can point at: one for most lookups, several for customer and owner.
        var lookupTargets = (await transport.ExecuteRequestAsync(HttpMethod.Get,
                $"EntityDefinitions(LogicalName='{table.LogicalName}')/Attributes/Microsoft.Dynamics.CRM.LookupAttributeMetadata?$select=LogicalName,Targets", ct: Ct))["value"]!
            .AsArray().ToDictionary(a => (string)a!["LogicalName"]!, a => a!["Targets"]!.AsArray().Select(t => (string)t!).ToHashSet());

        var problems = new List<string>();
        foreach (var property in model.GetProperties())
        {
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                continue;

            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
            if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (!collections.Contains(name))
                    problems.Add($"{property.Name}: no relationship '{name}'");
                continue;
            }

            var polymorphicTargets = property.GetCustomAttributes<DataverseLookupTargetAttribute>().ToList();
            if (property.GetCustomAttribute<DataverseLookupAttribute>() is { } polymorphic && polymorphicTargets.Count > 0)
            {
                var declared = lookupTargets.GetValueOrDefault(polymorphic.LogicalName) ?? [];
                var mapped = polymorphicTargets.Select(t => t.LogicalName).ToHashSet();
                if (!declared.SetEquals(mapped))
                    problems.Add($"{property.Name}: targets are {string.Join(",", declared.Order())}, mapped {string.Join(",", mapped.Order())}");

                var relationships = lookups.Where(r => (string?)r["ReferencingAttribute"] == polymorphic.LogicalName).ToList();
                foreach (var target in polymorphicTargets)
                {
                    // A customer lookup has a relationship per table; the owner has one, to the abstract owner table.
                    var relationship = relationships.FirstOrDefault(r => (string?)r["ReferencedEntity"] == target.LogicalName)
                        ?? (relationships.Count == 1 ? relationships[0] : null);
                    if ((string?)relationship?["ReferencingEntityNavigationPropertyName"] != target.NavigationProperty)
                        problems.Add($"{property.Name}: navigation property to {target.LogicalName} is '{relationship?["ReferencingEntityNavigationPropertyName"]}', mapped '{target.NavigationProperty}'");

                    var set = await transport.ExecuteRequestAsync(HttpMethod.Get,
                        $"EntityDefinitions(LogicalName='{target.LogicalName}')?$select=EntitySetName", ct: Ct);
                    if ((string?)set["EntitySetName"] != target.EntitySet)
                        problems.Add($"{property.Name}: entity set of {target.LogicalName} is '{set["EntitySetName"]}', mapped '{target.EntitySet}'");
                }
            }
            else if (property.GetCustomAttribute<DataverseLookupAttribute>() is { } lookup)
            {
                var relationship = lookups.FirstOrDefault(r => (string?)r["ReferencingAttribute"] == lookup.LogicalName);
                if (relationship is null)
                {
                    problems.Add($"{property.Name}: no lookup '{lookup.LogicalName}'");
                    continue;
                }

                if ((string?)relationship["ReferencingEntityNavigationPropertyName"] != lookup.NavigationProperty)
                    problems.Add($"{property.Name}: navigation property is '{relationship["ReferencingEntityNavigationPropertyName"]}', mapped '{lookup.NavigationProperty}'");

                var target = await transport.ExecuteRequestAsync(HttpMethod.Get,
                    $"EntityDefinitions(LogicalName='{relationship["ReferencedEntity"]}')?$select=EntitySetName", ct: Ct);
                if ((string?)target["EntitySetName"] != lookup.TargetEntitySet)
                    problems.Add($"{property.Name}: target entity set is '{target["EntitySetName"]}', mapped '{lookup.TargetEntitySet}'");
            }

            // A lookup read by its value name (_customerid_value) is the column customerid.
            var column = property.GetCustomAttribute<DataverseLookupAttribute>()?.LogicalName
                ?? (name!.StartsWith('_') && name.EndsWith("_value", StringComparison.Ordinal) ? name[1..^"_value".Length] : name!);
            if (!columns.TryGetValue(column, out var valid))
            {
                problems.Add($"{property.Name}: no column '{column}'");
                continue;
            }

            // Dataverse ignores a value it does not accept (checked 2026-11-20), so SetFrom must not send one: a column
            // that cannot be updated is excluded from updates, and one that cannot be written at all from both.
            // [DataverseReadOnly(Create)] also excludes from updates (see EntityWriteBuilder.SetFrom).
            var readOnly = property.GetCustomAttribute<DataverseReadOnlyAttribute>()?.ExcludeFrom ?? DataverseOperation.None;
            if (!valid.Update && (readOnly & DataverseOperation.CreateAndUpdate) == 0)
                problems.Add($"{property.Name}: '{column}' cannot be updated but is not [DataverseReadOnly]");
            if (!valid.Create && !valid.Update && readOnly != DataverseOperation.CreateAndUpdate)
                problems.Add($"{property.Name}: '{column}' cannot be written but is not [DataverseReadOnly(CreateAndUpdate)]");
        }

        Assert.True(problems.Count == 0, $"{model.Name}: {string.Join("; ", problems)}");
    }

    [Theory]
    [MemberData(nameof(Models))]
    public async Task Model_SelectAll_IsAcceptedByDataverse(Type model)
    {
        // A $select naming a column that does not exist fails with 400 before the record is looked up, so a 404 for a
        // record that does not exist proves every selected column is valid.
        var retrieve = typeof(SalesModelMappingTests).GetMethod(nameof(RetrieveSelectAllAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(model);

        var ex = await Assert.ThrowsAsync<DataverseException>(() => (Task)retrieve.Invoke(this, [])!);

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    private Task RetrieveSelectAllAsync<T>() => fixture.Client.Entity<T>(Guid.NewGuid()).SelectAll().RetrieveAsync(Ct);

    // ───────── Customer and owner ─────────

    [Fact]
    public async Task CustomerAndOwner_AreReadWithTheirTable_AndARetrievedRecordCanBeWrittenBack()
    {
        // Until 1.1.1 these were read-only Guid properties: the table was unknown and SetFrom never wrote them.
        // Created records are deleted by the fixture.
        var me = await fixture.Client.Function<ActionAndFunctionTests.WhoAmIResponse>("WhoAmI").ExecuteAsync(Ct);
        var account = await fixture.Client.Entity<Account>().Set(a => a.Name, $"Customer {Guid.NewGuid():N}").CreateAsync(Ct);
        var contact = await fixture.Client.Entity<Contact>()
            .SetFrom(new Contact { LastName = $"Customer {Guid.NewGuid():N}", ParentCustomerId = DataverseReference.To<Account>(account.Id!.Value) })
            .CreateAsync(Ct);

        var created = await fixture.Client.Entity<Incident>()
            .SetFrom(new Incident
            {
                Title = $"Polymorphic {Guid.NewGuid():N}",
                CustomerId = DataverseReference.To<Account>(account.Id.Value),
                OwnerId = new DataverseReference("systemuser", me!.UserId)
            })
            .CreateAsync(Ct);

        var incident = (await fixture.Client.Entity<Incident>(created.Id).SelectAll().RetrieveAsync(Ct))!;
        Assert.Equal(new DataverseReference("account", account.Id.Value), incident.CustomerId);
        Assert.Equal(new DataverseReference("systemuser", me.UserId), incident.OwnerId);

        // Written back pointing at another table; the owner is not sent on an update.
        incident.CustomerId = DataverseReference.To<Contact>(contact.Id!.Value);
        await fixture.Client.Entity<Incident>(incident.IncidentId).SetFrom(incident).UpdateAsync(Ct);

        var id = incident.IncidentId;
        var contactId = contact.Id.Value;
        var found = await fixture.Client.Query<Incident>()
            .Where(i => i.IncidentId == id && i.CustomerId!.Id == contactId)
            .Select(i => new { i.CustomerId, i.OwnerId })
            .GetListAsync(Ct);
        var row = Assert.Single(found);
        Assert.Equal(new DataverseReference("contact", contactId), row.CustomerId);
        Assert.Equal(new DataverseReference("systemuser", me.UserId), row.OwnerId);

        var linked = await fixture.Client.Entity<Contact>(contactId).Select(c => c.ParentCustomerId).RetrieveAsync(Ct);
        Assert.Equal(new DataverseReference("account", account.Id.Value), linked!.ParentCustomerId);
    }

    // ───────── Date-only columns ─────────

    [Fact]
    public async Task Opportunity_DateOnlyColumns_RoundTrip()
    {
        // Until 1.1.1 these were DateTime properties written as a date and time, which Dataverse rejects for a date-only
        // column (HTTP 400), so a retrieved opportunity with a close date could not be written back. Created records
        // are deleted by the fixture.
        var created = await fixture.Client.Entity<Opportunity>()
            .Set(o => o.Name, $"Date-only round trip {Guid.NewGuid():N}")
            .Set(o => o.EstimatedCloseDate, new DateOnly(2026, 3, 15))
            .ReturnRepresentation(o => new { o.OpportunityId, o.Name, o.EstimatedCloseDate })
            .CreateAsync(Ct);
        var opportunity = created.Entity!;
        Assert.Equal(new DateOnly(2026, 3, 15), opportunity.EstimatedCloseDate);

        opportunity.ActualCloseDate = new DateOnly(2026, 4, 1);
        await fixture.Client.Entity<Opportunity>(opportunity.OpportunityId).SetFrom(opportunity).UpdateAsync(Ct);

        var date = new DateOnly(2026, 4, 1);
        var id = opportunity.OpportunityId;
        var odata = await fixture.Client.Query<Opportunity>()
            .Where(o => o.OpportunityId == id && o.ActualCloseDate == date && o.EstimatedCloseDate < date)
            .Select(o => new { o.EstimatedCloseDate, o.ActualCloseDate })
            .GetListAsync(Ct);
        var fetch = await fixture.Client.FetchBuilder<Opportunity>()
            .AddAttributes(o => new { o.EstimatedCloseDate, o.ActualCloseDate })
            .AddFilter(o => o.OpportunityId == id && o.ActualCloseDate == date && o.EstimatedCloseDate < date)
            .ExecuteAsync(Ct);

        foreach (var row in new[] { Assert.Single(odata), Assert.Single(fetch) })
        {
            Assert.Equal(new DateOnly(2026, 3, 15), row.EstimatedCloseDate);
            Assert.Equal(new DateOnly(2026, 4, 1), row.ActualCloseDate);
        }
    }

    [Fact]
    public async Task Quote_DateOnlyColumns_RoundTrip()
    {
        var created = await fixture.Client.Entity<Quote>()
            .SetFrom(new Quote
            {
                Name = $"Date-only round trip {Guid.NewGuid():N}",
                ExpiresOn = new DateOnly(2026, 6, 30),
                ClosedOn = new DateOnly(2026, 6, 1),
                EffectiveFrom = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)
            })
            .ReturnRepresentation(q => new { q.QuoteId, q.ExpiresOn, q.ClosedOn, q.EffectiveFrom })
            .CreateAsync(Ct);

        var quote = created.Entity!;
        Assert.Equal(new DateOnly(2026, 6, 30), quote.ExpiresOn);
        Assert.Equal(new DateOnly(2026, 6, 1), quote.ClosedOn);
        Assert.NotNull(quote.EffectiveFrom);
    }
}
