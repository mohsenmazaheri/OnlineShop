using System.Linq.Expressions;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Accounts;
using OnlineShopping.Domain.Catalog;
using OnlineShopping.Domain.Common;
using OnlineShopping.Domain.Customers;
using OnlineShopping.Domain.Inventory;
using OnlineShopping.Domain.Pricing;
using OnlineShopping.Domain.Settings;
using OnlineShopping.Domain.Sales;
using OnlineShopping.Domain.Search;
using OnlineShopping.Domain.Seo;
using OnlineShopping.Domain.Staff;
using OnlineShopping.Infrastructure.Identity;

namespace OnlineShopping.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options), IApplicationDbContext, IDataProtectionKeyContext
{
    /// <summary>
    /// Filter for unique indexes so a soft-deleted row does not block reusing its value.
    /// Column names are snake_case (<c>UseSnakeCaseNamingConvention</c>); the SQL is valid in PostgreSQL and SQLite.
    /// </summary>
    internal const string NotDeletedFilter = "is_deleted = false";

    internal const string OrderStatusCheckConstraintName = "CK_Orders_Status";
    internal const string StockMovementKindCheckConstraintName = "CK_StockMovements_Kind";

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<SubCategory> SubCategories => Set<SubCategory>();
    public DbSet<ProductGroup> ProductGroups => Set<ProductGroup>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductSpec> ProductSpecs => Set<ProductSpec>();
    public DbSet<SpecOption> SpecOptions => Set<SpecOption>();
    public DbSet<ProductSpecValue> ProductSpecValues => Set<ProductSpecValue>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<SearchSynonym> SearchSynonyms => Set<SearchSynonym>();
    public DbSet<Redirect> Redirects => Set<Redirect>();
    public DbSet<ProductVideo> ProductVideos => Set<ProductVideo>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<PhoneVerificationCode> PhoneVerificationCodes => Set<PhoneVerificationCode>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<CompareItem> CompareItems => Set<CompareItem>();
    public DbSet<StaffInvitation> StaffInvitations => Set<StaffInvitation>();
    public DbSet<StaffAuditEntry> StaffAuditEntries => Set<StaffAuditEntry>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<LocaleCurrency> LocaleCurrencies => Set<LocaleCurrency>();
    public DbSet<StoreBranding> StoreBranding => Set<StoreBranding>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    public DbSet<Stocktake> Stocktakes => Set<Stocktake>();
    public DbSet<StocktakeLine> StocktakeLines => Set<StocktakeLine>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<PurchaseReceiptLine> PurchaseReceiptLines => Set<PurchaseReceiptLine>();
    public DbSet<StockItemInfo> StockItems => Set<StockItemInfo>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<SearchDocument> SearchDocuments => Set<SearchDocument>();
    public DbSet<SearchOutboxEntry> SearchOutbox => Set<SearchOutboxEntry>();
    public DbSet<SearchEngineOutboxEntry> SearchEngineOutbox => Set<SearchEngineOutboxEntry>();
    public DbSet<SearchTermStat> SearchTermStats => Set<SearchTermStat>();

