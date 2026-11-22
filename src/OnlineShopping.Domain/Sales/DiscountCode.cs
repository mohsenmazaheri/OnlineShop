using OnlineShopping.Domain.Common;

namespace OnlineShopping.Domain.Sales;

public enum DiscountKind
{
    /// <summary><see cref="DiscountCode.Value"/> percent of the goods, at most <see cref="DiscountCode.MaxDiscount"/>.</summary>
    Percent,

    /// <summary>A fixed amount off the goods (base currency), never more than the goods cost.</summary>
    Amount,

    /// <summary>The shipping costs nothing.</summary>
    FreeShipping,
}

/// <summary>
/// A code the customer types at checkout. It may need a minimum goods total, run between two dates, be usable a limited
/// number of times in all (<see cref="UsedCount"/> rises atomically when an order uses it) and once per customer.
/// </summary>
public sealed class DiscountCode : BaseEntity
{
    /// <summary>Upper-case letters, digits and dashes, unique among the shop's codes.</summary>
    public required string Code { get; set; }

    public DiscountKind Kind { get; set; }

    /// <summary>Percent (1–100) or amount (base currency); unused for free shipping.</summary>
    public decimal Value { get; set; }

    /// <summary>The goods must cost at least this (base currency).</summary>
    public decimal? MinSubtotal { get; set; }

    /// <summary>Upper limit of a percent discount (base currency).</summary>
    public decimal? MaxDiscount { get; set; }

    public DateTime? StartsUtc { get; set; }

    public DateTime? EndsUtc { get; set; }

    /// <summary>How many orders may use the code in all; null: no limit.</summary>
    public int? UsageLimit { get; set; }

    public int UsedCount { get; set; }

    /// <summary>One order per customer (account, email or phone number).</summary>
    public bool OncePerCustomer { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>A note for staff only ("Newsletter, spring").</summary>
    public string? Note { get; set; }
}
