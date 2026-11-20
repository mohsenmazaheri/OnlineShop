using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Search;
using OnlineShopping.Domain.Catalog;

namespace OnlineShopping.Application.Catalog.Browse;

/// <summary>Product listings with filters and facets: category, sub-category and brand pages, and search results.</summary>
public interface IProductBrowse
{
    Task<Result<BrowseResult>> BrowseAsync(BrowseQuery query, string? locale, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lists the published products of a scope (a category, a sub-category, a brand, a text search, or everything) with
/// filters and facet counts. The scope's products are loaded as small rows (price, stock, brand, group and their
/// filterable spec values, variant options included) and filtered, counted and sorted in memory — right for a shop with
/// up to a few thousand products; a search engine takes over for bigger catalogues (phase 6c). Facet counts are
/// "disjunctive": each facet counts with every other filter applied but not its own, so choosing a brand still shows
/// how many each other brand has. Available stock (stock − reserved) decides "in stock"; prices are in the base
/// currency.
/// </summary>
public sealed class ProductBrowseService(
    IApplicationDbContext db,
    IProductQueries cards,
    ProductTextSearch textSearch,
    SearchTermLog searchLog,
    IOptions<StoreOptions> store) : IProductBrowse
{
    public const int PageSize = 24;
    public const string NotFoundCode = "browse.not_found";

    private sealed record Row(Guid Id, decimal Price, decimal? CompareAt, bool Available, Guid? BrandId, DateTime Created, Guid GroupId, Guid SubId);

    private sealed record SpecInfo(Guid Id, string Code, SpecKind Kind, int SortOrder, string Name, string? Group, string? Unit);

    private sealed record OptionInfo(Guid Id, Guid SpecId, string Code, string Label, int SortOrder);

    public async Task<Result<BrowseResult>> BrowseAsync(BrowseQuery query, string? locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var language = store.Value.ContentLocale(locale);
        var products = db.Products.AsNoTracking().Where(p => p.IsPublished);

        var scoped = await ScopeAsync(query, language, products, cancellationToken);
        if (!scoped.IsSuccess)
            return scoped.Error!;
        var (scope, inScope, categoryId) = scoped.Value;

        var rows = await inScope
            .Select(p => new Row(p.Id, p.Price, p.CompareAtPrice, p.Stock - p.Reserved > 0, p.BrandId, p.CreatedUtc, p.ProductGroupId, p.ProductGroup.SubCategoryId))
            .ToListAsync(cancellationToken);
        IReadOnlyDictionary<Guid, double>? scores = null;
        TextSearchOutcome? search = null;
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            search = await textSearch.SearchAsync(query.Text, language, cancellationToken);
            scores = search.Scores;
            rows = rows.Where(r => scores.ContainsKey(r.Id)).ToList();
        }

        // Filterable spec values of the scope (a variant's options count as the product's).
        var values = await db.ProductSpecValues.AsNoTracking()
            .Where(v => v.Spec.IsFilterable && inScope.Any(p => p.Id == v.ProductId))
            .Select(v => new { v.ProductId, v.SpecId, v.OptionId, v.NumberValue, v.BooleanValue })
            .ToListAsync(cancellationToken);
        var variantOptions = await db.ProductVariants.AsNoTracking()
            .Where(v => inScope.Any(p => p.Id == v.ProductId))
            .SelectMany(v => v.Options, (v, o) => new { v.ProductId, o.SpecId, o.OptionId })
            .ToListAsync(cancellationToken);
        var rowIds = rows.Select(r => r.Id).ToHashSet();
        var optionsOf = values.Where(v => v.OptionId is not null).Select(v => (v.ProductId, v.SpecId, OptionId: v.OptionId!.Value))
            .Concat(variantOptions.Select(v => (v.ProductId, v.SpecId, v.OptionId)))
            .Where(v => rowIds.Contains(v.ProductId))
            .ToLookup(v => v.ProductId);
        var numbersOf = values.Where(v => v.NumberValue is not null && rowIds.Contains(v.ProductId))
            .ToLookup(v => v.ProductId, v => (v.SpecId, Value: v.NumberValue!.Value));
        var yesOf = values.Where(v => v.BooleanValue == true && rowIds.Contains(v.ProductId))
            .ToLookup(v => v.ProductId, v => v.SpecId);

        var specIds = optionsOf.SelectMany(g => g).Select(o => o.SpecId)
            .Concat(numbersOf.SelectMany(g => g).Select(n => n.SpecId))
            .Concat(yesOf.SelectMany(g => g))
            .ToHashSet();
        var specs = await SpecsAsync(specIds, language, cancellationToken);
        var specOptions = await OptionsAsync(specIds, language, cancellationToken);

        // The filters, each a test of one row, keyed so a facet can leave its own out.
        var tests = new Dictionary<string, Func<Row, bool>>(StringComparer.Ordinal);
        var brandIds = await BrandIdsAsync(query.Brands, cancellationToken);
        if (query.Brands.Count > 0)
            tests["brand"] = r => r.BrandId is { } id && brandIds.Contains(id);
        if (query.Groups.Count > 0)
            tests["group"] = r => query.Groups.Contains(r.GroupId);
        if (query.MinPrice is not null || query.MaxPrice is not null)
            tests["price"] = r => new NumberRange(query.MinPrice, query.MaxPrice).Contains(r.Price);
        if (query.InStock)
            tests["stock"] = r => r.Available;
        if (query.Discounted)
            tests["sale"] = r => r.CompareAt > r.Price;
        var selectedOptions = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var (code, chosen) in query.Options)
        {
            if (specs.Values.FirstOrDefault(s => s.Code == code && s.Kind == SpecKind.Option) is not { } spec)
                continue;
            var ids = specOptions.Where(o => o.SpecId == spec.Id && chosen.Contains(o.Code)).Select(o => o.Id).ToHashSet();
            selectedOptions[spec.Id] = ids;
            tests[$"o:{spec.Id}"] = r => optionsOf[r.Id].Any(o => ids.Contains(o.OptionId));
        }
        var selectedRanges = new Dictionary<Guid, NumberRange>();
        foreach (var (code, range) in query.Ranges)
        {
            if (specs.Values.FirstOrDefault(s => s.Code == code && s.Kind == SpecKind.Number) is not { } spec)
                continue;
            selectedRanges[spec.Id] = range;
            tests[$"n:{spec.Id}"] = r => numbersOf[r.Id].Any(n => n.SpecId == spec.Id && range.Contains(n.Value));
        }
        var selectedFlags = new HashSet<Guid>();
        foreach (var code in query.Flags)
        {
            if (specs.Values.FirstOrDefault(s => s.Code == code && s.Kind == SpecKind.Boolean) is not { } spec)
                continue;
            selectedFlags.Add(spec.Id);
            tests[$"y:{spec.Id}"] = r => yesOf[r.Id].Contains(spec.Id);
        }

        bool Matches(Row row, string? except = null) => tests.All(t => t.Key == except || t.Value(row));
        var matching = rows.Where(r => Matches(r)).ToList();

        var facets = new BrowseFacets(
            await SubCategoryFacetAsync(categoryId, query.SubCategory is null ? scope : null, matching, language, cancellationToken),
            await GroupFacetAsync(rows.Where(r => Matches(r, "group")).ToList(), rows, query.Groups, language, cancellationToken),
            await BrandFacetAsync(rows.Where(r => Matches(r, "brand")).ToList(), rows, brandIds, language, cancellationToken),
            PriceFacet(rows.Where(r => Matches(r, "price")).ToList()),
            query.MinPrice,
            query.MaxPrice,
            rows.Count(r => Matches(r, "stock") && r.Available),
            query.InStock,
            rows.Count(r => Matches(r, "sale") && r.CompareAt > r.Price),
            query.Discounted,
            specs.Values.OrderBy(s => s.SortOrder).Select(spec => spec.Kind switch
            {
                SpecKind.Option => OptionFacet(spec, specOptions, rows, r => Matches(r, $"o:{spec.Id}"), optionsOf, selectedOptions.GetValueOrDefault(spec.Id)),
                SpecKind.Number => NumberFacet(spec, rows.Where(r => Matches(r, $"n:{spec.Id}")), numbersOf, selectedRanges.GetValueOrDefault(spec.Id)),
                _ => new SpecFacet(spec.Code, spec.Name, spec.Group, spec.Kind.ToString(), spec.Unit, [], null, null, null, null,
                    rows.Count(r => Matches(r, $"y:{spec.Id}") && yesOf[r.Id].Contains(spec.Id)), selectedFlags.Contains(spec.Id)),
            }).ToList());

        var sort = query.Sort ?? (scores is null ? BrowseSort.Newest : BrowseSort.Relevance);
        IEnumerable<Row> sorted = sort switch
        {
            BrowseSort.PriceAsc => matching.OrderBy(r => r.Price).ThenByDescending(r => r.Created),
            BrowseSort.PriceDesc => matching.OrderByDescending(r => r.Price).ThenByDescending(r => r.Created),
            BrowseSort.Discount => matching.OrderByDescending(r => r.CompareAt > r.Price ? (r.CompareAt.Value - r.Price) / r.CompareAt.Value : 0).ThenByDescending(r => r.Created),
            BrowseSort.Relevance when scores is not null => matching.OrderByDescending(r => scores[r.Id]).ThenByDescending(r => r.Available).ThenByDescending(r => r.Created),
            _ => matching.OrderByDescending(r => r.Created),
        };
        var page = sorted.Skip((query.Page - 1) * PageSize).Take(PageSize).Select(r => r.Id).ToList();
        var items = await cards.CardsAsync(page, language, cancellationToken);
        // With nothing found under several filters, how many each filter alone is hiding ("remove this filter: 12").
        var relaxations = matching.Count == 0
            ? tests.Keys.Select(key => new Relaxation(FilterName(key, specs), rows.Count(r => Matches(r, key)))).Where(x => x.Count > 0).OrderByDescending(x => x.Count).ToList()
            : [];
        if (search is not null && query.Page == 1 && !query.HasFilters)
            await searchLog.RecordAsync(language, query.Text!, matching.Count > 0, cancellationToken);
        var help = search is null ? null : new BrowseSearch(search.ExactNumber, search.CorrectedText, search.DidYouMean);
        return new BrowseResult(scope, items, matching.Count, query.Page, PageSize, sort, query.HasFilters, facets, help, relaxations);
    }

