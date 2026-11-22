using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OnlineShop.Domain.Catalog;
using OnlineShop.Domain.Search;

namespace OnlineShop.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Queues every catalog change for the search index in the same save (the outbox), so no writer can forget to: a
/// product, its translations, variants or spec values → that product; a renamed brand, category, sub-category, product
/// group, spec or option → everything under it. New brands, categories and specs have no products yet and are skipped.
/// Stock changes (atomic updates past the change tracker) do not change the search texts.
/// </summary>
public sealed class SearchOutboxInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Enqueue(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Enqueue(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Enqueue(DbContext? context)
    {
        if (context is null)
            return;
        var targets = new HashSet<(SearchTarget, Guid)>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;
            var existing = entry.State != EntityState.Added;
            switch (entry.Entity)
            {
                case Product product:
                    targets.Add((SearchTarget.Product, product.Id));
                    break;
                case ProductVariant variant:
                    targets.Add((SearchTarget.Product, variant.ProductId));
                    break;
                case ProductSpecValue value:
                    targets.Add((SearchTarget.Product, value.ProductId));
                    break;
                case Brand brand when existing:
                    targets.Add((SearchTarget.Brand, brand.Id));
                    break;
                case Category category when existing:
                    targets.Add((SearchTarget.Category, category.Id));
                    break;
                case SubCategory sub when existing:
                    targets.Add((SearchTarget.SubCategory, sub.Id));
                    break;
                case ProductGroup group when existing:
                    targets.Add((SearchTarget.ProductGroup, group.Id));
                    break;
                case ProductSpec spec when existing:
                    targets.Add((SearchTarget.Spec, spec.Id));
                    break;
                case SpecOption option when existing:
                    targets.Add((SearchTarget.SpecOption, option.Id));
                    break;
                case CatalogTranslation or SpecTranslation or SpecOptionTranslation or VariantOption:
                    if (Owner(context, entry) is { } owner)
                        targets.Add(owner);
                    break;
            }
        }
        if (targets.Count == 0)
            return;
        var now = clock.GetUtcNow().UtcDateTime;
        var outbox = context.Set<SearchOutboxEntry>();
        foreach (var (target, id) in targets)
            outbox.Add(new SearchOutboxEntry { Target = target, TargetId = id, EnqueuedUtc = now });
    }

    /// <summary>The search target a translation (or a variant's option) belongs to, from its owner's key.</summary>
    private static (SearchTarget, Guid)? Owner(DbContext context, EntityEntry entry)
    {
        var ownership = entry.Metadata.FindOwnership();
        if (ownership is null || entry.Property(ownership.Properties[0].Name).CurrentValue is not Guid ownerId)
            return null;
        var type = ownership.PrincipalEntityType.ClrType;
        if (type == typeof(ProductVariant))
        {
            var variant = context.ChangeTracker.Entries<ProductVariant>().FirstOrDefault(v => v.Entity.Id == ownerId);
            return variant is null ? null : (SearchTarget.Product, variant.Entity.ProductId);
        }
        SearchTarget? target = type == typeof(Product) ? SearchTarget.Product
            : type == typeof(Brand) ? SearchTarget.Brand
            : type == typeof(Category) ? SearchTarget.Category
            : type == typeof(SubCategory) ? SearchTarget.SubCategory
            : type == typeof(ProductGroup) ? SearchTarget.ProductGroup
            : type == typeof(ProductSpec) ? SearchTarget.Spec
            : type == typeof(SpecOption) ? SearchTarget.SpecOption
            : null;
        return target is { } t ? (t, ownerId) : null;
    }
}
