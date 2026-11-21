using FluentValidation;

namespace OnlineShopping.Application.Catalog.Admin;

/// <summary>Error codes of the catalog admin (translated by the web app under <c>admin.catalogErrors</c>).</summary>
public static class CatalogErrors
{
    public const string NotFound = "catalog.not_found";
    public const string NameRequired = "catalog.name_required";
    public const string NameTooLong = "catalog.name_too_long";
    public const string DefaultTextRequired = "catalog.default_text_required";
    public const string LocaleInvalid = "catalog.locale_invalid";
    public const string LocaleRepeated = "catalog.locale_repeated";
    public const string SlugTaken = "catalog.slug_taken";
    public const string HasChildren = "catalog.has_children";
    public const string HasProducts = "catalog.has_products";
    public const string SkuRequired = "catalog.sku_required";
    public const string SkuTaken = "catalog.sku_taken";
    public const string PriceInvalid = "catalog.price_invalid";
    public const string CompareAtInvalid = "catalog.compare_at_invalid";
    public const string SaleNeedsOldPrice = "catalog.sale_needs_old_price";
    public const string LandingQueryInvalid = "catalog.landing_query_invalid";
    public const string LandingSlugTaken = "catalog.landing_slug_taken";
    public const string LandingNotFound = "catalog.landing_not_found";
    public const string StockInvalid = "catalog.stock_invalid";
    public const string GroupRequired = "catalog.group_required";
    public const string SpecNotFound = "catalog.spec_not_found";
    public const string SpecValueInvalid = "catalog.spec_value_invalid";
    public const string TooManyImages = "catalog.too_many_images";
    public const string ImagesMismatch = "catalog.images_mismatch";
    public const string BrandNotFound = "catalog.brand_not_found";
    public const string CodeInvalid = "catalog.code_invalid";
    public const string CodeTaken = "catalog.code_taken";
    public const string SpecInUse = "catalog.spec_in_use";
    public const string OptionInUse = "catalog.option_in_use";
    public const string OptionRepeated = "catalog.option_repeated";
    public const string SpecIsVariant = "catalog.spec_is_variant";
    public const string VariantSpecInvalid = "catalog.variant_spec_invalid";
    public const string VariantOptionsInvalid = "catalog.variant_options_invalid";
    public const string VariantRepeated = "catalog.variant_repeated";
    public const string TooManyVariants = "catalog.too_many_variants";
    public const string GtinInvalid = "catalog.gtin_invalid";
    public const string WeightInvalid = "catalog.weight_invalid";
    public const string TooManyVideos = "catalog.too_many_videos";
    public const string VideosMismatch = "catalog.videos_mismatch";
}

/// <summary>
/// An item's text in one language. The shop's default language is stored in the item's own columns, the others as
/// translations; an empty name for another language means "not translated" (the default language is shown).
/// </summary>
public sealed record LocalizedText(string Locale, string Name, string? Slug, string? Description);

public sealed record GroupNode(Guid Id, IReadOnlyList<LocalizedText> Texts, int ProductCount);

/// <summary><see cref="TemplateSpecs"/>: codes of the template's specs, in the specs' order.</summary>
public sealed record SubCategoryNode(Guid Id, IReadOnlyList<LocalizedText> Texts, IReadOnlyList<GroupNode> Groups, IReadOnlyList<string> TemplateSpecs);

/// <summary>The spec codes of a category's or sub-category's template (replaces the previous set).</summary>
public sealed record SaveTemplateRequest(IReadOnlyList<string> SpecCodes);

public sealed record CategoryNode(
    Guid Id, string? ImageUrl, IReadOnlyList<LocalizedText> Texts, IReadOnlyList<SubCategoryNode> SubCategories, IReadOnlyList<string> TemplateSpecs);

/// <summary>The whole category tree for the admin, with the languages to fill in.</summary>
public sealed record CatalogTree(string DefaultLocale, IReadOnlyList<string> Locales, IReadOnlyList<CategoryNode> Categories);

/// <summary>Create or rename a category, sub-category or product group (groups have no slug).</summary>
public sealed record SaveCatalogNodeRequest(IReadOnlyList<LocalizedText> Texts);

public sealed record CatalogNodeView(Guid Id, IReadOnlyList<LocalizedText> Texts);

/// <summary>One spec value of a product; exactly the field of the spec's kind is used, all empty removes the value.</summary>
public sealed record ProductSpecInput(string Code, decimal? Number, bool? Boolean, string? Text, string? OptionCode);

/// <summary>A product's search-result texts and search words in one language (all optional).</summary>
public sealed record SeoText(string Locale, string? MetaTitle, string? MetaDescription, string? Tags);

public sealed record SaveProductRequest(
    string Sku,
    decimal Price,
    decimal? CompareAtPrice,
    int Stock,
    bool IsPublished,
    Guid ProductGroupId,
    IReadOnlyList<LocalizedText> Texts,
    IReadOnlyList<ProductSpecInput> Specs,
    Guid? BrandId = null,
    string? Gtin = null,
    string? Mpn = null,
    int? WeightGrams = null,
    IReadOnlyList<SeoText>? Seo = null,
    DateTime? SaleEndsUtc = null);

