using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Content;

/// <summary>Where a piece of content lives: a blog post (/{locale}/blog/{slug}) or a page (/{locale}/{slug}, "about").</summary>
public enum ContentKind
{
    Post,
    Page,
}

/// <summary>
/// A blog post or a content page, written by the shop (docs/SEO-MARKETING.md §9). The default language is in the own
/// columns, the others in <see cref="Translations"/> (translated by hand only). The body is plain text with a small set
/// of marks (paragraphs, ## headings, - lists, **bold**, [links](…)), rendered safely by the storefront.
/// </summary>
public sealed class ContentPage : BaseEntity
{
    public ContentKind Kind { get; set; }

    public required string Slug { get; set; }

    public required string Title { get; set; }

    /// <summary>A short summary for lists and the search result description.</summary>
    public string? Summary { get; set; }

    public required string Body { get; set; }

    public string? MetaTitle { get; set; }

    public string? MetaDescription { get; set; }

    /// <summary>Comma-separated tags in the default language (posts).</summary>
    public string? Tags { get; set; }

    /// <summary>Cover image (a media URL, 1200 px).</summary>
    public string? CoverImageUrl { get; set; }

    /// <summary>The name shown as the author.</summary>
    public string? AuthorName { get; set; }

    /// <summary>Public numbers of products the post is about (comma-separated), shown below it.</summary>
    public string? RelatedProducts { get; set; }

    public bool IsPublished { get; set; }

    /// <summary>From when the post is shown (a later time schedules it); set on the first publication.</summary>
    public DateTime? PublishedUtc { get; set; }

    /// <summary>Pages: listed in the shop's footer.</summary>
    public bool ShowInFooter { get; set; }

    public ICollection<ContentTranslation> Translations { get; set; } = new List<ContentTranslation>();
}

/// <summary>A post's or page's texts in one more language.</summary>
public sealed class ContentTranslation
{
    public required string Locale { get; set; }

    public required string Title { get; set; }

    public required string Slug { get; set; }

    public string? Summary { get; set; }

    public required string Body { get; set; }

    public string? MetaTitle { get; set; }

    public string? MetaDescription { get; set; }

    public string? Tags { get; set; }
}
