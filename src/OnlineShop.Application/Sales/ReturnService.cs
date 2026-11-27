using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Application.Common;
using OnlineShop.Application.Inventory;
using OnlineShop.Application.Notifications;
using OnlineShop.Domain.Inventory;
using OnlineShop.Domain.Sales;

namespace OnlineShop.Application.Sales;

/// <summary>
/// Returns and refunds. A customer asks to send items of a delivered order back within the shop's return period (never
/// more than was ordered, counting earlier returns that were not rejected); staff approve or reject it, and mark the
/// items received, which puts them back into stock. Refunds are recorded by staff (by hand until the gateways of
/// phase 8 can refund): never more than was paid in all. Every step goes into the order's history and the customer
/// is told.
/// </summary>
public sealed class ReturnService(
    IApplicationDbContext db,
    StockLedger ledger,
    SalesSettingsService settings,
    OrderNotifier notifier,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public static string ReturnName(long number) => $"R-{number}";

    /// <summary>The customer's request (the caller checked that the order is theirs).</summary>
    public async Task<Result<long>> RequestAsync(Guid orderId, RequestReturnRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
            return AppError.NotFound(OrderErrors.NotFound, "Order not found.");
        var days = (await settings.GetAsync(cancellationToken)).ReturnDays;
        if (order.Status != OrderStatus.Completed || order.CompletedUtc is not { } delivered || days <= 0
            || delivered.AddDays(days) <= clock.GetUtcNow().UtcDateTime)
            return AppError.Conflict(OrderErrors.ReturnNotAllowed, "This order can no longer be returned.");

        var asked = await db.ReturnLines.AsNoTracking()
            .Where(l => db.ReturnRequests.Any(r => r.Id == l.ReturnRequestId && r.OrderId == orderId && r.Status != ReturnStatus.Rejected))
            .GroupBy(l => l.OrderItemId).Select(g => new { g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, cancellationToken);
        var wanted = request.Lines.GroupBy(l => l.OrderItemId).Select(g => new ReturnLineInput(g.Key, g.Sum(l => l.Quantity))).ToList();
        foreach (var line in wanted)
        {
            var item = order.Items.FirstOrDefault(i => i.Id == line.OrderItemId);
            if (item is null || line.Quantity <= 0 || line.Quantity > item.Quantity - asked.GetValueOrDefault(item.Id))
                return AppError.Validation(OrderErrors.ReturnQuantityInvalid, "A line or quantity cannot be returned.");
        }

        var number = (await db.ReturnRequests.MaxAsync(r => (long?)r.Number, cancellationToken) ?? 0) + 1;
        var returned = new ReturnRequest { Number = number, OrderId = orderId, Reason = request.Reason.Trim() };
        db.ReturnRequests.Add(returned);
        foreach (var line in wanted)
            db.ReturnLines.Add(new ReturnLine { ReturnRequestId = returned.Id, OrderItemId = line.OrderItemId, Quantity = line.Quantity });
        db.OrderEvents.Add(new OrderEvent { OrderId = orderId, Kind = OrderEventKind.ReturnRequested, Text = ReturnName(number) });
        await db.SaveChangesAsync(cancellationToken);
        await notifier.NotifyAsync(orderId, OrderNotice.ReturnRequested, ReturnName(number), null, cancellationToken);
        return number;
    }

    public Task<Result<ReturnStatus>> ApproveAsync(long orderNumber, Guid returnId, ReturnAnswerRequest request, CancellationToken cancellationToken = default) =>
        AnswerAsync(orderNumber, returnId, ReturnStatus.Requested, ReturnStatus.Approved, request?.Text, OrderEventKind.ReturnApproved, OrderNotice.ReturnApproved, cancellationToken);

    public async Task<Result<ReturnStatus>> RejectAsync(long orderNumber, Guid returnId, ReturnAnswerRequest request, CancellationToken cancellationToken = default)
    {
        var status = await db.ReturnRequests.AsNoTracking().Where(r => r.Id == returnId).Select(r => (ReturnStatus?)r.Status).FirstOrDefaultAsync(cancellationToken);
        // An approved return can still be turned down (the items never came, or came back unusable).
        var from = status == ReturnStatus.Approved ? ReturnStatus.Approved : ReturnStatus.Requested;
        return await AnswerAsync(orderNumber, returnId, from, ReturnStatus.Rejected, request?.Text, OrderEventKind.ReturnRejected, OrderNotice.ReturnRejected, cancellationToken);
    }

    /// <summary>The items arrived: they go back into the default warehouse as "Return" movements.</summary>
    public async Task<Result<ReturnStatus>> ReceiveAsync(long orderNumber, Guid returnId, ReturnAnswerRequest request, CancellationToken cancellationToken = default)
    {
        var target = await FindAsync(orderNumber, returnId, cancellationToken);
        if (target is null)
            return AppError.NotFound(OrderErrors.ReturnNotFound, "Return not found.");
        if (target.Status != ReturnStatus.Approved)
            return AppError.Conflict(OrderErrors.ReturnStatusInvalid, "Only an approved return can be received.");

        var result = await db.InTransactionAsync<ReturnStatus>(async () =>
        {
            if (!await ClaimAsync(returnId, ReturnStatus.Approved, ReturnStatus.Received, request?.Text, cancellationToken))
                return AppError.Conflict(OrderErrors.ReturnStatusInvalid, "The return changed meanwhile.");
            var warehouse = await ledger.DefaultWarehouseIdAsync(cancellationToken);
            var lines = await db.ReturnLines.AsNoTracking().Where(l => l.ReturnRequestId == returnId)
                .Join(db.OrderItems, l => l.OrderItemId, i => i.Id, (l, i) => new { i.ProductId, i.VariantId, l.Quantity })
                .ToListAsync(cancellationToken);
            foreach (var line in lines)
            {
                var posted = await ledger.PostAsync(warehouse, new StockItem(line.ProductId, line.VariantId), StockMovementKind.Return, line.Quantity,
                    ReturnName(target.Number), cancellationToken: cancellationToken);
                if (!posted.IsSuccess)
                    return posted.Error!;
            }
            db.OrderEvents.Add(new OrderEvent { OrderId = target.OrderId, Kind = OrderEventKind.ReturnReceived, Text = ReturnName(target.Number), ActorUserId = currentUser.UserId });
            await db.SaveChangesAsync(cancellationToken);
            return ReturnStatus.Received;
        }, cancellationToken);
        db.ChangeTracker.Clear();
        return result;
    }

    /// <summary>
    /// Records money paid back. Only for a paid order (it has an invoice), never more than what is still refundable,
    /// and for a return only once it was received.
    /// </summary>
    public async Task<Result<decimal>> RefundAsync(long orderNumber, RecordRefundRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var order = await db.Orders.AsNoTracking().Where(o => o.Number == orderNumber).Select(o => new { o.Id, o.Total }).FirstOrDefaultAsync(cancellationToken);
        if (order is null)
            return AppError.NotFound(OrderErrors.NotFound, "Order not found.");
        if (!await db.Invoices.AnyAsync(i => i.OrderId == order.Id, cancellationToken))
            return AppError.Conflict(OrderErrors.NoInvoice, "The order was never paid.");
        if (request.ReturnId is { } returnId
            && !await db.ReturnRequests.AnyAsync(r => r.Id == returnId && r.OrderId == order.Id && r.Status == ReturnStatus.Received, cancellationToken))
            return AppError.Conflict(OrderErrors.ReturnStatusInvalid, "Refund a return once its items were received.");

        var refunded = await db.Refunds.Where(r => r.OrderId == order.Id).SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0;
        if (request.Amount > order.Total - refunded)
            return AppError.Validation(OrderErrors.RefundTooMuch, "The refunds would exceed what was paid.");

        db.Refunds.Add(new Refund
        {
            OrderId = order.Id,
            ReturnRequestId = request.ReturnId,
            Amount = request.Amount,
            Method = request.Method.Trim(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            ActorUserId = currentUser.UserId,
        });
        var amount = request.Amount.ToString(CultureInfo.InvariantCulture);
        db.OrderEvents.Add(new OrderEvent { OrderId = order.Id, Kind = OrderEventKind.Refunded, Text = amount, ActorUserId = currentUser.UserId });
        await db.SaveChangesAsync(cancellationToken);
        await notifier.NotifyAsync(order.Id, OrderNotice.Refunded, amount, null, cancellationToken);
        return refunded + request.Amount;
    }

    private sealed record Target(Guid Id, Guid OrderId, long Number, ReturnStatus Status);

    private Task<Target?> FindAsync(long orderNumber, Guid returnId, CancellationToken cancellationToken) =>
        db.ReturnRequests.AsNoTracking()
            .Where(r => r.Id == returnId && db.Orders.Any(o => o.Id == r.OrderId && o.Number == orderNumber))
            .Select(r => new Target(r.Id, r.OrderId, r.Number, r.Status))
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<bool> ClaimAsync(Guid returnId, ReturnStatus from, ReturnStatus to, string? answer, CancellationToken cancellationToken)
    {
        var text = string.IsNullOrWhiteSpace(answer) ? null : answer.Trim();
        var claimed = text is null
            ? await db.ReturnRequests.Where(r => r.Id == returnId && r.Status == from).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, to), cancellationToken)
            : await db.ReturnRequests.Where(r => r.Id == returnId && r.Status == from)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, to).SetProperty(r => r.Answer, text), cancellationToken);
        return claimed == 1;
    }

    private async Task<Result<ReturnStatus>> AnswerAsync(
        long orderNumber, Guid returnId, ReturnStatus from, ReturnStatus to, string? answer, OrderEventKind kind, OrderNotice notice, CancellationToken cancellationToken)
    {
        var target = await FindAsync(orderNumber, returnId, cancellationToken);
        if (target is null)
            return AppError.NotFound(OrderErrors.ReturnNotFound, "Return not found.");
        if (target.Status != from || !await ClaimAsync(returnId, from, to, answer, cancellationToken))
            return AppError.Conflict(OrderErrors.ReturnStatusInvalid, $"A return that is {target.Status} cannot become {to}.");
        db.OrderEvents.Add(new OrderEvent { OrderId = target.OrderId, Kind = kind, Text = ReturnName(target.Number), ActorUserId = currentUser.UserId });
        await db.SaveChangesAsync(cancellationToken);
        await notifier.NotifyAsync(target.OrderId, notice, ReturnName(target.Number), answer, cancellationToken);
        return to;
    }
}
