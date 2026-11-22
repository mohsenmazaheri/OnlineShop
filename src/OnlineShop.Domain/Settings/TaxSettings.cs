using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Settings;

/// <summary>
/// The shop's single tax rate (decided: no per-product rates, no e-invoicing in v1) and whether the prices the admin
/// enters already include it. Shipping is taxed at the same rate. One row per shop; without it there is no tax.
/// </summary>
public sealed class TaxSettings : BaseEntity
{
    /// <summary>Percent, e.g. 10 for 10 %; 0: the shop charges no tax.</summary>
    public decimal Rate { get; set; }

    /// <summary>Prices and shipping costs include the tax (the usual case for consumers); otherwise it is added.</summary>
    public bool PricesIncludeTax { get; set; } = true;
}
