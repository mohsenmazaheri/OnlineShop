using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Sales;

namespace OnlineShop.Application.Sales;

/// <summary>
/// Shopping carts. A signed-in customer has one cart; a guest's cart is found by the hash of a random token kept in
/// the guest's cookie, and is merged into the account's cart after signing in. Lines hold only the item and quantity:
/// prices and stock are read now, every time (rule 5: prices are computed on the server). Adding more than is
/// available is refused; a line that became unavailable stays, marked, out of the subtotal.
/// </summary>
public sealed class CartService(IApplicationDbContext db, IOptions<StoreOptions> store)
{
    public const int MaxQuantity = 99;
    public const int MaxLines = 50;

    /// <summary>A new guest cart token (256 random bits, URL-safe); only its hash is stored.</summary>
    public static string NewGuestToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string HashToken(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<CartView> GetAsync(CartOwner owner, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var cart = await FindAsync(owner, cancellationToken);
        return await ViewAsync(cart?.Id, locale, cancellationToken);
    }

    /// <summary>The cart as checkout needs it: the view plus each line's product, variant, SKU and weight.</summary>
    public async Task<CheckoutCart> GetForCheckoutAsync(CartOwner owner, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var cart = await FindAsync(owner, cancellationToken);
        var items = cart is null ? [] : await ItemsAsync(cart.Id, locale, cancellationToken);
        return new CheckoutCart(cart?.Id, ToView(items), items);
    }

    /// <summary>Empties a cart after its order was placed.</summary>
    public async Task ClearAsync(Guid cartId, CancellationToken cancellationToken = default)
    {
        await db.CartItems.Where(i => i.CartId == cartId).ExecuteDeleteAsync(cancellationToken);
        await db.Carts.Where(c => c.Id == cartId).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<Result<CartView>> AddAsync(CartOwner owner, AddToCartRequest request, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);
        var product = await db.Products.AsNoTracking()
            .Where(p => p.IsPublished && p.PublicNumber == request.Number)
            .Select(p => new { p.Id, Available = p.Stock - p.Reserved, HasVariants = p.Variants.Any() })
            .FirstOrDefaultAsync(cancellationToken);
        if (product is null)
            return AppError.NotFound(CartErrors.ProductNotFound, $"No product with number {request.Number}.");

        Guid? variantId = null;
        var available = product.Available;
        if (product.HasVariants || !string.IsNullOrWhiteSpace(request.VariantSku))
        {
            var sku = (request.VariantSku ?? "").Trim();
            var variant = await db.ProductVariants.AsNoTracking()
                .Where(v => v.ProductId == product.Id && v.Sku == sku)
                .Select(v => new { v.Id, Available = v.Stock - v.Reserved })
                .FirstOrDefaultAsync(cancellationToken);
            if (variant is null)
                return AppError.Validation(CartErrors.VariantRequired, "Choose one of the product's variants.");
            (variantId, available) = (variant.Id, variant.Available);
        }

        var cart = await FindAsync(owner, cancellationToken) ?? Create(owner);
        var itemId = variantId ?? product.Id;
        var line = cart.Items.FirstOrDefault(i => i.ItemId == itemId);
        var quantity = Math.Min(MaxQuantity, (line?.Quantity ?? 0) + request.Quantity);
        if (quantity > available)
            return AppError.Conflict(CartErrors.NotEnoughStock, $"Only {Math.Max(0, available)} available.");
        if (line is null)
        {
            if (cart.Items.Count >= MaxLines)
                return AppError.Validation(CartErrors.TooManyLines, $"A cart holds at most {MaxLines} lines.");
            db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = product.Id, VariantId = variantId, ItemId = itemId, Quantity = quantity });
        }
        else
        {
            line.Quantity = quantity;
        }
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return await ViewAsync(cart.Id, locale, cancellationToken);
    }

    /// <summary>Changes a line's quantity (0 removes it). More than available is refused, less is always allowed.</summary>
    public async Task<Result<CartView>> SetQuantityAsync(CartOwner owner, Guid lineId, int quantity, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var cart = await FindAsync(owner, cancellationToken);
        var line = cart?.Items.FirstOrDefault(i => i.Id == lineId);
        if (cart is null || line is null)
            return AppError.NotFound(CartErrors.LineNotFound, "No such line in your cart.");
        if (quantity <= 0)
        {
            await db.CartItems.Where(i => i.Id == lineId && i.CartId == cart.Id).ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            if (quantity > line.Quantity)
            {
                var available = await AvailableAsync(line, cancellationToken);
                if (quantity > available)
                    return AppError.Conflict(CartErrors.NotEnoughStock, $"Only {Math.Max(0, available)} available.");
            }
            line.Quantity = Math.Min(MaxQuantity, quantity);
            await db.SaveChangesAsync(cancellationToken);
        }
        db.ChangeTracker.Clear();
        return await ViewAsync(cart.Id, locale, cancellationToken);
    }

    /// <summary>
    /// After signing in: the guest cart's lines move into the account's cart (quantities added up, never above what is
    /// available) and the guest cart is deleted. Nothing happens without a guest cart.
    /// </summary>
    public async Task MergeAsync(string userId, string guestToken, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(guestToken);
        var guest = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.CustomerId == null && c.GuestTokenHash == hash, cancellationToken);
        if (guest is null)
            return;
        var mine = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.CustomerId == userId, cancellationToken);
        if (mine is null)
        {
            // Simply becomes the customer's cart.
            guest.CustomerId = userId;
            guest.GuestTokenHash = null;
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return;
        }
        foreach (var item in guest.Items)
        {
            var line = mine.Items.FirstOrDefault(i => i.ItemId == item.ItemId);
            var available = await AvailableAsync(item, cancellationToken);
            if (line is null)
            {
                if (mine.Items.Count >= MaxLines)
                    continue;
                line = new CartItem { CartId = mine.Id, ProductId = item.ProductId, VariantId = item.VariantId, ItemId = item.ItemId, Quantity = 0 };
                db.CartItems.Add(line);
                mine.Items.Add(line);
            }
            line.Quantity = Math.Clamp(line.Quantity + item.Quantity, 1, Math.Max(1, Math.Min(MaxQuantity, available)));
        }
        await db.CartItems.Where(i => i.CartId == guest.Id).ExecuteDeleteAsync(cancellationToken);
        await db.Carts.Where(c => c.Id == guest.Id).ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    private async Task<Cart?> FindAsync(CartOwner owner, CancellationToken cancellationToken)
    {
        if (owner.UserId is { } userId)
            return await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.CustomerId == userId, cancellationToken);
        if (owner.GuestToken is { Length: > 0 } token)
        {
            var hash = HashToken(token);
            return await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.CustomerId == null && c.GuestTokenHash == hash, cancellationToken);
        }
        return null;
    }

    private Cart Create(CartOwner owner)
    {
        if (owner.IsEmpty)
            throw new InvalidOperationException("A cart needs a customer or a guest token.");
        var cart = new Cart { CustomerId = owner.UserId, GuestTokenHash = owner.UserId is null ? HashToken(owner.GuestToken!) : null };
        db.Carts.Add(cart);
        return cart;
    }

    private async Task<int> AvailableAsync(CartItem line, CancellationToken cancellationToken) =>
        line.VariantId is { } variantId
            ? await db.ProductVariants.Where(v => v.Id == variantId).Select(v => v.Stock - v.Reserved).FirstOrDefaultAsync(cancellationToken)
            : await db.Products.Where(p => p.Id == line.ProductId).Select(p => p.Stock - p.Reserved).FirstOrDefaultAsync(cancellationToken);

    private async Task<CartView> ViewAsync(Guid? cartId, string locale, CancellationToken cancellationToken) =>
        ToView(cartId is { } id ? await ItemsAsync(id, locale, cancellationToken) : []);

    private CartView ToView(IReadOnlyList<CheckoutItem> items)
    {
        var lines = items.Select(i => i.Line).ToList();
        return new CartView(lines, lines.Sum(l => l.Quantity), lines.Sum(l => l.LineTotal), store.Value.Currency, lines.Any(l => l.Problem != CartLineProblem.None));
    }

    private async Task<List<CheckoutItem>> ItemsAsync(Guid cartId, string locale, CancellationToken cancellationToken)
    {
        var items = await db.CartItems.AsNoTracking().Where(i => i.CartId == cartId).OrderBy(i => i.CreatedUtc)
            .Select(i => new { i.Id, i.ProductId, i.VariantId, i.Quantity }).ToListAsync(cancellationToken);
        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.PublicNumber,
                p.SKU,
                p.WeightGrams,
                Name = p.Translations.Where(t => t.Locale == locale).Select(t => t.Name).FirstOrDefault() ?? p.Name,
                Slug = p.Translations.Where(t => t.Locale == locale).Select(t => t.Slug).FirstOrDefault() ?? p.Slug,
                Image = p.Images.OrderBy(m => m.SortOrder).Select(m => m.Url).FirstOrDefault(),
                p.Price,
                p.CompareAtPrice,
                Available = p.Stock - p.Reserved,
                Buyable = p.IsPublished && !p.IsDeleted,
            })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var variantIds = items.Where(i => i.VariantId != null).Select(i => i.VariantId!.Value).ToList();
        var variants = await db.ProductVariants.IgnoreQueryFilters().AsNoTracking().Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku, v.Price, v.CompareAtPrice, Available = v.Stock - v.Reserved, v.IsDeleted })
            .ToDictionaryAsync(v => v.Id, cancellationToken);
        var labels = await VariantLabelsAsync(variantIds, locale, cancellationToken);

        var lines = new List<CheckoutItem>();
        foreach (var item in items)
        {
            if (!products.TryGetValue(item.ProductId, out var p))
                continue;
            var v = item.VariantId is { } vid && variants.TryGetValue(vid, out var found) ? found : null;
            var price = v?.Price ?? p.Price;
            var compareAt = v is null || v.Price is null ? p.CompareAtPrice : v.CompareAtPrice;
            var available = Math.Max(0, v?.Available ?? p.Available);
            var problem = !p.Buyable || (item.VariantId is not null && (v is null || v.IsDeleted)) ? CartLineProblem.Unavailable
                : available == 0 ? CartLineProblem.OutOfStock
                : item.Quantity > available ? CartLineProblem.NotEnoughStock
                : CartLineProblem.None;
            var line = new CartLine(
                item.Id, p.PublicNumber, p.Name, p.Slug, v?.Sku, v is null ? null : labels.GetValueOrDefault(v.Id), p.Image,
                price, compareAt, item.Quantity, available, problem == CartLineProblem.None ? price * item.Quantity : 0, problem);
            lines.Add(new CheckoutItem(line, p.Id, item.VariantId, v?.Sku ?? p.SKU, p.WeightGrams ?? 0));
        }
        return lines;
    }

    private sealed record OptionRow(Guid VariantId, int SpecOrder, string Value);

    /// <summary>"Beige / M": a variant's option values in the requested language, in the specs' order.</summary>
    private async Task<Dictionary<Guid, string>> VariantLabelsAsync(List<Guid> variantIds, string locale, CancellationToken cancellationToken)
    {
        if (variantIds.Count == 0)
            return [];
        var choices = await db.ProductVariants.IgnoreQueryFilters().AsNoTracking().Where(v => variantIds.Contains(v.Id))
            .SelectMany(v => v.Options, (v, o) => new { VariantId = v.Id, o.SpecId, o.OptionId }).ToListAsync(cancellationToken);
        var specIds = choices.Select(c => c.SpecId).Distinct().ToList();
        var optionIds = choices.Select(c => c.OptionId).Distinct().ToList();
        var specOrder = await db.ProductSpecs.IgnoreQueryFilters().AsNoTracking().Where(s => specIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.SortOrder, cancellationToken);
        var values = await db.SpecOptions.IgnoreQueryFilters().AsNoTracking().Where(o => optionIds.Contains(o.Id))
            .Select(o => new { o.Id, Value = o.Translations.Where(t => t.Locale == locale).Select(t => t.Value).FirstOrDefault() ?? o.Value })
            .ToDictionaryAsync(o => o.Id, o => o.Value, cancellationToken);
        return choices
            .Select(c => new OptionRow(c.VariantId, specOrder.GetValueOrDefault(c.SpecId), values.GetValueOrDefault(c.OptionId) ?? ""))
            .GroupBy(r => r.VariantId)
            .ToDictionary(g => g.Key, g => string.Join(" / ", g.OrderBy(r => r.SpecOrder).Select(r => r.Value)));
    }
}
