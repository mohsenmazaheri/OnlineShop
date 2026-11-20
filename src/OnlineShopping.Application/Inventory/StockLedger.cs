using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Catalog;
using OnlineShopping.Domain.Inventory;

namespace OnlineShopping.Application.Inventory;

/// <summary>
/// The only writer of stock. Every change is a <see cref="StockMovement"/> line plus the same change to the item's
/// <see cref="StockLevel"/> in that warehouse and to the cached totals the shop reads (<see cref="Product.Stock"/> —
/// the sum over all its items and warehouses — and <see cref="ProductVariant.Stock"/>). The level and the totals are
/// changed with one atomic UPDATE each ("quantity = quantity + n" where the result stays at or above zero), so two
/// movements at the same moment can never lose one another or take more than there is.
/// Callers run it inside a transaction (<see cref="Transactions.InTransactionAsync{T}"/>) so the lines of one action
/// are saved together.
/// </summary>
public sealed class StockLedger(IApplicationDbContext db, ICurrentUser currentUser, IOptions<StoreOptions> store)
{
    /// <summary>The default warehouse's name in a new shop, by its default language.</summary>
    public static IReadOnlyDictionary<string, string> DefaultWarehouseNames { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["fa"] = "انبار اصلی",
        ["en"] = "Main warehouse",
        ["hy"] = "Գլխավոր պահեստ",
        ["de"] = "Hauptlager",
        ["ar"] = "المستودع الرئيسي",
    };

