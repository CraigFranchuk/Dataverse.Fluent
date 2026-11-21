using System.Text.Json.Serialization;
using Dataverse.Fluent;

namespace Dataverse.Fluent.Sales.Entities;

/// <summary>
/// Dynamics 365 Sales Order entity (salesorder).
/// Inherit from this class to add org-specific custom fields.
/// </summary>
[DataverseEntity("salesorder", "salesorders")]
public class SalesOrder
{
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("salesorderid")]
    public virtual Guid SalesOrderId { get; set; }

    [JsonPropertyName("name")]
    public virtual string Name { get; set; } = string.Empty;

    /// <summary>The order number. Dataverse generates it when none is given, and it cannot be changed afterwards.</summary>
    [DataverseReadOnly(DataverseOperation.Update)]
    [JsonPropertyName("ordernumber")]
    public virtual string? OrderNumber { get; set; }

    [JsonPropertyName("description")]
    public virtual string? Description { get; set; }

    [JsonPropertyName("totalamount")]
    public virtual decimal? TotalAmount { get; set; }

    [JsonPropertyName("totallineitemamount")]
    public virtual decimal? TotalLineItemAmount { get; set; }

    [JsonPropertyName("totaldiscountamount")]
    public virtual decimal? TotalDiscountAmount { get; set; }

    [JsonPropertyName("totaltax")]
    public virtual decimal? TotalTax { get; set; }

    [JsonPropertyName("discountamount")]
    public virtual decimal? DiscountAmount { get; set; }

    [JsonPropertyName("discountpercentage")]
    public virtual decimal? DiscountPercentage { get; set; }

    [JsonPropertyName("freightamount")]
    public virtual decimal? FreightAmount { get; set; }

    [JsonPropertyName("requestdeliveryby")]
    public virtual DateTime? RequestDeliveryBy { get; set; }

    [JsonPropertyName("submitdate")]
    public virtual DateTime? SubmitDate { get; set; }

    [JsonPropertyName("datefulfilled")]
    public virtual DateTime? DateFulfilled { get; set; }

    /// <summary>Whether prices are locked, so that changes to the price list do not change the order.</summary>
    [JsonPropertyName("ispricelocked")]
    public virtual bool? IsPriceLocked { get; set; }

    [JsonPropertyName("paymenttermscode")]
    public virtual PaymentTerms? PaymentTermsCode { get; set; }

    [JsonPropertyName("shippingmethodcode")]
    public virtual ShippingMethod? ShippingMethodCode { get; set; }

    [JsonPropertyName("freighttermscode")]
    public virtual FreightTerms? FreightTermsCode { get; set; }

    // ───────────────────────── System fields ─────────────────────────

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("createdon")]
    public virtual DateTime CreatedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("modifiedon")]
    public virtual DateTime ModifiedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statecode")]
    public virtual SalesOrderState StateCode { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statuscode")]
    public virtual SalesOrderStatus StatusCode { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("versionnumber")]
    public virtual long? VersionNumber { get; set; }

    // ───────────────────────── Lookups ─────────────────────────

    /// <summary>The customer, an account or a contact.</summary>
    [DataverseLookup("customerid")]
    [DataverseLookupTarget("account", "accounts", "customerid_account")]
    [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
    public virtual DataverseReference? CustomerId { get; set; }

    /// <summary>The quote the order was created from.</summary>
    [DataverseLookup("quoteid", "quotes")]
    public virtual Guid? QuoteId { get; set; }

    [DataverseLookup("opportunityid", "opportunities")]
    public virtual Guid? OpportunityId { get; set; }

    [DataverseLookup("pricelevelid", "pricelevels")]
    public virtual Guid? PriceLevelId { get; set; }

    /// <summary>The currency. Set on create; it cannot be changed afterwards.</summary>
    [DataverseReadOnly(DataverseOperation.Update)]
    [DataverseLookup("transactioncurrencyid", "transactioncurrencies")]
    public virtual Guid? TransactionCurrencyId { get; set; }

    /// <summary>
    /// The owner, a user or a team. <c>SetFrom</c> sends it on create only, so writing back a retrieved record never
    /// reassigns the record; assign an existing record with <c>Set(x =&gt; x.OwnerId, reference)</c>.
    /// </summary>
    [DataverseReadOnly(DataverseOperation.Update)]
    [DataverseLookup("ownerid")]
    [DataverseLookupTarget("systemuser", "systemusers", "ownerid")]
    [DataverseLookupTarget("team", "teams", "ownerid")]
    public virtual DataverseReference? OwnerId { get; set; }

    // ───────────────────────── Navigation properties ─────────────────────────

    /// <summary>
    /// Related order lines (1:N). Populated when $expand=order_details is used.
    /// </summary>
    [JsonPropertyName("order_details")]
    public virtual List<SalesOrderDetail>? Details { get; set; }
}
