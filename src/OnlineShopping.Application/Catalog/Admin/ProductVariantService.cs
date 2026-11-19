using Microsoft.EntityFrameworkCore;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Inventory;
using OnlineShopping.Domain.Catalog;

namespace OnlineShopping.Application.Catalog.Admin;

/// <summary>
/// A product's variants (colour, size): the whole set is saved at once. Rules: the product varies by up to three
/// option specs, each variant has exactly one option of each, no combination appears twice, every SKU is unique among
/// products and variants, and an "old price" must be above the variant's price. The product's stock becomes the sum
/// of its variants' stock; every stock change is a line of the <see cref="StockLedger"/>.
/// </summary>
public sealed class ProductVariantService(IApplicationDbContext db, StockLedger ledger)
{
    public async Task<Result<ProductVariantsView>> SaveAsync(long number, SaveVariantsRequest request, CancellationToken cancellationToken = default)
    {
        var product = await db.Products
            .Include(p => p.Variants)
            .Include(p => p.SpecValues)
            .SingleOrDefaultAsync(p => p.PublicNumber == number, cancellationToken);
        if (product is null)
            return AppError.NotFound(CatalogErrors.NotFound, "No such product.");

        var axisCodes = request.SpecCodes.Distinct(StringComparer.Ordinal).ToList();
        var axes = await db.ProductSpecs.AsNoTracking().Include(s => s.Options)
            .Where(s => axisCodes.Contains(s.Code))
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken);
        if (request.Variants.Count > 0 && axisCodes.Count == 0)
            return AppError.Validation(CatalogErrors.VariantSpecInvalid, "Variants need at least one option spec.");
        if (axes.Count != axisCodes.Count || axisCodes.Count > SaveVariantsRequestValidator.MaxSpecs || axes.Any(a => a.Kind != SpecKind.Option))
            return AppError.Validation(CatalogErrors.VariantSpecInvalid, "Variants vary by up to three existing option specs.");
        if (product.SpecValues.Any(v => axes.Any(a => a.Id == v.SpecId)))
            return AppError.Conflict(CatalogErrors.SpecIsVariant, "Remove the product's own value of a spec its variants vary by.");