    /// <summary>A filter's name as the address bar has it: brand, group, price, stock, sale, o.{spec}, n.{spec}, y.{spec}.</summary>
    private static string FilterName(string key, Dictionary<Guid, SpecInfo> specs)
    {
        if (key.Length > 2 && key[1] == ':' && Guid.TryParse(key[2..], out var id) && specs.TryGetValue(id, out var spec))
            return $"{key[0]}.{spec.Code}";
        return key;
    }

    /// <summary>The scope's products and its description; a slug that names nothing is not found.</summary>
    private async Task<Result<(BrowseScope Scope, IQueryable<Product> Products, Guid? CategoryId)>> ScopeAsync(
        BrowseQuery query, string language, IQueryable<Product> products, CancellationToken cancellationToken)
    {
        var locales = store.Value.LocaleList;
        var defaultLocale = store.Value.DefaultLocale;
        if (query.Category is { } categorySlug)
        {
            var category = await db.Categories.AsNoTracking()
                .Where(c => c.Slug == categorySlug || c.Translations.Any(t => t.Slug == categorySlug))
                .Select(c => new { c.Id, c.Name, c.Slug, c.ImageUrl, Texts = c.Translations.Select(t => new { t.Locale, t.Name, t.Slug, t.Description }).ToList() })
                .FirstOrDefaultAsync(cancellationToken);
            if (category is null)
                return NotFound();
            string CategorySlug(string locale) => locale == defaultLocale ? category.Slug : category.Texts.FirstOrDefault(t => t.Locale == locale)?.Slug ?? category.Slug;
            var categoryName = category.Texts.FirstOrDefault(t => t.Locale == language)?.Name ?? category.Name;
            var categoryLink = new ScopeLink(categoryName, $"c/{CategorySlug(language)}");
            var inCategory = products.Where(p => p.ProductGroup.SubCategory.CategoryId == category.Id);

            if (query.SubCategory is not { } subSlug)
            {
                var alternates = locales
                    .Where(l => l == defaultLocale || category.Texts.Any(t => t.Locale == l))
                    .Select(l => new ScopeAlternate(l, $"c/{CategorySlug(l)}"))
                    .ToList();
                var description = category.Texts.FirstOrDefault(t => t.Locale == language)?.Description;
                return (new BrowseScope("category", categoryName, description, category.ImageUrl, categoryLink.Path, [categoryLink], alternates), inCategory, category.Id);
            }

            var sub = await db.SubCategories.AsNoTracking()
                .Where(s => s.CategoryId == category.Id && (s.Slug == subSlug || s.Translations.Any(t => t.Slug == subSlug)))
                .Select(s => new { s.Id, s.Name, s.Slug, Texts = s.Translations.Select(t => new { t.Locale, t.Name, t.Slug, t.Description }).ToList() })
                .FirstOrDefaultAsync(cancellationToken);
            if (sub is null)
                return NotFound();
            string SubSlug(string locale) => locale == defaultLocale ? sub.Slug : sub.Texts.FirstOrDefault(t => t.Locale == locale)?.Slug ?? sub.Slug;
            var subName = sub.Texts.FirstOrDefault(t => t.Locale == language)?.Name ?? sub.Name;
            var subLink = new ScopeLink(subName, $"c/{CategorySlug(language)}/{SubSlug(language)}");
            var subAlternates = locales
                .Where(l => l == defaultLocale || sub.Texts.Any(t => t.Locale == l))
                .Select(l => new ScopeAlternate(l, $"c/{CategorySlug(l)}/{SubSlug(l)}"))
                .ToList();
            return (new BrowseScope("subcategory", subName, sub.Texts.FirstOrDefault(t => t.Locale == language)?.Description, null, subLink.Path,
                [categoryLink, subLink], subAlternates), inCategory.Where(p => p.ProductGroup.SubCategoryId == sub.Id), category.Id);
        }

        if (query.Brand is { } brandSlug)
        {
            var brand = await db.Brands.AsNoTracking()
                .Where(b => b.Slug == brandSlug || b.Translations.Any(t => t.Slug == brandSlug))
                .Select(b => new { b.Id, b.Name, b.Slug, b.Description, b.LogoUrl, Texts = b.Translations.Select(t => new { t.Locale, t.Name, t.Slug, t.Description }).ToList() })
                .FirstOrDefaultAsync(cancellationToken);
            if (brand is null)
                return NotFound();
            string BrandSlug(string locale) => locale == defaultLocale ? brand.Slug : brand.Texts.FirstOrDefault(t => t.Locale == locale)?.Slug ?? brand.Slug;
            var text = brand.Texts.FirstOrDefault(t => t.Locale == language);
            var link = new ScopeLink(text?.Name ?? brand.Name, $"b/{BrandSlug(language)}");
            var alternates = locales
                .Where(l => l == defaultLocale || brand.Texts.Any(t => t.Locale == l))
                .Select(l => new ScopeAlternate(l, $"b/{BrandSlug(l)}"))
                .ToList();
            return (new BrowseScope("brand", link.Name, text?.Description ?? (language == defaultLocale ? brand.Description : null), brand.LogoUrl, link.Path, [link], alternates),
                products.Where(p => p.BrandId == brand.Id), null);
        }

        var kind = string.IsNullOrWhiteSpace(query.Text) ? "all" : "search";
        return (new BrowseScope(kind, query.Text, null, null, null, [], []), products, null);
    }

