using Microsoft.EntityFrameworkCore;
using OnlineShop.Application.Common;
using OnlineShop.Application.Inventory;
using OnlineShop.Application.Notifications;
using OnlineShop.Domain.Sales;

namespace OnlineShop.Application.Sales;

/// <summary>
/// Moves orders through their life (see <see cref="OrderTransitions"/>). Every change claims the order with a
/// conditional UPDATE ("status = expected"), so two staff members or the expiry job can never both act; it is
/// written to the history and the customer is told.
/// <list type="bullet">
/// <item>Paid (by hand until the payment gateways of phase 8): the held stock is sold. When the hold ran out
/// meanwhile, the stock is held again first, or the change is refused when it is gone.</item>
/// <item>Canceled (before payment, by staff or when the payment time runs out): the stock and the discount code's use
/// are given back.</item>
/// </list>
/// </summary>
public sealed class OrderLifecycleService(
    IApplicationDbContext db,
    StockReservationService reservations,
    DiscountCodeService discounts,
    OrderNotifier notifier,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public static string ReservationReference(long number) => $"order:{number}";

    public Task<Result<OrderStatus>> MarkPaidAsync(long number, OrderActionRequest request, CancellationToken cancellationToken = default) =>
        ChangeAsync(number, OrderStatus.Pending, OrderStatus.Paid, request?.Text, OrderNotice.Paid, cancellationToken, SellAsync);

    public Task<Result<OrderStatus>> CompleteAsync(long number, OrderActionRequest request, CancellationToken cancellationToken = default) =>
        ChangeAsync(number, null, OrderStatus.Completed, request?.Text, null, cancellationToken);

    public Task<Result<OrderStatus>> CancelAsync(long number, OrderActionRequest request, CancellationToken cancellationToken = default) =>
        ChangeAsync(number, OrderStatus.Pending, OrderStatus.Canceled, request?.Text, OrderNotice.Canceled, cancellationToken, GiveBackAsync);

    public async Task<Result<OrderStatus>> ShipAsync(long number, ShipOrderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var carrier = request.Carrier.Trim();
        var code = request.TrackingCode.Trim();
        return await ChangeAsync(number, OrderStatus.Paid, OrderStatus.Shipped, null, OrderNotice.Shipped, cancellationToken, async (order, ct) =>
        {
            await db.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.TrackingCarrier, carrier).SetProperty(o => o.TrackingCode, code), ct);
            db.OrderEvents.Add(new OrderEvent { OrderId = order.Id, Kind = OrderEventKind.TrackingSet, Text = $"{carrier} {code}", ActorUserId = currentUser.UserId });
            return null;
        });
    }

    /// <summary>A note for staff only.</summary>
    public async Task<Result<bool>> AddNoteAsync(long number, OrderNoteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = await db.Orders.Where(o => o.Number == number).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(cancellationToken);
        if (id is null)
            return AppError.NotFound(OrderErrors.NotFound, "Order not found.");
        db.OrderEvents.Add(new OrderEvent { OrderId = id.Value, Kind = OrderEventKind.Note, Text = request.Text.Trim(), IsInternal = true, ActorUserId = currentUser.UserId });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Cancels the unpaid orders whose payment time ran out (the expiry job). Returns how many.</summary>
    public async Task<int> CancelExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Pending && o.PaymentDueUtc != null && o.PaymentDueUtc <= now)
            .OrderBy(o => o.PaymentDueUtc).Select(o => o.Number).Take(100)
            .ToListAsync(cancellationToken);
        var canceled = 0;
        foreach (var number in due)
        {
            var result = await ChangeAsync(number, OrderStatus.Pending, OrderStatus.Canceled, null, OrderNotice.PaymentExpired, cancellationToken, async (order, ct) =>
            {
                // After giving back: the reservation calls clear the change tracker.
                var error = await GiveBackAsync(order, ct);
                db.OrderEvents.Add(new OrderEvent { OrderId = order.Id, Kind = OrderEventKind.PaymentExpired });
                return error;
            }, bySystem: true);
            if (result.IsSuccess)
                canceled++;
        }
        return canceled;
    }

    private sealed record Target(Guid Id, long Number, OrderStatus Status, bool IsPickup, Guid? DiscountCodeId);

    /// <param name="from">The status the order must be in (null: any status the transition table allows).</param>
    private async Task<Result<OrderStatus>> ChangeAsync(
        long number, OrderStatus? from, OrderStatus to, string? text, OrderNotice? notice, CancellationToken cancellationToken,
        Func<Target, CancellationToken, Task<AppError?>>? work = null, bool bySystem = false)
    {
        var order = await db.Orders.AsNoTracking().Where(o => o.Number == number)
            .Select(o => new Target(o.Id, o.Number, o.Status, o.IsPickup, o.DiscountCodeId))
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null)
            return AppError.NotFound(OrderErrors.NotFound, "Order not found.");
        if ((from is not null && order.Status != from) || !Allowed(order, to))
            return AppError.Conflict(OrderErrors.StatusInvalid, $"An order that is {order.Status} cannot become {to}.");

        var actor = bySystem ? null : currentUser.UserId;
        var result = await db.InTransactionAsync<OrderStatus>(async () =>
        {
            var claimed = await db.Orders.Where(o => o.Id == order.Id && o.Status == order.Status)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, to), cancellationToken);
            if (claimed == 0)
                return AppError.Conflict(OrderErrors.StatusInvalid, "The order changed meanwhile.");
            if (work is not null && await work(order, cancellationToken) is { } error)
                return error;
            db.OrderEvents.Add(new OrderEvent { OrderId = order.Id, Kind = OrderEventKind.StatusChanged, Status = to, ActorUserId = actor });
            // A cancellation's reason is for the customer; any other note (a payment reference) is for staff only.
            if (!string.IsNullOrWhiteSpace(text))
                db.OrderEvents.Add(new OrderEvent { OrderId = order.Id, Kind = OrderEventKind.Note, Text = text.Trim(), ActorUserId = actor, IsInternal = to != OrderStatus.Canceled });
            await db.SaveChangesAsync(cancellationToken);
            return to;
        }, cancellationToken);
        db.ChangeTracker.Clear();

        if (result.IsSuccess && notice is { } n)
            await notifier.NotifyAsync(order.Id, n, cancellationToken);
        return result;
    }

    private static bool Allowed(Target order, OrderStatus to)
    {
        var actions = OrderTransitions.For(order.Status, order.IsPickup);
        return to switch
        {
            OrderStatus.Paid => actions.CanMarkPaid,
            OrderStatus.Shipped => actions.CanShip,
            OrderStatus.Completed => actions.CanComplete,
            OrderStatus.Canceled => actions.CanCancel,
            _ => false,
        };
    }

    /// <summary>The held stock is sold; a hold that ran out is renewed first (or the payment is refused: nothing left).</summary>
    private async Task<AppError?> SellAsync(Target order, CancellationToken cancellationToken)
    {
        var reference = ReservationReference(order.Number);
        var committed = await reservations.CommitAsync(reference, reference, cancellationToken);
        if (committed.IsSuccess)
            return null;
        if (committed.Error!.Code != ReservationErrors.NothingReserved)
            return committed.Error;

        // The hold ran out (maybe not yet released by the job): release what is left, hold again, then sell.
        await reservations.ReleaseAsync(reference, cancellationToken);
        var items = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == order.Id)
            .Select(i => new { i.ProductId, i.VariantId, i.Quantity }).ToListAsync(cancellationToken);
        var held = await reservations.ReserveAsync(
            reference, items.Select(i => new ReservationInput(new StockItem(i.ProductId, i.VariantId), i.Quantity)).ToList(), cancellationToken: cancellationToken);
        if (!held.IsSuccess)
            return AppError.Conflict(OrderErrors.NotAvailable, "Some items of the order are no longer in stock.");
        committed = await reservations.CommitAsync(reference, reference, cancellationToken);
        return committed.IsSuccess ? null : committed.Error;
    }

    /// <summary>The held stock goes back on sale and the discount code may be used again.</summary>
    private async Task<AppError?> GiveBackAsync(Target order, CancellationToken cancellationToken)
    {
        await reservations.ReleaseAsync(ReservationReference(order.Number), cancellationToken);
        if (order.DiscountCodeId is { } codeId)
            await discounts.UnclaimAsync(codeId, cancellationToken);
        return null;
    }
}
