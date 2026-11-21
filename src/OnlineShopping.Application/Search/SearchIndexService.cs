using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Seo;
using OnlineShopping.Domain.Catalog;
using OnlineShopping.Domain.Search;

namespace OnlineShopping.Application.Search;

/// <summary>The search documents, the changes waiting for them, and the external engine (null when the database searches).</summary>
public sealed record SearchIndexStatus(int Documents, int Pending, SearchEngineStatus? Engine = null);

/// <summary>
/// Keeps the search documents in step with the catalog. Every catalog change queues an outbox entry in its own
/// transaction (the SaveChanges interceptor); <see cref="ProcessOutboxAsync"/> turns the queued entries into product
/// ids (a renamed brand means all its products) and rebuilds their documents: one per published product and shop
/// language, every text in one spelling. Unpublished or deleted products lose theirs. A full rebuild queues
/// <see cref="SearchTarget.All"/>. With an external engine, the rebuilt products (or a full rebuild) are queued for it in
/// the same transaction that removes the handled entries (<see cref="SearchEngineSyncService"/>).
/// </summary>
public sealed class SearchIndexService(IApplicationDbContext db, TimeProvider clock, IOptions<StoreOptions> store, IOptions<SearchOptions> search)
{
    public const int Batch = 200;

    /// <summary>Handles up to <see cref="Batch"/> queued entries; returns how many it took (0 = nothing to do).</summary>
    public async Task<int> ProcessOutboxAsync(CancellationToken cancellationToken = default)
    {
        var entries = await db.SearchOutbox.AsNoTracking().OrderBy(e => e.Id).Take(Batch).ToListAsync(cancellationToken);
        if (entries.Count == 0)
            return 0;
        var ids = await ExpandAsync(entries, cancellationToken);
        foreach (var chunk in ids.Chunk(Batch))
            await RebuildAsync(chunk, cancellationToken);
        var done = entries.Select(e => e.Id).ToList();
        await db.InTransactionAsync<bool>(async () =>
        {
            if (search.Value.UsesEngine)
            {
                var now = clock.GetUtcNow().UtcDateTime;
                if (entries.Any(e => e.Target == SearchTarget.All))
                    db.SearchEngineOutbox.Add(new SearchEngineOutboxEntry { ProductId = Guid.Empty, EnqueuedUtc = now });
                else
                    db.SearchEngineOutbox.AddRange(ids.Select(id => new SearchEngineOutboxEntry { ProductId = id, EnqueuedUtc = now }));
                await db.SaveChangesAsync(cancellationToken);
            }
            await db.SearchOutbox.Where(e => done.Contains(e.Id)).ExecuteDeleteAsync(cancellationToken);
            return true;
        }, cancellationToken);
        db.ChangeTracker.Clear();
        return entries.Count;
    }

