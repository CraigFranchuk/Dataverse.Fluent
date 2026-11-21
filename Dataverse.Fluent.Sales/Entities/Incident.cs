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

    /// <summary>The case number. Dataverse generates it when none is given, and it cannot be changed afterwards.</summary>
    [DataverseReadOnly(DataverseOperation.Update)]
    [JsonPropertyName("ticketnumber")]
    public virtual string? TicketNumber { get; set; }

    [JsonPropertyName("casetypecode")]
    public virtual int? CaseTypeCode { get; set; }

    [JsonPropertyName("prioritycode")]
    public virtual IncidentPriority? PriorityCode { get; set; }

    [JsonPropertyName("severitycode")]
    public virtual int? SeverityCode { get; set; }

    [JsonPropertyName("caseorigincode")]
    public virtual CaseOrigin? CaseOriginCode { get; set; }

    /// <summary>The customer's satisfaction (<c>customersatisfactioncode</c>, a choice column).</summary>
    [JsonPropertyName("customersatisfactioncode")]
    public virtual CustomerSatisfaction? SatisfactionCode { get; set; }

    [JsonPropertyName("isescalated")]
    public virtual bool? IsEscalated { get; set; }

    [JsonPropertyName("followupby")]
    public virtual DateTime? FollowUpBy { get; set; }

    [JsonPropertyName("resolveby")]
    public virtual DateTime? ResolveBy { get; set; }

    /// <summary>When the case was escalated, set by Dataverse.</summary>
    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
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
    public virtual IncidentState StateCode { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statuscode")]
    public virtual IncidentStatus StatusCode { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("versionnumber")]
    public virtual long? VersionNumber { get; set; }

    // ───────────────────────── Lookups ─────────────────────────

    /// <summary>The customer, an account or a contact.</summary>
    [DataverseLookup("customerid")]
    [DataverseLookupTarget("account", "accounts", "customerid_account")]
    [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
    public virtual DataverseReference? CustomerId { get; set; }

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

    /// <summary>
    /// The owner, a user or a team. <c>SetFrom</c> sends it on create only, so writing back a retrieved record never
    /// reassigns the record; assign an existing record with <c>Set(x =&gt; x.OwnerId, reference)</c>.
    /// </summary>
    [DataverseReadOnly(DataverseOperation.Update)]
    [DataverseLookup("ownerid")]
    [DataverseLookupTarget("systemuser", "systemusers", "ownerid")]
    [DataverseLookupTarget("team", "teams", "ownerid")]
    public virtual DataverseReference? OwnerId { get; set; }
}
