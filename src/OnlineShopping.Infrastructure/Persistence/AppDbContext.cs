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
using OnlineShopping.Domain.Pricing;
using OnlineShopping.Domain.Settings;
using OnlineShopping.Domain.Sales;
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
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<PhoneVerificationCode> PhoneVerificationCodes => Set<PhoneVerificationCode>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<StaffInvitation> StaffInvitations => Set<StaffInvitation>();
    public DbSet<StaffAuditEntry> StaffAuditEntries => Set<StaffAuditEntry>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<LocaleCurrency> LocaleCurrencies => Set<LocaleCurrency>();
    public DbSet<StoreBranding> StoreBranding => Set<StoreBranding>();

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
        ConfigureCart(builder);
        ConfigureOrders(builder);
        ConfigurePhoneCodes(builder);
        ConfigureAddresses(builder);
        ConfigureStaff(builder);
        ConfigureCurrencies(builder);
        ApplySoftDeleteQueryFilters(builder);
    }

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
