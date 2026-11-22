using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Inventory;

/// <summary>
/// A place that holds stock (the shop's store room, a second branch). Every shop has exactly one default warehouse:
/// it takes the stock set in the product editor or an import and, later, the online sales. The shop's stock of an item
/// is the sum over all warehouses. Names are for staff only, so they are not translated.
/// </summary>
public sealed class Warehouse : BaseEntity
{
    /// <summary>Short Latin code ("main", "branch_2"), unique.</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? Address { get; set; }

    public bool IsDefault { get; set; }

    /// <summary>An inactive warehouse holds no stock and takes no movements.</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}
