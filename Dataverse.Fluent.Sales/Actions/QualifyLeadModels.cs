using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Dataverse.Fluent.Sales.Actions;

/// <summary>
/// Request payload for the QualifyLead action, which is bound to the lead
/// (<c>leads(&lt;id&gt;)/Microsoft.Dynamics.CRM.QualifyLead</c>); the lead is identified by the URL, not the payload.
/// </summary>
public class QualifyLeadRequest
{
    [JsonPropertyName("CreateAccount")]
    public bool CreateAccount { get; set; }

    [JsonPropertyName("CreateContact")]
    public bool CreateContact { get; set; }

    [JsonPropertyName("CreateOpportunity")]
    public bool CreateOpportunity { get; set; }

    /// <summary>The status reason of the qualified lead: a <c>LeadStatus</c> value of the Qualified state.</summary>
    [JsonPropertyName("Status")]
    public int Status { get; set; }

    /// <summary>
    /// An existing account or contact to make the customer of the created opportunity, instead of the account or
    /// contact the action creates. Build it with <see cref="ActionRecord.Reference{T}(Guid)"/>.
    /// </summary>
    [JsonPropertyName("OpportunityCustomerId")]
    public JsonObject? OpportunityCustomerId { get; set; }

    /// <summary>
    /// The currency of the created opportunity. Build it with <c>ActionRecord.Reference("transactioncurrency", id)</c>.
    /// </summary>
    [JsonPropertyName("OpportunityCurrencyId")]
    public JsonObject? OpportunityCurrencyId { get; set; }

    /// <summary>
    /// The campaign the lead came from. Build it with <c>ActionRecord.Reference("campaign", id)</c>.
    /// </summary>
    [JsonPropertyName("SourceCampaignId")]
    public JsonObject? SourceCampaignId { get; set; }
}

/// <summary>
/// Response from the QualifyLead action containing references to the created entities,
/// in the order Dataverse returned them.
/// </summary>
public class QualifyLeadResponse
{
    public List<CreatedEntityReference>? CreatedEntities { get; set; }
}

/// <summary>
/// Reference to an entity created as a result of a CRM action.
/// </summary>
public class CreatedEntityReference
{
    /// <summary>The OData type of the record (e.g., <c>#Microsoft.Dynamics.CRM.account</c>).</summary>
    public string? ODataType { get; set; }

    /// <summary>The record's primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>The logical name of the record's table (e.g., <c>account</c>).</summary>
    public string? LogicalName { get; set; }
}
