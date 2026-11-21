using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Customers;

namespace OnlineShopping.Application.Catalog.Compare;

/// <summary>
/// A comparison list: product numbers in the order they were added. <paramref name="SignedIn"/> is false for a guest,
/// whose list lives in the browser.
/// </summary>
public sealed record CompareListView(IReadOnlyList<long> Numbers, bool SignedIn = true);

/// <summary>The whole list to keep (the browser merges a guest's list into it after signing in).</summary>
public sealed record SaveCompareListRequest(IReadOnlyList<long> Numbers);

public sealed class SaveCompareListRequestValidator : AbstractValidator<SaveCompareListRequest>
{
    public SaveCompareListRequestValidator() =>
        RuleFor(r => r.Numbers).NotNull().Must(n => n.Count <= 20).WithErrorCode(CompareListService.TooManyCode);
}

/// <summary>
/// A signed-in customer's comparison list (guests keep theirs in the browser). Every query is filtered by the customer
/// id from the session; products that are no longer published simply drop out.
/// </summary>
public sealed class CompareListService(IApplicationDbContext db, TimeProvider clock)
{
    public const string TooManyCode = "account.compare_too_many";

    public async Task<CompareListView> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var numbers = await db.CompareItems.AsNoTracking()
            .Where(c => c.CustomerId == userId)
            .Join(db.Products.Where(p => p.IsPublished), c => c.ProductId, p => p.Id, (c, p) => new { c.SortOrder, p.PublicNumber })
            .OrderBy(x => x.SortOrder)
            .Select(x => x.PublicNumber)
            .ToListAsync(cancellationToken);
        return new CompareListView(numbers);
    }

    /// <summary>Replaces the list with these products (published ones only, repeats dropped, at most four).</summary>
    public async Task<CompareListView> SaveAsync(string userId, IReadOnlyList<long> numbers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        var wanted = numbers.Where(n => n > 0).Distinct().ToList();
        var found = await db.Products.AsNoTracking()
            .Where(p => p.IsPublished && wanted.Contains(p.PublicNumber))
            .Select(p => new { p.Id, p.PublicNumber })
            .ToListAsync(cancellationToken);
        var kept = found.OrderBy(p => wanted.IndexOf(p.PublicNumber)).Take(ProductComparisonService.MaxProducts).ToList();
        var now = clock.GetUtcNow().UtcDateTime;

        await db.InTransactionAsync<bool>(async () =>
        {
            await db.CompareItems.Where(c => c.CustomerId == userId).ExecuteDeleteAsync(cancellationToken);
            db.CompareItems.AddRange(kept.Select((p, i) => new CompareItem { CustomerId = userId, ProductId = p.Id, SortOrder = i, AddedUtc = now }));
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
        db.ChangeTracker.Clear();
        return new CompareListView(kept.Select(p => p.PublicNumber).ToList());
    }
}
