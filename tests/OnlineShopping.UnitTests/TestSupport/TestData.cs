using OnlineShopping.Infrastructure.Persistence;
using OnlineShopping.Infrastructure.Persistence.Seeding;
using OnlineShopping.Domain.Catalog;
using OnlineShopping.Domain.Common;
using OnlineShopping.Domain.Sales;
using OnlineShopping.Application.Authorization;
using OnlineShopping.Infrastructure.Identity;

namespace OnlineShopping.UnitTests.TestSupport;

internal static class TestData
{
    // Public product numbers are unique; each saved test product gets the next one.
    private static long _nextNumber = 5000;

    public static AppUser User(string email = "user@test.local") =>
        new() { UserName = email, Email = email, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant() };

    /// <summary>Saves a full Category → SubCategory → ProductGroup → Product chain.</summary>
    public static async Task<Product> SaveProductAsync(AppDbContext db, string sku = "SKU-1", string? slug = null)
    {
        var category = new Category { Name = "Digital", Slug = slug ?? $"digital-{Guid.NewGuid():N}" };
        var sub = new SubCategory { Name = "Phones", Slug = "phones", Category = category };
        var group = new ProductGroup { Name = "Smartphones", SubCategory = sub };
        var product = new Product { Name = "Phone X", SKU = sku, PublicNumber = Interlocked.Increment(ref _nextNumber), Price = 199.99m, ProductGroup = group };

        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }
}
