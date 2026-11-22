using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Catalog;

/// <summary>
/// A product video (MP4, stored as uploaded). Its title is in the default language here and in
/// <see cref="TitleTranslations"/> for the others; without a title the product name is used.
/// </summary>
public sealed class ProductVideo : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = default!;

    /// <summary>The media address of the file (<c>/api/v1/media/{key}/video.mp4</c>).</summary>
    public required string Url { get; set; }

    /// <summary>Still image shown before playing (a media image, 1200 px); without one the product's main image is used.</summary>
    public string? PosterUrl { get; set; }

    public string? Title { get; set; }

    public long SizeBytes { get; set; }

    public double? DurationSeconds { get; set; }

    public int SortOrder { get; set; }

    public ICollection<VideoTitleTranslation> TitleTranslations { get; set; } = new List<VideoTitleTranslation>();
}

public sealed class VideoTitleTranslation
{
    public required string Locale { get; set; }
    public required string Title { get; set; }
}
