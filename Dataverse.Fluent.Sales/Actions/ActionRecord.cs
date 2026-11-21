using System.Text.Json.Nodes;

namespace Dataverse.Fluent.Sales.Actions;

/// <summary>
/// Builds the value of an action parameter that is a record, such as <see cref="QualifyLeadRequest.OpportunityCustomerId"/>.
/// </summary>
public static class ActionRecord
{
    /// <summary>
    /// A reference to an existing record, for a parameter that takes a record: its type and its primary key.
    /// </summary>
    /// <param name="logicalName">The table's logical name, such as <c>account</c>.</param>
    /// <param name="id">The record's ID.</param>
    /// <remarks>The primary key is taken to be <c>{logicalName}id</c>, as it is for every table but the activities.</remarks>
    public static JsonObject Reference(string logicalName, Guid id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);

        return new JsonObject
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM." + logicalName,
            [logicalName + "id"] = id
        };
    }

    /// <summary>
    /// A reference to an existing record of the table <typeparamref name="T"/> maps, for a parameter that takes a record.
    /// </summary>
    /// <typeparam name="T">A model mapped with <c>[DataverseEntity]</c>, such as <c>Account</c>.</typeparam>
    /// <param name="id">The record's ID.</param>
    public static JsonObject Reference<T>(Guid id)
        => Reference(DataverseReference.To<T>(id).LogicalName!, id);
}
