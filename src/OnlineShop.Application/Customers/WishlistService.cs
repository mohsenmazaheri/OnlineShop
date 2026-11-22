using Microsoft.EntityFrameworkCore;
using OnlineShop.Application.Catalog;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Customers;

namespace OnlineShop.Application.Customers;

/// <summary>
/// Which products are on the visitor's wishlist (product numbers). <paramref name="SignedIn"/> is false for a guest:
/// the wishlist needs an account.
/// </summary>
public sealed record WishlistIds(IReadOnlyList<long> Numbers, bool SignedIn = true);

/// <summary>
/// A signed-in customer's wishlist. Every query is filtered by the customer id from the session; products that are no
/// longer published are left out of the list (and come back if published again).
/// </summary>
public sealed class WishlistService(IApplicationDbContext db, IProductQueries products, TimeProvider clock)
{
    public const string NotFoundCode = "account.wishlist_product_not_found";
    public const int MaxItems = 200;

    public async Task<WishlistIds> IdsAsync(string userId, CancellationToken cancellationToken = default) =>
        new(await Mine(userId).Select(x => x.PublicNumber).ToListAsync(cancellationToken));

    /// <summary>The products as cards, the most recently added first.</summary>
    public async Task<IReadOnlyList<ProductCard>> ListAsync(string userId, string locale, CancellationToken cancellationToken = default)
    {
        var ids = await Mine(userId).Select(x => x.Id).ToListAsync(cancellationToken);
        return await products.CardsAsync(ids, locale, cancellationToken);
    }

    public async Task<Result<WishlistIds>> AddAsync(string userId, long number, CancellationToken cancellationToken = default)
    {
        var productId = await db.Products.Where(p => p.IsPublished && p.PublicNumber == number).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        if (productId is not { } id)
            return AppError.NotFound(NotFoundCode, $"No product with number {number}.");
        if (!await db.WishlistItems.AnyAsync(w => w.CustomerId == userId && w.ProductId == id, cancellationToken)
            && await db.WishlistItems.CountAsync(w => w.CustomerId == userId, cancellationToken) < MaxItems)
        {
            db.WishlistItems.Add(new WishlistItem { CustomerId = userId, ProductId = id, AddedUtc = clock.GetUtcNow().UtcDateTime });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Added at the same moment from another tab: it is there either way.
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
        return await IdsAsync(userId, cancellationToken);
    }

    public async Task<WishlistIds> RemoveAsync(string userId, long number, CancellationToken cancellationToken = default)
    {
        await db.WishlistItems
            .Where(w => w.CustomerId == userId && db.Products.IgnoreQueryFilters().Any(p => p.Id == w.ProductId && p.PublicNumber == number))
            .ExecuteDeleteAsync(cancellationToken);
        return await IdsAsync(userId, cancellationToken);
    }

    /// <summary>The customer's published wishlist products, the most recently added first.</summary>
    private IQueryable<Domain.Catalog.Product> Mine(string userId) =>
        from w in db.WishlistItems.AsNoTracking()
        where w.CustomerId == userId
        join p in db.Products.AsNoTracking().Where(p => p.IsPublished) on w.ProductId equals p.Id
        orderby w.AddedUtc descending
        select p;
}
