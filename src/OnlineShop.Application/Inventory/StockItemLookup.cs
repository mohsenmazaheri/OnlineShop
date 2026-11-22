using Microsoft.EntityFrameworkCore;
using OnlineShop.Application.Common;

namespace OnlineShop.Application.Inventory;

/// <summary>Finds the stock item a SKU names: a variant's SKU, or the SKU of a product without variants.</summary>
public static class StockItemLookup
{
    public static async Task<Result<StockItem>> FindAsync(IApplicationDbContext db, string sku, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var code = (sku ?? "").Trim();
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
}
