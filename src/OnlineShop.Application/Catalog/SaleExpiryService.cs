using Microsoft.EntityFrameworkCore;
using OnlineShop.Application.Common;

namespace OnlineShop.Application.Catalog;

/// <summary>
/// Ends special offers whose time is up (<see cref="Domain.Catalog.Product.SaleEndsUtc"/>): the old price becomes the
/// price again, for the product and for variants with their own old price, and the end time is cleared. Run by a
/// background job every minute; storefront caches of such products expire at the end time.
/// </summary>
public sealed class SaleExpiryService(IApplicationDbContext db, TimeProvider clock)
{
    /// <returns>How many products' offers ended.</returns>
    public async Task<int> EndExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var ended = await db.Products.IgnoreQueryFilters()
            .Where(p => p.SaleEndsUtc != null && p.SaleEndsUtc <= now)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        if (ended.Count == 0)
            return 0;

        await db.InTransactionAsync<bool>(async () =>
        {
            await db.ProductVariants.IgnoreQueryFilters()
                .Where(v => ended.Contains(v.ProductId) && v.Price != null && v.CompareAtPrice != null)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Price, v => v.CompareAtPrice).SetProperty(v => v.CompareAtPrice, (decimal?)null), cancellationToken);
            await db.Products.IgnoreQueryFilters()
                .Where(p => ended.Contains(p.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Price, p => p.CompareAtPrice ?? p.Price)
                    .SetProperty(p => p.CompareAtPrice, (decimal?)null)
                    .SetProperty(p => p.SaleEndsUtc, (DateTime?)null)
                    .SetProperty(p => p.ModifiedUtc, now), cancellationToken);
            return true;
        }, cancellationToken);
        return ended.Count;
    }
}