    private async Task<Dictionary<Guid, SpecInfo>> SpecsAsync(HashSet<Guid> ids, string language, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return [];
        return await db.ProductSpecs.AsNoTracking()
            .Where(s => ids.Contains(s.Id) && s.IsFilterable)
            .Select(s => new SpecInfo(
                s.Id, s.Code, s.Kind, s.SortOrder,
                s.Translations.Where(t => t.Locale == language).Select(t => t.Name).FirstOrDefault() ?? s.Name,
                s.Translations.Where(t => t.Locale == language).Select(t => t.GroupName).FirstOrDefault() ?? s.GroupName,
                s.Translations.Where(t => t.Locale == language).Select(t => t.Unit).FirstOrDefault() ?? s.Unit))
            .ToDictionaryAsync(s => s.Id, cancellationToken);
    }

    private async Task<List<OptionInfo>> OptionsAsync(HashSet<Guid> specIds, string language, CancellationToken cancellationToken)
    {
        if (specIds.Count == 0)
            return [];
        return await db.SpecOptions.AsNoTracking()
            .Where(o => specIds.Contains(o.SpecId))
            .OrderBy(o => o.SortOrder)
            .Select(o => new OptionInfo(o.Id, o.SpecId, o.Code, o.Translations.Where(t => t.Locale == language).Select(t => t.Value).FirstOrDefault() ?? o.Value, o.SortOrder))
            .ToListAsync(cancellationToken);
    }

