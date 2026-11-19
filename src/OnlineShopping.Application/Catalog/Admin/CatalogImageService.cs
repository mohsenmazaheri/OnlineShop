using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Media;
using OnlineShopping.Domain.Catalog;

namespace OnlineShopping.Application.Catalog.Admin;

/// <summary>
/// Images of products and categories: upload (checked and re-encoded by <see cref="MediaService"/>), order, alternative
/// texts per language, removal (the stored files are deleted too).
/// </summary>
public sealed class CatalogImageService(IApplicationDbContext db, MediaService media, IOptions<StoreOptions> store)
{
    public const int MaxImagesPerProduct = 12;

    private StoreOptions Store => store.Value;

    public async Task<Result<ProductImageView>> AddProductImageAsync(long number, Stream upload, long length, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.Include(p => p.Images).SingleOrDefaultAsync(p => p.PublicNumber == number, cancellationToken);
        if (product is null)
            return AppError.NotFound(CatalogErrors.NotFound, "No such product.");
        if (product.Images.Count >= MaxImagesPerProduct)
            return AppError.Validation(CatalogErrors.TooManyImages, $"A product can have at most {MaxImagesPerProduct} images.");

        var stored = await media.SaveImageAsync(upload, length, cancellationToken);
        if (!stored.IsSuccess)
            return stored.Error!;

        var image = new ProductImage
        {
            ProductId = product.Id,
            Url = stored.Value.Url,
            Width = stored.Value.Width,
            Height = stored.Value.Height,
            SortOrder = product.Images.Count == 0 ? 0 : product.Images.Max(i => i.SortOrder) + 1,
        };
        db.ProductImages.Add(image);
        await db.SaveChangesAsync(cancellationToken);
        return ToView(image, Store);
    }

    /// <summary>Puts the images in the given order (the first is the main image) and saves their alternative texts.</summary>
    public async Task<Result<IReadOnlyList<ProductImageView>>> SaveProductImagesAsync(long number, SaveProductImagesRequest request, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.Include(p => p.Images).SingleOrDefaultAsync(p => p.PublicNumber == number, cancellationToken);
        if (product is null)
            return AppError.NotFound(CatalogErrors.NotFound, "No such product.");

        var byId = product.Images.ToDictionary(i => i.Id);
        if (request.Images.Count != byId.Count || request.Images.Any(i => !byId.ContainsKey(i.Id)) || request.Images.DistinctBy(i => i.Id).Count() != byId.Count)
            return AppError.Validation(CatalogErrors.ImagesMismatch, "Send every image of the product exactly once.");
        if (request.Images.SelectMany(i => i.Alts).Any(a => !Store.LocaleList.Contains(a.Locale, StringComparer.Ordinal)))
            return AppError.Validation(CatalogErrors.LocaleInvalid, "An alternative text is in a language the shop does not offer.");

        for (var i = 0; i < request.Images.Count; i++)
        {
            var image = byId[request.Images[i].Id];
            image.SortOrder = i;
            ApplyAlts(image, request.Images[i].Alts);
        }
        await db.SaveChangesAsync(cancellationToken);
        return product.Images.OrderBy(i => i.SortOrder).Select(i => ToView(i, Store)).ToList();
    }

    public async Task<Result<bool>> DeleteProductImageAsync(long number, Guid imageId, CancellationToken cancellationToken = default)
    {
        var image = await db.ProductImages.SingleOrDefaultAsync(i => i.Id == imageId && i.Product.PublicNumber == number, cancellationToken);
        if (image is null)
            return AppError.NotFound(CatalogErrors.NotFound, "No such image.");
        db.ProductImages.Remove(image);
        await db.SaveChangesAsync(cancellationToken);
        await media.DeleteAsync(image.Url, cancellationToken);
        return true;
    }

    /// <summary>Replaces a category's tile image (the old uploaded files are deleted).</summary>
    public async Task<Result<string>> SetCategoryImageAsync(Guid categoryId, Stream upload, long length, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
        if (category is null)
            return AppError.NotFound(CatalogErrors.NotFound, "No such category.");
        var stored = await media.SaveImageAsync(upload, length, cancellationToken);
        if (!stored.IsSuccess)
            return stored.Error!;

        var old = category.ImageUrl;
        category.ImageUrl = MediaUrls.For(stored.Value.Key, 600);
        await db.SaveChangesAsync(cancellationToken);
        await media.DeleteAsync(old, cancellationToken);
        return category.ImageUrl;
    }

    public async Task<Result<bool>> RemoveCategoryImageAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
        if (category is null)
            return AppError.NotFound(CatalogErrors.NotFound, "No such category.");
        var old = category.ImageUrl;
        category.ImageUrl = null;
        await db.SaveChangesAsync(cancellationToken);
        await media.DeleteAsync(old, cancellationToken);
        return true;
    }

    private void ApplyAlts(ProductImage image, IReadOnlyList<ImageAltText> alts)
    {
        static string Clean(string? s)
        {
            var text = (s ?? "").Trim();
            return text.Length > 300 ? text[..300] : text;
        }

        image.Alt = alts.FirstOrDefault(a => a.Locale == Store.DefaultLocale) is { } main && Clean(main.Alt).Length > 0 ? Clean(main.Alt) : null;
        foreach (var alt in alts.Where(a => a.Locale != Store.DefaultLocale))
        {
            var text = Clean(alt.Alt);
            var existing = image.AltTranslations.FirstOrDefault(t => t.Locale == alt.Locale);
            if (text.Length == 0)
            {
                if (existing is not null)
                    image.AltTranslations.Remove(existing);
            }
            else if (existing is null)
            {
                image.AltTranslations.Add(new ImageAltTranslation { Locale = alt.Locale, Alt = text });
            }
            else
            {
                existing.Alt = text;
            }
        }
    }

    /// <summary>An image with its alternative text in every shop language (empty where not written).</summary>
    public static ProductImageView ToView(ProductImage image, StoreOptions store) => new(
        image.Id,
        image.Url,
        image.Width,
        image.Height,
        store.LocaleList
            .OrderBy(l => l == store.DefaultLocale ? 0 : 1)
            .Select(l => new ImageAltText(l, l == store.DefaultLocale ? image.Alt ?? "" : image.AltTranslations.FirstOrDefault(t => t.Locale == l)?.Alt ?? ""))
            .ToList());
}
