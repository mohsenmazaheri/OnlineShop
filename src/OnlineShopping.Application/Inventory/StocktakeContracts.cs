using FluentValidation;
using OnlineShopping.Domain.Inventory;

namespace OnlineShopping.Application.Inventory;

/// <summary>Stable error codes of stocktaking (translated by the web app under admin.inventoryErrors).</summary>
public static class StocktakeErrors
{
    public const string NotFound = "inventory.stocktake_not_found";
    public const string AlreadyOpen = "inventory.stocktake_already_open";
    public const string NotOpen = "inventory.stocktake_not_open";
    public const string NothingCounted = "inventory.stocktake_nothing_counted";
}

public enum StocktakeLineFilter
{
    All,
    Uncounted,
    Counted,
    Differences
}

public sealed record OpenStocktakeRequest(Guid WarehouseId, string? Note);

public sealed class OpenStocktakeRequestValidator : AbstractValidator<OpenStocktakeRequest>
{
    public OpenStocktakeRequestValidator()
    {
        RuleFor(r => r.WarehouseId).NotEmpty().WithErrorCode(InventoryErrors.WarehouseNotFound);
        RuleFor(r => r.Note).MaximumLength(500).WithErrorCode(InventoryErrors.TextTooLong);
    }
}

/// <summary>One count: the quantity found of an item (by SKU). With <see cref="CountRequest.Add"/> it adds to the count so far.</summary>
public sealed record CountInput(string Sku, int Quantity);

/// <summary>
/// Counts to record. Set mode (default) replaces an item's count; add mode adds to it, for counting piece by piece
/// with a barcode scanner.
/// </summary>
public sealed record CountRequest(IReadOnlyList<CountInput> Counts, bool Add = false);

public sealed class CountRequestValidator : AbstractValidator<CountRequest>
{
    public const int MaxCounts = 500;

    public CountRequestValidator()
    {
        RuleFor(r => r.Counts).NotEmpty().WithErrorCode(InventoryErrors.QuantityInvalid)
            .Must(c => c.Count <= MaxCounts).WithErrorCode(InventoryErrors.QuantityInvalid);
        RuleForEach(r => r.Counts).ChildRules(count =>
        {
            count.RuleFor(c => c.Sku).NotEmpty().WithErrorCode(InventoryErrors.ItemNotFound);
            count.RuleFor(c => c.Quantity).InclusiveBetween(0, InventoryLimits.MaxQuantity).WithErrorCode(InventoryErrors.QuantityInvalid);
        });
    }
}

public sealed record StocktakeSummary(
    int Number,
    Guid WarehouseId,
    string Warehouse,
    StocktakeStatus Status,
    string? Note,
    DateTime CreatedUtc,
    DateTime? ClosedUtc,
    int Lines,
    int Counted);

/// <summary>
/// One item of a stocktake. <see cref="Expected"/> is the warehouse's stock at the moment of the count (or now, when
/// not counted yet), so sales and deliveries during the count are not mistaken for differences.
/// </summary>
public sealed record StocktakeLineView(
    Guid ItemId,
    long ProductNumber,
    string Name,
    string? VariantLabel,
    string Sku,
    int Snapshot,
    int Expected,
    int? Counted,
    int? Difference,
    DateTime? CountedUtc);

public sealed record StocktakeTotals(int Lines, int Counted, int WithDifference, int UnitsOver, int UnitsShort);

public sealed record StocktakeView(
    int Number,
    Guid WarehouseId,
    string Warehouse,
    StocktakeStatus Status,
    string? Note,
    DateTime CreatedUtc,
    DateTime? ClosedUtc,
    string? OpenedBy,
    string? ClosedBy,
    StocktakeTotals Totals,
    IReadOnlyList<StocktakeLineView> Lines,
    int Total,
    int Page,
    int PageSize);
