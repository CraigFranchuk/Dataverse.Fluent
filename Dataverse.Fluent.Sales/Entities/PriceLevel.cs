using System.Text.Json.Serialization;
using Dataverse.Fluent;

namespace Dataverse.Fluent.Sales.Entities;

/// <summary>
/// Dynamics 365 Sales Price List entity (pricelevel).
/// Inherit from this class to add org-specific custom fields.
/// </summary>
[DataverseEntity("pricelevel", "pricelevels")]
public class PriceLevel
{
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("pricelevelid")]
    public virtual Guid PriceLevelId { get; set; }

    [JsonPropertyName("name")]
    public virtual string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public virtual string? Description { get; set; }

    [JsonPropertyName("begindate")]
    public virtual DateTime? BeginDate { get; set; }

    [JsonPropertyName("enddate")]
    public virtual DateTime? EndDate { get; set; }

    // ───────────────────────── System fields ─────────────────────────

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("createdon")]
    public virtual DateTime CreatedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("modifiedon")]
    public virtual DateTime ModifiedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statecode")]
    public virtual PriceLevelState StateCode { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statuscode")]
    public virtual PriceLevelStatus StatusCode { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("versionnumber")]
    public virtual long? VersionNumber { get; set; }

    // ───────────────────────── Lookups ─────────────────────────

    /// <summary>The currency of the price list. Set on create; it cannot be changed afterwards.</summary>
    [DataverseReadOnly(DataverseOperation.Update)]
    [DataverseLookup("transactioncurrencyid", "transactioncurrencies")]
    public virtual Guid? TransactionCurrencyId { get; set; }
}
