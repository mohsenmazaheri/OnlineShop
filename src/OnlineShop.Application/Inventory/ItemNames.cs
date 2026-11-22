using Microsoft.EntityFrameworkCore;
using OnlineShop.Application.Common;

namespace OnlineShop.Application.Inventory;

/// <summary>How an item is shown in inventory lists: product number, name in the language, variant label and SKU.</summary>
public sealed record ItemName(long ProductNumber, string Name, string? VariantLabel, string Sku);

/// <summary>Names of stock items (products and variants, deleted ones too) in one language, with flat queries.</summary>
public static class ItemNames
{
    public static async Task<Dictionary<Guid, ItemName>> LoadAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<StockItem> items,
        string language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(items);
        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var variantIds = items.Where(i => i.VariantId is not null).Select(i => i.VariantId!.Value).Distinct().ToList();

        var products = await db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.PublicNumber, p.SKU, Name = p.Translations.Where(t => t.Locale == language).Select(t => t.Name).FirstOrDefault() ?? p.Name })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var variants = await db.ProductVariants.IgnoreQueryFilters().AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku, Options = v.Options.Select(o => o.OptionId).ToList() })
            .ToDictionaryAsync(v => v.Id, cancellationToken);
        var optionIds = variants.Values.SelectMany(v => v.Options).Distinct().ToList();
        var labels = optionIds.Count == 0
            ? []
            : await db.SpecOptions.IgnoreQueryFilters().AsNoTracking()
                .Where(o => optionIds.Contains(o.Id))
                .Select(o => new { o.Id, Value = o.Translations.Where(t => t.Locale == language).Select(t => t.Value).FirstOrDefault() ?? o.Value })
                .ToDictionaryAsync(o => o.Id, o => o.Value, cancellationToken);

        var names = new Dictionary<Guid, ItemName>();
        foreach (var item in items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
                continue;
            var variant = item.VariantId is { } id ? variants.GetValueOrDefault(id) : null;
            names[item.ItemId] = new ItemName(
                product.PublicNumber,
                product.Name,
                variant is null ? null : string.Join(" / ", variant.Options.Select(o => labels.GetValueOrDefault(o, ""))),
                variant?.Sku ?? product.SKU);
        }
        return names;
    }
}
