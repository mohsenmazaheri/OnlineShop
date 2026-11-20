using FluentValidation;
using OnlineShopping.Domain.Inventory;

namespace OnlineShopping.Application.Inventory;

/// <summary>Stable error codes of suppliers and purchase receipts (translated under admin.inventoryErrors).</summary>
public static class PurchasingErrors
{
    public const string SupplierNotFound = "inventory.supplier_not_found";
    public const string SupplierNameRequired = "inventory.supplier_name_required";
    public const string SupplierInactive = "inventory.supplier_inactive";
    public const string SupplierUsed = "inventory.supplier_used";
    public const string EmailInvalid = "inventory.email_invalid";
    public const string ReceiptNotFound = "inventory.receipt_not_found";
    public const string ReceiptNotDraft = "inventory.receipt_not_draft";
    public const string ReceiptEmpty = "inventory.receipt_empty";
    public const string ItemRepeated = "inventory.item_repeated";
}

public sealed record SupplierView(
    Guid Id,
    string Name,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Address,
    string? Note,
    bool IsActive,
    int Receipts);

public sealed record SaveSupplierRequest(string Name, string? ContactName, string? Phone, string? Email, string? Address, string? Note, bool IsActive);

public sealed class SaveSupplierRequestValidator : AbstractValidator<SaveSupplierRequest>
{
    public SaveSupplierRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().WithErrorCode(PurchasingErrors.SupplierNameRequired)
            .MaximumLength(150).WithErrorCode(InventoryErrors.TextTooLong);
        RuleFor(r => r.ContactName).MaximumLength(150).WithErrorCode(InventoryErrors.TextTooLong);
        RuleFor(r => r.Phone).MaximumLength(30).WithErrorCode(InventoryErrors.TextTooLong);
        RuleFor(r => r.Email).MaximumLength(254).WithErrorCode(InventoryErrors.TextTooLong)
            .EmailAddress().When(r => !string.IsNullOrWhiteSpace(r.Email)).WithErrorCode(PurchasingErrors.EmailInvalid);
        RuleFor(r => r.Address).MaximumLength(300).WithErrorCode(InventoryErrors.TextTooLong);
        RuleFor(r => r.Note).MaximumLength(500).WithErrorCode(InventoryErrors.TextTooLong);
    }
}

/// <summary>A receipt line as staff enter it: the item by SKU, the quantity and the cost of one unit.</summary>
public sealed record ReceiptLineInput(string Sku, int Quantity, decimal UnitCost);

/// <summary>A draft receipt: header and the whole set of lines (replaces the previous lines).</summary>
public sealed record SaveReceiptRequest(Guid SupplierId, Guid WarehouseId, string? SupplierReference, string? Note, IReadOnlyList<ReceiptLineInput> Lines);

public sealed class SaveReceiptRequestValidator : AbstractValidator<SaveReceiptRequest>
{
    public const int MaxLines = 500;

    /// <summary>The largest unit cost: well inside numeric(18,4) even multiplied by a quantity.</summary>
    public const decimal MaxUnitCost = 1_000_000_000_000m;

    public SaveReceiptRequestValidator()
    {
        RuleFor(r => r.SupplierId).NotEmpty().WithErrorCode(PurchasingErrors.SupplierNotFound);
        RuleFor(r => r.WarehouseId).NotEmpty().WithErrorCode(InventoryErrors.WarehouseNotFound);
        RuleFor(r => r.SupplierReference).MaximumLength(100).WithErrorCode(InventoryErrors.TextTooLong);
        RuleFor(r => r.Note).MaximumLength(500).WithErrorCode(InventoryErrors.TextTooLong);
        RuleFor(r => r.Lines).Must(l => l.Count <= MaxLines).WithErrorCode(InventoryErrors.QuantityInvalid);
        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Sku).NotEmpty().WithErrorCode(InventoryErrors.ItemNotFound);
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, InventoryLimits.MaxQuantity).WithErrorCode(InventoryErrors.QuantityInvalid);
            line.RuleFor(l => l.UnitCost).InclusiveBetween(0, MaxUnitCost).WithErrorCode(InventoryErrors.CostInvalid);
        });
    }
}

public sealed record ReceiptSummary(
    int Number,
    Guid SupplierId,
    string Supplier,
    Guid WarehouseId,
    string Warehouse,
    PurchaseReceiptStatus Status,
    string? SupplierReference,
    string Currency,
    int Lines,
    int Units,
    decimal Total,
    DateTime CreatedUtc,
    DateTime? ClosedUtc);

public sealed record ReceiptLineView(
    Guid ItemId,
    long ProductNumber,
    string Name,
    string? VariantLabel,
    string Sku,
    int Quantity,
    decimal UnitCost,
    decimal Total);

public sealed record ReceiptView(
    ReceiptSummary Summary,
    string? Note,
    string? CreatedBy,
    string? ClosedBy,
    IReadOnlyList<ReceiptLineView> Lines);

/// <summary>An item's purchase settings: its minimum stock (null = no alert) and, to start from, its average cost.</summary>
public sealed record SaveItemSettingsRequest(string Sku, int? MinimumStock, decimal? AverageCost);

public sealed class SaveItemSettingsRequestValidator : AbstractValidator<SaveItemSettingsRequest>
{
    public SaveItemSettingsRequestValidator()
    {
        RuleFor(r => r.Sku).NotEmpty().WithErrorCode(InventoryErrors.ItemNotFound);
        RuleFor(r => r.MinimumStock).InclusiveBetween(0, InventoryLimits.MaxQuantity).When(r => r.MinimumStock is not null)
            .WithErrorCode(InventoryErrors.MinimumInvalid);
        RuleFor(r => r.AverageCost).InclusiveBetween(0, SaveReceiptRequestValidator.MaxUnitCost).When(r => r.AverageCost is not null)
            .WithErrorCode(InventoryErrors.CostInvalid);
    }
}

public sealed record ItemSettingsView(Guid ItemId, string Sku, int? MinimumStock, decimal? AverageCost);
