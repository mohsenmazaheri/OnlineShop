using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Common;
using OnlineShop.Application.Media;
using OnlineShop.Domain.Catalog;

namespace OnlineShop.Application.Catalog.Admin;

/// <summary>A video's title in one language (empty: not written; the product name is used).</summary>
public sealed record VideoTitleText(string Locale, string Title);

/// <summary>A product video for the admin.</summary>
public sealed record ProductVideoView(Guid Id, string Url, string? PosterUrl, long SizeBytes, double? DurationSeconds, IReadOnlyList<VideoTitleText> Titles);

public sealed record ProductVideoInput(Guid Id, IReadOnlyList<VideoTitleText> Titles);

/// <summary>All of a product's videos in their new order, with their titles.</summary>
public sealed record SaveProductVideosRequest(IReadOnlyList<ProductVideoInput> Videos);

/// <summary>
/// Product videos: upload (an MP4, checked by <see cref="MediaService.SaveVideoAsync"/>), a poster image, titles per
/// language, order and removal (the stored files are deleted too).
/// </summary>
public sealed class CatalogVideoService(IApplicationDbContext db, MediaService media, IOptions<StoreOptions> store)
{
    public const int MaxVideosPerProduct = 3;
    private const int MaxTitleLength = 200;

    private StoreOptions Store => store.Value;

    public async Task<Result<ProductVideoView>> AddAsync(long number, Stream upload, long length, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.Include(p => p.Videos).SingleOrDefaultAsync(p => p.PublicNumber == number, cancellationToken);
        if (product is null)
            return NotFound();
        if (product.Videos.Count >= MaxVideosPerProduct)
            return AppError.Validation(CatalogErrors.TooManyVideos, $"A product can have at most {MaxVideosPerProduct} videos.");

        var stored = await media.SaveVideoAsync(upload, length, cancellationToken);
        if (!stored.IsSuccess)
            return stored.Error!;

        var video = new ProductVideo
        {
            ProductId = product.Id,
            Url = stored.Value.Url,
            SizeBytes = stored.Value.SizeBytes,
            DurationSeconds = stored.Value.DurationSeconds,
            SortOrder = product.Videos.Count == 0 ? 0 : product.Videos.Max(v => v.SortOrder) + 1,
        };
        db.ProductVideos.Add(video);
        await db.SaveChangesAsync(cancellationToken);
        return ToView(video, Store);
    }

    /// <summary>Puts the videos in the given order and saves their titles.</summary>
    public async Task<Result<IReadOnlyList<ProductVideoView>>> SaveAsync(long number, SaveProductVideosRequest request, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.Include(p => p.Videos).SingleOrDefaultAsync(p => p.PublicNumber == number, cancellationToken);
        if (product is null)
            return NotFound();
        var byId = product.Videos.ToDictionary(v => v.Id);
        if (request.Videos.Count != byId.Count || request.Videos.Any(v => !byId.ContainsKey(v.Id)) || request.Videos.DistinctBy(v => v.Id).Count() != byId.Count)
            return AppError.Validation(CatalogErrors.VideosMismatch, "Send every video of the product exactly once.");
        if (request.Videos.SelectMany(v => v.Titles).Any(t => !Store.LocaleList.Contains(t.Locale, StringComparer.Ordinal)))
            return AppError.Validation(CatalogErrors.LocaleInvalid, "A title is in a language the shop does not offer.");

        for (var i = 0; i < request.Videos.Count; i++)
        {
            var video = byId[request.Videos[i].Id];
            video.SortOrder = i;
            ApplyTitles(video, request.Videos[i].Titles);
        }
        await db.SaveChangesAsync(cancellationToken);
        return product.Videos.OrderBy(v => v.SortOrder).Select(v => ToView(v, Store)).ToList();
    }

    public async Task<Result<bool>> DeleteAsync(long number, Guid id, CancellationToken cancellationToken = default)
    {
        var video = await FindAsync(number, id, cancellationToken);
        if (video is null)
            return NotFound();
        db.ProductVideos.Remove(video);
        await db.SaveChangesAsync(cancellationToken);
        await media.DeleteAsync(video.Url, cancellationToken);
        await media.DeleteAsync(video.PosterUrl, cancellationToken);
        return true;
    }

    /// <summary>Replaces the poster image (checked and re-encoded like every image; the old files are deleted).</summary>
    public async Task<Result<string>> SetPosterAsync(long number, Guid id, Stream upload, long length, CancellationToken cancellationToken = default)
    {
        var video = await FindAsync(number, id, cancellationToken);
        if (video is null)
            return NotFound();
        var stored = await media.SaveImageAsync(upload, length, cancellationToken);
        if (!stored.IsSuccess)
            return stored.Error!;

        var old = video.PosterUrl;
        video.PosterUrl = stored.Value.Url;
        await db.SaveChangesAsync(cancellationToken);
        await media.DeleteAsync(old, cancellationToken);
        return video.PosterUrl;
    }

    public async Task<Result<bool>> RemovePosterAsync(long number, Guid id, CancellationToken cancellationToken = default)
    {
        var video = await FindAsync(number, id, cancellationToken);
        if (video is null)
            return NotFound();
        var old = video.PosterUrl;
        video.PosterUrl = null;
        await db.SaveChangesAsync(cancellationToken);
        await media.DeleteAsync(old, cancellationToken);
        return true;
    }

    /// <summary>A video for the admin, with its title in every shop language (empty where not written).</summary>
    public static ProductVideoView ToView(ProductVideo video, StoreOptions store) => new(
        video.Id,
        video.Url,
        video.PosterUrl,
        video.SizeBytes,
        video.DurationSeconds,
        store.LocaleList
            .OrderBy(l => l == store.DefaultLocale ? 0 : 1)
            .Select(l => new VideoTitleText(l, l == store.DefaultLocale ? video.Title ?? "" : video.TitleTranslations.FirstOrDefault(t => t.Locale == l)?.Title ?? ""))
            .ToList());

    private Task<ProductVideo?> FindAsync(long number, Guid id, CancellationToken cancellationToken) =>
        db.ProductVideos.SingleOrDefaultAsync(v => v.Id == id && v.Product.PublicNumber == number, cancellationToken);

    private void ApplyTitles(ProductVideo video, IReadOnlyList<VideoTitleText> titles)
    {
        static string Clean(string? text)
        {
            var trimmed = (text ?? "").Trim();
            return trimmed.Length > MaxTitleLength ? trimmed[..MaxTitleLength] : trimmed;
        }

        foreach (var title in titles)
        {
            var text = Clean(title.Title);
            if (title.Locale == Store.DefaultLocale)
            {
                video.Title = text.Length > 0 ? text : null;
                continue;
            }
            var existing = video.TitleTranslations.FirstOrDefault(t => t.Locale == title.Locale);
            if (text.Length == 0)
            {
                if (existing is not null)
                    video.TitleTranslations.Remove(existing);
            }
            else if (existing is null)
            {
                video.TitleTranslations.Add(new VideoTitleTranslation { Locale = title.Locale, Title = text });
            }
            else
            {
                existing.Title = text;
            }
        }
    }

    private static AppError NotFound() => AppError.NotFound(CatalogErrors.NotFound, "No such product or video.");
}
