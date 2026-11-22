using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Catalog;
using OnlineShop.Application.Catalog.Admin;
using OnlineShop.Application.Common;
using OnlineShop.Application.Customers;
using OnlineShop.Application.Media;
using OnlineShop.Application.Notifications;
using OnlineShop.Application.Pricing;
using OnlineShop.Application.Settings;
using OnlineShop.Application.Staff;

namespace OnlineShop.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Search: the database's engine (Infrastructure replaces the simple one on PostgreSQL) answers unless
    /// Search:Provider names an external engine, which then answers while healthy, with the database as fallback.
    /// The provider is read at runtime, so tests and environment variables can switch it.
    /// </summary>
    private static void AddSearch(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<Search.SearchOptions>()
            .Bind(configuration.GetSection(Search.SearchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddScoped<Search.IDatabaseProductSearch, Search.SimpleProductSearch>();
        services.AddSingleton<Search.SearchEngineHealth>();
        services.AddScoped<Search.EngineProductSearch>();
        services.AddScoped<Search.SearchEngineSyncService>();
        services.AddScoped<Search.IProductSearch>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Search.SearchOptions>>().Value.UsesEngine
                ? sp.GetRequiredService<Search.EngineProductSearch>()
                : sp.GetRequiredService<Search.IDatabaseProductSearch>());
    }

    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StoreOptions>()
            .Bind(configuration.GetSection(StoreOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName));

        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>(includeInternalTypes: true);

        services.AddScoped<ICategoryQueries, CategoryQueries>();
        services.AddScoped<IProductQueries, ProductQueries>();
        services.AddScoped<Catalog.Browse.IProductBrowse, Catalog.Browse.ProductBrowseService>();
        AddSearch(services, configuration);
        services.AddScoped<Catalog.Compare.ProductComparisonService>();
        services.AddScoped<Catalog.SaleExpiryService>();
        services.AddScoped<Catalog.LandingPageService>();
        services.AddScoped<Catalog.SitemapService>();
        services.AddScoped<Content.ContentService>();
        services.AddScoped<Sales.CartService>();
        services.AddScoped<Sales.ShippingMethodService>();
        services.AddScoped<Sales.TaxSettingsService>();
        services.AddScoped<Sales.DiscountCodeService>();
        services.AddScoped<Sales.CheckoutService>();
        services.AddScoped<Catalog.Compare.CompareListService>();
        services.AddScoped<Catalog.Reviews.ReviewService>();
        services.AddScoped<Customers.WishlistService>();
        services.AddScoped<Search.ProductTextSearch>();
        services.AddScoped<Search.SearchTermLog>();
        services.AddScoped<Search.SearchIndexService>();
        services.AddScoped<Search.SearchSuggestionService>();
        services.AddScoped<CatalogTreeService>();
        services.AddScoped<ProductAdminService>();
        services.AddScoped<CatalogImageService>();
        services.AddScoped<CatalogVideoService>();
        services.AddScoped<BrandAdminService>();
        services.AddScoped<SpecAdminService>();
        services.AddScoped<ProductVariantService>();
        services.AddScoped<Seo.SynonymService>();
        services.AddScoped<Seo.RedirectService>();
        services.AddSingleton<Catalog.Transfer.ITableFile, Catalog.Transfer.CsvTableFile>();
        services.AddSingleton<Catalog.Transfer.TableFiles>();
        services.AddScoped<Catalog.Transfer.ProductExportService>();
        services.AddScoped<Catalog.Transfer.ProductImportService>();
        services.AddScoped<MediaService>();
        services.TryAddScoped<ICurrentUser, NoCurrentUser>();
        services.AddScoped<Inventory.StockLedger>();
        services.AddScoped<Inventory.WarehouseService>();
        services.AddScoped<Inventory.InventoryService>();
        services.AddScoped<Inventory.StocktakeService>();
        services.AddScoped<Inventory.SupplierService>();
        services.AddScoped<Inventory.PurchaseService>();
        services.AddScoped<Inventory.StockReservationService>();
        services.AddOptions<Inventory.InventoryOptions>()
            .Bind(configuration.GetSection(Inventory.InventoryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<MediaOptions>().Bind(configuration.GetSection(MediaOptions.SectionName));
        services.AddScoped<LoginIdentifierParser>();
        services.AddScoped<AccountService>();
        services.AddScoped<PhoneCodeService>();
        services.AddScoped<ExternalLoginService>();
        services.AddScoped<TwoFactorService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<PersonalDataService>();
        services.AddScoped<AddressService>();
        services.AddScoped<StaffService>();
        services.AddScoped<CurrencyService>();
        services.AddScoped<BrandingService>();
        services.TryAddSingleton<IExchangeRateSource, NoExchangeRateSource>();
        services.AddOptions<SecurityOptions>().Bind(configuration.GetSection(SecurityOptions.SectionName));
        services.AddOptions<PhoneCodeOptions>()
            .Bind(configuration.GetSection(PhoneCodeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Replaced when an SMS provider is configured (phase 2b).
        services.TryAddSingleton<ISmsCapability, NoSmsCapability>();
        return services;
    }
}
