using OnlineShopping.Domain.Common;

namespace OnlineShopping.Domain.Settings;

/// <summary>How round corners are (the design token <c>--radius</c>).</summary>
public enum CornerStyle
{
    Sharp,
    Soft,
    Round,
}

/// <summary>The storefront's font: the bundled Vazirmatn/Inter set, or the visitor's system font.</summary>
public enum FontChoice
{
    Vazirmatn,
    System,
}

/// <summary>Ready-made arrangements of the home page sections (no rebuild needed to switch).</summary>
public enum HomeLayout
{
    /// <summary>Banner, shortcuts, offers, categories, new arrivals, benefits.</summary>
    Classic,

    /// <summary>Categories first, then offers and new arrivals (shops with many categories).</summary>
    Catalog,

    /// <summary>Banner, then new arrivals and categories (shops that show off products).</summary>
    Showcase,
}

/// <summary>The shop's look, edited in the admin panel and applied at runtime. One row per shop.</summary>
public sealed class StoreBranding : BaseEntity
{
    public const string DefaultPrimaryColor = "#0f766e";

    /// <summary>Main colour as #rrggbb (buttons, links, banner).</summary>
    public string PrimaryColor { get; set; } = DefaultPrimaryColor;

    public CornerStyle Corners { get; set; } = CornerStyle.Soft;

    public FontChoice Font { get; set; } = FontChoice.Vazirmatn;

    public HomeLayout HomeLayout { get; set; } = HomeLayout.Classic;

    /// <summary>Uploaded logo (a media address); without one the header shows the shop name's first letter.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Uploaded image for the home page banner; without one the banner shows a drawn pattern.</summary>
    public string? HeroImageUrl { get; set; }
}
