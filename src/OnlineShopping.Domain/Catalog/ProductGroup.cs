using OnlineShopping.Domain.Common;

namespace OnlineShopping.Domain.Catalog;

public class ProductGroup : BaseEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }

    public Guid SubCategoryId { get; set; }
    public SubCategory SubCategory { get; set; } = default!;

    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<CatalogTranslation> Translations { get; set; } = new List<CatalogTranslation>();
}
