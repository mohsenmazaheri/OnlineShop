using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Common;
using OnlineShop.Application.Customers;

namespace OnlineShop.Application.Accounts;

/// <summary>
/// The customer's data rights: download everything stored about them, and delete the account.
/// Deleting erases personal data but keeps orders (financial records) attached to an anonymous account row.
/// </summary>
public sealed class PersonalDataService(
    IAccountIdentity identity,
    IAccountDataIdentity data,
    IApplicationDbContext db,
    AddressService addresses,
    TimeProvider clock,
    IOptions<AuthOptions> auth)
{
    public async Task<Result<PersonalDataExport>> ExportAsync(string userId, CancellationToken cancellationToken = default)
    {
        var account = await data.GetAccountDataAsync(userId);
        if (account is null)
            return AppError.NotFound(AccountErrors.NotFound, "Account not found.");

        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == userId)
            .OrderBy(o => o.OrderedUtc)
            .Select(o => new
            {
                o.Number, o.OrderedUtc, o.Status, o.Subtotal, o.Discount, o.Tax, o.Shipping, o.Total, o.Currency,
                Contact = new OrderContactData(o.Email, o.RecipientName, o.PhoneNumber, o.CountryCode, o.Region, o.City, o.Line1, o.Line2, o.PostalCode, o.Note),
                Items = o.Items.Select(i => new OrderItemData(i.ProductName, i.UnitPrice, i.Quantity)).ToList()
            })
            .ToListAsync(cancellationToken);

        var cart = await db.CartItems.AsNoTracking()
            .Where(i => i.Cart.CustomerId == userId)
            .Select(i => new CartItemData(i.Product.Name, i.Quantity))
            .ToListAsync(cancellationToken);

        var compare = await db.CompareItems.AsNoTracking()
            .Where(c => c.CustomerId == userId)
            .OrderBy(c => c.SortOrder)
            .Join(db.Products.IgnoreQueryFilters(), c => c.ProductId, p => p.Id, (c, p) => new SavedProductData(p.PublicNumber, p.Name, c.AddedUtc))
            .ToListAsync(cancellationToken);

        var wishlist = await db.WishlistItems.AsNoTracking()
            .Where(w => w.CustomerId == userId)
            .OrderBy(w => w.AddedUtc)
            .Join(db.Products.IgnoreQueryFilters(), w => w.ProductId, p => p.Id, (w, p) => new SavedProductData(p.PublicNumber, p.Name, w.AddedUtc))
            .ToListAsync(cancellationToken);

        var reviews = await db.ProductReviews.AsNoTracking().IgnoreQueryFilters()
            .Where(r => r.CustomerId == userId)
            .OrderBy(r => r.CreatedUtc)
            .Join(db.Products.IgnoreQueryFilters(), r => r.ProductId, p => p.Id,
                (r, p) => new ReviewData(p.PublicNumber, p.Name, r.Rating, r.Title, r.Body, r.Status.ToString(), r.CreatedUtc))
            .ToListAsync(cancellationToken);

        return new PersonalDataExport(
            clock.GetUtcNow().UtcDateTime,
            account,
            await addresses.ListAsync(userId, cancellationToken),
            orders.Select(o => new OrderData(o.Number, o.OrderedUtc, o.Status.ToString(), o.Subtotal, o.Discount, o.Tax, o.Shipping, o.Total, o.Currency, o.Contact, o.Items)).ToList(),
            cart,
            compare,
            wishlist,
            reviews);
    }

    /// <param name="signedInAt">When this session's sign-in happened (<see cref="AccountClaims.AuthTime"/>), if known.</param>
    public async Task<Result<bool>> DeleteAsync(string userId, DeleteAccountRequest request, DateTimeOffset? signedInAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await identity.FindAsync(userId);
        if (user is null)
            return AppError.NotFound(AccountErrors.NotFound, "Account not found.");

        // Staff accounts are removed by an administrator (staff management), so a shop never loses its last admin.
        if (user.IsStaff)
            return AppError.Forbidden(ProfileErrors.StaffCannotDelete, "Staff accounts cannot delete themselves.");

        var confirmed = await identity.HasPasswordAsync(userId)
            ? !string.IsNullOrEmpty(request.Password) && await data.CheckPasswordAsync(userId, request.Password)
            : signedInAt is { } at && clock.GetUtcNow() - at <= TimeSpan.FromMinutes(auth.Value.ReauthenticationMinutes);
        if (!confirmed)
            return await identity.HasPasswordAsync(userId)
                ? AppError.Validation(ProfileErrors.PasswordIncorrect, "The password is not correct.")
                : AppError.Forbidden(ProfileErrors.ReauthenticationRequired, "Sign in again, then delete the account.");

        // Real deletes (not soft deletes): the point is that the data is gone. Each step is idempotent, and the
        // account row is erased last, so a failure half-way can simply be retried by the still signed-in user.
        await db.CustomerAddresses.IgnoreQueryFilters().Where(a => a.CustomerId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.CartItems.IgnoreQueryFilters().Where(i => i.Cart.CustomerId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Carts.IgnoreQueryFilters().Where(c => c.CustomerId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.CompareItems.Where(c => c.CustomerId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.WishlistItems.Where(w => w.CustomerId == userId).ExecuteDeleteAsync(cancellationToken);
        // Reviews are the customer's own words: erased, and the products' ratings recomputed without them.
        var reviewed = await db.ProductReviews.IgnoreQueryFilters().Where(r => r.CustomerId == userId).Select(r => r.ProductId).ToListAsync(cancellationToken);
        await db.ProductReviews.IgnoreQueryFilters().Where(r => r.CustomerId == userId).ExecuteDeleteAsync(cancellationToken);
        foreach (var productId in reviewed.Distinct())
            await Catalog.Reviews.ReviewService.UpdateRatingAsync(db, productId, cancellationToken);
        await db.PhoneVerificationCodes
            .Where(c => c.UserId == userId || (user.PhoneNumber != null && c.PhoneNumber == user.PhoneNumber))
            .ExecuteDeleteAsync(cancellationToken);
        // Orders stay (financial records with product snapshots and totals), without who they went to and where.
        await db.Orders.IgnoreQueryFilters().Where(o => o.CustomerId == userId).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Email, (string?)null)
            .SetProperty(o => o.RecipientName, "")
            .SetProperty(o => o.PhoneNumber, "")
            .SetProperty(o => o.Region, (string?)null)
            .SetProperty(o => o.City, (string?)null)
            .SetProperty(o => o.Line1, (string?)null)
            .SetProperty(o => o.Line2, (string?)null)
            .SetProperty(o => o.PostalCode, (string?)null)
            .SetProperty(o => o.Note, (string?)null), cancellationToken);

        await data.EraseAsync(userId);
        return true;
    }
}