    /// <summary>
    /// Adds (positive) or takes out (negative) stock of one item in one warehouse. Returns the new balance there.
    /// A receipt with a <paramref name="unitCost"/> (a purchase) also moves the item's weighted average cost:
    /// (stock before × average + quantity × cost) ÷ (stock before + quantity), over all warehouses. Every line records
    /// a unit cost: the purchase price, else the average at that moment.
    /// </summary>
    public async Task<Result<int>> PostAsync(
        Guid warehouseId,
        StockItem item,
        StockMovementKind kind,
        int quantity,
        string? reference = null,
        string? note = null,
        Guid? transferId = null,
        decimal? unitCost = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (quantity == 0)
            return AppError.Validation(InventoryErrors.QuantityInvalid, "A movement needs a quantity.");
        if (unitCost is < 0)
            return AppError.Validation(InventoryErrors.CostInvalid, "A cost cannot be negative.");
        var itemId = item.ItemId;
        var stockBefore = item.VariantId is { } variant
            ? await db.ProductVariants.IgnoreQueryFilters().Where(v => v.Id == variant).Select(v => v.Stock).SingleAsync(cancellationToken)
            : await db.Products.IgnoreQueryFilters().Where(p => p.Id == item.ProductId).Select(p => p.Stock).SingleAsync(cancellationToken);

        var changed = await db.StockLevels
            .Where(l => l.WarehouseId == warehouseId && l.ItemId == itemId && l.Quantity + quantity >= 0)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Quantity, l => l.Quantity + quantity), cancellationToken);
        int balance;
        if (changed == 0)
        {
            var exists = await db.StockLevels.AnyAsync(l => l.WarehouseId == warehouseId && l.ItemId == itemId, cancellationToken);
            if (exists || quantity < 0)
                return AppError.Conflict(InventoryErrors.Insufficient, "There is not that much stock of this item in this warehouse.");
            db.StockLevels.Add(new StockLevel { WarehouseId = warehouseId, ItemId = itemId, ProductId = item.ProductId, VariantId = item.VariantId, Quantity = quantity });
            balance = quantity;
        }
        else
        {
            balance = await db.StockLevels.Where(l => l.WarehouseId == warehouseId && l.ItemId == itemId).Select(l => l.Quantity).SingleAsync(cancellationToken);
        }

        await db.Products.IgnoreQueryFilters().Where(p => p.Id == item.ProductId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + quantity), cancellationToken);
        if (item.VariantId is { } variantId)
        {
            await db.ProductVariants.IgnoreQueryFilters().Where(v => v.Id == variantId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Stock, v => v.Stock + quantity), cancellationToken);
        }

        var info = await db.StockItems.FirstOrDefaultAsync(i => i.ItemId == itemId, cancellationToken);
        if (kind == StockMovementKind.Receipt && quantity > 0 && unitCost is { } cost)
        {
            if (info is null)
            {
                info = new StockItemInfo { ItemId = itemId, ProductId = item.ProductId, VariantId = item.VariantId };
                db.StockItems.Add(info);
            }
            info.AverageCost = AverageCost(info.AverageCost, stockBefore, quantity, cost);
        }

        var line = new StockMovement
        {
            WarehouseId = warehouseId,
            ProductId = item.ProductId,
            VariantId = item.VariantId,
            ItemId = itemId,
            Kind = kind,
            Quantity = quantity,
            BalanceAfter = balance,
            Reference = reference,
            Note = note,
            TransferId = transferId,
            UserId = currentUser.UserId,
            UnitCost = unitCost ?? info?.AverageCost,
        };
        db.StockMovements.Add(line);
        await db.SaveChangesAsync(cancellationToken);
        Forget<StockLevel>(l => l.WarehouseId == warehouseId && l.ItemId == itemId);
        Forget<StockMovement>(m => m.Id == line.Id);
        Forget<StockItemInfo>(i => i.ItemId == itemId);
        SyncTracked(item, quantity);
        return balance;
    }

    /// <summary>
    /// The weighted average after receiving <paramref name="quantity"/> at <paramref name="cost"/>. Without an average
    /// yet, or with no stock before (nothing left to average with), the new cost is the average. Four decimals.
    /// </summary>
    public static decimal AverageCost(decimal? average, int stockBefore, int quantity, decimal cost)
    {
        if (average is not { } current || stockBefore <= 0)
            return decimal.Round(cost, 4, MidpointRounding.AwayFromZero);
        return decimal.Round((stockBefore * current + quantity * cost) / (stockBefore + quantity), 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Brings an item's stock over all warehouses to <paramref name="target"/> with adjustment lines: an increase goes
    /// into the default warehouse, a decrease comes out of the default warehouse first, then the others. This is how
    /// the product editor, the variant editor and an import set "the stock" of an item.
    /// </summary>
    public async Task<Result<int>> SetTotalAsync(StockItem item, int target, string reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (target is < 0 or > InventoryLimits.MaxQuantity)
            return AppError.Validation(InventoryErrors.QuantityInvalid, "Stock cannot be negative.");
        var itemId = item.ItemId;
        var levels = await db.StockLevels.AsNoTracking()
            .Where(l => l.ItemId == itemId && l.Quantity > 0)
            .Select(l => new { l.WarehouseId, l.Quantity })
            .ToListAsync(cancellationToken);
        var difference = target - levels.Sum(l => l.Quantity);
        if (difference == 0)
            return target;

        var defaultId = await DefaultWarehouseIdAsync(cancellationToken);
        if (difference > 0)
        {
            var posted = await PostAsync(defaultId, item, StockMovementKind.Adjustment, difference, reference, cancellationToken: cancellationToken);
            return posted.IsSuccess ? target : posted.Error!;
        }

        var taken = await TakeAsync(item, -difference, StockMovementKind.Adjustment, reference, cancellationToken);
        return taken.IsSuccess ? target : taken.Error!;
    }

    /// <summary>
    /// Takes <paramref name="quantity"/> units of an item out of stock wherever they are: the default warehouse first,
    /// then the others in their order (a sale, a write-down). One line per warehouse it takes from; refused when the
    /// warehouses together do not hold that many. Run it inside a transaction.
    /// </summary>
    public async Task<Result<bool>> TakeAsync(StockItem item, int quantity, StockMovementKind kind, string? reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (quantity <= 0)
            return AppError.Validation(InventoryErrors.QuantityInvalid, "Take at least one unit.");
        var itemId = item.ItemId;
        var levels = await db.StockLevels.AsNoTracking()
            .Where(l => l.ItemId == itemId && l.Quantity > 0)
            .Select(l => new { l.WarehouseId, l.Quantity })
            .ToListAsync(cancellationToken);
        if (levels.Sum(l => l.Quantity) < quantity)
            return AppError.Conflict(InventoryErrors.Insufficient, "There is not that much stock of this item.");

        var defaultId = await DefaultWarehouseIdAsync(cancellationToken);
        var order = await db.Warehouses.AsNoTracking().OrderBy(w => w.SortOrder).Select(w => w.Id).ToListAsync(cancellationToken);
        var remaining = quantity;
        foreach (var level in levels.OrderBy(l => l.WarehouseId == defaultId ? -1 : order.IndexOf(l.WarehouseId)))
        {
            var take = Math.Min(remaining, level.Quantity);
            var posted = await PostAsync(level.WarehouseId, item, kind, -take, reference, cancellationToken: cancellationToken);
            if (!posted.IsSuccess)
                return posted.Error!;
            remaining -= take;
            if (remaining == 0)
                break;
        }
        return true;
    }

    /// <summary>The default warehouse; a shop without one (a new database) gets "Main warehouse" in its default language.</summary>
    public async Task<Guid> DefaultWarehouseIdAsync(CancellationToken cancellationToken = default)
    {
        var current = await db.Warehouses.Where(w => w.IsDefault).Select(w => (Guid?)w.Id).FirstOrDefaultAsync(cancellationToken);
        if (current is { } id)
            return id;

        // Only possible on a new shop: the rules never leave a shop without a default.
        var taken = await db.Warehouses.IgnoreQueryFilters().Select(w => w.Code).ToListAsync(cancellationToken);
        var code = taken.Contains("main", StringComparer.Ordinal) ? $"main_{taken.Count + 1}" : "main";
        var warehouse = new Warehouse
        {
            Code = code,
            Name = DefaultWarehouseNames.GetValueOrDefault(store.Value.DefaultLocale, DefaultWarehouseNames["en"]),
            IsDefault = true,
        };
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync(cancellationToken);
        return warehouse.Id;
    }

    /// <summary>
    /// Starts the ledger of items that have stock but no lines yet (a shop from before the ledger, or demo products):
    /// one opening line each in the default warehouse. The cached totals already hold that stock, so they are not
    /// changed. Safe to run on every start. Returns the number of lines written.
    /// </summary>
    public async Task<int> OpenUntrackedAsync(CancellationToken cancellationToken = default)
    {
        var tracked = (await db.StockMovements.AsNoTracking().Select(m => m.ItemId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var products = await db.Products.AsNoTracking()
            .Where(p => p.Stock > 0 && !p.Variants.Any())
            .Select(p => new { p.Id, p.Stock })
            .ToListAsync(cancellationToken);
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => v.Stock > 0)
            .Select(v => new { v.Id, v.ProductId, v.Stock })
            .ToListAsync(cancellationToken);
        var items = products.Select(p => (Item: new StockItem(p.Id, null), p.Stock))
            .Concat(variants.Select(v => (Item: new StockItem(v.ProductId, v.Id), v.Stock)))
            .Where(x => !tracked.Contains(x.Item.ItemId))
            .ToList();
        if (items.Count == 0)
            return 0;

        var warehouseId = await DefaultWarehouseIdAsync(cancellationToken);
        foreach (var (item, stock) in items)
        {
            db.StockLevels.Add(new StockLevel { WarehouseId = warehouseId, ItemId = item.ItemId, ProductId = item.ProductId, VariantId = item.VariantId, Quantity = stock });
            db.StockMovements.Add(new StockMovement
            {
                WarehouseId = warehouseId,
                ProductId = item.ProductId,
                VariantId = item.VariantId,
                ItemId = item.ItemId,
                Kind = StockMovementKind.Opening,
                Quantity = stock,
                BalanceAfter = stock,
                Reference = StockReferences.Opening,
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        Forget<StockLevel>(_ => true);
        Forget<StockMovement>(_ => true);
        return items.Count;
    }

    /// <summary>
    /// The totals were changed in the database, not through the change tracker: give loaded copies the new value
    /// without marking them changed, so a later save does not write an old number back.
    /// </summary>
    private void SyncTracked(StockItem item, int quantity)
    {
        foreach (var entry in db.ChangeTracker.Entries<Product>().Where(e => e.Entity.Id == item.ProductId).ToList())
        {
            var stock = entry.Property(p => p.Stock);
            stock.CurrentValue += quantity;
            stock.OriginalValue = stock.CurrentValue;
            stock.IsModified = false;
        }
        if (item.VariantId is not { } variantId)
            return;
        foreach (var entry in db.ChangeTracker.Entries<ProductVariant>().Where(e => e.Entity.Id == variantId).ToList())
        {
            var stock = entry.Property(v => v.Stock);
            stock.CurrentValue += quantity;
            stock.OriginalValue = stock.CurrentValue;
            stock.IsModified = false;
        }
    }

    /// <summary>
    /// Rows written here are not tracked afterwards: levels change by UPDATE past the tracker, and lines are never
    /// changed (a tracked line would also stop a product from being soft-deleted in the same context).
    /// </summary>
    private void Forget<T>(Func<T, bool> match) where T : class
    {
        foreach (var entry in db.ChangeTracker.Entries<T>().Where(e => match(e.Entity)).ToList())
            entry.State = EntityState.Detached;
    }
}
