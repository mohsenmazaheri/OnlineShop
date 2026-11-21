using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Catalog;
using OnlineShopping.Domain.Catalog;
using StoreOptions = OnlineShopping.Application.Common.StoreOptions;

namespace OnlineShopping.Infrastructure.Persistence.Seeding.Demo;

/// <summary>Section <c>DemoCatalog</c>: fill an empty shop with sample products for demonstrations.</summary>
public sealed class DemoCatalogOptions
{
    public const string SectionName = "DemoCatalog";

    /// <summary>Off by default. Only a demo deployment turns it on (<c>DemoCatalog__Enabled=true</c>).</summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Adds the demo catalog (<see cref="DemoCatalogData"/>): categories, sub-categories, product groups, specs and products,
/// with every text in all five languages. The shop's default language goes into the items' own columns, the others into
/// translation rows; prices are converted to <c>Store:Currency</c>. Runs once: nothing happens when any demo product
/// (SKU prefix <see cref="SkuPrefix"/>) exists. Existing categories with the same slug or name are reused, so a shop's
/// own categories get the demo sub-categories instead of duplicates.
/// </summary>
public sealed partial class DemoCatalogSeeder(
    AppDbContext db,
    IOptions<StoreOptions> store,
    IOptions<DemoCatalogOptions> options,
    ILogger<DemoCatalogSeeder> logger)
{
    public const string SkuPrefix = "DEMO-";
    private const long FirstPublicNumber = 1001;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
            return;
        if (await db.Products.IgnoreQueryFilters().AnyAsync(p => p.SKU.StartsWith(SkuPrefix), cancellationToken))
            return;

        var baseLocale = DemoText.Locales.Contains(store.Value.DefaultLocale) ? store.Value.DefaultLocale : "en";
        var currency = store.Value.Currency;

        var specs = await EnsureSpecsAsync(baseLocale, cancellationToken);
        var brands = await EnsureBrandsAsync(cancellationToken);
        var nextNumber = Math.Max(
            FirstPublicNumber,
            (await db.Products.IgnoreQueryFilters().MaxAsync(p => (long?)p.PublicNumber, cancellationToken) ?? 0) + 1);

        var categories = await db.Categories
            .Include(c => c.SubCategories).ThenInclude(s => s.Groups)
            .ToListAsync(cancellationToken);

        var added = 0;
        foreach (var demoCategory in DemoCatalogData.Categories)
        {
            var category = categories.FirstOrDefault(c => c.Slug == demoCategory.Slug || Matches(c.Name, demoCategory.Name));
            if (category is null)
            {
                category = new Category { Name = demoCategory.Name.For(baseLocale), Slug = demoCategory.Slug };
                db.Categories.Add(category);
                categories.Add(category);
            }
            category.ImageUrl ??= demoCategory.Image;
            AddMissingTranslations(category.Translations, demoCategory.Name, baseLocale, withSlug: true);

            foreach (var demoSub in demoCategory.SubCategories)
            {
                var sub = category.SubCategories.FirstOrDefault(s => s.Slug == demoSub.Slug || Matches(s.Name, demoSub.Name));
                if (sub is null)
                {
                    sub = new SubCategory { Name = demoSub.Name.For(baseLocale), Slug = demoSub.Slug };
                    category.SubCategories.Add(sub);
                    db.SubCategories.Add(sub);
                }
                AddMissingTranslations(sub.Translations, demoSub.Name, baseLocale, withSlug: true);
                // The sub-category's spec template: the specs its demo products have (only when it has none yet).
                if (sub.TemplateSpecs.Count == 0)
                {
                    foreach (var code in demoSub.Groups.SelectMany(g => g.Products).SelectMany(p => p.Specs).Select(v => v.Spec).Distinct())
                        sub.TemplateSpecs.Add(new TemplateSpec { SpecId = specs[code].Spec.Id });
                }

                foreach (var demoGroup in demoSub.Groups)
                {
                    var group = sub.Groups.FirstOrDefault(g => Matches(g.Name, demoGroup.Name));
                    if (group is null)
                    {
                        group = new ProductGroup { Name = demoGroup.Name.For(baseLocale) };
                        sub.Groups.Add(group);
                        db.ProductGroups.Add(group);
                    }
                    AddMissingTranslations(group.Translations, demoGroup.Name, baseLocale, withSlug: false);

                    foreach (var demoProduct in demoGroup.Products)
                    {
                        var product = CreateProduct(demoProduct, nextNumber++, baseLocale, currency, specs, brands);
                        group.Products.Add(product);
                        db.Products.Add(product);
                        added++;
                    }
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, added, baseLocale, currency);
    }

    private Product CreateProduct(DemoProduct demo, long number, string baseLocale, string currency, SpecLookup specs, Dictionary<string, Brand> brands)
    {
        var name = demo.Name.For(baseLocale);
        var product = new Product
        {
            Name = name,
            Description = demo.Description.For(baseLocale),
            SKU = demo.Sku,
            PublicNumber = number,
            Slug = Slugs.From(name),
            Price = DemoPrices.Convert(demo.PriceEur, currency),
            CompareAtPrice = demo.CompareAtEur is { } compareAt ? DemoPrices.Convert(compareAt, currency) : null,
            Stock = demo.Stock,
            IsPublished = true,
            Brand = demo.Brand is null ? null : brands[demo.Brand],
        };
        product.Images.Add(new ProductImage { Url = demo.Image, SortOrder = 0 });

        foreach (var locale in DemoText.Locales.Where(l => l != baseLocale))
        {
            var translated = demo.Name.For(locale);
            product.Translations.Add(new CatalogTranslation
            {
                Locale = locale,
                Name = translated,
                Slug = Slugs.From(translated),
                Description = demo.Description.For(locale),
            });
        }

        foreach (var value in demo.Specs)
        {
            var (spec, optionsByCode) = specs[value.Spec];
            product.SpecValues.Add(new ProductSpecValue
            {
                Spec = spec,
                NumberValue = value.Number,
                BooleanValue = value.Boolean,
                TextValue = value.Text,
                Option = value.Option is null ? null : optionsByCode[value.Option],
            });
        }

        var sortOrder = 0;
        foreach (var demoVariant in demo.Variants ?? [])
        {
            var variant = new ProductVariant { Sku = demoVariant.Sku, Stock = demoVariant.Stock, SortOrder = sortOrder++, Product = product };
            foreach (var choice in demoVariant.Options)
            {
                var (spec, optionsByCode) = specs[choice.Spec];
                variant.Options.Add(new VariantOption { SpecId = spec.Id, OptionId = optionsByCode[choice.Option!].Id });
            }
            product.Variants.Add(variant);
            db.ProductVariants.Add(variant);
        }
        if (product.Variants.Count > 0)
            product.Stock = product.Variants.Sum(v => v.Stock);

        return product;
    }

    /// <summary>The demo brands, reusing any with the same name.</summary>
    private async Task<Dictionary<string, Brand>> EnsureBrandsAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Brands.ToListAsync(cancellationToken);
        var lookup = new Dictionary<string, Brand>(StringComparer.Ordinal);
        foreach (var name in DemoCatalogData.Brands)
        {
            var brand = existing.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
            if (brand is null)
            {
                brand = new Brand { Name = name, Slug = Slugs.From(name) };
                db.Brands.Add(brand);
            }
            lookup[name] = brand;
        }
        return lookup;
    }

    /// <summary>The demo specs, reusing any with the same code (a real shop may already have "color").</summary>
    private async Task<SpecLookup> EnsureSpecsAsync(string baseLocale, CancellationToken cancellationToken)
    {
        var existing = await db.ProductSpecs.Include(s => s.Options).ToDictionaryAsync(s => s.Code, cancellationToken);
        var lookup = new SpecLookup();
        var sortOrder = existing.Count == 0 ? 0 : existing.Values.Max(s => s.SortOrder) + 1;

        foreach (var demo in DemoCatalogData.Specs)
        {
            if (!existing.TryGetValue(demo.Code, out var spec))
            {
                spec = new ProductSpec
                {
                    Code = demo.Code,
                    Name = demo.Name.For(baseLocale),
                    GroupName = demo.Group.For(baseLocale),
                    Unit = demo.Unit?.For(baseLocale),
                    Kind = demo.Kind,
                    SortOrder = sortOrder++,
                    IsFilterable = demo.Filterable,
                    IsComparable = true,
                    Better = demo.Better,
                    ShowInSummary = demo.Summary,
                };
                foreach (var locale in DemoText.Locales.Where(l => l != baseLocale))
                {
                    spec.Translations.Add(new SpecTranslation
                    {
                        Locale = locale, Name = demo.Name.For(locale), GroupName = demo.Group.For(locale), Unit = demo.Unit?.For(locale),
                    });
                }
                db.ProductSpecs.Add(spec);
            }

            var optionSort = 0;
            foreach (var demoOption in demo.Options ?? [])
            {
                if (spec.Options.Any(o => o.Code == demoOption.Code))
                    continue;
                var option = new SpecOption { Code = demoOption.Code, Value = demoOption.Value.For(baseLocale), SortOrder = optionSort++ };
                foreach (var locale in DemoText.Locales.Where(l => l != baseLocale))
                    option.Translations.Add(new SpecOptionTranslation { Locale = locale, Value = demoOption.Value.For(locale) });
                spec.Options.Add(option);
                db.SpecOptions.Add(option);
            }

            lookup[demo.Code] = (spec, spec.Options.ToDictionary(o => o.Code));
        }

        return lookup;
    }

    private static void AddMissingTranslations(ICollection<CatalogTranslation> translations, DemoText text, string baseLocale, bool withSlug)
    {
        foreach (var locale in DemoText.Locales.Where(l => l != baseLocale && translations.All(t => t.Locale != l)))
        {
            var name = text.For(locale);
            translations.Add(new CatalogTranslation { Locale = locale, Name = name, Slug = withSlug ? Slugs.From(name) : null });
        }
    }

    /// <summary>An existing item matches a demo item when its name equals the demo name in any language.</summary>
    private static bool Matches(string name, DemoText text) =>
        DemoText.Locales.Any(l => string.Equals(name.Trim(), text.For(l), StringComparison.OrdinalIgnoreCase));

    private sealed class SpecLookup : Dictionary<string, (ProductSpec Spec, Dictionary<string, SpecOption> Options)>
    {
        public SpecLookup() : base(StringComparer.Ordinal) { }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo catalog added: {Count} products (base language {Locale}, prices in {Currency}).")]
    private static partial void LogSeeded(ILogger logger, int count, string locale, string currency);
}
