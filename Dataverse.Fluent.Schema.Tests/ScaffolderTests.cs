namespace Dataverse.Fluent.Schema.Tests;

/// <summary><see cref="DataverseModelScaffolder"/> against a fake environment: what each requested property maps to.</summary>
public class ScaffolderTests
{
    private static FakeEnvironment Environment()
    {
        var environment = FakeEnvironment.WithStandardTables();
        environment.Table("incident", "incidents")
            .Column("incidentid", "Uniqueidentifier", update: false)
            .Column("title", "String")
            .Column("revenue", "Money")
            .Column("somethingnew", "Mystery")
            .DateTime("duedate", "DateOnly")
            .DateTime("createdon", "UserLocal", create: false, update: false)
            .Choice("prioritycode", "Picklist", 1, 2)
            .Labels("prioritycode", "High", "Normal")
            .Lookup("customerid", "Customer", ("account", "customerid_account"), ("contact", "customerid_contact"))
            .Lookup("primarycontactid", "Lookup", ("contact", "primarycontactid"))
            .Schema("duedate", "DueDate")
            .SchemaName = "Incident";
        return environment;
    }

    private static Task<ScaffoldedModel> ScaffoldAsync(FakeEnvironment environment, ScaffoldRequest request)
        => DataverseModelScaffolder.ScaffoldAsync(environment.Transport, request, TestContext.Current.CancellationToken);

    [Fact]
    public async Task EachProperty_IsMatchedToItsColumn_IgnoringCase()
    {
        var model = await ScaffoldAsync(Environment(), new ScaffoldRequest
        {
            LogicalName = "incident",
            Properties =
            [
                new("IncidentId", typeof(Guid)), new("Title", typeof(string)), new("DueDate", typeof(DateTime?)), new("Revenue", typeof(double)),
                new("CreatedOn"), new("SomethingNew", typeof(int))
            ]
        });

        Assert.True(model.TableFound);
        Assert.Equal(("incident", "incidents", "Incident"), (model.LogicalName, model.EntitySet, model.SchemaName));
        Assert.All(model.Properties, p => Assert.Equal(ScaffoldedPropertyKind.Column, p.Kind));

        var id = model.Properties[0];
        Assert.Equal(("incidentid", "Guid", true, true, DataverseOperation.Update), (id.LogicalName, id.ClrType, id.ClrTypeIsValueType, id.TypeFits, id.ReadOnlyFor));
        Assert.Equal(("string", false, true), (model.Properties[1].ClrType, model.Properties[1].ClrTypeIsValueType, model.Properties[1].TypeFits));

        // A type that cannot hold the column, or holds it with loss, does not fit.
        Assert.Equal(("DateOnly", false, "DueDate"), (model.Properties[2].ClrType, model.Properties[2].TypeFits, model.Properties[2].SchemaName));
        Assert.Equal(("decimal", false), (model.Properties[3].ClrType, model.Properties[3].TypeFits));

        // No type was given, so there is nothing to judge.
        Assert.Equal(("DateTime", null, DataverseOperation.CreateAndUpdate), (model.Properties[4].ClrType, model.Properties[4].TypeFits, model.Properties[4].ReadOnlyFor));

        // A column type without a rule recommends nothing, and whatever the model has is kept.
        Assert.Equal((null, true), (model.Properties[5].ClrType, model.Properties[5].TypeFits));
    }

    [Fact]
    public async Task Lookups_CarryTheirTargets()
    {
        var model = await ScaffoldAsync(Environment(), new ScaffoldRequest
        {
            LogicalName = "incident",
            Properties = [new("CustomerId", typeof(Guid)), new("PrimaryContactId", typeof(Guid?)), new("primarycontactid", typeof(DataverseReference))]
        });

        var customer = model.Properties[0];
        Assert.Equal((ScaffoldedPropertyKind.PolymorphicLookup, "DataverseReference", false), (customer.Kind, customer.ClrType, customer.TypeFits));
        Assert.Equal(
            [new ScaffoldedLookupTarget("account", "accounts", "customerid_account"), new ScaffoldedLookupTarget("contact", "contacts", "customerid_contact")],
            customer.Targets);

        var contact = model.Properties[1];
        Assert.Equal((ScaffoldedPropertyKind.Lookup, "Guid", true), (contact.Kind, contact.ClrType, contact.TypeFits));
        Assert.Equal(new ScaffoldedLookupTarget("contact", "contacts", "primarycontactid"), Assert.Single(contact.Targets));

        // A property that is already a DataverseReference stays one, with its single target.
        Assert.Equal((ScaffoldedPropertyKind.PolymorphicLookup, true), (model.Properties[2].Kind, model.Properties[2].TypeFits));
    }

    [Fact]
    public async Task APropertyWithoutAColumn_IsNotFound_WithASuggestion()
    {
        var model = await ScaffoldAsync(Environment(), new ScaffoldRequest
        {
            EntitySet = "incidents",
            Properties = [new("Titel"), new("PrimaryContactIdName")]
        });

        Assert.Equal("incident", model.LogicalName);
        Assert.Equal((ScaffoldedPropertyKind.NotFound, "No column 'titel' on incident.", "Did you mean 'title'?"),
            (model.Properties[0].Kind, model.Properties[0].Message, model.Properties[0].Suggestion));
        Assert.Contains("is the display value of 'primarycontactid'", model.Properties[1].Message);
    }

    [Fact]
    public async Task ChoiceOptions_AreReadOnlyWhenAskedFor()
    {
        var environment = Environment();

        var without = await ScaffoldAsync(environment, new ScaffoldRequest { LogicalName = "incident", Properties = [new("PriorityCode")] });
        var with = await ScaffoldAsync(environment, new ScaffoldRequest { LogicalName = "incident", Properties = [new("PriorityCode")], IncludeChoiceOptions = true });

        Assert.True(without.Properties[0].IsChoice);
        Assert.Empty(without.Properties[0].Options);
        Assert.Equal([new ScaffoldedOption(1, "High"), new ScaffoldedOption(2, "Normal")], with.Properties[0].Options);
    }

    [Fact]
    public async Task ATableThatDoesNotExist_IsReported_NotThrown()
    {
        var byName = await ScaffoldAsync(Environment(), new ScaffoldRequest { LogicalName = "incidnet", Properties = [new("Title")] });
        var bySet = await ScaffoldAsync(Environment(), new ScaffoldRequest { EntitySet = "incidnets" });

        Assert.False(byName.TableFound);
        Assert.Empty(byName.Properties);
        Assert.Equal(("Table 'incidnet' does not exist.", "Did you mean 'incident'?"), (byName.Message, byName.Suggestion));
        Assert.Equal(("No table has the entity set 'incidnets'.", "Did you mean 'incidents'?"), (bySet.Message, bySet.Suggestion));
    }

    [Fact]
    public async Task ARequestWithoutATable_IsRejected()
        => await Assert.ThrowsAsync<ArgumentException>(() => ScaffoldAsync(Environment(), new ScaffoldRequest()));
}