        var resolved = new List<(VariantInput Input, string Sku, List<VariantOption> Options)>();
        var combinations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in request.Variants)
        {
            if (input.Options.Count != axes.Count || input.Options.Select(o => o.Spec).Distinct(StringComparer.Ordinal).Count() != axes.Count)
                return OptionsInvalid();
            var options = new List<VariantOption>();
            foreach (var axis in axes)
            {
                var choice = input.Options.FirstOrDefault(o => o.Spec == axis.Code);
                var option = choice is null ? null : axis.Options.FirstOrDefault(o => o.Code == choice.Option);
                if (option is null)
                    return OptionsInvalid();
                options.Add(new VariantOption { SpecId = axis.Id, OptionId = option.Id });
            }
            if (!combinations.Add(string.Join('|', options.Select(o => o.OptionId))))
                return AppError.Validation(CatalogErrors.VariantRepeated, "Two variants have the same options.");

            var price = input.Price ?? product.Price;
            if (input.CompareAtPrice is { } compareAt && compareAt <= price)
                return AppError.Validation(CatalogErrors.CompareAtInvalid, "An old price must be above the price.");
            if (!string.IsNullOrWhiteSpace(input.Gtin) && ProductCodes.NormalizeGtin(input.Gtin) is null)
                return AppError.Validation(CatalogErrors.GtinInvalid, $"{input.Gtin} is not a valid barcode.");
            if (input.Id is { } id && product.Variants.All(v => v.Id != id))
                return AppError.NotFound(CatalogErrors.NotFound, "No such variant of this product.");
            resolved.Add((input, input.Sku.Trim(), options));
        }

        var skus = resolved.Select(r => r.Sku).ToList();
        if (skus.Distinct(StringComparer.Ordinal).Count() != skus.Count)
            return AppError.Conflict(CatalogErrors.SkuTaken, "Two variants have the same SKU.");
        var ownIds = product.Variants.Select(v => v.Id).ToList();
        if (await db.Products.AnyAsync(p => skus.Contains(p.SKU), cancellationToken)
            || await db.ProductVariants.AnyAsync(v => skus.Contains(v.Sku) && !ownIds.Contains(v.Id), cancellationToken))
            return AppError.Conflict(CatalogErrors.SkuTaken, "A SKU is already used by a product or another product's variant.");

        // A row without an id but with the SKU of an existing variant is that variant (no delete + insert of one SKU).
        var claimed = resolved.Where(r => r.Input.Id is not null).Select(r => r.Input.Id!.Value).ToHashSet();
        var targets = resolved.Select(r =>
            {
                var variant = r.Input.Id is { } id
                    ? product.Variants.First(v => v.Id == id)
                    : product.Variants.FirstOrDefault(v => v.Sku == r.Sku && !claimed.Contains(v.Id));
                if (variant is not null)
                    claimed.Add(variant.Id);
                return variant;
            })
            .ToList();

        // Variants left out are soft-deleted (a later order keeps pointing at them).
        var removed = product.Variants.Where(v => !claimed.Contains(v.Id) && !v.IsDeleted).ToList();
        foreach (var gone in removed)
            db.ProductVariants.Remove(gone);

        var stocks = new List<(ProductVariant Variant, int Stock)>();
        for (var i = 0; i < resolved.Count; i++)
        {
            var (input, sku, options) = resolved[i];
            var variant = targets[i];
            if (variant is null)
            {
                variant = new ProductVariant { Sku = sku, Product = product };
                product.Variants.Add(variant);
                db.ProductVariants.Add(variant);
            }
            variant.Sku = sku;
            variant.Gtin = ProductCodes.NormalizeGtin(input.Gtin);
            variant.Price = input.Price;
            variant.CompareAtPrice = input.CompareAtPrice;
            stocks.Add((variant, input.Stock));
            variant.SortOrder = i;
            // Owned rows keyed by spec: change them in place rather than replacing them with equal keys.
            foreach (var stale in variant.Options.Where(o => options.All(n => n.SpecId != o.SpecId)).ToList())
                variant.Options.Remove(stale);
            foreach (var option in options)
            {
                if (variant.Options.FirstOrDefault(o => o.SpecId == option.SpecId) is { } existing)
                    existing.OptionId = option.OptionId;
                else
                    variant.Options.Add(option);
            }
        }

        return await db.InTransactionAsync<ProductVariantsView>(async () =>
        {
            await db.SaveChangesAsync(cancellationToken);
            // Stock through the ledger: a removed variant's stock is written off, the product's own stock (from before
            // it had variants) too, and each variant gets its number as an adjustment.
            (StockItem Item, int Stock)[] own = resolved.Count > 0 ? [(new StockItem(product.Id, null), 0)] : [];
            var changes = removed.Select(v => (Item: new StockItem(product.Id, v.Id), Stock: 0))
                .Concat(own)
                .Concat(stocks.Select(s => (Item: new StockItem(product.Id, s.Variant.Id), s.Stock)));
            foreach (var (item, stock) in changes)
            {
                var set = await ledger.SetTotalAsync(item, stock, StockReferences.Variants, cancellationToken);
                if (!set.IsSuccess)
                    return set.Error!;
            }
            return await ViewAsync(db, product.Variants.Where(v => !v.IsDeleted), cancellationToken);
        }, cancellationToken);
    }

    /// <summary>The variants as the admin edits them, with spec and option codes.</summary>
    public static async Task<ProductVariantsView> ViewAsync(IApplicationDbContext db, IEnumerable<ProductVariant> variants, CancellationToken cancellationToken)
    {
        var list = variants.OrderBy(v => v.SortOrder).ToList();
        var specIds = list.SelectMany(v => v.Options).Select(o => o.SpecId).Distinct().ToList();
        var specs = await db.ProductSpecs.AsNoTracking()
            .Where(s => specIds.Contains(s.Id))
            .OrderBy(s => s.SortOrder)
            .Select(s => new { s.Id, s.Code, Options = s.Options.Select(o => new { o.Id, o.Code }).ToList() })
            .ToListAsync(cancellationToken);
        var optionCodes = specs.SelectMany(s => s.Options).ToDictionary(o => o.Id, o => o.Code);

        return new ProductVariantsView(
            specs.Select(s => s.Code).ToList(),
            list.Select(v => new VariantView(
                    v.Id,
                    v.Sku,
                    v.Price,
                    v.CompareAtPrice,
                    v.Stock,
                    specs.Select(s => new VariantOptionInput(s.Code, optionCodes.GetValueOrDefault(v.Options.FirstOrDefault(o => o.SpecId == s.Id)?.OptionId ?? Guid.Empty, "")))
                        .ToList(),
                    v.Gtin))
                .ToList());
    }

    private static AppError OptionsInvalid() =>
        AppError.Validation(CatalogErrors.VariantOptionsInvalid, "Every variant needs exactly one existing option of each variant spec.");
}