    /// <summary>Queues a rebuild of every document (the admin's button, a shop without documents at start-up).</summary>
    public async Task EnqueueAllAsync(CancellationToken cancellationToken = default)
    {
        db.SearchOutbox.Add(new SearchOutboxEntry { Target = SearchTarget.All, TargetId = Guid.Empty, EnqueuedUtc = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>At start-up: a shop with products but no documents (a new install, an upgrade) gets a full build.</summary>
    public async Task<bool> EnsureBuiltAsync(CancellationToken cancellationToken = default)
    {
        if (await db.SearchDocuments.AnyAsync(cancellationToken) || await db.SearchOutbox.AnyAsync(cancellationToken)
            || !await db.Products.AnyAsync(p => p.IsPublished, cancellationToken))
            return false;
        await EnqueueAllAsync(cancellationToken);
        return true;
    }

    public async Task<SearchIndexStatus> StatusAsync(CancellationToken cancellationToken = default) =>
        new(await db.SearchDocuments.CountAsync(cancellationToken), await db.SearchOutbox.CountAsync(cancellationToken));

    /// <summary>The products a set of queued entries touches (deleted ones included, so their documents go).</summary>
    private async Task<List<Guid>> ExpandAsync(List<SearchOutboxEntry> entries, CancellationToken cancellationToken)
    {
        var products = db.Products.IgnoreQueryFilters().AsNoTracking();
        if (entries.Any(e => e.Target == SearchTarget.All))
            return await products.Select(p => p.Id).ToListAsync(cancellationToken);

        List<Guid> Of(SearchTarget target) => entries.Where(e => e.Target == target).Select(e => e.TargetId).Distinct().ToList();
        var ids = Of(SearchTarget.Product).ToHashSet();
        var brands = Of(SearchTarget.Brand);
        var categories = Of(SearchTarget.Category);
        var subs = Of(SearchTarget.SubCategory);
        var groups = Of(SearchTarget.ProductGroup);
        if (brands.Count + categories.Count + subs.Count + groups.Count > 0)
        {
            ids.UnionWith(await products
                .Where(p => (p.BrandId != null && brands.Contains(p.BrandId.Value)) || groups.Contains(p.ProductGroupId)
                    || subs.Contains(p.ProductGroup.SubCategoryId) || categories.Contains(p.ProductGroup.SubCategory.CategoryId))
                .Select(p => p.Id)
                .ToListAsync(cancellationToken));
        }
        var specs = Of(SearchTarget.Spec);
        var options = Of(SearchTarget.SpecOption);
        if (specs.Count + options.Count > 0)
        {
            ids.UnionWith(await db.ProductSpecValues.IgnoreQueryFilters().AsNoTracking()
                .Where(v => specs.Contains(v.SpecId) || (v.OptionId != null && options.Contains(v.OptionId.Value)))
                .Select(v => v.ProductId)
                .ToListAsync(cancellationToken));
            ids.UnionWith(await db.ProductVariants.IgnoreQueryFilters().AsNoTracking()
                .SelectMany(v => v.Options, (v, o) => new { v.ProductId, o.SpecId, o.OptionId })
                .Where(o => specs.Contains(o.SpecId) || options.Contains(o.OptionId))
                .Select(o => o.ProductId)
                .ToListAsync(cancellationToken));
        }
        return ids.ToList();
    }

    /// <summary>Replaces the documents of these products with fresh ones (none for unpublished or deleted products).</summary>
    public async Task RebuildAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        var documents = await BuildAsync(productIds, cancellationToken);
        await db.InTransactionAsync<bool>(async () =>
        {
            await db.SearchDocuments.Where(d => productIds.Contains(d.ProductId)).ExecuteDeleteAsync(cancellationToken);
            db.SearchDocuments.AddRange(documents);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
        db.ChangeTracker.Clear();
    }

    private async Task<List<SearchDocument>> BuildAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var products = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.IsPublished)
            .Select(p => new
            {
                p.Id, p.Name, p.Description, p.Tags, p.SKU, p.Gtin, p.PublicNumber, p.BrandId, p.ProductGroupId,
                SubId = p.ProductGroup.SubCategoryId, CategoryId = p.ProductGroup.SubCategory.CategoryId,
            })
            .ToListAsync(cancellationToken);
        if (products.Count == 0)
            return [];
        var found = products.Select(p => p.Id).ToList();
        var translations = (await db.Products.AsNoTracking().Where(p => found.Contains(p.Id))
            .SelectMany(p => p.Translations, (p, t) => new { p.Id, t.Locale, t.Name, t.Description, t.Tags })
            .ToListAsync(cancellationToken)).ToLookup(t => t.Id);

        var names = await NamesAsync(products.Select(p => p.BrandId).OfType<Guid>().Concat(products.Select(p => p.ProductGroupId))
            .Concat(products.Select(p => p.SubId)).Concat(products.Select(p => p.CategoryId)).ToHashSet(), cancellationToken);

        var values = await db.ProductSpecValues.AsNoTracking().Where(v => found.Contains(v.ProductId))
            .Select(v => new { v.ProductId, v.TextValue, v.OptionId }).ToListAsync(cancellationToken);
        var variants = await db.ProductVariants.AsNoTracking().Where(v => found.Contains(v.ProductId))
            .Select(v => new { v.ProductId, v.Sku, v.Gtin }).ToListAsync(cancellationToken);
        var variantOptions = await db.ProductVariants.AsNoTracking().Where(v => found.Contains(v.ProductId))
            .SelectMany(v => v.Options, (v, o) => new { v.ProductId, o.OptionId }).ToListAsync(cancellationToken);
        var optionIds = values.Where(v => v.OptionId != null).Select(v => v.OptionId!.Value).Concat(variantOptions.Select(o => o.OptionId)).ToHashSet();
        var optionValues = (await db.SpecOptions.AsNoTracking().Where(o => optionIds.Contains(o.Id)).Select(o => new { o.Id, o.Value }).ToListAsync(cancellationToken))
            .ToDictionary(o => o.Id, o => o.Value);
        var optionTranslations = (await db.SpecOptions.AsNoTracking().Where(o => optionIds.Contains(o.Id))
            .SelectMany(o => o.Translations, (o, t) => new { o.Id, t.Locale, t.Value }).ToListAsync(cancellationToken)).ToLookup(t => (t.Id, t.Locale));

        var now = clock.GetUtcNow().UtcDateTime;
        var defaultLocale = store.Value.DefaultLocale;
        var documents = new List<SearchDocument>();
        foreach (var p in products)
        {
            var specOptions = values.Where(v => v.ProductId == p.Id && v.OptionId != null).Select(v => v.OptionId!.Value)
                .Concat(variantOptions.Where(o => o.ProductId == p.Id).Select(o => o.OptionId)).Distinct().ToList();
            var texts = values.Where(v => v.ProductId == p.Id && v.TextValue != null).Select(v => v.TextValue!);
            var ownVariants = variants.Where(v => v.ProductId == p.Id).ToList();
            var codes = Clean(4000, ownVariants.Select(v => v.Sku).Concat(ownVariants.Select(v => v.Gtin)).Prepend(p.Gtin).Prepend(p.SKU).Prepend(p.PublicNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            foreach (var locale in store.Value.LocaleList)
            {
                var own = locale == defaultLocale ? null : translations[p.Id].FirstOrDefault(t => t.Locale == locale);
                string Name(Guid id) => names.GetValueOrDefault((id, locale)) ?? names.GetValueOrDefault((id, "")) ?? "";
                var placeNames = new[] { p.CategoryId, p.SubId, p.ProductGroupId }.Select(Name);
                var brand = p.BrandId is { } brandId ? new[] { Name(brandId), names.GetValueOrDefault((brandId, "")) ?? "" } : [];
                documents.Add(new SearchDocument
                {
                    ProductId = p.Id,
                    Locale = locale,
                    Title = Clean(500, [own?.Name ?? p.Name]),
                    // The default-language name too, so a Latin brand or model name is found in every language.
                    Keywords = Clean(2000, (own is null ? [] : new[] { p.Name }).Concat(brand).Concat(placeNames)),
                    Details = Clean(4000, specOptions
                        .Select(o => optionTranslations[(o, locale)].Select(t => t.Value).FirstOrDefault() ?? optionValues.GetValueOrDefault(o) ?? "")
                        .Concat(texts)
                        .Prepend(own?.Tags ?? p.Tags ?? "")),
                    Codes = codes,
                    Description = Clean(4000, [own?.Description ?? p.Description ?? ""]),
                    UpdatedUtc = now,
                });
            }
        }
        return documents;
    }

    private sealed record NameRow(Guid Id, string Locale, string Name);

    /// <summary>Names of brands and catalog nodes: (id, "") is the own (default-language) name, (id, locale) a translation.</summary>
    private async Task<Dictionary<(Guid, string), string>> NamesAsync(HashSet<Guid> ids, CancellationToken cancellationToken)
    {
        var rows = new List<NameRow>();
        rows.AddRange(await db.Brands.AsNoTracking().Where(b => ids.Contains(b.Id)).Select(b => new NameRow(b.Id, "", b.Name)).ToListAsync(cancellationToken));
        rows.AddRange(await db.Brands.AsNoTracking().Where(b => ids.Contains(b.Id)).SelectMany(b => b.Translations, (b, t) => new NameRow(b.Id, t.Locale, t.Name)).ToListAsync(cancellationToken));
        rows.AddRange(await db.Categories.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new NameRow(c.Id, "", c.Name)).ToListAsync(cancellationToken));
        rows.AddRange(await db.Categories.AsNoTracking().Where(c => ids.Contains(c.Id)).SelectMany(c => c.Translations, (c, t) => new NameRow(c.Id, t.Locale, t.Name)).ToListAsync(cancellationToken));
        rows.AddRange(await db.SubCategories.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new NameRow(x.Id, "", x.Name)).ToListAsync(cancellationToken));
        rows.AddRange(await db.SubCategories.AsNoTracking().Where(x => ids.Contains(x.Id)).SelectMany(x => x.Translations, (x, t) => new NameRow(x.Id, t.Locale, t.Name)).ToListAsync(cancellationToken));
        rows.AddRange(await db.ProductGroups.AsNoTracking().Where(g => ids.Contains(g.Id)).Select(g => new NameRow(g.Id, "", g.Name)).ToListAsync(cancellationToken));
        rows.AddRange(await db.ProductGroups.AsNoTracking().Where(g => ids.Contains(g.Id)).SelectMany(g => g.Translations, (g, t) => new NameRow(g.Id, t.Locale, t.Name)).ToListAsync(cancellationToken));
        var result = new Dictionary<(Guid, string), string>();
        foreach (var row in rows)
            result[(row.Id, row.Locale)] = row.Name;
        return result;
    }

    /// <summary>The texts in one spelling, joined and cut to the column's length.</summary>
    private static string Clean(int max, IEnumerable<string?> parts)
    {
        var text = SearchText.Normalize(string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p))));
        return text.Length > max ? text[..max] : text;
    }
}
