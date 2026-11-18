using OnlineShopping.Domain.Common;

namespace OnlineShopping.Domain.Catalog;

/// <summary>How a spec's values are stored: in a typed column, so they can be filtered and compared.</summary>
public enum SpecKind
{
    /// <summary>Free text that reads the same in every language (brand, model code, "A5").</summary>
    Text,

    /// <summary>A number with an optional unit (6.5 inch, 128 GB).</summary>
    Number,

    /// <summary>Yes or no.</summary>
    Boolean,

    /// <summary>One of the spec's options, each translated (colour, material).</summary>
    Option,
}

/// <summary>
/// A product property such as "Screen size" or "Colour" (the base of specifications, filters and comparison).
/// Name, group and unit are in the default language; <see cref="Translations"/> holds the others.
/// </summary>
public sealed class ProductSpec : BaseEntity
{
    /// <summary>Stable identifier used by code and imports (<c>screen_size</c>); never shown.</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    /// <summary>Heading the spec is listed under on the product page ("Display", "Battery").</summary>
    public string? GroupName { get; set; }

    public string? Unit { get; set; }

    public SpecKind Kind { get; set; }

    public int SortOrder { get; set; }

    public bool IsFilterable { get; set; }

    public bool IsComparable { get; set; }

    /// <summary>Shown in the short key-facts list next to the product's price.</summary>
    public bool ShowInSummary { get; set; }

    public ICollection<SpecTranslation> Translations { get; set; } = new List<SpecTranslation>();

    public ICollection<SpecOption> Options { get; set; } = new List<SpecOption>();
}

/// <summary>An attribute's name, group heading and unit in one more language.</summary>
public sealed class SpecTranslation
{
    public required string Locale { get; set; }
    public required string Name { get; set; }
    public string? GroupName { get; set; }
    public string? Unit { get; set; }
}

/// <summary>One choice of an <see cref="SpecKind.Option"/> attribute ("Black" for "Colour").</summary>
public sealed class SpecOption : BaseEntity
{
    public Guid SpecId { get; set; }
    public ProductSpec Spec { get; set; } = default!;

    /// <summary>Stable identifier (<c>black</c>).</summary>
    public required string Code { get; set; }

    /// <summary>Display value in the default language.</summary>
    public required string Value { get; set; }

    public int SortOrder { get; set; }

    public ICollection<SpecOptionTranslation> Translations { get; set; } = new List<SpecOptionTranslation>();
}

public sealed class SpecOptionTranslation
{
    public required string Locale { get; set; }
    public required string Value { get; set; }
}

/// <summary>
/// One product's value for one attribute, in the column that matches <see cref="ProductSpec.Kind"/>
/// (a separate table so filters and facet counts can use indexes).
/// </summary>
public sealed class ProductSpecValue : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = default!;

    public Guid SpecId { get; set; }
    public ProductSpec Spec { get; set; } = default!;

    public decimal? NumberValue { get; set; }
    public bool? BooleanValue { get; set; }
    public string? TextValue { get; set; }

    public Guid? OptionId { get; set; }
    public SpecOption? Option { get; set; }
}
