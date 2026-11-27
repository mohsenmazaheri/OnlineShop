using Microsoft.EntityFrameworkCore;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Sales;

namespace OnlineShop.Application.Sales;

/// <summary>
/// Reading orders: a customer's own (ownership checked in the query), a guest's through its signed link, and every
/// order for staff. Customers never see internal notes or who changed what.
/// </summary>
public sealed class OrderQueries(IApplicationDbContext db, IOrderLinks links)
{
    public const int PageSize = 20;

    private sealed record ProductImage(Guid ProductId, string Url);

    public Task<OrderPage> ListForCustomerAsync(string userId, int page, CancellationToken cancellationToken = default) =>
        PageAsync(db.Orders.AsNoTracking().Where(o => o.CustomerId == userId), page, cancellationToken);

    /// <summary>Staff: newest first, by status and a search over the number, name, email and phone number.</summary>
    public Task<OrderPage> ListAsync(OrderListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var orders = db.Orders.AsNoTracking();
        if (query.Status is { } status)
            orders = orders.Where(o => o.Status == status);
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            if (long.TryParse(search, out var number))
            {
                orders = orders.Where(o => o.Number == number);
            }
            else
            {
                var term = search.ToLowerInvariant();
                var digits = new string(search.Where(char.IsAsciiDigit).ToArray());
                // ToLower() runs in SQL (lower()), not in .NET: the culture rules of the analyzers do not apply here.
#pragma warning disable CA1304, CA1311, CA1862
                orders = orders.Where(o =>
                    o.RecipientName.ToLower().Contains(term)
                    || (o.Email != null && o.Email.ToLower().Contains(term))
                    || (digits.Length >= 4 && o.PhoneNumber.Contains(digits)));
#pragma warning restore CA1304, CA1311, CA1862
            }
        }
        return PageAsync(orders, query.Page, cancellationToken);
    }

    public async Task<OrderView?> GetForCustomerAsync(string userId, long number, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Number == number && o.CustomerId == userId, cancellationToken);
        return order is null ? null : await ViewAsync(order, staff: false, cancellationToken);
    }

    /// <summary>A guest's order through the link in its emails; a wrong token is "not found", like a wrong number.</summary>
    public async Task<OrderView?> GetWithTokenAsync(long number, string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Number == number, cancellationToken);
        return order is null || !links.IsValid(order.Id, token) ? null : await ViewAsync(order, staff: false, cancellationToken);
    }

    public async Task<OrderView?> GetForStaffAsync(long number, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Number == number, cancellationToken);
        return order is null ? null : await ViewAsync(order, staff: true, cancellationToken);
    }

    private async Task<OrderPage> PageAsync(IQueryable<Order> orders, int page, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        var total = await orders.CountAsync(cancellationToken);
        var rows = await orders
            .OrderByDescending(o => o.OrderedUtc).ThenByDescending(o => o.Number)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(o => new
            {
                o.Number,
                o.Status,
                o.OrderedUtc,
                o.RecipientName,
                o.Email,
                ItemCount = o.Items.Sum(i => i.Quantity),
                o.Total,
                o.Currency,
                FirstProduct = o.Items.OrderBy(i => i.CreatedUtc).Select(i => (Guid?)i.ProductId).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        var images = await ImagesAsync(rows.Where(r => r.FirstProduct != null).Select(r => r.FirstProduct!.Value).ToList(), cancellationToken);
        var items = rows.Select(r => new OrderSummary(
            r.Number, r.Status, r.OrderedUtc, r.RecipientName, r.Email, r.ItemCount, r.Total, r.Currency,
            r.FirstProduct is { } id ? images.GetValueOrDefault(id) : null)).ToList();
        return new OrderPage(items, total, page, PageSize);
    }

    private async Task<OrderView> ViewAsync(Order order, bool staff, CancellationToken cancellationToken)
    {
        var events = await db.OrderEvents.AsNoTracking()
            .Where(e => e.OrderId == order.Id && (staff || !e.IsInternal))
            // Entries written together share their time: placed, then tracking or expiry, the status change, the note.
            .OrderBy(e => e.CreatedUtc)
            .ThenBy(e => e.Kind == OrderEventKind.Placed ? 0
                : e.Kind == OrderEventKind.TrackingSet || e.Kind == OrderEventKind.PaymentExpired ? 1
                : e.Kind == OrderEventKind.StatusChanged ? 2 : 3)
            .Select(e => new OrderEventView(e.CreatedUtc, e.Kind, e.Status, e.Text, e.IsInternal, e.ActorUserId != null))
            .ToListAsync(cancellationToken);
        var images = await ImagesAsync(order.Items.Select(i => i.ProductId).Distinct().ToList(), cancellationToken);
        var lines = order.Items.OrderBy(i => i.CreatedUtc)
            .Select(i => new OrderLineView(i.ProductNumber, i.ProductName, i.Sku, i.VariantLabel, images.GetValueOrDefault(i.ProductId), i.UnitPrice, i.Quantity, i.UnitPrice * i.Quantity))
            .ToList();
        var address = order.IsPickup ? null : new OrderAddressView(order.CountryCode, order.Region, order.City, order.Line1, order.Line2, order.PostalCode);
        return new OrderView(
            order.Number, order.Status, order.OrderedUtc, order.Status == OrderStatus.Pending ? order.PaymentDueUtc : null, order.Locale,
            order.Email, order.RecipientName, order.PhoneNumber, address, order.IsPickup, order.ShippingMethodName, order.TrackingCarrier,
            order.TrackingCode, order.Note, order.DiscountCodeText, order.Currency, order.Subtotal, order.Discount, order.Shipping, order.Tax,
            order.TaxRate, order.PricesIncludeTax, order.Total, lines, events, OrderTransitions.For(order.Status, order.IsPickup),
            staff ? order.CustomerId : null);
    }

    /// <summary>Each product's first image (deleted products too: the order still shows what was bought).</summary>
    private async Task<Dictionary<Guid, string>> ImagesAsync(List<Guid> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
            return [];
        var images = await db.ProductImages.IgnoreQueryFilters().AsNoTracking()
            .Where(i => productIds.Contains(i.ProductId) && !i.IsDeleted)
            .Select(i => new { i.ProductId, i.Url, i.SortOrder })
            .ToListAsync(cancellationToken);
        return images.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.OrderBy(i => i.SortOrder).First().Url);
    }
}
