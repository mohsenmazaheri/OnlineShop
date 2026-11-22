using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OnlineShop.Domain.Accounts;
using OnlineShop.Domain.Catalog;
using OnlineShop.Domain.Content;
using OnlineShop.Domain.Customers;
using OnlineShop.Domain.Inventory;
using OnlineShop.Domain.Pricing;
using OnlineShop.Domain.Settings;
using OnlineShop.Domain.Sales;
using OnlineShop.Domain.Search;
using OnlineShop.Domain.Seo;
using OnlineShop.Domain.Staff;

namespace OnlineShop.Application.Common;

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
    DbSet<ProductVideo> ProductVideos { get; }
    DbSet<Cart> Carts { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<PhoneVerificationCode> PhoneVerificationCodes { get; }
    DbSet<CustomerAddress> CustomerAddresses { get; }
    DbSet<CompareItem> CompareItems { get; }
    DbSet<WishlistItem> WishlistItems { get; }
    DbSet<ProductReview> ProductReviews { get; }
    DbSet<LandingPage> LandingPages { get; }
    DbSet<ContentPage> ContentPages { get; }
    DbSet<StaffInvitation> StaffInvitations { get; }
    DbSet<StaffAuditEntry> StaffAuditEntries { get; }
    DbSet<Currency> Currencies { get; }
    DbSet<LocaleCurrency> LocaleCurrencies { get; }
    DbSet<StoreBranding> StoreBranding { get; }
    DbSet<TaxSettings> TaxSettings { get; }
    DbSet<ShippingMethod> ShippingMethods { get; }
    DbSet<DiscountCode> DiscountCodes { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<StockLevel> StockLevels { get; }
    DbSet<Stocktake> Stocktakes { get; }
    DbSet<StocktakeLine> StocktakeLines { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<PurchaseReceipt> PurchaseReceipts { get; }
    DbSet<PurchaseReceiptLine> PurchaseReceiptLines { get; }
    DbSet<StockItemInfo> StockItems { get; }
    DbSet<StockReservation> StockReservations { get; }
    DbSet<SearchDocument> SearchDocuments { get; }
    DbSet<SearchOutboxEntry> SearchOutbox { get; }
    DbSet<SearchEngineOutboxEntry> SearchEngineOutbox { get; }
    DbSet<SearchTermStat> SearchTermStats { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Transactions for work that must succeed or fail as a whole (a bulk import, its dry run).</summary>
    DatabaseFacade Database { get; }

    /// <summary>To forget the unsaved changes of a failed step before the next one.</summary>
    ChangeTracker ChangeTracker { get; }
}
