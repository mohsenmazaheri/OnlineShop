using Microsoft.EntityFrameworkCore;
using OnlineShopping.Application.Catalog.Admin;
using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Inventory;

namespace OnlineShopping.Application.Inventory;

/// <summary>
/// The shop's warehouses. Rules: there is always exactly one default warehouse, and it stays active (another one
/// becomes the default by being made default); a warehouse is deactivated only when it holds no stock; one that ever
/// had a movement is never deleted (its lines must keep pointing at it), only deactivated.
/// </summary>
public sealed class WarehouseService(IApplicationDbContext db, StockLedger ledger)
{
    public async Task<IReadOnlyList<WarehouseView>> ListAsync(CancellationToken cancellationToken = default)
    {
        await ledger.DefaultWarehouseIdAsync(cancellationToken);
        var warehouses = await db.Warehouses.AsNoTracking()
            .OrderByDescending(w => w.IsDefault).ThenBy(w => w.SortOrder).ThenBy(w => w.Name)
            .ToListAsync(cancellationToken);
        var totals = await db.StockLevels.AsNoTracking()
            .Where(l => l.Quantity > 0)
            .GroupBy(l => l.WarehouseId)
            .Select(g => new { Id = g.Key, Units = g.Sum(l => l.Quantity), Items = g.Count() })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var used = (await db.StockMovements.AsNoTracking().Select(m => m.WarehouseId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        return warehouses.Select(w => new WarehouseView(
                w.Id, w.Code, w.Name, w.Address, w.IsDefault, w.IsActive,
                totals.GetValueOrDefault(w.Id)?.Units ?? 0, totals.GetValueOrDefault(w.Id)?.Items ?? 0, used.Contains(w.Id)))
            .ToList();
    }

    public async Task<Result<WarehouseView>> CreateAsync(SaveWarehouseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await ledger.DefaultWarehouseIdAsync(cancellationToken);
        var count = await db.Warehouses.IgnoreQueryFilters().CountAsync(cancellationToken);
        var warehouse = new Warehouse { Code = "", Name = "", SortOrder = count };
        var applied = await ApplyAsync(warehouse, request, cancellationToken);
        if (!applied.IsSuccess)
            return applied.Error!;
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(warehouse.Id, cancellationToken);
    }

    public async Task<Result<WarehouseView>> UpdateAsync(Guid id, SaveWarehouseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var warehouse = await db.Warehouses.SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (warehouse is null)
            return NotFound();
        var applied = await ApplyAsync(warehouse, request, cancellationToken);
        if (!applied.IsSuccess)
            return applied.Error!;
        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(warehouse.Id, cancellationToken);
    }

    /// <summary>Soft delete of a warehouse that never had a movement (any other is deactivated instead).</summary>
    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var warehouse = await db.Warehouses.SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (warehouse is null)
            return NotFound();
        if (warehouse.IsDefault)
            return AppError.Conflict(InventoryErrors.DefaultRequired, "Make another warehouse the default first.");
        if (await db.StockMovements.AnyAsync(m => m.WarehouseId == id, cancellationToken))
            return AppError.Conflict(InventoryErrors.WarehouseUsed, "A warehouse with movements is deactivated, not deleted.");
        db.Warehouses.Remove(warehouse);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<Result<bool>> ApplyAsync(Warehouse warehouse, SaveWarehouseRequest request, CancellationToken cancellationToken)
    {
        var taken = await db.Warehouses.Where(w => w.Id != warehouse.Id).Select(w => w.Code).ToListAsync(cancellationToken);
        var code = SpecAdminService.MakeCode(request.Code, request.Name, taken, "warehouse");
        if (code is null)
            return AppError.Validation(InventoryErrors.CodeInvalid, "A code is Latin lower-case letters, digits and _.");
        if (taken.Contains(code, StringComparer.Ordinal))
            return AppError.Conflict(InventoryErrors.CodeTaken, $"Another warehouse has code {code}.");

        var makeDefault = request.IsDefault && !warehouse.IsDefault;
        if (warehouse.IsDefault && (!request.IsDefault || !request.IsActive))
            return AppError.Conflict(InventoryErrors.DefaultRequired, "Make another warehouse the default first.");
        if (makeDefault && !request.IsActive)
            return AppError.Validation(InventoryErrors.WarehouseInactive, "The default warehouse must be active.");
        if (warehouse.IsActive && !request.IsActive
            && await db.StockLevels.AnyAsync(l => l.WarehouseId == warehouse.Id && l.Quantity > 0, cancellationToken))
            return AppError.Conflict(InventoryErrors.WarehouseHasStock, "Move or write off its stock before deactivating it.");

        if (makeDefault)
        {
            foreach (var other in await db.Warehouses.Where(w => w.IsDefault && w.Id != warehouse.Id).ToListAsync(cancellationToken))
                other.IsDefault = false;
        }
        warehouse.Code = code;
        warehouse.Name = request.Name.Trim();
        warehouse.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        warehouse.IsActive = request.IsActive;
        warehouse.IsDefault = warehouse.IsDefault || makeDefault;
        return true;
    }

    private async Task<WarehouseView> ViewAsync(Guid id, CancellationToken cancellationToken) =>
        (await ListAsync(cancellationToken)).Single(w => w.Id == id);

    private static AppError NotFound() => AppError.NotFound(InventoryErrors.WarehouseNotFound, "No such warehouse.");
}
