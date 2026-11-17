using System.Text.Json.Serialization;
using Dataverse.Fluent;

namespace Dataverse.Fluent.Sales.Entities;

/// <summary>
/// Dynamics 365 Customer Service Incident (Case) entity (incident).
/// Inherit from this class to add org-specific custom fields.
/// </summary>
[DataverseEntity("incident", "incidents")]
public class Incident
{
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("incidentid")]
    public virtual Guid IncidentId { get; set; }

    [JsonPropertyName("title")]
    public virtual string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public virtual string? Description { get; set; }

    [JsonPropertyName("ticketnumber")]
    public virtual string? TicketNumber { get; set; }

    [JsonPropertyName("casetypecode")]
    public virtual int? CaseTypeCode { get; set; }

    [JsonPropertyName("prioritycode")]
    public virtual int? PriorityCode { get; set; }

    [JsonPropertyName("severitycode")]
    public virtual int? SeverityCode { get; set; }

    [JsonPropertyName("caseorigincode")]
    public virtual int? CaseOriginCode { get; set; }

    [JsonPropertyName("satisfactioncode")]
    public virtual int? SatisfactionCode { get; set; }

    [JsonPropertyName("isescalated")]
    public virtual bool? IsEscalated { get; set; }

    [JsonPropertyName("followupby")]
    public virtual DateTime? FollowUpBy { get; set; }

    [JsonPropertyName("resolveby")]
    public virtual DateTime? ResolveBy { get; set; }

    [JsonPropertyName("escalatedon")]
    public virtual DateTime? EscalatedOn { get; set; }

    // ───────────────────────── System fields ─────────────────────────

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("createdon")]
    public virtual DateTime CreatedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("modifiedon")]
    public virtual DateTime ModifiedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statecode")]
    public virtual int StateCode { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statuscode")]
    public virtual int StatusCode { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("versionnumber")]
    public virtual long? VersionNumber { get; set; }

    // ───────────────────────── Lookups ─────────────────────────

    /// <summary>
    /// The customer (account or contact). Read-only here because the lookup is polymorphic: set it with
    /// <c>SetLookup("customerid_account", "accounts", id)</c> or <c>SetLookup("customerid_contact", "contacts", id)</c>.
    /// </summary>
    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("_customerid_value")]
    public virtual Guid? CustomerId { get; set; }

    [DataverseLookup("primarycontactid", "contacts")]
    public virtual Guid? PrimaryContactId { get; set; }

    [DataverseLookup("subjectid", "subjects")]
    public virtual Guid? SubjectId { get; set; }

    [DataverseLookup("productid", "products")]
    public virtual Guid? ProductId { get; set; }

    [DataverseLookup("entitlementid", "entitlements")]
    public virtual Guid? EntitlementId { get; set; }

    [DataverseLookup("parentcaseid", "incidents")]
    public virtual Guid? ParentCaseId { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("_ownerid_value")]
    public virtual Guid? OwnerId { get; set; }
}
