using System.Text.Json.Serialization;
using Dataverse.Fluent;

namespace Dataverse.Fluent.Sales.Entities;

/// <summary>
/// Dynamics 365 Sales Quote Detail (line item) entity (quotedetail).
/// Inherit from this class to add org-specific custom fields.
/// </summary>
[DataverseEntity("quotedetail", "quotedetails")]
public class QuoteDetail
{
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("quotedetailid")]
    public virtual Guid QuoteDetailId { get; set; }

    [JsonPropertyName("productdescription")]
    public virtual string? ProductDescription { get; set; }

    [JsonPropertyName("quantity")]
    public virtual decimal? Quantity { get; set; }

    [JsonPropertyName("priceperunit")]
    public virtual decimal? PricePerUnit { get; set; }

    [JsonPropertyName("manualdiscountamount")]
    public virtual decimal? ManualDiscountAmount { get; set; }

    [JsonPropertyName("tax")]
    public virtual decimal? Tax { get; set; }

    [JsonPropertyName("extendedamount")]
    public virtual decimal? ExtendedAmount { get; set; }

    [JsonPropertyName("baseamount")]
    public virtual decimal? BaseAmount { get; set; }

    [JsonPropertyName("ispriceoverridden")]
    public virtual bool? IsPriceOverridden { get; set; }

    /// <summary>Whether the line is a write-in product. Set when the line is created; it cannot be changed afterwards.</summary>
    [DataverseReadOnly(DataverseOperation.Update)]
    [JsonPropertyName("isproductoverridden")]
    public virtual bool? IsProductOverridden { get; set; }

    // ───────────────────────── System fields ─────────────────────────

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("createdon")]
    public virtual DateTime CreatedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("modifiedon")]
    public virtual DateTime ModifiedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("versionnumber")]
    public virtual long? VersionNumber { get; set; }

    // ───────────────────────── Lookups ─────────────────────────

    [DataverseLookup("quoteid", "quotes")]
    public virtual Guid? QuoteId { get; set; }

    [DataverseLookup("productid", "products")]
    public virtual Guid? ProductId { get; set; }

    [DataverseLookup("uomid", "uoms")]
    public virtual Guid? UomId { get; set; }
}
