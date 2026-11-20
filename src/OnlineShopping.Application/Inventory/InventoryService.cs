using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Staff;
using OnlineShopping.Domain.Inventory;

namespace OnlineShopping.Application.Inventory;

/// <summary>
/// Stock for staff: the stock of every item per warehouse, the ledger, and movements entered by hand (receipt,
/// issue, adjustment, transfer between warehouses). Items are found by SKU: a product's own SKU when it has no
/// variants, else a variant's.
/// </summary>
public sealed class InventoryService(
    IApplicationDbContext db,
    StockLedger ledger,
    WarehouseService warehouses,
    IStaffIdentity staff,
    IOptions<StoreOptions> store)
{
    public const int StockPageSize = 20;
    public const int MovementPageSize = 50;

    /// <summary>
    /// A page of products (by number), each as one row or one row per variant, with average cost and minimum stock.
    /// "Only out" keeps products with an item out of stock; "only low" those with an item at or below its minimum.
    /// </summary>
    public async Task<StockPage> StockAsync(string? search, bool onlyOut, int page, string? locale, bool onlyLow = false, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        var language = store.Value.ContentLocale(locale);
        var query = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var number = long.TryParse(term, out var n) ? n : -1;
            // ToLower() runs in SQL (lower()); the culture rules of the analyzers do not apply.
#pragma warning disable CA1304, CA1311, CA1862
            var lower = term.ToLower();
            query = query.Where(p =>
                p.PublicNumber == number
                || p.SKU.ToLower().Contains(lower)
                || p.Name.ToLower().Contains(lower)
                || p.Translations.Any(t => t.Name.ToLower().Contains(lower))
                || p.Variants.Any(v => v.Sku.ToLower().Contains(lower)));
#pragma warning restore CA1304, CA1311, CA1862
        }
        if (onlyOut)
            query = query.Where(p => p.Variants.Any() ? p.Variants.Any(v => v.Stock == 0) : p.Stock == 0);
        if (onlyLow)
        {
            query = query.Where(p => p.Variants.Any()
                ? p.Variants.Any(v => db.StockItems.Any(i => i.ItemId == v.Id && i.MinimumStock != null && v.Stock <= i.MinimumStock))
                : db.StockItems.Any(i => i.ItemId == p.Id && i.MinimumStock != null && p.Stock <= i.MinimumStock));
        }

        var total = await query.CountAsync(cancellationToken);
        var products = await query
            .OrderBy(p => p.PublicNumber)
            .Skip((page - 1) * StockPageSize)
            .Take(StockPageSize)
            .Select(p => new
            {
                p.Id,
                p.PublicNumber,
                Name = p.Translations.Where(t => t.Locale == language).Select(t => t.Name).FirstOrDefault() ?? p.Name,
                p.SKU,
                p.Stock,
                p.Reserved,
                p.IsPublished,
            })
            .ToListAsync(cancellationToken);

        var ids = products.Select(p => p.Id).ToList();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => ids.Contains(v.ProductId))
            .OrderBy(v => v.SortOrder)
            .Select(v => new { v.Id, v.ProductId, v.Sku, v.Stock, v.Reserved, Options = v.Options.Select(o => o.OptionId).ToList() })
            .ToListAsync(cancellationToken);
        var levels = await LevelsAsync(db.StockLevels.Where(l => ids.Contains(l.ProductId)), cancellationToken);
        var labels = await OptionLabelsAsync(variants.SelectMany(v => v.Options), language, cancellationToken);
        var infos = await db.StockItems.AsNoTracking().Where(i => ids.Contains(i.ProductId)).ToDictionaryAsync(i => i.ItemId, cancellationToken);

        var rows = new List<StockRow>();
        foreach (var p in products)
        {
            var own = variants.Where(v => v.ProductId == p.Id).ToList();
            if (own.Count == 0)
                rows.Add(Row(p.Id, p.PublicNumber, p.Name, null, p.SKU, p.IsPublished, p.Stock, p.Reserved, levels.GetValueOrDefault(p.Id, []), infos.GetValueOrDefault(p.Id)));
            rows.AddRange(own.Select(v => Row(
                v.Id, p.PublicNumber, p.Name, Label(v.Options, labels), v.Sku, p.IsPublished, v.Stock, v.Reserved, levels.GetValueOrDefault(v.Id, []), infos.GetValueOrDefault(v.Id))));
        }
        return new StockPage(rows, total, page, StockPageSize, await warehouses.ListAsync(cancellationToken), await LowStockCountAsync(cancellationToken), store.Value.Currency);
    }

    /// <summary>How many items (products without variants, and variants) are at or below their minimum stock.</summary>
    public async Task<int> LowStockCountAsync(CancellationToken cancellationToken = default) =>
        await db.Products.CountAsync(p => !p.Variants.Any() && db.StockItems.Any(i => i.ItemId == p.Id && i.MinimumStock != null && p.Stock <= i.MinimumStock), cancellationToken)
        + await db.ProductVariants.CountAsync(v => !v.Product.IsDeleted && db.StockItems.Any(i => i.ItemId == v.Id && i.MinimumStock != null && v.Stock <= i.MinimumStock), cancellationToken);

    /// <summary>Sets an item's minimum stock (null = no alert) and, when given, its average cost (for stock bought before the ledger).</summary>
    public async Task<Result<ItemSettingsView>> SaveItemSettingsAsync(SaveItemSettingsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = await FindItemAsync(request.Sku, cancellationToken);
        if (!item.IsSuccess)
            return item.Error!;
        var info = await db.StockItems.FirstOrDefaultAsync(i => i.ItemId == item.Value.ItemId, cancellationToken);
        if (info is null)
        {
            info = new StockItemInfo { ItemId = item.Value.ItemId, ProductId = item.Value.ProductId, VariantId = item.Value.VariantId };
            db.StockItems.Add(info);
        }
        info.MinimumStock = request.MinimumStock;
        if (request.AverageCost is { } cost)
            info.AverageCost = decimal.Round(cost, 4, MidpointRounding.AwayFromZero);
        await db.SaveChangesAsync(cancellationToken);
        return new ItemSettingsView(info.ItemId, request.Sku.Trim(), info.MinimumStock, info.AverageCost);
    }

    private static StockRow Row(Guid itemId, long number, string name, string? label, string sku, bool published, int total, int reserved, IReadOnlyList<WarehouseQuantity> levels, StockItemInfo? info) =>
        new(itemId, number, name, label, sku, published, total, reserved, Math.Max(0, total - reserved), levels, info?.AverageCost, info?.MinimumStock, info?.MinimumStock is { } minimum && total <= minimum,
            info?.AverageCost is { } cost ? decimal.Round(total * cost, 2, MidpointRounding.AwayFromZero) : null);

    /// <summary>The ledger, newest first; by SKU (a product with variants: all of them), warehouse and kind.</summary>
    public async Task<MovementPage> MovementsAsync(string? sku, Guid? warehouseId, StockMovementKind? kind, int page, string? locale, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        var language = store.Value.ContentLocale(locale);
        var query = db.StockMovements.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sku))
        {
            var code = sku.Trim();
            var variantId = await db.ProductVariants.IgnoreQueryFilters().Where(v => v.Sku == code && !v.IsDeleted).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(cancellationToken);
            var productId = await db.Products.Where(p => p.SKU == code).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
            query = variantId is { } v ? query.Where(m => m.VariantId == v)
                : productId is { } p ? query.Where(m => m.ProductId == p)
                : query.Where(m => false);
        }
        if (warehouseId is { } warehouse)
            query = query.Where(m => m.WarehouseId == warehouse);
        if (kind is { } k)
            query = query.Where(m => m.Kind == k);

        var total = await query.CountAsync(cancellationToken);
        var lines = await query
            .OrderByDescending(m => m.CreatedUtc).ThenByDescending(m => m.Id)
            .Skip((page - 1) * MovementPageSize)
            .Take(MovementPageSize)
            .ToListAsync(cancellationToken);

        var productIds = lines.Select(m => m.ProductId).Distinct().ToList();
        var products = await db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.PublicNumber, p.SKU, Name = p.Translations.Where(t => t.Locale == language).Select(t => t.Name).FirstOrDefault() ?? p.Name })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var variantIds = lines.Where(m => m.VariantId is not null).Select(m => m.VariantId!.Value).Distinct().ToList();
        var variants = await db.ProductVariants.IgnoreQueryFilters().AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku, Options = v.Options.Select(o => o.OptionId).ToList() })
            .ToDictionaryAsync(v => v.Id, cancellationToken);
        var labels = await OptionLabelsAsync(variants.Values.SelectMany(v => v.Options), language, cancellationToken);
        var warehouseNames = await db.Warehouses.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);
        var users = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var userId in lines.Where(m => m.UserId is not null).Select(m => m.UserId!).Distinct())
            users[userId] = (await staff.FindAsync(userId))?.Name;

        var items = lines.Select(m =>
            {
                var product = products[m.ProductId];
                var variant = m.VariantId is { } id ? variants.GetValueOrDefault(id) : null;
                return new MovementView(
                    m.Id, m.CreatedUtc, m.WarehouseId, warehouseNames.GetValueOrDefault(m.WarehouseId, ""),
                    product.PublicNumber, product.Name, variant is null ? null : Label(variant.Options, labels), variant?.Sku ?? product.SKU,
                    m.Kind, m.Quantity, m.BalanceAfter, m.Reference, m.Note, m.TransferId,
                    m.UserId is null ? null : users.GetValueOrDefault(m.UserId));
            })
            .ToList();
        return new MovementPage(items, total, page, MovementPageSize);
    }

    /// <summary>A receipt, an issue or an adjustment entered by hand. Returns the item's row afterwards.</summary>
    public async Task<Result<StockRow>> PostAsync(PostMovementRequest request, string? locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = await FindItemAsync(request.Sku, cancellationToken);
        if (!item.IsSuccess)
            return item.Error!;
        if (await CheckWarehouseAsync(request.WarehouseId, cancellationToken) is { } problem)
            return problem;
        var quantity = request.Kind == StockMovementKind.Issue ? -request.Quantity : request.Quantity;

        var posted = await db.InTransactionAsync(() => ledger.PostAsync(
            request.WarehouseId, item.Value, request.Kind, quantity, Clean(request.Reference), Clean(request.Note), cancellationToken: cancellationToken), cancellationToken);
        return posted.IsSuccess ? await RowAsync(item.Value, locale, cancellationToken) : posted.Error!;
    }

    /// <summary>Moves stock from one warehouse to another: two lines with one transfer id, saved together.</summary>
    public async Task<Result<StockRow>> TransferAsync(TransferStockRequest request, string? locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.FromWarehouseId == request.ToWarehouseId)
            return AppError.Validation(InventoryErrors.SameWarehouse, "Choose two different warehouses.");
        var item = await FindItemAsync(request.Sku, cancellationToken);
        if (!item.IsSuccess)
            return item.Error!;
        if (await CheckWarehouseAsync(request.FromWarehouseId, cancellationToken) is { } fromProblem)
            return fromProblem;
        if (await CheckWarehouseAsync(request.ToWarehouseId, cancellationToken) is { } toProblem)
            return toProblem;

        var transferId = Guid.CreateVersion7();
        var (reference, note) = (Clean(request.Reference), Clean(request.Note));
        var moved = await db.InTransactionAsync(async () =>
        {
            var taken = await ledger.PostAsync(request.FromWarehouseId, item.Value, StockMovementKind.TransferOut, -request.Quantity, reference, note, transferId, cancellationToken: cancellationToken);
            return taken.IsSuccess
                ? await ledger.PostAsync(request.ToWarehouseId, item.Value, StockMovementKind.TransferIn, request.Quantity, reference, note, transferId, cancellationToken: cancellationToken)
                : taken;
        }, cancellationToken);
        if (!moved.IsSuccess)
        {
            db.ChangeTracker.Clear();
            return moved.Error!;
        }
        return await RowAsync(item.Value, locale, cancellationToken);
    }

    private Task<Result<StockItem>> FindItemAsync(string sku, CancellationToken cancellationToken) => StockItemLookup.FindAsync(db, sku, cancellationToken);

    private async Task<AppError?> CheckWarehouseAsync(Guid id, CancellationToken cancellationToken)
    {
        var warehouse = await db.Warehouses.AsNoTracking().Where(w => w.Id == id).Select(w => new { w.IsActive }).FirstOrDefaultAsync(cancellationToken);
        if (warehouse is null)
            return AppError.NotFound(InventoryErrors.WarehouseNotFound, "No such warehouse.");
        return warehouse.IsActive ? null : AppError.Validation(InventoryErrors.WarehouseInactive, "This warehouse is inactive.");
    }

    private async Task<StockRow> RowAsync(StockItem item, string? locale, CancellationToken cancellationToken)
    {
        var language = store.Value.ContentLocale(locale);
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == item.ProductId)
            .Select(p => new { p.PublicNumber, p.SKU, p.Stock, p.Reserved, p.IsPublished, Name = p.Translations.Where(t => t.Locale == language).Select(t => t.Name).FirstOrDefault() ?? p.Name })
            .SingleAsync(cancellationToken);
        var levels = await LevelsAsync(db.StockLevels.Where(l => l.ItemId == item.ItemId), cancellationToken);
        var info = await db.StockItems.AsNoTracking().FirstOrDefaultAsync(i => i.ItemId == item.ItemId, cancellationToken);
        if (item.VariantId is not { } variantId)
            return Row(item.ItemId, product.PublicNumber, product.Name, null, product.SKU, product.IsPublished, product.Stock, product.Reserved, levels.GetValueOrDefault(item.ItemId, []), info);

        var variant = await db.ProductVariants.AsNoTracking()
            .Where(v => v.Id == variantId)
            .Select(v => new { v.Sku, v.Stock, v.Reserved, Options = v.Options.Select(o => o.OptionId).ToList() })
            .SingleAsync(cancellationToken);
        var labels = await OptionLabelsAsync(variant.Options, language, cancellationToken);
        return Row(item.ItemId, product.PublicNumber, product.Name, Label(variant.Options, labels), variant.Sku, product.IsPublished, variant.Stock, variant.Reserved, levels.GetValueOrDefault(item.ItemId, []), info);
    }

    private static async Task<Dictionary<Guid, IReadOnlyList<WarehouseQuantity>>> LevelsAsync(IQueryable<StockLevel> levels, CancellationToken cancellationToken)
    {
        var rows = await levels.AsNoTracking().Where(l => l.Quantity > 0).Select(l => new { l.ItemId, l.WarehouseId, l.Quantity }).ToListAsync(cancellationToken);
        return rows.GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<WarehouseQuantity>)g.Select(l => new WarehouseQuantity(l.WarehouseId, l.Quantity)).ToList());
    }

    private async Task<Dictionary<Guid, string>> OptionLabelsAsync(IEnumerable<Guid> optionIds, string language, CancellationToken cancellationToken)
    {
        var ids = optionIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];
        return await db.SpecOptions.IgnoreQueryFilters().AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, Value = o.Translations.Where(t => t.Locale == language).Select(t => t.Value).FirstOrDefault() ?? o.Value })
            .ToDictionaryAsync(o => o.Id, o => o.Value, cancellationToken);
    }

    private static string Label(IEnumerable<Guid> options, Dictionary<Guid, string> labels) =>
        string.Join(" / ", options.Select(o => labels.GetValueOrDefault(o, "")));

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
