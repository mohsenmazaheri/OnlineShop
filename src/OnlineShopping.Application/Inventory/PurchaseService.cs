using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Staff;
using OnlineShopping.Domain.Inventory;

namespace OnlineShopping.Application.Inventory;

/// <summary>
/// Purchase receipts. A receipt is written as a draft (supplier, warehouse, lines by SKU with quantity and unit cost;
/// totals always computed here), then posted: each line becomes a receipt line of the ledger ("PR-{number}") with its
/// cost, which also moves the item's weighted average cost. Posted and cancelled receipts are read-only; goods sent
/// back are an issue in the ledger.
/// </summary>
public sealed class PurchaseService(
    IApplicationDbContext db,
    StockLedger ledger,
    ICurrentUser currentUser,
    TimeProvider clock,
    IStaffIdentity staff,
    IOptions<StoreOptions> store)
{
    public static string Reference(int number) => $"PR-{number}";

    public async Task<IReadOnlyList<ReceiptSummary>> ListAsync(PurchaseReceiptStatus? status, CancellationToken cancellationToken = default)
    {
        var query = db.PurchaseReceipts.AsNoTracking();
        if (status is { } wanted)
            query = query.Where(r => r.Status == wanted);
        var receipts = await query.OrderByDescending(r => r.Number).Include(r => r.Lines).ToListAsync(cancellationToken);
        var (suppliers, warehouses) = await NamesAsync(cancellationToken);
        return receipts.Select(r => Summary(r, suppliers, warehouses)).ToList();
    }

    public async Task<Result<ReceiptView>> GetAsync(int number, string? locale, CancellationToken cancellationToken = default)
    {
        var receipt = await db.PurchaseReceipts.AsNoTracking().Include(r => r.Lines).SingleOrDefaultAsync(r => r.Number == number, cancellationToken);
        return receipt is null ? NotFound() : await ViewAsync(receipt, locale, cancellationToken);
    }

    /// <summary>A new draft with its header and lines.</summary>
    public async Task<Result<ReceiptView>> CreateAsync(SaveReceiptRequest request, string? locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var last = await db.PurchaseReceipts.IgnoreQueryFilters().MaxAsync(r => (int?)r.Number, cancellationToken) ?? 0;
        var receipt = new PurchaseReceipt
        {
            Number = last + 1,
            Status = PurchaseReceiptStatus.Draft,
            Currency = store.Value.Currency,
            CreatedByUserId = currentUser.UserId,
        };
        var lines = await ApplyAsync(receipt, request, cancellationToken);
        if (!lines.IsSuccess)
            return lines.Error!;
        db.PurchaseReceipts.Add(receipt);
        db.PurchaseReceiptLines.AddRange(lines.Value);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return await GetAsync(receipt.Number, locale, cancellationToken);
    }

    /// <summary>Replaces a draft's header and lines.</summary>
    public async Task<Result<ReceiptView>> UpdateAsync(int number, SaveReceiptRequest request, string? locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var receipt = await db.PurchaseReceipts.SingleOrDefaultAsync(r => r.Number == number, cancellationToken);
        if (receipt is null)
            return NotFound();
        if (receipt.Status != PurchaseReceiptStatus.Draft)
            return NotDraft();
        var lines = await ApplyAsync(receipt, request, cancellationToken);
        if (!lines.IsSuccess)
            return lines.Error!;
        // Lines are keyed by item: the old set goes and the new one comes in, together.
        await db.InTransactionAsync<bool>(async () =>
        {
            await db.PurchaseReceiptLines.Where(l => l.ReceiptId == receipt.Id).ExecuteDeleteAsync(cancellationToken);
            db.PurchaseReceiptLines.AddRange(lines.Value);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
        db.ChangeTracker.Clear();
        return await GetAsync(number, locale, cancellationToken);
    }

    /// <summary>Brings the goods in: one ledger receipt per line, with its cost, all or nothing.</summary>
    public async Task<Result<ReceiptView>> PostAsync(int number, string? locale, CancellationToken cancellationToken = default)
    {
        var receipt = await db.PurchaseReceipts.Include(r => r.Lines).SingleOrDefaultAsync(r => r.Number == number, cancellationToken);
        if (receipt is null)
            return NotFound();
        if (receipt.Status != PurchaseReceiptStatus.Draft)
            return NotDraft();
        if (receipt.Lines.Count == 0)
            return AppError.Validation(PurchasingErrors.ReceiptEmpty, "Add at least one line first.");
        if (!await db.Suppliers.AnyAsync(s => s.Id == receipt.SupplierId && s.IsActive, cancellationToken))
            return AppError.Validation(PurchasingErrors.SupplierInactive, "This supplier is inactive.");
        if (!await db.Warehouses.AnyAsync(w => w.Id == receipt.WarehouseId && w.IsActive, cancellationToken))
            return AppError.Validation(InventoryErrors.WarehouseInactive, "This warehouse is inactive.");

        var posted = await db.InTransactionAsync<bool>(async () =>
        {
            foreach (var line in receipt.Lines.OrderBy(l => l.SortOrder))
            {
                var result = await ledger.PostAsync(receipt.WarehouseId, new StockItem(line.ProductId, line.VariantId), StockMovementKind.Receipt, line.Quantity,
                    Reference(receipt.Number), receipt.SupplierReference, unitCost: line.UnitCost, cancellationToken: cancellationToken);
                if (!result.IsSuccess)
                    return result.Error!;
            }
            Close(receipt, PurchaseReceiptStatus.Posted);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
        db.ChangeTracker.Clear();
        return posted.IsSuccess ? await GetAsync(number, locale, cancellationToken) : posted.Error!;
    }

    /// <summary>Drops a draft; the stock was never touched.</summary>
    public async Task<Result<ReceiptView>> CancelAsync(int number, string? locale, CancellationToken cancellationToken = default)
    {
        var receipt = await db.PurchaseReceipts.SingleOrDefaultAsync(r => r.Number == number, cancellationToken);
        if (receipt is null)
            return NotFound();
        if (receipt.Status != PurchaseReceiptStatus.Draft)
            return NotDraft();
        Close(receipt, PurchaseReceiptStatus.Cancelled);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return await GetAsync(number, locale, cancellationToken);
    }

    /// <summary>Checks the request, sets the header and returns the new lines (not yet added).</summary>
    private async Task<Result<List<PurchaseReceiptLine>>> ApplyAsync(PurchaseReceipt receipt, SaveReceiptRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Suppliers.AnyAsync(s => s.Id == request.SupplierId, cancellationToken))
            return AppError.NotFound(PurchasingErrors.SupplierNotFound, "No such supplier.");
        if (!await db.Suppliers.AnyAsync(s => s.Id == request.SupplierId && s.IsActive, cancellationToken))
            return AppError.Validation(PurchasingErrors.SupplierInactive, "This supplier is inactive.");
        var warehouse = await db.Warehouses.AsNoTracking().Where(w => w.Id == request.WarehouseId).Select(w => new { w.IsActive }).FirstOrDefaultAsync(cancellationToken);
        if (warehouse is null)
            return AppError.NotFound(InventoryErrors.WarehouseNotFound, "No such warehouse.");
        if (!warehouse.IsActive)
            return AppError.Validation(InventoryErrors.WarehouseInactive, "This warehouse is inactive.");

        var lines = new List<PurchaseReceiptLine>();
        foreach (var input in request.Lines)
        {
            var item = await StockItemLookup.FindAsync(db, input.Sku, cancellationToken);
            if (!item.IsSuccess)
                return item.Error!;
            if (lines.Any(l => l.ItemId == item.Value.ItemId))
                return AppError.Validation(PurchasingErrors.ItemRepeated, $"{input.Sku.Trim()} appears twice; put it on one line.");
            lines.Add(new PurchaseReceiptLine
            {
                ReceiptId = receipt.Id,
                ItemId = item.Value.ItemId,
                ProductId = item.Value.ProductId,
                VariantId = item.Value.VariantId,
                SortOrder = lines.Count,
                Quantity = input.Quantity,
                UnitCost = decimal.Round(input.UnitCost, 4, MidpointRounding.AwayFromZero),
            });
        }

        receipt.SupplierId = request.SupplierId;
        receipt.WarehouseId = request.WarehouseId;
        receipt.SupplierReference = string.IsNullOrWhiteSpace(request.SupplierReference) ? null : request.SupplierReference.Trim();
        receipt.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        return lines;
    }

    private void Close(PurchaseReceipt receipt, PurchaseReceiptStatus status)
    {
        receipt.Status = status;
        receipt.ClosedUtc = clock.GetUtcNow().UtcDateTime;
        receipt.ClosedByUserId = currentUser.UserId;
    }

    private async Task<ReceiptView> ViewAsync(PurchaseReceipt receipt, string? locale, CancellationToken cancellationToken)
    {
        var language = store.Value.ContentLocale(locale);
        var (suppliers, warehouses) = await NamesAsync(cancellationToken);
        var names = await ItemNames.LoadAsync(db, receipt.Lines.Select(l => new StockItem(l.ProductId, l.VariantId)).ToList(), language, cancellationToken);
        var lines = receipt.Lines.OrderBy(l => l.SortOrder)
            .Where(l => names.ContainsKey(l.ItemId))
            .Select(l =>
            {
                var name = names[l.ItemId];
                return new ReceiptLineView(l.ItemId, name.ProductNumber, name.Name, name.VariantLabel, name.Sku, l.Quantity, l.UnitCost, LineTotal(l));
            })
            .ToList();
        return new ReceiptView(Summary(receipt, suppliers, warehouses), receipt.Note, await NameOfAsync(receipt.CreatedByUserId), await NameOfAsync(receipt.ClosedByUserId), lines);
    }

    private static ReceiptSummary Summary(PurchaseReceipt r, Dictionary<Guid, string> suppliers, Dictionary<Guid, string> warehouses) => new(
        r.Number, r.SupplierId, suppliers.GetValueOrDefault(r.SupplierId, ""), r.WarehouseId, warehouses.GetValueOrDefault(r.WarehouseId, ""),
        r.Status, r.SupplierReference, r.Currency, r.Lines.Count, r.Lines.Sum(l => l.Quantity), r.Lines.Sum(LineTotal), r.CreatedUtc, r.ClosedUtc);

    /// <summary>Quantity × unit cost, to the cent (amounts are numeric(18,2) everywhere else).</summary>
    private static decimal LineTotal(PurchaseReceiptLine line) => decimal.Round(line.Quantity * line.UnitCost, 2, MidpointRounding.AwayFromZero);

    private async Task<(Dictionary<Guid, string> Suppliers, Dictionary<Guid, string> Warehouses)> NamesAsync(CancellationToken cancellationToken) =>
        (await db.Suppliers.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken),
         await db.Warehouses.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken));

    private async Task<string?> NameOfAsync(string? userId) => userId is null ? null : (await staff.FindAsync(userId))?.Name;

    private static AppError NotFound() => AppError.NotFound(PurchasingErrors.ReceiptNotFound, "No such purchase receipt.");

    private static AppError NotDraft() => AppError.Conflict(PurchasingErrors.ReceiptNotDraft, "Only a draft can change.");
}
