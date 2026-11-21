using System.Text.Json.Serialization;
using Dataverse.Fluent;

namespace Dataverse.Fluent.Sales.Entities;

/// <summary>
/// Dynamics 365 Sales Invoice entity (invoice).
/// Inherit from this class to add org-specific custom fields.
/// </summary>
[DataverseEntity("invoice", "invoices")]
public class Invoice
{
    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("invoiceid")]
    public virtual Guid InvoiceId { get; set; }

    [JsonPropertyName("name")]
    public virtual string Name { get; set; } = string.Empty;

    /// <summary>The invoice number. Dataverse generates it when none is given, and it cannot be changed afterwards.</summary>
    [DataverseReadOnly(DataverseOperation.Update)]
    [JsonPropertyName("invoicenumber")]
    public virtual string? InvoiceNumber { get; set; }

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

    [JsonPropertyName("datedelivered")]
    public virtual DateTime? DateDelivered { get; set; }

    /// <summary>The date payment is due, a date-only column.</summary>
    [JsonPropertyName("duedate")]
    public virtual DateOnly? DueDate { get; set; }

    /// <summary>Whether prices are locked, so that changes to the price list do not change the invoice.</summary>
    [JsonPropertyName("ispricelocked")]
    public virtual bool? IsPriceLocked { get; set; }

    [JsonPropertyName("paymenttermscode")]
    public virtual PaymentTerms? PaymentTermsCode { get; set; }

    [JsonPropertyName("shippingmethodcode")]
    public virtual ShippingMethod? ShippingMethodCode { get; set; }

    // ───────────────────────── System fields ─────────────────────────

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("createdon")]
    public virtual DateTime CreatedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("modifiedon")]
    public virtual DateTime ModifiedOn { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statecode")]
    public virtual InvoiceState StateCode { get; set; }

    [DataverseReadOnly(DataverseOperation.Create)]
    [JsonPropertyName("statuscode")]
    public virtual InvoiceStatus StatusCode { get; set; }

    [DataverseReadOnly(DataverseOperation.CreateAndUpdate)]
    [JsonPropertyName("versionnumber")]
    public virtual long? VersionNumber { get; set; }

    // ───────────────────────── Lookups ─────────────────────────

    /// <summary>The customer, an account or a contact.</summary>
    [DataverseLookup("customerid")]
    [DataverseLookupTarget("account", "accounts", "customerid_account")]
    [DataverseLookupTarget("contact", "contacts", "customerid_contact")]
    public virtual DataverseReference? CustomerId { get; set; }

    /// <summary>The order the invoice was created from.</summary>
    [DataverseLookup("salesorderid", "salesorders")]
    public virtual Guid? SalesOrderId { get; set; }

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
    /// Related invoice lines (1:N). Populated when $expand=invoice_details is used.
    /// </summary>
    [JsonPropertyName("invoice_details")]
    public virtual List<InvoiceDetail>? Details { get; set; }
}
