using OnlineShopping.Domain.Common;

namespace OnlineShopping.Domain.Sales;

/// <summary>
/// A placed order. Everything the customer saw is copied (snapshot): items, prices, shipping method, tax rate, contact
/// and address, so later changes to the catalog or the address book never change it. Amounts are in
/// <see cref="Currency"/> (the base currency until phase 8 lets customers pay in another one at
/// <see cref="ExchangeRate"/>).
/// </summary>
public class Order : BaseEntity
{
    /// <summary>The order number customers and staff use (1001, 1002, …), never reused.</summary>
    public long Number { get; set; }

    /// <summary>The account that ordered; null for a guest order.</summary>
    public string? CustomerId { get; set; }

    public DateTime OrderedUtc { get; set; }

    /// <summary>Language of the order's emails and pages.</summary>
    public string Locale { get; set; } = string.Empty;

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>The stock reservation (and so the unpaid order) ends at this time.</summary>
    public DateTime? PaymentDueUtc { get; set; }

    // Contact and address (personal data: blanked when the account is erased).
    public string? Email { get; set; }

    public string RecipientName { get; set; } = string.Empty;

    /// <summary>E.164.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>The address fields below are empty for pickup orders.</summary>
    public string? CountryCode { get; set; }

    public string? Region { get; set; }

    public string? City { get; set; }

    public string? Line1 { get; set; }

    public string? Line2 { get; set; }

    public string? PostalCode { get; set; }

    /// <summary>The customer's note to the shop.</summary>
    public string? Note { get; set; }

    public Guid? ShippingMethodId { get; set; }

    public string ShippingMethodName { get; set; } = string.Empty;

    public bool IsPickup { get; set; }

    public Guid? DiscountCodeId { get; set; }

    public string? DiscountCodeText { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>Units of <see cref="Currency"/> per base-currency unit (1 in the base currency).</summary>
    public decimal ExchangeRate { get; set; } = 1;

    /// <summary>The goods at their prices, before the discount.</summary>
    public decimal Subtotal { get; set; }

    public decimal Discount { get; set; }

    public decimal Shipping { get; set; }

    /// <summary>The tax in <see cref="Total"/> (included or added, see <see cref="PricesIncludeTax"/>).</summary>
    public decimal Tax { get; set; }

    public decimal TaxRate { get; set; }

    public bool PricesIncludeTax { get; set; }

    public decimal Total { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