/// <summary>A product row of the admin list. <see cref="MissingLocales"/>: shop languages without a translation.</summary>
public sealed record ProductListItem(
    long Number,
    string Name,
    string Sku,
    decimal Price,
    int Stock,
    bool IsPublished,
    string GroupPath,
    string? ImageUrl,
    IReadOnlyList<string> MissingLocales);

public sealed record ProductPage(IReadOnlyList<ProductListItem> Items, int Total, int Page, int PageSize);

public sealed record ProductEditView(
    long Number,
    string Sku,
    decimal Price,
    decimal? CompareAtPrice,
    int Stock,
    bool IsPublished,
    Guid ProductGroupId,
    IReadOnlyList<LocalizedText> Texts,
    IReadOnlyList<ProductSpecInput> Specs,
    IReadOnlyList<ProductImageView> Images,
    Guid? BrandId,
    ProductVariantsView Variants,
    string? Gtin,
    string? Mpn,
    int? WeightGrams,
    IReadOnlyList<SeoText> Seo,
    IReadOnlyList<ProductVideoView> Videos,
    DateTime? SaleEndsUtc = null);

/// <summary>An image's alternative text in one language (empty: not translated).</summary>
public sealed record ImageAltText(string Locale, string Alt);

/// <summary>A product image for the admin: its address, size and alternative text in every shop language.</summary>
public sealed record ProductImageView(Guid Id, string Url, int? Width, int? Height, IReadOnlyList<ImageAltText> Alts);

public sealed record ProductImageInput(Guid Id, IReadOnlyList<ImageAltText> Alts);

/// <summary>All of a product's images in their new order, with their alternative texts.</summary>
public sealed record SaveProductImagesRequest(IReadOnlyList<ProductImageInput> Images);

public sealed record SpecOptionChoice(string Code, string Value);

/// <summary>A spec the product editor offers, named in the admin's language.</summary>
public sealed record SpecChoice(string Code, string Name, string? GroupName, string Kind, string? Unit, IReadOnlyList<SpecOptionChoice> Options);

public sealed class SaveCatalogNodeRequestValidator : AbstractValidator<SaveCatalogNodeRequest>
{
    public SaveCatalogNodeRequestValidator()
    {
        RuleFor(r => r.Texts).NotEmpty().WithErrorCode(CatalogErrors.DefaultTextRequired);
        RuleForEach(r => r.Texts).ChildRules(text =>
        {
            text.RuleFor(t => t.Name).MaximumLength(200).WithErrorCode(CatalogErrors.NameTooLong);
            text.RuleFor(t => t.Slug).MaximumLength(200).WithErrorCode(CatalogErrors.NameTooLong);
        });
    }
}

public sealed class SaveProductRequestValidator : AbstractValidator<SaveProductRequest>
{
    public SaveProductRequestValidator()
    {
        RuleFor(r => r.Sku).NotEmpty().WithErrorCode(CatalogErrors.SkuRequired)
            .MaximumLength(64).WithErrorCode(CatalogErrors.SkuRequired);
        RuleFor(r => r.Price).GreaterThanOrEqualTo(0).WithErrorCode(CatalogErrors.PriceInvalid)
            .LessThan(10_000_000_000_000_000m).WithErrorCode(CatalogErrors.PriceInvalid);
        // An "old price" only makes sense above the price (it shows a discount).
        RuleFor(r => r.CompareAtPrice).Must((r, compareAt) => compareAt is null || compareAt > r.Price)
            .WithErrorCode(CatalogErrors.CompareAtInvalid);
        // A special offer ends by going back to the old price, so it needs one.
        RuleFor(r => r.SaleEndsUtc).Must((r, end) => end is null || r.CompareAtPrice is not null).WithErrorCode(CatalogErrors.SaleNeedsOldPrice);
        RuleFor(r => r.Stock).InclusiveBetween(0, 1_000_000_000).WithErrorCode(CatalogErrors.StockInvalid);
        RuleFor(r => r.ProductGroupId).NotEmpty().WithErrorCode(CatalogErrors.GroupRequired);
        RuleFor(r => r.Texts).NotEmpty().WithErrorCode(CatalogErrors.DefaultTextRequired);
        RuleForEach(r => r.Texts).ChildRules(text =>
        {
            text.RuleFor(t => t.Name).MaximumLength(300).WithErrorCode(CatalogErrors.NameTooLong);
            text.RuleFor(t => t.Slug).MaximumLength(200).WithErrorCode(CatalogErrors.NameTooLong);
            text.RuleFor(t => t.Description).MaximumLength(10_000).WithErrorCode(CatalogErrors.NameTooLong);
        });
        RuleFor(r => r.Mpn).MaximumLength(70).WithErrorCode(CatalogErrors.NameTooLong);
        RuleFor(r => r.WeightGrams).InclusiveBetween(0, 1_000_000).WithErrorCode(CatalogErrors.WeightInvalid);
        RuleForEach(r => r.Seo).ChildRules(seo =>
        {
            seo.RuleFor(s => s.MetaTitle).MaximumLength(200).WithErrorCode(CatalogErrors.NameTooLong);
            seo.RuleFor(s => s.MetaDescription).MaximumLength(400).WithErrorCode(CatalogErrors.NameTooLong);
            seo.RuleFor(s => s.Tags).MaximumLength(500).WithErrorCode(CatalogErrors.NameTooLong);
        });
    }
}
