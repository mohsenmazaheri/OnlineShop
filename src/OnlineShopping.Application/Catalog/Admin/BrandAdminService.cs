using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Media;
using OnlineShopping.Domain.Catalog;

namespace OnlineShopping.Application.Catalog.Admin;

/// <summary>
/// Brands for the admin: list, create, rename (texts per language, unique slug), logo, and delete. A brand that
/// products still use cannot be deleted, so no product points at a removed brand.
/// </summary>
public sealed class BrandAdminService(IApplicationDbContext db, MediaService media, IOptions<StoreOptions> store)
{
    /// <summary>Logos are small: the 300 px size is stored.</summary>
    public const int LogoSize = 300;

    private StoreOptions Store => store.Value;

    public async Task<BrandList> ListAsync(CancellationToken cancellationToken = default)
    {
        var brands = await db.Brands.AsNoTracking().ToListAsync(cancellationToken);
        var counts = await db.Products.AsNoTracking()
            .Where(p => p.BrandId != null)
            .GroupBy(p => p.BrandId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        var views = brands
            .OrderBy(b => b.Name, StringComparer.CurrentCulture)
            .Select(b => View(b, counts.GetValueOrDefault(b.Id)))
            .ToList();
        return new BrandList(Store.DefaultLocale, Store.LocaleList, views);
    }

    public async Task<Result<BrandView>> CreateAsync(SaveBrandRequest request, CancellationToken cancellationToken = default)
    {
        var brand = new Brand { Name = "" };
        var applied = await ApplyAsync(brand, request, cancellationToken);
        if (!applied.IsSuccess)
            return applied.Error!;
        db.Brands.Add(brand);
        await db.SaveChangesAsync(cancellationToken);
        return View(brand, 0);
    }

    public async Task<Result<BrandView>> UpdateAsync(Guid id, SaveBrandRequest request, CancellationToken cancellationToken = default)
    {
        var brand = await db.Brands.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (brand is null)
            return NotFound();
        var applied = await ApplyAsync(brand, request, cancellationToken);
        if (!applied.IsSuccess)
            return applied.Error!;
        await db.SaveChangesAsync(cancellationToken);
        return View(brand, await db.Products.CountAsync(p => p.BrandId == id, cancellationToken));
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var brand = await db.Brands.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (brand is null)
            return NotFound();
        if (await db.Products.AnyAsync(p => p.BrandId == id, cancellationToken))
            return AppError.Conflict(CatalogErrors.HasProducts, "Products still use this brand.");

        var logo = brand.LogoUrl;
        brand.LogoUrl = null;
        db.Brands.Remove(brand);
        await db.SaveChangesAsync(cancellationToken);
        await media.DeleteAsync(logo, cancellationToken);
        return true;
    }

    /// <summary>Replaces the brand's logo (the old uploaded files are deleted).</summary>
    public async Task<Result<string>> SetLogoAsync(Guid id, Stream upload, long length, CancellationToken cancellationToken = default)
    {
        var brand = await db.Brands.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (brand is null)
            return NotFound();
        var stored = await media.SaveImageAsync(upload, length, cancellationToken);
        if (!stored.IsSuccess)
            return stored.Error!;

        var old = brand.LogoUrl;
        brand.LogoUrl = MediaUrls.For(stored.Value.Key, LogoSize);
        await db.SaveChangesAsync(cancellationToken);
        await media.DeleteAsync(old, cancellationToken);
        return brand.LogoUrl;
    }

    public async Task<Result<bool>> RemoveLogoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var brand = await db.Brands.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (brand is null)
            return NotFound();
        var old = brand.LogoUrl;
        brand.LogoUrl = null;
        await db.SaveChangesAsync(cancellationToken);
        await media.DeleteAsync(old, cancellationToken);
        return true;
    }

    private async Task<Result<bool>> ApplyAsync(Brand brand, SaveBrandRequest request, CancellationToken cancellationToken)
    {
        if (LocalizedTexts.Validate(request.Texts, Store) is { } error)
            return error;
        var main = LocalizedTexts.Default(request.Texts, Store.DefaultLocale);
        var slug = LocalizedTexts.SlugFor(main);
        if (await db.Brands.AnyAsync(b => b.Slug == slug && b.Id != brand.Id, cancellationToken))
            return AppError.Conflict(CatalogErrors.SlugTaken, $"A brand with slug {slug} exists.");

        brand.Name = main.Name;
        brand.Slug = slug;
        brand.Description = main.Description;
        LocalizedTexts.ApplyTranslations(request.Texts, Store.DefaultLocale, brand.Translations, withSlug: true, withDescription: true);
        return true;
    }

    private BrandView View(Brand b, int productCount) =>
        new(b.Id, b.LogoUrl, LocalizedTexts.Read(Store, b.Name, b.Slug, b.Description, b.Translations), productCount);

    private static AppError NotFound() => AppError.NotFound(CatalogErrors.NotFound, "No such brand.");
}
