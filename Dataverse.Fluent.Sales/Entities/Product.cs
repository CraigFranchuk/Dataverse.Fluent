using System.Text.Json.Serialization;
using Dataverse.Fluent;

namespace Dataverse.Fluent.Sales.Entities;

/// <summary>
/// Dynamics 365 Sales Product entity (product): a product, a product family or a bundle.
/// Inherit from this class to add org-specific custom fields.
/// </summary>
[DataverseEntity("product", "products")]
public class Product
{
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("productid")]
    public virtual Guid ProductId { get; set; }

    [JsonPropertyName("name")]
    public virtual string Name { get; set; } = string.Empty;

    [JsonPropertyName("productnumber")]
    public virtual string ProductNumber { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public virtual string? Description { get; set; }

    /// <summary>Whether the record is a product, a family or a bundle. Set on create; it cannot be changed afterwards.</summary>
    [DataverseReadOnly(DataverseOperation.Update)]
    [JsonPropertyName("productstructure")]
    public virtual ProductStructure? ProductStructure { get; set; }

    [JsonPropertyName("producttypecode")]
    public virtual ProductType? ProductTypeCode { get; set; }

    /// <summary>The list price.</summary>
    [JsonPropertyName("price")]
    public virtual decimal? Price { get; set; }

    [JsonPropertyName("standardcost")]
    public virtual decimal? StandardCost { get; set; }

    [JsonPropertyName("currentcost")]
    public virtual decimal? CurrentCost { get; set; }

    [JsonPropertyName("quantityonhand")]
    public virtual decimal? QuantityOnHand { get; set; }

    /// <summary>The number of decimal places a quantity of the product can have.</summary>
    [JsonPropertyName("quantitydecimal")]
    public virtual int? QuantityDecimal { get; set; }

    [JsonPropertyName("size")]
    public virtual string? Size { get; set; }

    [JsonPropertyName("vendorname")]
    public virtual string? VendorName { get; set; }

    [JsonPropertyName("suppliername")]
    public virtual string? SupplierName { get; set; }

    /// <summary>The date the product becomes valid, a date-only column.</summary>
    [JsonPropertyName("validfromdate")]
    public virtual DateOnly? ValidFromDate { get; set; }

    /// <summary>The date the product stops being valid, a date-only column.</summary>
    [JsonPropertyName("validtodate")]
    public virtual DateOnly? ValidToDate { get; set; }

    // ───────────────────────── System fields ─────────────────────────

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("createdon")]
    public virtual DateTime CreatedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("modifiedon")]
    public virtual DateTime ModifiedOn { get; set; }

    /// <summary>The state. A new product is a draft or active, according to the organisation's settings.</summary>
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statecode")]
    public virtual ProductState StateCode { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statuscode")]
    public virtual ProductStatus StatusCode { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("versionnumber")]
    public virtual long? VersionNumber { get; set; }

    // ───────────────────────── Lookups ─────────────────────────

    /// <summary>The default unit, which must belong to <see cref="DefaultUomScheduleId"/>.</summary>
    [DataverseLookup("defaultuomid", "uoms")]
    public virtual Guid? DefaultUomId { get; set; }

    /// <summary>The unit group.</summary>
    [DataverseLookup("defaultuomscheduleid", "uomschedules")]
    public virtual Guid? DefaultUomScheduleId { get; set; }

    /// <summary>The default price list. It can be set only once the product exists.</summary>
    [DataverseReadOnly(DataverseOperation.Create)]
    [DataverseLookup("pricelevelid", "pricelevels")]
    public virtual Guid? PriceLevelId { get; set; }

    /// <summary>The product family the record belongs to.</summary>
    [DataverseLookup("parentproductid", "products")]
    public virtual Guid? ParentProductId { get; set; }

    [DataverseLookup("transactioncurrencyid", "transactioncurrencies")]
    public virtual Guid? TransactionCurrencyId { get; set; }
}
