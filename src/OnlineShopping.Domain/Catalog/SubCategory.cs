using OnlineShopping.Domain.Common;

namespace OnlineShopping.Domain.Catalog;

public class SubCategory : BaseEntity
{
    public required string Name { get; set; }
    public string Slug { get; set; } = string.Empty;

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = default!;

    public ICollection<ProductGroup> Groups { get; set; } = new List<ProductGroup>();

    /// <summary>Specs added to the category's template for products in this sub-category.</summary>
    public ICollection<TemplateSpec> TemplateSpecs { get; set; } = new List<TemplateSpec>();
    public ICollection<CatalogTranslation> Translations { get; set; } = new List<CatalogTranslation>();
}
