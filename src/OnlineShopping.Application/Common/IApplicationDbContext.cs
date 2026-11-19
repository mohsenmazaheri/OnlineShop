using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OnlineShopping.Domain.Accounts;
using OnlineShopping.Domain.Catalog;
using OnlineShopping.Domain.Customers;
using OnlineShopping.Domain.Pricing;
using OnlineShopping.Domain.Settings;
using OnlineShopping.Domain.Sales;
using OnlineShopping.Domain.Seo;
using OnlineShopping.Domain.Staff;

namespace OnlineShopping.Application.Common;

/// <summary>
/// The data access the application layer needs. Implemented by the Infrastructure <c>AppDbContext</c>;
/// use cases depend on this interface, never on a concrete database provider.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Category> Categories { get; }
    DbSet<SubCategory> SubCategories { get; }
    DbSet<ProductGroup> ProductGroups { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<ProductSpec> ProductSpecs { get; }
    DbSet<SpecOption> SpecOptions { get; }
    DbSet<ProductSpecValue> ProductSpecValues { get; }
    DbSet<Brand> Brands { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<SearchSynonym> SearchSynonyms { get; }
    DbSet<Redirect> Redirects { get; }
    DbSet<Cart> Carts { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<PhoneVerificationCode> PhoneVerificationCodes { get; }
    DbSet<CustomerAddress> CustomerAddresses { get; }
    DbSet<StaffInvitation> StaffInvitations { get; }
    DbSet<StaffAuditEntry> StaffAuditEntries { get; }
    DbSet<Currency> Currencies { get; }
    DbSet<LocaleCurrency> LocaleCurrencies { get; }
    DbSet<StoreBranding> StoreBranding { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Transactions for work that must succeed or fail as a whole (a bulk import, its dry run).</summary>
    DatabaseFacade Database { get; }

    /// <summary>To forget the unsaved changes of a failed step before the next one.</summary>
    ChangeTracker ChangeTracker { get; }
}
