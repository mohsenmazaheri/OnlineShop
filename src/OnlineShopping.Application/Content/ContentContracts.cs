using FluentValidation;
using OnlineShopping.Application.Catalog;
using OnlineShopping.Domain.Content;

namespace OnlineShopping.Application.Content;

public static class ContentErrors
{
    public const string NotFound = "content.not_found";
    public const string TitleRequired = "content.title_required";
    public const string BodyRequired = "content.body_required";
    public const string TooLong = "content.too_long";
    public const string LocaleInvalid = "content.locale_invalid";
    public const string SlugTaken = "content.slug_taken";
    public const string SlugReserved = "content.slug_reserved";
}

/// <summary>A post's or page's texts in one language (an empty title in another language: not translated).</summary>
public sealed record ContentText(string Locale, string Title, string? Slug, string? Summary, string Body, string? MetaTitle, string? MetaDescription, string? Tags);

/// <summary>Create or change a post or page. <paramref name="PublishedUtc"/>: empty = now, on publishing.</summary>
public sealed record SaveContentRequest(
    ContentKind Kind,
    IReadOnlyList<ContentText> Texts,
    string? AuthorName,
    IReadOnlyList<long> RelatedProducts,
    bool IsPublished,
    DateTime? PublishedUtc,
    bool ShowInFooter);

public sealed class SaveContentRequestValidator : AbstractValidator<SaveContentRequest>
{
    public SaveContentRequestValidator()
    {
        RuleFor(r => r.Texts).NotEmpty().WithErrorCode(ContentErrors.TitleRequired);
        RuleForEach(r => r.Texts).ChildRules(text =>
        {
            text.RuleFor(t => t.Title).MaximumLength(300).WithErrorCode(ContentErrors.TooLong);
            text.RuleFor(t => t.Slug).MaximumLength(200).WithErrorCode(ContentErrors.TooLong);
            text.RuleFor(t => t.Summary).MaximumLength(1000).WithErrorCode(ContentErrors.TooLong);
            text.RuleFor(t => t.Body).MaximumLength(100_000).WithErrorCode(ContentErrors.TooLong);
            text.RuleFor(t => t.MetaTitle).MaximumLength(200).WithErrorCode(ContentErrors.TooLong);
            text.RuleFor(t => t.MetaDescription).MaximumLength(400).WithErrorCode(ContentErrors.TooLong);
            text.RuleFor(t => t.Tags).MaximumLength(500).WithErrorCode(ContentErrors.TooLong);
        });
        RuleFor(r => r.AuthorName).MaximumLength(100).WithErrorCode(ContentErrors.TooLong);
        RuleFor(r => r.RelatedProducts).NotNull().Must(r => r.Count <= 12).WithErrorCode(ContentErrors.TooLong);
    }
}

/// <summary>A post or page for the admin.</summary>
public sealed record ContentView(
    Guid Id,
    ContentKind Kind,
    IReadOnlyList<ContentText> Texts,
    string? AuthorName,
    IReadOnlyList<long> RelatedProducts,
    bool IsPublished,
    DateTime? PublishedUtc,
    bool ShowInFooter,
    string? CoverImageUrl,
    DateTime ModifiedUtc);

public sealed record ContentList(string DefaultLocale, IReadOnlyList<string> Locales, IReadOnlyList<ContentView> Items);

public sealed record ContentAlternate(string Locale, string Slug);

/// <summary>A published post or page as the storefront shows it (the default language where not translated).</summary>
public sealed record ContentArticle(
    ContentKind Kind,
    string Title,
    string Slug,
    string? Summary,
    string Body,
    string? MetaTitle,
    string? MetaDescription,
    string? CoverImageUrl,
    string? AuthorName,
    IReadOnlyList<string> Tags,
    DateTime PublishedUtc,
    DateTime ModifiedUtc,
    IReadOnlyList<ContentAlternate> Alternates,
    IReadOnlyList<ProductCard> RelatedProducts);

/// <summary>A post in the blog's list.</summary>
public sealed record PostSummary(string Title, string Slug, string? Summary, string? CoverImageUrl, DateTime PublishedUtc, IReadOnlyList<string> Tags);

/// <summary>A page of the blog (newest first), optionally one tag's posts, and every tag in use.</summary>
public sealed record PostList(IReadOnlyList<PostSummary> Items, int Page, int PageSize, int Total, IReadOnlyList<string> Tags);

/// <summary>A page linked in the footer.</summary>
public sealed record FooterPage(string Title, string Slug);
