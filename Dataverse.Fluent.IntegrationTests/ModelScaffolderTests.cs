using Dataverse.Fluent.Schema;

namespace Dataverse.Fluent.IntegrationTests;

/// <summary>
/// The model scaffolder against a live environment: what the metadata of the standard incident table gives for each
/// kind of property. Every request reads metadata.
/// </summary>
[Collection("Dataverse Integration Tests")]
public class ModelScaffolderTests(DataverseTestFixture fixture)
{
    private Task<ScaffoldedModel> ScaffoldAsync(ScaffoldRequest request)
        => DataverseModelScaffolder.ScaffoldAsync(fixture.Client.Transport, request, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Incident_IsScaffolded_FromItsMetadata()
    {
        var model = await ScaffoldAsync(new ScaffoldRequest
        {
            LogicalName = "incident",
            Properties =
            [
                new("IncidentId", typeof(Guid)), new("Title", typeof(string)), new("TicketNumber", typeof(string)), new("FollowUpBy", typeof(DateOnly?)),
                new("CreatedOn", typeof(DateTime?)), new("PriorityCode", typeof(int?)), new("CustomerId", typeof(Guid)), new("SubjectId", typeof(Guid?)),
                new("CreatedBy", typeof(Guid?)), new("CreatedByName", typeof(string)), new("SatisfactionCode", typeof(int?))
            ],
            IncludeChoiceOptions = true
        });

        Assert.Equal(("incident", "incidents", "Incident"), (model.LogicalName, model.EntitySet, model.SchemaName));
        ScaffoldedProperty Property(string name) => model.Properties.Single(p => p.Name == name);

        Assert.Equal((ScaffoldedPropertyKind.Column, "incidentid", "Guid", true, DataverseOperation.Update),
            (Property("IncidentId").Kind, Property("IncidentId").LogicalName, Property("IncidentId").ClrType, Property("IncidentId").TypeFits, Property("IncidentId").ReadOnlyFor));
        Assert.Equal(("string", true, DataverseOperation.None, "Title"),
            (Property("Title").ClrType, Property("Title").TypeFits, Property("Title").ReadOnlyFor, Property("Title").SchemaName));
        Assert.Equal(DataverseOperation.Update, Property("TicketNumber").ReadOnlyFor);

        // followupby is a date and time column, so DateOnly must change.
        Assert.Equal(("DateTime", false), (Property("FollowUpBy").ClrType, Property("FollowUpBy").TypeFits));
        Assert.Equal(DataverseOperation.CreateAndUpdate, Property("CreatedOn").ReadOnlyFor);

        var priority = Property("PriorityCode");
        Assert.True(priority.IsChoice);
        Assert.Contains(priority.Options, o => o is { Value: 1, Label: not null });

        var customer = Property("CustomerId");
        Assert.Equal((ScaffoldedPropertyKind.PolymorphicLookup, "DataverseReference", false), (customer.Kind, customer.ClrType, customer.TypeFits));
        Assert.Equal(
            [new ScaffoldedLookupTarget("account", "accounts", "customerid_account"), new ScaffoldedLookupTarget("contact", "contacts", "customerid_contact")],
            customer.Targets.OrderBy(t => t.LogicalName));

        var subject = Property("SubjectId");
        Assert.Equal((ScaffoldedPropertyKind.Lookup, true), (subject.Kind, subject.TypeFits));
        Assert.Equal(new ScaffoldedLookupTarget("subject", "subjects", "subjectid"), Assert.Single(subject.Targets));

        Assert.Equal((ScaffoldedPropertyKind.Lookup, DataverseOperation.CreateAndUpdate), (Property("CreatedBy").Kind, Property("CreatedBy").ReadOnlyFor));

        Assert.Equal(ScaffoldedPropertyKind.NotFound, Property("CreatedByName").Kind);
        Assert.Contains("display value of 'createdby'", Property("CreatedByName").Message);
        Assert.Contains("'customersatisfactioncode'", Property("SatisfactionCode").Suggestion);
    }

    [Fact]
    public async Task ATableThatDoesNotExist_SuggestsTheTable()
    {
        var model = await ScaffoldAsync(new ScaffoldRequest { LogicalName = "acount" });

        Assert.False(model.TableFound);
        Assert.Contains("'account'", model.Suggestion);
    }
}
