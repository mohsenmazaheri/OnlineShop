using OnlineShop.Domain.Catalog;
using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Sales;

/// <summary>
/// A way to get an order to the customer, defined in the admin panel: a fixed cost, plus an amount per started
/// kilogram (weight-based), free above a goods total, for some countries only (a zone), or pickup at the shop (no
/// address). Amounts are in the base currency, with or without tax as the shop's prices are. The default language is in
/// the own columns, the others in <see cref="Translations"/> (name and description).
/// </summary>
public sealed class ShippingMethod : BaseEntity
{
    public required string Name { get; set; }

    /// <summary>A short note for the customer ("2–4 working days, tracked").</summary>
    public string? Description { get; set; }

    /// <summary>The customer collects the order at the shop: no shipping address is asked.</summary>
    public bool IsPickup { get; set; }

    public decimal Cost { get; set; }

    /// <summary>Added for every started kilogram of the order's weight (0: the cost does not depend on weight).</summary>
    public decimal CostPerKg { get; set; }

    /// <summary>The method is free when the goods (after any discount) cost at least this; null: never free.</summary>
    public decimal? FreeOver { get; set; }

    /// <summary>Comma-separated ISO country codes the method ships to; empty: every shipping country of the shop.</summary>
    public string Countries { get; set; } = string.Empty;

    public int? MinDays { get; set; }

    public int? MaxDays { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public ICollection<CatalogTranslation> Translations { get; set; } = new List<CatalogTranslation>();

    public IReadOnlyList<string> CountryList =>
        Countries.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Whether the method ships to the country (pickup and "every country" methods always do).</summary>
    public bool ServesCountry(string countryCode) => IsPickup || CountryList.Count == 0 || CountryList.Contains(countryCode, StringComparer.Ordinal);
}
