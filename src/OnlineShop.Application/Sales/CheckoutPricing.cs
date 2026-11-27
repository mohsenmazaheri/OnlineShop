using OnlineShop.Domain.Sales;

namespace OnlineShop.Application.Sales;

/// <summary>The shop's tax as checkout applies it.</summary>
public sealed record TaxView(decimal Rate, bool PricesIncludeTax);

/// <summary>An order's amounts (base currency). <see cref="Tax"/> is part of <see cref="Total"/> either way.</summary>
public sealed record CheckoutTotals(decimal Subtotal, decimal Discount, decimal Shipping, decimal Tax, decimal Total, decimal TaxRate, bool PricesIncludeTax);

/// <summary>
/// The arithmetic of checkout, without any data access: discount, shipping and tax. Every amount is rounded to the
/// currency's decimals (half away from zero), so the totals shown are the totals charged.
/// </summary>
public static class CheckoutPricing
{
    public static decimal Round(decimal amount, int decimals) => Math.Round(amount, decimals, MidpointRounding.AwayFromZero);

    /// <summary>Currencies written without minor units unless the shop set them up otherwise (rial, toman, yen, won).</summary>
    private static readonly string[] WholeCurrencies = ["IRR", "IRT", "JPY", "KRW"];

    /// <summary>The usual decimals of a currency when the shop has not set them up: 0 for rial-like ones, else 2.</summary>
    public static int DefaultDecimals(string currency) => WholeCurrencies.Contains(currency, StringComparer.Ordinal) ? 0 : 2;

    /// <summary>
    /// What a code takes off the goods: a percent (at most the cap) or an amount (at most the goods). Free shipping
    /// takes nothing off the goods (see <see cref="ShippingCost"/>).
    /// </summary>
    public static decimal DiscountFor(DiscountCode? code, decimal subtotal, int decimals)
    {
        if (code is null || subtotal <= 0)
            return 0;
        var discount = code.Kind switch
        {
            DiscountKind.Percent => Round(subtotal * code.Value / 100m, decimals),
            DiscountKind.Amount => code.Value,
            _ => 0,
        };
        if (code.Kind == DiscountKind.Percent && code.MaxDiscount is { } cap)
            discount = Math.Min(discount, cap);
        return Math.Min(discount, subtotal);
    }

    /// <summary>
    /// A method's cost for goods worth <paramref name="goods"/> (after the discount) weighing
    /// <paramref name="weightGrams"/>: the fixed cost plus the cost of every started kilogram; nothing when the goods
    /// reach the method's free threshold, or with a free-shipping code.
    /// </summary>
    public static decimal ShippingCost(ShippingMethod method, decimal goods, int weightGrams, bool freeShippingCode = false)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (freeShippingCode || (method.FreeOver is { } threshold && goods >= threshold))
            return 0;
        var kilograms = (int)Math.Ceiling(Math.Max(0, weightGrams) / 1000m);
        return method.Cost + (method.CostPerKg * kilograms);
    }

    /// <summary>
    /// The totals. Tax is charged on the goods after the discount and on the shipping, at the shop's one rate: taken out
    /// of the amounts when prices include it, added on top when they do not.
    /// </summary>
    public static CheckoutTotals Totals(decimal subtotal, decimal discount, decimal shipping, TaxView tax, int decimals)
    {
        ArgumentNullException.ThrowIfNull(tax);
        var taxable = subtotal - discount + shipping;
        if (tax.Rate <= 0)
            return new CheckoutTotals(subtotal, discount, shipping, 0, taxable, 0, tax.PricesIncludeTax);
        var amount = tax.PricesIncludeTax
            ? Round(taxable * tax.Rate / (100m + tax.Rate), decimals)
            : Round(taxable * tax.Rate / 100m, decimals);
        return new CheckoutTotals(subtotal, discount, shipping, amount, tax.PricesIncludeTax ? taxable : taxable + amount, tax.Rate, tax.PricesIncludeTax);
    }
}