    /// <summary>ASP.NET Core Data Protection keys, shared by all app instances (cookies, antiforgery tokens).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money columns: a fixed precision keeps every database provider consistent (no unbounded numeric).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureIdentityTables(builder);
        ConfigureCatalog(builder);
        ConfigureSeo(builder);
        ConfigureCart(builder);
        ConfigureOrders(builder);
        ConfigurePhoneCodes(builder);
        ConfigureAddresses(builder);
        ConfigureStaff(builder);
        ConfigureCurrencies(builder);
        ConfigureInventory(builder);
        ConfigureStocktakes(builder);
        ConfigurePurchasing(builder);
        ConfigureReservations(builder);
        ConfigureSearch(builder);
        ApplySoftDeleteQueryFilters(builder);
    }

    /// <summary>The SQL check that keeps the Kind column in sync with every <see cref="StockMovementKind"/> value.</summary>
    internal static string BuildStockMovementKindCheckSql() =>
        $"kind IN ({string.Join(", ", Enum.GetNames<StockMovementKind>().Select(n => $"'{n}'"))})";

    /// <summary>The SQL check that keeps the Status column in sync with every <see cref="StocktakeStatus"/> value.</summary>
    internal static string BuildStocktakeStatusCheckSql() =>
        $"status IN ({string.Join(", ", Enum.GetNames<StocktakeStatus>().Select(n => $"'{n}'"))})";

    /// <summary>The SQL check that keeps the Status column in sync with every <see cref="PurchaseReceiptStatus"/> value.</summary>
    internal static string BuildPurchaseReceiptStatusCheckSql() =>
        $"status IN ({string.Join(", ", Enum.GetNames<PurchaseReceiptStatus>().Select(n => $"'{n}'"))})";

    /// <summary>The SQL check that keeps the Status column in sync with every <see cref="StockReservationStatus"/> value.</summary>
    internal static string BuildReservationStatusCheckSql() =>
        $"status IN ({string.Join(", ", Enum.GetNames<StockReservationStatus>().Select(n => $"'{n}'"))})";

    /// <summary>The SQL check that keeps the Status column in sync with every <see cref="OrderStatus"/> value.</summary>
    internal static string BuildOrderStatusCheckSql() =>
        $"status IN ({string.Join(", ", Enum.GetNames<OrderStatus>().Select(n => $"'{n}'"))})";

    /// <summary>
    /// Identity sets explicit PascalCase table names ("AspNetUsers") that the snake_case convention does not rename;
    /// in PostgreSQL those would need quoting in every hand-written query.
    /// </summary>
    private static void ConfigureIdentityTables(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.ToTable("users");
            e.Property(u => u.DisplayName).HasMaxLength(100);
            e.Property(u => u.PreferredLocale).HasMaxLength(8);
            e.Property(u => u.PhoneNumber).HasMaxLength(20);
            // Accounts sign in with email OR phone: each must be unique when present.
            e.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ux_users_normalized_email")
                .IsUnique().HasFilter("normalized_email IS NOT NULL");
            e.HasIndex(u => u.PhoneNumber).HasDatabaseName("ux_users_phone_number")
                .IsUnique().HasFilter("phone_number IS NOT NULL");
        });
        b.Entity<IdentityRole>().ToTable("roles");
        b.Entity<IdentityUserRole<string>>().ToTable("user_roles");
        b.Entity<IdentityUserClaim<string>>().ToTable("user_claims");
        b.Entity<IdentityUserLogin<string>>().ToTable("user_logins");
        b.Entity<IdentityUserToken<string>>().ToTable("user_tokens");
        b.Entity<IdentityRoleClaim<string>>().ToTable("role_claims");
    }

    private static void ConfigureCatalog(ModelBuilder b)
    {
        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(200);
            e.HasIndex(x => x.Slug).IsUnique().HasFilter(NotDeletedFilter);
        });

        b.Entity<SubCategory>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(200);
            e.HasIndex(x => new { x.CategoryId, x.Slug }).IsUnique().HasFilter(NotDeletedFilter);
            e.HasOne(s => s.Category)
                .WithMany(c => c.SubCategories)
                .HasForeignKey(s => s.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProductGroup>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.SubCategoryId, x.Name });
            e.HasOne(g => g.SubCategory)
                .WithMany(s => s.Groups)
                .HasForeignKey(g => g.SubCategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Product>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.SKU).HasMaxLength(64);
            e.HasIndex(x => x.SKU).IsUnique().HasFilter(NotDeletedFilter);
            e.HasIndex(x => x.Name);
            e.HasOne(p => p.ProductGroup)
                .WithMany(pg => pg.Products)
                .HasForeignKey(p => p.ProductGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProductImage>(e =>
        {
            e.Property(x => x.Url).HasMaxLength(2048);
            e.Property(x => x.Alt).HasMaxLength(300);
            e.OwnsMany(x => x.AltTranslations, t =>
            {
                t.ToTable("product_image_alt_translations");
                t.WithOwner().HasForeignKey("ProductImageId");
                t.HasKey("ProductImageId", nameof(ImageAltTranslation.Locale));
                t.Property(x => x.Locale).HasMaxLength(8);
                t.Property(x => x.Alt).HasMaxLength(300);
            });
            e.HasOne(i => i.Product)
                .WithMany(p => p.Images)
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.ImageUrl).HasMaxLength(2048);
            e.OwnsMany(x => x.Translations, t => ConfigureTranslation(t, "category_translations", "CategoryId"));
        });
        b.Entity<SubCategory>().OwnsMany(x => x.Translations, t => ConfigureTranslation(t, "sub_category_translations", "SubCategoryId"));
        b.Entity<ProductGroup>().OwnsMany(x => x.Translations, t => ConfigureTranslation(t, "product_group_translations", "ProductGroupId"));
        b.Entity<Brand>(e =>
        {
            e.ToTable("brands");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(200);
            e.Property(x => x.LogoUrl).HasMaxLength(2048);
            e.HasIndex(x => x.Slug).IsUnique().HasFilter(NotDeletedFilter);
            e.OwnsMany(x => x.Translations, t => ConfigureTranslation(t, "brand_translations", "BrandId"));
        });

        b.Entity<ProductVariant>(e =>
        {
            e.ToTable("product_variants");
            e.Property(x => x.Sku).HasMaxLength(64);
            e.HasIndex(x => x.Sku).IsUnique().HasFilter(NotDeletedFilter);
            e.HasIndex(x => x.ProductId);
            e.HasOne(v => v.Product)
                .WithMany(p => p.Variants)
                .HasForeignKey(v => v.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.OwnsMany(x => x.Options, o =>
            {
                o.ToTable("product_variant_options");
                o.WithOwner().HasForeignKey("VariantId");
                o.HasKey("VariantId", nameof(VariantOption.SpecId));
                o.HasOne<ProductSpec>().WithMany().HasForeignKey(x => x.SpecId).OnDelete(DeleteBehavior.Restrict);
                o.HasOne<SpecOption>().WithMany().HasForeignKey(x => x.OptionId).OnDelete(DeleteBehavior.Restrict);
                // "Which variants are beige": the base of colour/size filters.
                o.HasIndex(x => x.OptionId);
            });
        });

        b.Entity<Category>().OwnsMany(x => x.TemplateSpecs, t => ConfigureTemplate(t, "category_template_specs", "CategoryId"));
        b.Entity<SubCategory>().OwnsMany(x => x.TemplateSpecs, t => ConfigureTemplate(t, "sub_category_template_specs", "SubCategoryId"));

        b.Entity<Product>(e =>
        {
            e.HasOne(p => p.Brand)
                .WithMany()
                .HasForeignKey(p => p.BrandId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Slug).HasMaxLength(200);
            // Numbers are never reused, so the index ignores soft deletes on purpose.
            e.HasIndex(x => x.PublicNumber).IsUnique();
            e.OwnsMany(x => x.Translations, t => ConfigureTranslation(t, "product_translations", "ProductId"));
        });

        ConfigureSpecs(b);
    }

    /// <summary>One row per item and language; the item's own columns hold the default language.</summary>
    private static void ConfigureTranslation<TOwner>(OwnedNavigationBuilder<TOwner, CatalogTranslation> t, string table, string ownerKey)
        where TOwner : class
    {
        t.ToTable(table);
        t.WithOwner().HasForeignKey(ownerKey);
        t.HasKey(ownerKey, nameof(CatalogTranslation.Locale));
        t.Property(x => x.Locale).HasMaxLength(8);
        t.Property(x => x.Name).HasMaxLength(300);
        t.Property(x => x.Slug).HasMaxLength(200);
        t.Property(x => x.MetaTitle).HasMaxLength(MetaTitleLength);
        t.Property(x => x.MetaDescription).HasMaxLength(MetaDescriptionLength);
        t.Property(x => x.Tags).HasMaxLength(TagsLength);
    }

    internal const int MetaTitleLength = 200;
    internal const int MetaDescriptionLength = 400;
    internal const int TagsLength = 500;

    private static void ConfigureSeo(ModelBuilder b)
    {
        b.Entity<Product>(e =>
        {
            e.Property(x => x.Gtin).HasMaxLength(14);
            e.Property(x => x.Mpn).HasMaxLength(70);
            e.Property(x => x.MetaTitle).HasMaxLength(MetaTitleLength);
            e.Property(x => x.MetaDescription).HasMaxLength(MetaDescriptionLength);
            e.Property(x => x.Tags).HasMaxLength(TagsLength);
            // Barcode scans and feeds look products up by GTIN.
            e.HasIndex(x => x.Gtin);
        });
        b.Entity<ProductVariant>(e =>
        {
            e.Property(x => x.Gtin).HasMaxLength(14);
            e.HasIndex(x => x.Gtin);
        });

        b.Entity<ProductVideo>(e =>
        {
            e.ToTable("product_videos");
            e.Property(x => x.Url).HasMaxLength(2048);
            e.Property(x => x.PosterUrl).HasMaxLength(2048);
            e.Property(x => x.Title).HasMaxLength(200);
            e.HasIndex(x => x.ProductId);
            e.HasOne(v => v.Product)
                .WithMany(p => p.Videos)
                .HasForeignKey(v => v.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.OwnsMany(x => x.TitleTranslations, t =>
            {
                t.ToTable("product_video_title_translations");
                t.WithOwner().HasForeignKey("ProductVideoId");
                t.HasKey("ProductVideoId", nameof(VideoTitleTranslation.Locale));
                t.Property(x => x.Locale).HasMaxLength(8);
                t.Property(x => x.Title).HasMaxLength(200);
            });
        });

        b.Entity<SearchSynonym>(e =>
        {
            e.ToTable("search_synonyms");
            e.Property(x => x.Locale).HasMaxLength(8);
            e.Property(x => x.Terms).HasMaxLength(1000);
            e.HasIndex(x => x.Locale);
        });

        b.Entity<Redirect>(e =>
        {
            e.ToTable("redirects");
            e.Property(x => x.FromPath).HasMaxLength(500);
            e.Property(x => x.ToPath).HasMaxLength(2048);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => x.FromPath).IsUnique().HasFilter(NotDeletedFilter);
        });
    }

    private static void ConfigureTemplate<TOwner>(OwnedNavigationBuilder<TOwner, TemplateSpec> t, string table, string ownerKey)
        where TOwner : class
    {
        t.ToTable(table);
        t.WithOwner().HasForeignKey(ownerKey);
        t.HasKey(ownerKey, nameof(TemplateSpec.SpecId));
        t.HasOne<ProductSpec>().WithMany().HasForeignKey(x => x.SpecId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureSpecs(ModelBuilder b)
    {
        b.Entity<ProductSpec>(e =>
        {
            e.ToTable("product_specs");
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.GroupName).HasMaxLength(200);
            e.Property(x => x.Unit).HasMaxLength(32);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Better).HasConversion<string>().HasMaxLength(8);
            e.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeletedFilter);
            e.OwnsMany(x => x.Translations, t =>
            {
                t.ToTable("product_spec_translations");
                t.WithOwner().HasForeignKey("SpecId");
                t.HasKey("SpecId", nameof(SpecTranslation.Locale));
                t.Property(x => x.Locale).HasMaxLength(8);
                t.Property(x => x.Name).HasMaxLength(200);
                t.Property(x => x.GroupName).HasMaxLength(200);
                t.Property(x => x.Unit).HasMaxLength(32);
            });
        });

        b.Entity<SpecOption>(e =>
        {
            e.ToTable("spec_options");
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Value).HasMaxLength(200);
            e.HasIndex(x => new { x.SpecId, x.Code }).IsUnique().HasFilter(NotDeletedFilter);
            e.HasOne(o => o.Spec)
                .WithMany(a => a.Options)
                .HasForeignKey(o => o.SpecId)
                .OnDelete(DeleteBehavior.Cascade);
            e.OwnsMany(x => x.Translations, t =>
            {
                t.ToTable("spec_option_translations");
                t.WithOwner().HasForeignKey("OptionId");
                t.HasKey("OptionId", nameof(SpecOptionTranslation.Locale));
                t.Property(x => x.Locale).HasMaxLength(8);
                t.Property(x => x.Value).HasMaxLength(200);
            });
        });

        b.Entity<ProductSpecValue>(e =>
        {
            e.ToTable("product_spec_values");
            e.Property(x => x.TextValue).HasMaxLength(500);
            // Filter and facet lookups: "which products have option X / a number in this range for spec Y".
            e.HasIndex(x => new { x.ProductId, x.SpecId }).IsUnique().HasFilter(NotDeletedFilter);
            e.HasIndex(x => new { x.SpecId, x.OptionId });
            e.HasIndex(x => new { x.SpecId, x.NumberValue });
            e.HasOne(v => v.Product)
                .WithMany(p => p.SpecValues)
                .HasForeignKey(v => v.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.Spec)
                .WithMany()
                .HasForeignKey(v => v.SpecId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.Option)
                .WithMany()
                .HasForeignKey(v => v.OptionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureCurrencies(ModelBuilder b)
    {
        b.Entity<Currency>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(3);
            e.Property(x => x.Symbol).HasMaxLength(12);
            // Rates span many orders of magnitude (1 IRR = 0.0000005 USD): more scale than the money convention.
            e.Property(x => x.Rate).HasPrecision(28, 12);
            e.Property(x => x.RoundingStep).HasPrecision(18, 6);
            e.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeletedFilter);
        });
        b.Entity<StoreBranding>(e =>
        {
            e.ToTable("store_branding");
            e.Property(x => x.PrimaryColor).HasMaxLength(7);
            e.Property(x => x.Corners).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Font).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.HomeLayout).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.LogoUrl).HasMaxLength(2048);
            e.Property(x => x.HeroImageUrl).HasMaxLength(2048);
        });
        b.Entity<LocaleCurrency>(e =>
        {
            e.HasKey(x => x.Locale);
            e.Property(x => x.Locale).HasMaxLength(8);
            e.Property(x => x.CurrencyCode).HasMaxLength(3);
        });
    }

    private static void ConfigureCart(ModelBuilder b)
    {
        b.Entity<Cart>(e =>
        {
            e.HasIndex(x => x.CustomerId).IsUnique().HasFilter(NotDeletedFilter);
            e.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(c => c.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CartItem>(e =>
        {
            e.HasIndex(x => new { x.CartId, x.ProductId }).IsUnique().HasFilter(NotDeletedFilter);
            e.HasOne(i => i.Cart)
                .WithMany(c => c.Items)
                .HasForeignKey(i => i.CartId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Product)
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurePhoneCodes(ModelBuilder b)
    {
        b.Entity<PhoneVerificationCode>(e =>
        {
            e.Property(c => c.PhoneNumber).HasMaxLength(20);
            e.Property(c => c.Purpose).HasConversion<string>().HasMaxLength(16);
            e.Property(c => c.UserId).HasMaxLength(450);
            e.Property(c => c.CodeHash).HasMaxLength(64);
            e.Property(c => c.Salt).HasMaxLength(32);
            // Every lookup is "latest codes for this number".
            e.HasIndex(c => new { c.PhoneNumber, c.Purpose, c.CreatedUtc });
        });
    }

    private static void ConfigureAddresses(ModelBuilder b)
    {
        b.Entity<CustomerAddress>(e =>
        {
            e.ToTable("customer_addresses");
            e.Property(a => a.CustomerId).HasMaxLength(450);
            e.Property(a => a.Label).HasMaxLength(50);
            e.Property(a => a.RecipientName).HasMaxLength(100);
            e.Property(a => a.PhoneNumber).HasMaxLength(20);
            e.Property(a => a.CountryCode).HasMaxLength(2).IsFixedLength();
            e.Property(a => a.Region).HasMaxLength(100);
            e.Property(a => a.City).HasMaxLength(100);
            e.Property(a => a.Line1).HasMaxLength(300);
            e.Property(a => a.Line2).HasMaxLength(300);
            e.Property(a => a.PostalCode).HasMaxLength(20);
            e.HasIndex(a => a.CustomerId);
            e.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(a => a.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CompareItem>(e =>
        {
            e.ToTable("compare_items");
            e.HasKey(c => new { c.CustomerId, c.ProductId });
            e.Property(c => c.CustomerId).HasMaxLength(450);
            e.HasOne<AppUser>().WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Product>().WithMany().HasForeignKey(c => c.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureStaff(ModelBuilder b)
    {
        b.Entity<StaffInvitation>(e =>
        {
            e.ToTable("staff_invitations");
            e.Property(i => i.Email).HasMaxLength(254);
            e.Property(i => i.NormalizedEmail).HasMaxLength(254);
            e.Property(i => i.PhoneNumber).HasMaxLength(20);
            e.Property(i => i.Roles).HasMaxLength(200);
            e.Property(i => i.InvitedByUserId).HasMaxLength(450);
            e.Property(i => i.AcceptedByUserId).HasMaxLength(450);
            e.Ignore(i => i.RoleList);
            // Lookups are "open invitation for this email / phone".
            e.HasIndex(i => i.NormalizedEmail);
            e.HasIndex(i => i.PhoneNumber);
            e.HasOne<AppUser>().WithMany().HasForeignKey(i => i.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<StaffAuditEntry>(e =>
        {
            e.ToTable("staff_audit_entries");
            e.Property(a => a.ActorUserId).HasMaxLength(450);
            e.Property(a => a.TargetUserId).HasMaxLength(450);
            e.Property(a => a.TargetIdentifier).HasMaxLength(254);
            e.Property(a => a.Details).HasMaxLength(300);
            e.Property(a => a.Action).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(a => a.CreatedUtc);
        });
    }

    private static void ConfigureInventory(ModelBuilder b)
    {
        b.Entity<Warehouse>(e =>
        {
            e.ToTable("warehouses");
            e.Property(w => w.Code).HasMaxLength(64);
            e.Property(w => w.Name).HasMaxLength(100);
            e.Property(w => w.Address).HasMaxLength(300);
            e.HasIndex(w => w.Code).IsUnique().HasFilter(NotDeletedFilter);
        });

        b.Entity<StockMovement>(e =>
        {
            e.ToTable("stock_movements", tb => tb.HasCheckConstraint(StockMovementKindCheckConstraintName,
                BuildStockMovementKindCheckSql()));
            e.Property(m => m.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(m => m.Reference).HasMaxLength(100);
            e.Property(m => m.Note).HasMaxLength(500);
            e.Property(m => m.UserId).HasMaxLength(450);
            e.Property(m => m.UnitCost).HasPrecision(18, 4);
            // The ledger of one item, of one warehouse, and the latest lines overall.
            e.HasIndex(m => new { m.ItemId, m.CreatedUtc });
            e.HasIndex(m => new { m.WarehouseId, m.CreatedUtc });
            e.HasIndex(m => m.CreatedUtc);
            e.HasIndex(m => m.TransferId);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(m => m.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Product>().WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(m => m.VariantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<StockLevel>(e =>
        {
            e.ToTable("stock_levels", tb => tb.HasCheckConstraint("CK_StockLevels_Quantity", "quantity >= 0"));
            // Keyed by the item (variant id, else product id): a nullable variant column could not be part of a key.
            e.HasKey(l => new { l.WarehouseId, l.ItemId });
            e.HasIndex(l => l.ItemId);
            e.HasIndex(l => l.ProductId);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(l => l.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(l => l.VariantId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureStocktakes(ModelBuilder b)
    {
        b.Entity<Stocktake>(e =>
        {
            e.ToTable("stocktakes", tb => tb.HasCheckConstraint("CK_Stocktakes_Status", BuildStocktakeStatusCheckSql()));
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(s => s.Note).HasMaxLength(500);
            e.Property(s => s.OpenedByUserId).HasMaxLength(450);
            e.Property(s => s.ClosedByUserId).HasMaxLength(450);
            // Numbers are never reused, so the index ignores soft deletes on purpose.
            e.HasIndex(s => s.Number).IsUnique();
            // At most one open stocktake per warehouse.
            e.HasIndex(s => s.WarehouseId).IsUnique().HasFilter("status = 'Open' AND is_deleted = false");
            e.HasOne<Warehouse>().WithMany().HasForeignKey(s => s.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(s => s.OpenedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(s => s.ClosedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(s => s.Lines).WithOne().HasForeignKey(l => l.StocktakeId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StocktakeLine>(e =>
        {
            e.ToTable("stocktake_lines");
            e.HasKey(l => new { l.StocktakeId, l.ItemId });
            e.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(l => l.VariantId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>Costs keep four decimals: an average of several purchases is rarely a whole cent.</summary>
    private static void ConfigurePurchasing(ModelBuilder b)
    {
        b.Entity<Supplier>(e =>
        {
            e.ToTable("suppliers");
            e.Property(s => s.Name).HasMaxLength(150);
            e.Property(s => s.ContactName).HasMaxLength(150);
            e.Property(s => s.Phone).HasMaxLength(30);
            e.Property(s => s.Email).HasMaxLength(254);
            e.Property(s => s.Address).HasMaxLength(300);
            e.Property(s => s.Note).HasMaxLength(500);
            e.HasIndex(s => s.Name);
        });

        b.Entity<PurchaseReceipt>(e =>
        {
            e.ToTable("purchase_receipts", tb => tb.HasCheckConstraint("CK_PurchaseReceipts_Status", BuildPurchaseReceiptStatusCheckSql()));
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(p => p.SupplierReference).HasMaxLength(100);
            e.Property(p => p.Note).HasMaxLength(500);
            e.Property(p => p.Currency).HasMaxLength(3);
            e.Property(p => p.CreatedByUserId).HasMaxLength(450);
            e.Property(p => p.ClosedByUserId).HasMaxLength(450);
            // Numbers are never reused, so the index ignores soft deletes on purpose.
            e.HasIndex(p => p.Number).IsUnique();
            e.HasIndex(p => p.SupplierId);
            e.HasOne<Supplier>().WithMany().HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(p => p.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(p => p.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(p => p.ClosedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(p => p.Lines).WithOne().HasForeignKey(l => l.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PurchaseReceiptLine>(e =>
        {
            e.ToTable("purchase_receipt_lines", tb => tb.HasCheckConstraint("CK_PurchaseReceiptLines_Values", "quantity > 0 AND unit_cost >= 0"));
            e.HasKey(l => new { l.ReceiptId, l.ItemId });
            e.Property(l => l.UnitCost).HasPrecision(18, 4);
            e.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(l => l.VariantId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<StockItemInfo>(e =>
        {
            e.ToTable("stock_items", tb => tb.HasCheckConstraint("CK_StockItems_Values", "(average_cost IS NULL OR average_cost >= 0) AND (minimum_stock IS NULL OR minimum_stock >= 0)"));
            e.HasKey(i => i.ItemId);
            e.Property(i => i.AverageCost).HasPrecision(18, 4);
            e.HasIndex(i => i.ProductId);
            e.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(i => i.VariantId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureReservations(ModelBuilder b)
    {
        b.Entity<StockReservation>(e =>
        {
            e.ToTable("stock_reservations", tb =>
            {
                tb.HasCheckConstraint("CK_StockReservations_Status", BuildReservationStatusCheckSql());
                tb.HasCheckConstraint("CK_StockReservations_Quantity", "quantity > 0");
            });
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(r => r.Reference).HasMaxLength(100);
            e.HasIndex(r => r.Reference);
            // The expiry job looks for active reservations whose time ran out.
            e.HasIndex(r => new { r.Status, r.ExpiresUtc });
            e.HasIndex(r => r.ItemId);
            e.HasOne<Product>().WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProductVariant>().WithMany().HasForeignKey(r => r.VariantId).OnDelete(DeleteBehavior.Restrict);
        });

        // Reserved is a count of held units: never below zero.
        b.Entity<Product>().ToTable(tb => tb.HasCheckConstraint("CK_Products_Reserved", "reserved >= 0"));
        b.Entity<ProductVariant>().ToTable(tb => tb.HasCheckConstraint("CK_ProductVariants_Reserved", "reserved >= 0"));
    }

    /// <summary>
    /// The search tables. On PostgreSQL the documents table also has a generated full-text column and GIN indexes,
    /// added in its migration (provider-specific SQL stays in the provider's migrations).
    /// </summary>
    private static void ConfigureSearch(ModelBuilder b)
    {
        b.Entity<SearchDocument>(e =>
        {
            e.ToTable("search_documents");
            e.HasKey(d => new { d.ProductId, d.Locale });
            e.Property(d => d.Locale).HasMaxLength(10);
            e.Property(d => d.Title).HasMaxLength(500);
            e.Property(d => d.Keywords).HasMaxLength(2000);
            e.Property(d => d.Details).HasMaxLength(4000);
            e.Property(d => d.Codes).HasMaxLength(4000);
            e.Property(d => d.Description).HasMaxLength(4000);
            e.HasIndex(d => d.Locale);
            e.HasOne<Product>().WithMany().HasForeignKey(d => d.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SearchOutboxEntry>(e =>
        {
            e.ToTable("search_outbox");
            e.Property(o => o.Target).HasConversion<string>().HasMaxLength(16);
        });

        b.Entity<SearchEngineOutboxEntry>(e => e.ToTable("search_engine_outbox"));

        b.Entity<SearchTermStat>(e =>
        {
            e.ToTable("search_term_stats");
            e.HasKey(t => new { t.Locale, t.Term });
            e.Property(t => t.Locale).HasMaxLength(10);
            e.Property(t => t.Term).HasMaxLength(100);
            e.HasIndex(t => new { t.Locale, t.Searches });
        });
    }

    private static void ConfigureOrders(ModelBuilder b)
    {
        b.Entity<Order>(e =>
        {
            // Orders are financial records: deleting a user must never delete their orders.
            e.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CustomerId, x.OrderedUtc });

            e.Property(o => o.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            e.ToTable(tb => tb.HasCheckConstraint(OrderStatusCheckConstraintName, BuildOrderStatusCheckSql()));
        });

        b.Entity<OrderItem>(e =>
        {
            e.Property(x => x.ProductName).HasMaxLength(300);
            e.HasOne(i => i.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            // ProductName/UnitPrice are snapshots; the FK only guarantees the product existed.
            e.HasOne<Product>()
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ApplySoftDeleteQueryFilters(ModelBuilder b)
    {
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType) || entityType.BaseType is not null)
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Not(Expression.Property(parameter, nameof(BaseEntity.IsDeleted)));
            entityType.SetQueryFilter(Expression.Lambda(body, parameter));
        }
    }
}
