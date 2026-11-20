using FluentValidation;
using OnlineShopping.Domain.Inventory;

namespace OnlineShopping.Application.Inventory;

/// <summary>Stable error codes of the inventory (translated by the web app under admin.inventoryErrors).</summary>
public static class InventoryErrors
{
    public const string WarehouseNotFound = "inventory.warehouse_not_found";
    public const string NameRequired = "inventory.name_required";
    public const string CodeInvalid = "inventory.code_invalid";
    public const string CodeTaken = "inventory.code_taken";
    public const string WarehouseInactive = "inventory.warehouse_inactive";
    public const string WarehouseHasStock = "inventory.warehouse_has_stock";
    public const string WarehouseUsed = "inventory.warehouse_used";
    public const string DefaultRequired = "inventory.default_required";
    public const string ItemNotFound = "inventory.item_not_found";
    public const string ItemHasVariants = "inventory.item_has_variants";
    public const string QuantityInvalid = "inventory.quantity_invalid";
    public const string Insufficient = "inventory.insufficient";
    public const string KindInvalid = "inventory.kind_invalid";
    public const string SameWarehouse = "inventory.same_warehouse";
    public const string TextTooLong = "inventory.text_too_long";
    public const string CostInvalid = "inventory.cost_invalid";
    public const string MinimumInvalid = "inventory.minimum_invalid";
}

/// <summary>
/// Where automatic ledger lines come from ("auto:…"; the web app translates them). A reference typed by staff is shown
/// as is.
/// </summary>
public static class StockReferences
{
    public const string Opening = "auto:opening";
    public const string ProductEditor = "auto:editor";
    public const string Variants = "auto:variants";
    public const string Import = "auto:import";
}

/// <summary>An item that holds stock: a product without variants, or one variant.</summary>
public sealed record StockItem(Guid ProductId, Guid? VariantId)
{
    public Guid ItemId => VariantId ?? ProductId;
}

public sealed record WarehouseView(
    Guid Id,
    string Code,
    string Name,
    string? Address,
    bool IsDefault,
    bool IsActive,
    int Units,
    int Items,
    bool Used);

public sealed record SaveWarehouseRequest(string? Code, string Name, string? Address, bool IsActive, bool IsDefault);

public sealed class SaveWarehouseRequestValidator : AbstractValidator<SaveWarehouseRequest>
{
    public SaveWarehouseRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().WithErrorCode(InventoryErrors.NameRequired)
            .MaximumLength(100).WithErrorCode(InventoryErrors.TextTooLong);
        RuleFor(r => r.Code).MaximumLength(64).WithErrorCode(InventoryErrors.CodeInvalid);
        RuleFor(r => r.Address).MaximumLength(300).WithErrorCode(InventoryErrors.TextTooLong);
    }
}

/// <summary>One item's stock in one warehouse.</summary>
public sealed record WarehouseQuantity(Guid WarehouseId, int Quantity);

/// <summary>One row of the stock list: a product without variants, or a variant.</summary>
public sealed record StockRow(
    Guid ItemId,
    long ProductNumber,
    string Name,
    string? VariantLabel,
    string Sku,
    bool IsPublished,
    int Total,
    int Reserved,
    int Available,
    IReadOnlyList<WarehouseQuantity> Levels,
    decimal? AverageCost,
    int? MinimumStock,
    bool Low,
    decimal? Value);

public sealed record StockPage(
    IReadOnlyList<StockRow> Items,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<WarehouseView> Warehouses,
    int LowCount,
    string Currency);

public sealed record MovementView(
    Guid Id,
    DateTime CreatedUtc,
    Guid WarehouseId,
    string Warehouse,
    long ProductNumber,
    string Name,
    string? VariantLabel,
    string Sku,
    StockMovementKind Kind,
    int Quantity,
    int BalanceAfter,
    string? Reference,
    string? Note,
    Guid? TransferId,
    string? UserName);

public sealed record MovementPage(IReadOnlyList<MovementView> Items, int Total, int Page, int PageSize);

/// <summary>
/// A movement entered by hand. Receipt and Issue take a positive quantity (Issue takes it out); an Adjustment takes a
/// signed one.
/// </summary>
public sealed record PostMovementRequest(string Sku, Guid WarehouseId, StockMovementKind Kind, int Quantity, string? Reference, string? Note);

public sealed record TransferStockRequest(string Sku, Guid FromWarehouseId, Guid ToWarehouseId, int Quantity, string? Reference, string? Note);

public sealed class PostMovementRequestValidator : AbstractValidator<PostMovementRequest>
{
    public PostMovementRequestValidator()
    {
        RuleFor(r => r.Sku).NotEmpty().WithErrorCode(InventoryErrors.ItemNotFound);
        RuleFor(r => r.Kind).Must(k => k is StockMovementKind.Receipt or StockMovementKind.Issue or StockMovementKind.Adjustment)
            .WithErrorCode(InventoryErrors.KindInvalid);
        RuleFor(r => r.Quantity)
            .Must((r, q) => r.Kind == StockMovementKind.Adjustment ? q != 0 && Math.Abs(q) <= InventoryLimits.MaxQuantity : q > 0 && q <= InventoryLimits.MaxQuantity)
            .WithErrorCode(InventoryErrors.QuantityInvalid);
        RuleFor(r => r.Reference).MaximumLength(100).WithErrorCode(InventoryErrors.TextTooLong);
        RuleFor(r => r.Note).MaximumLength(500).WithErrorCode(InventoryErrors.TextTooLong);
    }
}

public sealed class TransferStockRequestValidator : AbstractValidator<TransferStockRequest>
{
    public TransferStockRequestValidator()
    {
        RuleFor(r => r.Sku).NotEmpty().WithErrorCode(InventoryErrors.ItemNotFound);
        RuleFor(r => r.Quantity).InclusiveBetween(1, InventoryLimits.MaxQuantity).WithErrorCode(InventoryErrors.QuantityInvalid);
        RuleFor(r => r.ToWarehouseId).NotEqual(r => r.FromWarehouseId).WithErrorCode(InventoryErrors.SameWarehouse);
        RuleFor(r => r.Reference).MaximumLength(100).WithErrorCode(InventoryErrors.TextTooLong);
        RuleFor(r => r.Note).MaximumLength(500).WithErrorCode(InventoryErrors.TextTooLong);
    }
}

public static class InventoryLimits
{
    /// <summary>The same ceiling as a product's stock in the editor.</summary>
    public const int MaxQuantity = 1_000_000_000;
}
