using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Catalog.Transfer;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Staff;
using OnlineShopping.Domain.Inventory;

namespace OnlineShopping.Application.Inventory;

/// <summary>
/// Stocktaking. Opening lists every item with the warehouse's stock at that moment. Counts are entered by SKU (or
/// from a CSV/Excel sheet) while the shop keeps selling: each count remembers its moment, and the difference is
/// counted − the stock at that moment (the ledger gives the stock at any moment: now minus the lines after it).
/// Posting writes one adjustment line per difference, with the reference "ST-{number}"; uncounted items stay as they
/// are. Posted and cancelled stocktakes are read-only.
/// </summary>
public sealed class StocktakeService(
    IApplicationDbContext db,
    StockLedger ledger,
    ICurrentUser currentUser,
    TimeProvider clock,
    TableFiles files,
    IStaffIdentity staff,
    IOptions<StoreOptions> store)
{
    public const int PageSize = 50;

    public static string Reference(int number) => $"ST-{number}";

    public async Task<IReadOnlyList<StocktakeSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var warehouses = await db.Warehouses.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);
        var rows = await db.Stocktakes.AsNoTracking()
            .OrderByDescending(s => s.Number)
            .Select(s => new { s.Number, s.WarehouseId, s.Status, s.Note, s.CreatedUtc, s.ClosedUtc, Lines = s.Lines.Count(), Counted = s.Lines.Count(l => l.CountedQuantity != null) })
            .ToListAsync(cancellationToken);
        return rows.Select(s => new StocktakeSummary(s.Number, s.WarehouseId, warehouses.GetValueOrDefault(s.WarehouseId, ""), s.Status, s.Note, s.CreatedUtc, s.ClosedUtc, s.Lines, s.Counted))
            .ToList();
    }

    /// <summary>Opens a count of an active warehouse that has no open one: every item, with its stock there now.</summary>
    public async Task<Result<StocktakeSummary>> OpenAsync(OpenStocktakeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var warehouse = await db.Warehouses.AsNoTracking().Where(w => w.Id == request.WarehouseId).Select(w => new { w.Name, w.IsActive }).FirstOrDefaultAsync(cancellationToken);
        if (warehouse is null)
            return AppError.NotFound(InventoryErrors.WarehouseNotFound, "No such warehouse.");
        if (!warehouse.IsActive)
            return AppError.Validation(InventoryErrors.WarehouseInactive, "This warehouse is inactive.");
        if (await db.Stocktakes.AnyAsync(s => s.WarehouseId == request.WarehouseId && s.Status == StocktakeStatus.Open, cancellationToken))
            return AppError.Conflict(StocktakeErrors.AlreadyOpen, "This warehouse is already being counted.");

        var levels = await db.StockLevels.AsNoTracking().Where(l => l.WarehouseId == request.WarehouseId)
            .ToDictionaryAsync(l => l.ItemId, l => l.Quantity, cancellationToken);
        var items = await AllItemsAsync(cancellationToken);
        var last = await db.Stocktakes.IgnoreQueryFilters().MaxAsync(s => (int?)s.Number, cancellationToken) ?? 0;
        var stocktake = new Stocktake
        {
            Number = last + 1,
            WarehouseId = request.WarehouseId,
            Status = StocktakeStatus.Open,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            OpenedByUserId = currentUser.UserId,
        };
        db.Stocktakes.Add(stocktake);
        db.StocktakeLines.AddRange(items.Select(i => new StocktakeLine
        {
            StocktakeId = stocktake.Id,
            ItemId = i.ItemId,
            ProductId = i.ProductId,
            VariantId = i.VariantId,
            SnapshotQuantity = levels.GetValueOrDefault(i.ItemId),
        }));
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return new StocktakeSummary(stocktake.Number, stocktake.WarehouseId, warehouse.Name, stocktake.Status, stocktake.Note, stocktake.CreatedUtc, null, items.Count, 0);
    }

    /// <summary>A stocktake with its totals and one page of lines (filtered, searched by name or SKU).</summary>
    public async Task<Result<StocktakeView>> GetAsync(int number, StocktakeLineFilter filter, string? search, int page, string? locale, CancellationToken cancellationToken = default)
    {
        if (await LoadAsync(number, locale, cancellationToken) is not { } loaded)
            return NotFound();
        var (stocktake, lines) = loaded;
        page = Math.Max(1, page);
        var totals = new StocktakeTotals(
            lines.Count,
            lines.Count(l => l.Counted is not null),
            lines.Count(l => l.Difference is not null and not 0),
            lines.Where(l => l.Difference > 0).Sum(l => l.Difference!.Value),
            lines.Where(l => l.Difference < 0).Sum(l => -l.Difference!.Value));

        IEnumerable<StocktakeLineView> shown = filter switch
        {
            StocktakeLineFilter.Uncounted => lines.Where(l => l.Counted is null),
            StocktakeLineFilter.Counted => lines.Where(l => l.Counted is not null),
            StocktakeLineFilter.Differences => lines.Where(l => l.Difference is not null and not 0),
            _ => lines,
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            shown = shown.Where(l => l.Sku.Contains(term, StringComparison.OrdinalIgnoreCase) || l.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        }
        var matching = shown.ToList();

        var warehouse = await db.Warehouses.IgnoreQueryFilters().AsNoTracking().Where(w => w.Id == stocktake.WarehouseId).Select(w => w.Name).SingleAsync(cancellationToken);
        return new StocktakeView(
            stocktake.Number, stocktake.WarehouseId, warehouse, stocktake.Status, stocktake.Note, stocktake.CreatedUtc, stocktake.ClosedUtc,
            await NameOfAsync(stocktake.OpenedByUserId), await NameOfAsync(stocktake.ClosedByUserId),
            totals, matching.Skip((page - 1) * PageSize).Take(PageSize).ToList(), matching.Count, page, PageSize);
    }

    /// <summary>
    /// The stocktake and all its lines as shown: while open, the expected stock is the stock at each count's moment;
    /// once posted, what was posted. Null when there is no such stocktake.
    /// </summary>
    private async Task<(Stocktake Stocktake, List<StocktakeLineView> Lines)?> LoadAsync(int number, string? locale, CancellationToken cancellationToken)
    {
        var stocktake = await db.Stocktakes.AsNoTracking().Include(s => s.Lines).SingleOrDefaultAsync(s => s.Number == number, cancellationToken);
        if (stocktake is null)
            return null;
        var language = store.Value.ContentLocale(locale);
        var expected = stocktake.Status == StocktakeStatus.Open ? await ExpectedAsync(stocktake, cancellationToken) : null;
        var names = await ItemNames.LoadAsync(db, stocktake.Lines.Select(l => new StockItem(l.ProductId, l.VariantId)).ToList(), language, cancellationToken);

        var lines = stocktake.Lines
            .Where(l => names.ContainsKey(l.ItemId))
            .Select(l =>
            {
                var name = names[l.ItemId];
                var expectedNow = expected is null ? l.ExpectedQuantity ?? l.SnapshotQuantity : expected[l.ItemId];
                int? difference = l.CountedQuantity is { } counted ? (expected is null ? l.PostedDifference : counted - expectedNow) : null;
                return new StocktakeLineView(l.ItemId, name.ProductNumber, name.Name, name.VariantLabel, name.Sku, l.SnapshotQuantity, expectedNow, l.CountedQuantity, difference, l.CountedUtc);
            })
            .OrderBy(l => l.ProductNumber).ThenBy(l => l.Sku, StringComparer.Ordinal)
            .ToList();
        return (stocktake, lines);
    }

    /// <summary>Records counts by SKU. An item added to the shop after opening joins the count with its stock now.</summary>
    public async Task<Result<IReadOnlyList<StocktakeLineView>>> CountAsync(int number, CountRequest request, string? locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stocktake = await db.Stocktakes.Include(s => s.Lines).SingleOrDefaultAsync(s => s.Number == number, cancellationToken);
        if (stocktake is null)
            return NotFound();
        if (stocktake.Status != StocktakeStatus.Open)
            return AppError.Conflict(StocktakeErrors.NotOpen, "This stocktake is closed.");

        var resolved = new List<(StockItem Item, int Quantity)>();
        foreach (var count in request.Counts)
        {
            var item = await FindItemAsync(count.Sku, cancellationToken);
            if (!item.IsSuccess)
                return item.Error!;
            resolved.Add((item.Value, count.Quantity));
        }

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var (item, quantity) in resolved)
        {
            var line = stocktake.Lines.FirstOrDefault(l => l.ItemId == item.ItemId);
            if (line is null)
            {
                var stock = await db.StockLevels.Where(l => l.WarehouseId == stocktake.WarehouseId && l.ItemId == item.ItemId).Select(l => (int?)l.Quantity).FirstOrDefaultAsync(cancellationToken) ?? 0;
                line = new StocktakeLine { StocktakeId = stocktake.Id, ItemId = item.ItemId, ProductId = item.ProductId, VariantId = item.VariantId, SnapshotQuantity = stock };
                stocktake.Lines.Add(line);
                db.StocktakeLines.Add(line);
            }
            var counted = request.Add ? (line.CountedQuantity ?? 0) + quantity : quantity;
            if (counted > InventoryLimits.MaxQuantity)
                return AppError.Validation(InventoryErrors.QuantityInvalid, "That count is too large.");
            line.CountedQuantity = counted;
            line.CountedUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        var wanted = resolved.Select(r => r.Item.ItemId).ToHashSet();
        var (_, all) = (await LoadAsync(number, locale, cancellationToken))!.Value;
        return all.Where(l => wanted.Contains(l.ItemId)).ToList();
    }

    /// <summary>Counts from a CSV or Excel sheet with "sku" and "counted" columns (as <see cref="SheetAsync"/> writes): all or nothing.</summary>
    public async Task<Result<ImportReport>> ImportCountsAsync(int number, Stream upload, long length, CancellationToken cancellationToken = default)
    {
        var read = await ProductImportService.ReadTableAsync(files, upload, length, cancellationToken);
        if (!read.IsSuccess)
            return read.Error!;
        var table = read.Value;
        var headers = table.Headers.Select(h => h.Trim().TrimStart('﻿').ToLowerInvariant()).ToList();
        var (skuColumn, countColumn) = (headers.IndexOf(ProductColumns.Sku), headers.IndexOf(SheetColumns.Counted));
        var problems = new List<ImportProblem>();
        if (skuColumn < 0)
            problems.Add(new(1, ProductColumns.Sku, TransferErrors.ColumnMissing));
        if (countColumn < 0)
            problems.Add(new(1, SheetColumns.Counted, TransferErrors.ColumnMissing));
        if (problems.Count > 0)
            return new ImportReport(false, 0, 0, table.Rows.Count, problems);

        var counts = new List<CountInput>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            var sku = skuColumn < row.Count ? row[skuColumn].Trim() : "";
            var cell = countColumn < row.Count ? row[countColumn].Trim() : "";
            if (sku.Length == 0 || cell.Length == 0)
                continue; // a sheet row nobody counted
            var (ok, value) = Cells.Number(cell);
            if (!seen.Add(sku))
                problems.Add(new(i + 2, ProductColumns.Sku, TransferErrors.SkuRepeated));
            else if (!ok || value is not { } quantity || quantity < 0 || quantity > InventoryLimits.MaxQuantity || quantity != decimal.Truncate(quantity))
                problems.Add(new(i + 2, SheetColumns.Counted, TransferErrors.NumberInvalid));
            else if (!(await FindItemAsync(sku, cancellationToken)).IsSuccess)
                problems.Add(new(i + 2, ProductColumns.Sku, TransferErrors.SkuNotFound));
            else
                counts.Add(new CountInput(sku, (int)quantity));
        }
        if (problems.Count > 0 || counts.Count == 0)
            return new ImportReport(false, 0, 0, table.Rows.Count, problems);

        var counted = await CountAsync(number, new CountRequest(counts), null, cancellationToken);
        return counted.IsSuccess ? new ImportReport(true, 0, counts.Count, table.Rows.Count, []) : counted.Error!;
    }

    /// <summary>A count sheet to print or fill in: SKU, name and an empty "counted" column (the expected stock is not shown).</summary>
    public async Task<Result<ExportFile>> SheetAsync(int number, string? format, string? locale, CancellationToken cancellationToken = default)
    {
        if (files.ByFormat(format) is not { } file)
            return AppError.Validation(TransferErrors.FormatUnknown, "Choose csv or xlsx.");
        if (await LoadAsync(number, locale, cancellationToken) is not { } loaded)
            return NotFound();
        var lines = loaded.Lines;
        var rows = lines.Select(l => (IReadOnlyList<string>)[l.Sku, l.VariantLabel is null ? l.Name : $"{l.Name} – {l.VariantLabel}", l.Counted?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""]).ToList();
        return new ExportFile(file.Write(new TableData([ProductColumns.Sku, "name", SheetColumns.Counted], rows)), file.ContentType, $"{Reference(number)}.{file.Format}");
    }

    /// <summary>Writes every counted difference to the ledger as an adjustment ("ST-{number}") and closes the stocktake.</summary>
    public async Task<Result<StocktakeSummary>> PostAsync(int number, CancellationToken cancellationToken = default)
    {
        var stocktake = await db.Stocktakes.Include(s => s.Lines).SingleOrDefaultAsync(s => s.Number == number, cancellationToken);
        if (stocktake is null)
            return NotFound();
        if (stocktake.Status != StocktakeStatus.Open)
            return AppError.Conflict(StocktakeErrors.NotOpen, "This stocktake is closed.");
        var counted = stocktake.Lines.Where(l => l.CountedQuantity is not null).ToList();
        if (counted.Count == 0)
            return AppError.Validation(StocktakeErrors.NothingCounted, "Count at least one item first.");

        var expected = await ExpectedAsync(stocktake, cancellationToken);
        var posted = await db.InTransactionAsync<bool>(async () =>
        {
            foreach (var line in counted)
            {
                var difference = line.CountedQuantity!.Value - expected[line.ItemId];
                if (difference != 0)
                {
                    // Goods sold after the count may already have taken part of a shortage: never below zero.
                    var current = await db.StockLevels.AsNoTracking()
                        .Where(l => l.WarehouseId == stocktake.WarehouseId && l.ItemId == line.ItemId)
                        .Select(l => (int?)l.Quantity).FirstOrDefaultAsync(cancellationToken) ?? 0;
                    difference = Math.Max(difference, -current);
                }
                if (difference != 0)
                {
                    var result = await ledger.PostAsync(stocktake.WarehouseId, new StockItem(line.ProductId, line.VariantId), StockMovementKind.Adjustment,
                        difference, Reference(stocktake.Number), cancellationToken: cancellationToken);
                    if (!result.IsSuccess)
                        return result.Error!;
                }
                line.ExpectedQuantity = expected[line.ItemId];
                line.PostedDifference = difference;
            }
            Close(stocktake, StocktakeStatus.Posted);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
        if (!posted.IsSuccess)
        {
            db.ChangeTracker.Clear();
            return posted.Error!;
        }
        return (await ListAsync(cancellationToken)).Single(s => s.Number == number);
    }

    /// <summary>Drops an open stocktake; the stock is not touched.</summary>
    public async Task<Result<StocktakeSummary>> CancelAsync(int number, CancellationToken cancellationToken = default)
    {
        var stocktake = await db.Stocktakes.SingleOrDefaultAsync(s => s.Number == number, cancellationToken);
        if (stocktake is null)
            return NotFound();
        if (stocktake.Status != StocktakeStatus.Open)
            return AppError.Conflict(StocktakeErrors.NotOpen, "This stocktake is closed.");
        Close(stocktake, StocktakeStatus.Cancelled);
        await db.SaveChangesAsync(cancellationToken);
        return (await ListAsync(cancellationToken)).Single(s => s.Number == number);
    }

    private void Close(Stocktake stocktake, StocktakeStatus status)
    {
        stocktake.Status = status;
        stocktake.ClosedUtc = clock.GetUtcNow().UtcDateTime;
        stocktake.ClosedByUserId = currentUser.UserId;
    }

    /// <summary>
    /// Each line's stock at the moment of its count (now for uncounted lines): the stock now minus the ledger lines
    /// written after that moment.
    /// </summary>
    private async Task<Dictionary<Guid, int>> ExpectedAsync(Stocktake stocktake, CancellationToken cancellationToken)
    {
        var levels = await db.StockLevels.AsNoTracking().Where(l => l.WarehouseId == stocktake.WarehouseId)
            .ToDictionaryAsync(l => l.ItemId, l => l.Quantity, cancellationToken);
        var since = await db.StockMovements.AsNoTracking()
            .Where(m => m.WarehouseId == stocktake.WarehouseId && m.CreatedUtc >= stocktake.CreatedUtc)
            .Select(m => new { m.ItemId, m.CreatedUtc, m.Quantity })
            .ToListAsync(cancellationToken);
        var byItem = since.ToLookup(m => m.ItemId);
        return stocktake.Lines.ToDictionary(
            l => l.ItemId,
            l => levels.GetValueOrDefault(l.ItemId) - (l.CountedUtc is { } at ? byItem[l.ItemId].Where(m => m.CreatedUtc > at).Sum(m => m.Quantity) : 0));
    }

    /// <summary>Every item that can hold stock: products without variants and every variant.</summary>
    private async Task<List<StockItem>> AllItemsAsync(CancellationToken cancellationToken)
    {
        var products = await db.Products.AsNoTracking().Where(p => !p.Variants.Any()).Select(p => p.Id).ToListAsync(cancellationToken);
        var variants = await db.ProductVariants.AsNoTracking().Where(v => !v.Product.IsDeleted).Select(v => new { v.Id, v.ProductId }).ToListAsync(cancellationToken);
        return products.Select(id => new StockItem(id, null)).Concat(variants.Select(v => new StockItem(v.ProductId, v.Id))).ToList();
    }

    private async Task<Result<StockItem>> FindItemAsync(string sku, CancellationToken cancellationToken)
    {
        var code = sku.Trim();
        var variant = await db.ProductVariants.AsNoTracking().Where(v => v.Sku == code).Select(v => new { v.Id, v.ProductId }).FirstOrDefaultAsync(cancellationToken);
        if (variant is not null)
            return new StockItem(variant.ProductId, variant.Id);
        var product = await db.Products.AsNoTracking().Where(p => p.SKU == code).Select(p => new { p.Id, HasVariants = p.Variants.Any() }).FirstOrDefaultAsync(cancellationToken);
        if (product is null)
            return AppError.NotFound(InventoryErrors.ItemNotFound, $"No product or variant has SKU {code}.");
        return product.HasVariants
            ? AppError.Validation(InventoryErrors.ItemHasVariants, "This product's stock is per variant: use a variant's SKU.")
            : new StockItem(product.Id, null);
    }

    private async Task<string?> NameOfAsync(string? userId) => userId is null ? null : (await staff.FindAsync(userId))?.Name;

    private static AppError NotFound() => AppError.NotFound(StocktakeErrors.NotFound, "No such stocktake.");
}

/// <summary>The count sheet's own column (besides "sku" and "name").</summary>
public static class SheetColumns
{
    public const string Counted = "counted";
}
