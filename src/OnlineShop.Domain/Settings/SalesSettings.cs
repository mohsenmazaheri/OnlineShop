using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Settings;

/// <summary>
/// The seller's details printed on invoices and the return period. One row per shop; without it invoices show the
/// shop's name only and returns are possible for <see cref="DefaultReturnDays"/> days.
/// </summary>
public sealed class SalesSettings : BaseEntity
{
    /// <summary>The EU right of withdrawal: 14 days.</summary>
    public const int DefaultReturnDays = 14;

    /// <summary>The legal name on invoices (empty: the shop's name).</summary>
    public string? SellerName { get; set; }

    public string? SellerAddress { get; set; }

    /// <summary>VAT number, national ID or economic code, as the shop's country asks.</summary>
    public string? SellerTaxNumber { get; set; }

    /// <summary>A line printed under every invoice ("Thank you for your order").</summary>
    public string? InvoiceNote { get; set; }

    /// <summary>Days after delivery in which customers may ask to return items; 0: no returns through the shop.</summary>
    public int ReturnDays { get; set; } = DefaultReturnDays;
}
