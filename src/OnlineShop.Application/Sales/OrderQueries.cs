using Microsoft.EntityFrameworkCore;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Sales;

namespace OnlineShop.Application.Sales;

/// <summary>
/// Reading orders: a customer's own (ownership checked in the query), a guest's through its signed link, and every
/// order for staff. Customers never see internal notes or who changed what.
/// </summary>
public sealed class OrderQueries(IApplicationDbContext db, IOrderLinks links, SalesSettingsService settings, TimeProvider clock)
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

    /// <summary>The id of the customer's own order, or of a guest's through its link (null: none of theirs).</summary>
    public async Task<Guid?> FindIdAsync(long number, string? userId, string? token, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.AsNoTracking().Where(o => o.Number == number).Select(o => new { o.Id, o.CustomerId }).FirstOrDefaultAsync(cancellationToken);
        if (order is null)
            return null;
        if (userId is not null && order.CustomerId == userId)
            return order.Id;
        return !string.IsNullOrWhiteSpace(token) && links.IsValid(order.Id, token) ? order.Id : null;
    }

    /// <summary>The invoice page of the customer's own order, a guest's through its link, or any for staff (null: no invoice yet).</summary>
    public async Task<InvoiceView?> GetInvoiceAsync(long number, string? userId, string? token, bool staff, CancellationToken cancellationToken = default)
    {
        var view = staff ? await GetForStaffAsync(number, cancellationToken)
            : userId is not null ? await GetForCustomerAsync(userId, number, cancellationToken)
            : await GetWithTokenAsync(number, token ?? "", cancellationToken);
        if (view is null)
            return null;
        var invoice = await db.Invoices.AsNoTracking().Where(i => db.Orders.Any(o => o.Id == i.OrderId && o.Number == number))
            .FirstOrDefaultAsync(cancellationToken);
        return invoice is null
            ? null
            : new InvoiceView(invoice.Number, invoice.IssuedUtc, invoice.SellerName, invoice.SellerAddress, invoice.SellerTaxNumber, invoice.Note, view);
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
            // Entries written together share their time: placed, then tracking or expiry, the status change, the invoice,
            // the note.
            .OrderBy(e => e.CreatedUtc)
            .ThenBy(e => e.Kind == OrderEventKind.Placed ? 0
                : e.Kind == OrderEventKind.TrackingSet || e.Kind == OrderEventKind.PaymentExpired ? 1
                : e.Kind == OrderEventKind.StatusChanged ? 2
                : e.Kind == OrderEventKind.Invoiced ? 3 : 4)
            .Select(e => new OrderEventView(e.CreatedUtc, e.Kind, e.Status, e.Text, e.IsInternal, e.ActorUserId != null))
            .ToListAsync(cancellationToken);
        var images = await ImagesAsync(order.Items.Select(i => i.ProductId).Distinct().ToList(), cancellationToken);
        var invoice = await db.Invoices.AsNoTracking().Where(i => i.OrderId == order.Id)
            .Select(i => new InvoiceSummary(i.Number, i.IssuedUtc)).FirstOrDefaultAsync(cancellationToken);
        var returns = await db.ReturnRequests.AsNoTracking().Include(r => r.Lines).Where(r => r.OrderId == order.Id)
            .OrderBy(r => r.CreatedUtc).ToListAsync(cancellationToken);
        var refunds = await db.Refunds.AsNoTracking().Where(r => r.OrderId == order.Id).OrderBy(r => r.CreatedUtc).ToListAsync(cancellationToken);

        // What is still out there to ask back: ordered minus what is in returns that were not rejected.
        var asked = returns.Where(r => r.Status != ReturnStatus.Rejected).SelectMany(r => r.Lines)
            .GroupBy(l => l.OrderItemId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var items = order.Items.ToDictionary(i => i.Id);
        var lines = order.Items.OrderBy(i => i.CreatedUtc)
            .Select(i => new OrderLineView(i.Id, i.ProductNumber, i.ProductName, i.Sku, i.VariantLabel, images.GetValueOrDefault(i.ProductId), i.UnitPrice, i.Quantity,
                i.UnitPrice * i.Quantity, Math.Max(0, i.Quantity - asked.GetValueOrDefault(i.Id))))
            .ToList();
        var returnNumbers = returns.ToDictionary(r => r.Id, r => r.Number);
        var returnViews = returns.Select(r =>
        {
            var steps = OrderTransitions.ForReturn(r.Status);
            var returnLines = r.Lines.Where(l => items.ContainsKey(l.OrderItemId))
                .Select(l => new ReturnLineView(l.OrderItemId, items[l.OrderItemId].ProductName, items[l.OrderItemId].VariantLabel, items[l.OrderItemId].Sku, l.Quantity))
                .ToList();
            var goods = r.Lines.Where(l => items.ContainsKey(l.OrderItemId)).Sum(l => items[l.OrderItemId].UnitPrice * l.Quantity);
            return new ReturnView(r.Id, r.Number, r.Status, r.CreatedUtc, r.Reason, r.Answer, returnLines, SuggestedRefund(order, goods),
                staff && steps.Approve, staff && steps.Reject, staff && steps.Receive);
        }).ToList();
        var refundViews = refunds
            .Select(r => new RefundView(r.CreatedUtc, r.Amount, r.Method, staff ? r.Note : null, r.ReturnRequestId is { } id ? returnNumbers.GetValueOrDefault(id) : null))
            .ToList();
        var refunded = refunds.Sum(r => r.Amount);
        var refundable = invoice is null ? 0 : Math.Max(0, order.Total - refunded);

        var returnDays = (await settings.GetAsync(cancellationToken)).ReturnDays;
        DateTime? returnableUntil = order.Status == OrderStatus.Completed && order.CompletedUtc is { } delivered && returnDays > 0
            ? delivered.AddDays(returnDays) : null;
        if (returnableUntil <= clock.GetUtcNow().UtcDateTime)
            returnableUntil = null;

        var actions = OrderTransitions.For(order.Status, order.IsPickup) with
        {
            CanRequestReturn = !staff && returnableUntil is not null && lines.Any(l => l.Returnable > 0),
            CanRefund = staff && refundable > 0,
        };
        var address = order.IsPickup ? null : new OrderAddressView(order.CountryCode, order.Region, order.City, order.Line1, order.Line2, order.PostalCode);
        return new OrderView(
            order.Number, order.Status, order.OrderedUtc, order.Status == OrderStatus.Pending ? order.PaymentDueUtc : null, order.Locale,
            order.Email, order.RecipientName, order.PhoneNumber, address, order.IsPickup, order.ShippingMethodName, order.TrackingCarrier,
            order.TrackingCode, order.Note, order.DiscountCodeText, order.Currency, order.Subtotal, order.Discount, order.Shipping, order.Tax,
            order.TaxRate, order.PricesIncludeTax, order.Total, lines, events, actions,
            staff ? order.CustomerId : null, invoice, returnViews, refundViews, refunded, refundable, returnableUntil);
    }

    /// <summary>
    /// What a return of goods worth <paramref name="goods"/> (at their prices) gives back: their share of what was paid
    /// for the goods after the discount, rounded like the order's currency. Staff may change it.
    /// </summary>
    public static decimal SuggestedRefund(Order order, decimal goods)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.Subtotal <= 0)
            return 0;
        var share = goods * (order.Subtotal - order.Discount) / order.Subtotal;
        var decimals = CheckoutPricing.DefaultDecimals(order.Currency);
        return CheckoutPricing.Round(share, decimals);
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