    private async Task<HashSet<Guid>> BrandIdsAsync(IReadOnlyList<string> slugs, CancellationToken cancellationToken)
    {
        if (slugs.Count == 0)
            return [];
        return (await db.Brands.AsNoTracking()
            .Where(b => slugs.Contains(b.Slug) || b.Translations.Any(t => t.Slug != null && slugs.Contains(t.Slug)))
            .Select(b => b.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
    }

    /// <summary>A category page offers its sub-categories with how many matching products each has.</summary>
    private async Task<IReadOnlyList<FacetLink>> SubCategoryFacetAsync(Guid? categoryId, BrowseScope? categoryScope, List<Row> matching, string language, CancellationToken cancellationToken)
    {
        if (categoryId is not { } id || categoryScope?.Path is not { } categoryPath)
            return [];
        var subs = await db.SubCategories.AsNoTracking()
            .Where(s => s.CategoryId == id)
            .Select(s => new
            {
                s.Id,
                Name = s.Translations.Where(t => t.Locale == language).Select(t => t.Name).FirstOrDefault() ?? s.Name,
                Slug = s.Translations.Where(t => t.Locale == language).Select(t => t.Slug).FirstOrDefault() ?? s.Slug,
            })
            .ToListAsync(cancellationToken);
        var counts = matching.GroupBy(r => r.SubId).ToDictionary(g => g.Key, g => g.Count());
        return subs.Where(s => counts.ContainsKey(s.Id))
            .OrderBy(s => s.Name, StringComparer.CurrentCulture)
            .Select(s => new FacetLink(s.Name, $"{categoryPath}/{s.Slug}", counts[s.Id]))
            .ToList();
    }

    private async Task<IReadOnlyList<FacetValue>> GroupFacetAsync(List<Row> counted, List<Row> all, IReadOnlyList<Guid> selected, string language, CancellationToken cancellationToken)
    {
        var present = all.Select(r => r.GroupId).Distinct().ToList();
        if (present.Count < 2 && selected.Count == 0)
            return [];
        var names = await db.ProductGroups.AsNoTracking()
            .Where(g => present.Contains(g.Id))
            .Select(g => new { g.Id, Name = g.Translations.Where(t => t.Locale == language).Select(t => t.Name).FirstOrDefault() ?? g.Name })
            .ToListAsync(cancellationToken);
        var counts = counted.GroupBy(r => r.GroupId).ToDictionary(g => g.Key, g => g.Count());
        return names.OrderBy(g => g.Name, StringComparer.CurrentCulture)
            .Select(g => new FacetValue(g.Id.ToString(), g.Name, counts.GetValueOrDefault(g.Id), selected.Contains(g.Id)))
            .ToList();
    }

    private async Task<IReadOnlyList<FacetValue>> BrandFacetAsync(List<Row> counted, List<Row> all, HashSet<Guid> selected, string language, CancellationToken cancellationToken)
    {
        var present = all.Where(r => r.BrandId is not null).Select(r => r.BrandId!.Value).Distinct().ToList();
        if (present.Count == 0)
            return [];
        var brands = await db.Brands.AsNoTracking()
            .Where(b => present.Contains(b.Id))
            .Select(b => new
            {
                b.Id,
                Name = b.Translations.Where(t => t.Locale == language).Select(t => t.Name).FirstOrDefault() ?? b.Name,
                Slug = b.Translations.Where(t => t.Locale == language).Select(t => t.Slug).FirstOrDefault() ?? b.Slug,
            })
            .ToListAsync(cancellationToken);
        var counts = counted.Where(r => r.BrandId is not null).GroupBy(r => r.BrandId!.Value).ToDictionary(g => g.Key, g => g.Count());
        return brands.OrderByDescending(b => counts.GetValueOrDefault(b.Id)).ThenBy(b => b.Name, StringComparer.CurrentCulture)
            .Select(b => new FacetValue(b.Slug, b.Name, counts.GetValueOrDefault(b.Id), selected.Contains(b.Id)))
            .ToList();
    }

    private static PriceRange? PriceFacet(List<Row> rows) =>
        rows.Count == 0 ? null : new PriceRange(rows.Min(r => r.Price), rows.Max(r => r.Price));

    private static SpecFacet OptionFacet(
        SpecInfo spec,
        List<OptionInfo> options,
        List<Row> rows,
        Func<Row, bool> counted,
        ILookup<Guid, (Guid ProductId, Guid SpecId, Guid OptionId)> optionsOf,
        HashSet<Guid>? selected)
    {
        var present = rows.SelectMany(r => optionsOf[r.Id]).Where(o => o.SpecId == spec.Id).Select(o => o.OptionId).ToHashSet();
        var counts = rows.Where(counted)
            .SelectMany(r => optionsOf[r.Id].Where(o => o.SpecId == spec.Id).Select(o => o.OptionId).Distinct())
            .GroupBy(o => o)
            .ToDictionary(g => g.Key, g => g.Count());
        var values = options.Where(o => o.SpecId == spec.Id && present.Contains(o.Id))
            .Select(o => new FacetValue(o.Code, o.Label, counts.GetValueOrDefault(o.Id), selected?.Contains(o.Id) == true))
            .ToList();
        return new SpecFacet(spec.Code, spec.Name, spec.Group, spec.Kind.ToString(), spec.Unit, values, null, null, null, null, 0, false);
    }

    private static SpecFacet NumberFacet(SpecInfo spec, IEnumerable<Row> rows, ILookup<Guid, (Guid SpecId, decimal Value)> numbersOf, NumberRange? selected)
    {
        var numbers = rows.SelectMany(r => numbersOf[r.Id]).Where(n => n.SpecId == spec.Id).Select(n => n.Value).ToList();
        return new SpecFacet(spec.Code, spec.Name, spec.Group, spec.Kind.ToString(), spec.Unit, [],
            numbers.Count == 0 ? null : numbers.Min(), numbers.Count == 0 ? null : numbers.Max(), selected?.Min, selected?.Max, 0, false);
    }

    private static AppError NotFound() => AppError.NotFound(NotFoundCode, "Nothing to list at this address.");
}
