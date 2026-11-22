using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Catalog;
using OnlineShop.Domain.Sales;

namespace OnlineShop.Application.Catalog.Reviews;

/// <summary>
/// Product reviews. Customers with a confirmed email address or mobile number write one review per product (writing
/// again replaces it and sends it back to moderation); links are refused and a customer can write at most
/// <see cref="MaxPerHour"/> reviews an hour. Only approved reviews are shown and counted in the product's cached
/// rating (<see cref="Product.RatingAverage"/>, <see cref="Product.RatingCount"/>), which every change recomputes.
/// </summary>
public sealed partial class ReviewService(IApplicationDbContext db, IAccountIdentity accounts, ICurrentUser currentUser, TimeProvider clock)
{
    public const int MaxPerHour = 5;
    public const int PageSize = 10;
    public const int AdminPageSize = 25;

    [GeneratedRegex(@"https?://|www\.|\b[a-z0-9-]+\.(com|net|org|ir|io|am|de|info|biz|xyz|link|ly)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    /// <summary>Whether the text holds something that looks like a web address (reviews are not for advertising).</summary>
    public static bool HasLink(string? text) => text is not null && LinkPattern().IsMatch(text);

    public async Task<Result<MyReview>> SubmitAsync(string userId, long number, ReviewInput input, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var productId = await db.Products.Where(p => p.IsPublished && p.PublicNumber == number).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        if (productId is not { } id)
            return AppError.NotFound(ReviewErrors.NotFound, $"No product with number {number}.");
        var user = await accounts.FindAsync(userId);
        if (user is null || !(user.EmailConfirmed || user.PhoneNumberConfirmed))
            return AppError.Forbidden(ReviewErrors.UnverifiedAccount, "Confirm your email address or mobile number before writing a review.");
        if (HasLink(input.Title) || HasLink(input.Body))
            return AppError.Validation(ReviewErrors.LinksNotAllowed, "Reviews cannot contain links.");

        var now = clock.GetUtcNow().UtcDateTime;
        var hourAgo = now.AddHours(-1);
        var recent = await db.ProductReviews.IgnoreQueryFilters()
            .CountAsync(r => r.CustomerId == userId && (r.CreatedUtc > hourAgo || r.ModifiedUtc > hourAgo), cancellationToken);
        if (recent >= MaxPerHour)
            return AppError.TooManyRequests(ReviewErrors.TooMany, "Too many reviews in a short time.");

        var verified = await db.Orders.AnyAsync(
            o => o.CustomerId == userId && (o.Status == OrderStatus.Paid || o.Status == OrderStatus.Shipped || o.Status == OrderStatus.Completed)
                && o.Items.Any(i => i.ProductId == id),
            cancellationToken);
        var review = await db.ProductReviews.FirstOrDefaultAsync(r => r.ProductId == id && r.CustomerId == userId, cancellationToken);
        if (review is null)
        {
            review = new ProductReview { ProductId = id, CustomerId = userId, Body = "", Locale = locale };
            db.ProductReviews.Add(review);
        }
        review.Rating = input.Rating;
        review.Title = Clean(input.Title);
        review.Body = input.Body.Trim();
        review.Locale = locale;
        review.AuthorName = Clean(user.DisplayName) is { } name ? name[..Math.Min(name.Length, 60)] : null;
        review.VerifiedBuyer = verified;
        review.Status = ReviewStatus.Pending;
        review.ModeratedUtc = null;
        review.ModeratedBy = null;
        await db.SaveChangesAsync(cancellationToken);
        await UpdateRatingAsync(db, id, cancellationToken);
        return new MyReview(review.Rating, review.Title, review.Body, review.Status, review.ModifiedUtc ?? review.CreatedUtc);
    }

    /// <summary>Whether the customer may write about this product, and their review if any.</summary>
    public async Task<Result<MyReviewState>> MineAsync(string userId, long number, CancellationToken cancellationToken = default)
    {
        var productId = await db.Products.Where(p => p.IsPublished && p.PublicNumber == number).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        if (productId is null)
            return AppError.NotFound(ReviewErrors.NotFound, $"No product with number {number}.");
        var user = await accounts.FindAsync(userId);
        var review = await db.ProductReviews.AsNoTracking()
            .Where(r => r.ProductId == productId && r.CustomerId == userId)
            .Select(r => new MyReview(r.Rating, r.Title, r.Body, r.Status, r.ModifiedUtc ?? r.CreatedUtc))
            .FirstOrDefaultAsync(cancellationToken);
        return new MyReviewState(true, user is not null && (user.EmailConfirmed || user.PhoneNumberConfirmed), review);
    }

    /// <summary>Removes the customer's own review of the product.</summary>
    public async Task<Result<bool>> DeleteMineAsync(string userId, long number, CancellationToken cancellationToken = default)
    {
        var review = await db.ProductReviews
            .FirstOrDefaultAsync(r => r.CustomerId == userId && db.Products.Any(p => p.Id == r.ProductId && p.PublicNumber == number), cancellationToken);
        if (review is null)
            return AppError.NotFound(ReviewErrors.NotFound, "You have not reviewed this product.");
        db.ProductReviews.Remove(review);
        await db.SaveChangesAsync(cancellationToken);
        await UpdateRatingAsync(db, review.ProductId, cancellationToken);
        return true;
    }

    /// <summary>A page of the product's approved reviews with its rating summary.</summary>
    public async Task<Result<ProductReviews>> ListAsync(long number, int page, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.AsNoTracking().Where(p => p.IsPublished && p.PublicNumber == number)
            .Select(p => new { p.Id, p.RatingAverage, p.RatingCount }).FirstOrDefaultAsync(cancellationToken);
        if (product is null)
            return AppError.NotFound(ReviewErrors.NotFound, $"No product with number {number}.");
        var approved = db.ProductReviews.AsNoTracking().Where(r => r.ProductId == product.Id && r.Status == ReviewStatus.Approved);
        var counts = await approved.GroupBy(r => r.Rating).Select(g => new { Rating = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var stars = Enumerable.Range(0, 5).Select(i => counts.FirstOrDefault(c => c.Rating == 5 - i)?.Count ?? 0).ToList();
        page = Math.Max(1, page);
        var items = await approved
            .OrderByDescending(r => r.CreatedUtc)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(r => new ReviewView(r.Id, r.AuthorName, r.Rating, r.Title, r.Body, r.Locale, r.VerifiedBuyer, r.ModifiedUtc ?? r.CreatedUtc))
            .ToListAsync(cancellationToken);
        return new ProductReviews(new RatingSummary(product.RatingAverage, product.RatingCount, stars), items, page, PageSize, stars.Sum());
    }

    /// <summary>The moderation queue: reviews of one status (all when null), oldest pending first, else newest first.</summary>
    public async Task<ReviewAdminList> AdminListAsync(ReviewStatus? status, int page, CancellationToken cancellationToken = default)
    {
        var query = db.ProductReviews.AsNoTracking().Where(r => status == null || r.Status == status);
        query = status == ReviewStatus.Pending ? query.OrderBy(r => r.CreatedUtc) : query.OrderByDescending(r => r.CreatedUtc);
        page = Math.Max(1, page);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * AdminPageSize).Take(AdminPageSize)
            .Join(db.Products.IgnoreQueryFilters(), r => r.ProductId, p => p.Id, (r, p) => new ReviewAdminRow(
                r.Id, p.PublicNumber, p.Name, r.AuthorName, r.Rating, r.Title, r.Body, r.Locale, r.Status, r.VerifiedBuyer, r.ModifiedUtc ?? r.CreatedUtc))
            .ToListAsync(cancellationToken);
        var pending = await db.ProductReviews.CountAsync(r => r.Status == ReviewStatus.Pending, cancellationToken);
        return new ReviewAdminList(items, page, AdminPageSize, total, pending);
    }

    /// <summary>Approves or rejects a review (the moderator is the signed-in staff member) and updates the rating.</summary>
    public async Task<Result<ReviewAdminRow>> ModerateAsync(Guid id, ReviewStatus status, CancellationToken cancellationToken = default)
    {
        if (status == ReviewStatus.Pending)
            return AppError.Validation(ReviewErrors.StatusInvalid, "Approve or reject the review.");
        var review = await db.ProductReviews.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (review is null)
            return AppError.NotFound(ReviewErrors.NotFound, "No such review.");
        review.Status = status;
        review.ModeratedUtc = clock.GetUtcNow().UtcDateTime;
        review.ModeratedBy = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
        await UpdateRatingAsync(db, review.ProductId, cancellationToken);
        var product = await db.Products.IgnoreQueryFilters().Where(p => p.Id == review.ProductId).Select(p => new { p.PublicNumber, p.Name }).SingleAsync(cancellationToken);
        return new ReviewAdminRow(review.Id, product.PublicNumber, product.Name, review.AuthorName, review.Rating, review.Title, review.Body,
            review.Locale, review.Status, review.VerifiedBuyer, review.ModifiedUtc ?? review.CreatedUtc);
    }

    /// <summary>Recomputes the product's cached rating from its approved reviews.</summary>
    public static async Task UpdateRatingAsync(IApplicationDbContext db, Guid productId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var ratings = await db.ProductReviews.Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved)
            .Select(r => r.Rating).ToListAsync(cancellationToken);
        var count = ratings.Count;
        var average = count == 0 ? 0m : Math.Round((decimal)ratings.Sum() / count, 2, MidpointRounding.AwayFromZero);
        await db.Products.IgnoreQueryFilters().Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.RatingAverage, average).SetProperty(p => p.RatingCount, count), cancellationToken);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
