using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Catalog;

public class ProductImage : BaseEntity
{
    /// <summary>Address of the largest stored size (/api/v1/media/{key}/1200.webp) or, for demo data, a site path.</summary>
    public required string Url { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Pixel size of the stored image (unknown for demo illustrations).</summary>
    public int? Width { get; set; }
    public int? Height { get; set; }

    /// <summary>Alternative text in the default language; other languages in <see cref="AltTranslations"/>.</summary>
    public string? Alt { get; set; }

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = default!;

    public ICollection<ImageAltTranslation> AltTranslations { get; set; } = new List<ImageAltTranslation>();
}

/// <summary>An image's alternative text in one more language (for screen readers and image search).</summary>
public sealed class ImageAltTranslation
{
    public required string Locale { get; set; }
    public required string Alt { get; set; }
}
