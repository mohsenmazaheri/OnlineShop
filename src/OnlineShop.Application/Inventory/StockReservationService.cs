using System.ComponentModel.DataAnnotations;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Inventory;

namespace OnlineShop.Application.Inventory;

/// <summary>Settings of stock reservations (section "Inventory").</summary>
public sealed class InventoryOptions
{
    public const string SectionName = "Inventory";

    /// <summary>How long a checkout holds its units before they go back on sale.</summary>
    [Range(1, 1440)]
    public int ReservationMinutes { get; set; } = 15;

    /// <summary>How often the expiry job looks for reservations whose time ran out.</summary>
    [Range(10, 3600)]
    public int ExpiryCheckSeconds { get; set; } = 60;
}

/// <summary>One item and quantity to reserve.</summary>
public sealed record ReservationInput(StockItem Item, int Quantity);

public sealed record ReservationView(Guid Id, Guid ItemId, int Quantity, string Reference, StockReservationStatus Status, DateTime ExpiresUtc);

/// <summary>An active reservation as staff see it.</summary>
public sealed record ActiveReservationView(
    Guid Id,
    long ProductNumber,
    string Name,
    string? VariantLabel,
    string Sku,
    int Quantity,
    string Reference,
    DateTime CreatedUtc,
    DateTime ExpiresUtc);

public sealed record ReleaseReservationsRequest(string Reference);

public sealed class ReleaseReservationsRequestValidator : AbstractValidator<ReleaseReservationsRequest>
{
    public ReleaseReservationsRequestValidator()
    {
        RuleFor(r => r.Reference).NotEmpty().WithErrorCode(ReservationErrors.ReferenceRequired)
            .MaximumLength(100).WithErrorCode(ReservationErrors.ReferenceRequired);
    }
}

/// <summary>Stable error codes of reservations (translated under admin.inventoryErrors).</summary>
public static class ReservationErrors
{
    public const string NotAvailable = "inventory.not_available";
    public const string ReferenceRequired = "inventory.reference_required";
    public const string NothingReserved = "inventory.nothing_reserved";
}

/// <summary>
/// Holds stock for checkouts so the shop never sells more than it has. A reservation takes units from an item's
/// available stock (stock over all warehouses − units already reserved) with one atomic UPDATE per item:
/// "reserved = reserved + n where stock − reserved ≥ n", so two customers cannot both get the last piece. A checkout's
/// items are reserved all together or not at all. Reservations end in one of three ways: committed (the order is paid:
/// the units leave the stock as "Sale" lines of the ledger), released (abandoned or failed), or expired (time ran out,
/// see <see cref="ReleaseExpiredAsync"/>). Phase 7 calls it from the cart and the order.
/// </summary>
public sealed class StockReservationService(IApplicationDbContext db, StockLedger ledger, TimeProvider clock, IOptions<InventoryOptions> options, IOptions<StoreOptions> store)
{
    /// <summary>Reserves every item, or nothing: the first one that is not available stops it (inventory.not_available).</summary>
    public async Task<Result<IReadOnlyList<ReservationView>>> ReserveAsync(
        string reference,
        IReadOnlyList<ReservationInput> items,
        TimeSpan? hold = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 100)
            return AppError.Validation(ReservationErrors.ReferenceRequired, "A reservation needs a reference of at most 100 characters.");
        if (items.Count == 0 || items.Any(i => i.Quantity is <= 0 or > InventoryLimits.MaxQuantity))
            return AppError.Validation(InventoryErrors.QuantityInvalid, "Reserve at least one unit of each item.");
        // The same item twice is one reservation of the sum.
        var wanted = items.GroupBy(i => i.Item.ItemId).Select(g => new ReservationInput(g.First().Item, g.Sum(i => i.Quantity))).ToList();
        var expires = clock.GetUtcNow().UtcDateTime + (hold ?? TimeSpan.FromMinutes(options.Value.ReservationMinutes));

        var result = await db.InTransactionAsync<IReadOnlyList<ReservationView>>(async () =>
        {
            var created = new List<StockReservation>();
            foreach (var (item, quantity) in wanted)
            {
                if (!await HoldAsync(item, quantity, cancellationToken))
                    return AppError.Conflict(ReservationErrors.NotAvailable, $"Item {item.ItemId} does not have {quantity} available.");
                var reservation = new StockReservation
                {
                    ItemId = item.ItemId,
                    ProductId = item.ProductId,
                    VariantId = item.VariantId,
                    Quantity = quantity,
                    Reference = reference.Trim(),
                    Status = StockReservationStatus.Active,
                    ExpiresUtc = expires,
                };
                db.StockReservations.Add(reservation);
                created.Add(reservation);
            }
            await db.SaveChangesAsync(cancellationToken);
            return created.Select(View).ToList();
        }, cancellationToken);
        db.ChangeTracker.Clear();
        return result;
    }

    /// <summary>Gives back every active reservation of a checkout. Returns how many were released.</summary>
    public Task<int> ReleaseAsync(string reference, CancellationToken cancellationToken = default) =>
        CloseAsync(r => r.Reference == reference && r.Status == StockReservationStatus.Active, StockReservationStatus.Released, cancellationToken);

    /// <summary>Releases every active reservation whose time ran out (the expiry job). Returns how many.</summary>
    public Task<int> ReleaseExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return CloseAsync(r => r.Status == StockReservationStatus.Active && r.ExpiresUtc <= now, StockReservationStatus.Expired, cancellationToken);
    }

    /// <summary>
    /// The checkout is paid: its reserved units leave the stock as "Sale" lines (default warehouse first), with
    /// <paramref name="saleReference"/> (the order number) on the ledger. All or nothing. A reservation that already
    /// expired is not committed (nothing_reserved): the order has to reserve again.
    /// </summary>
    public async Task<Result<int>> CommitAsync(string reference, string saleReference, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var active = await db.StockReservations.AsNoTracking()
            .Where(r => r.Reference == reference && r.Status == StockReservationStatus.Active && r.ExpiresUtc > now)
            .ToListAsync(cancellationToken);
        if (active.Count == 0)
            return AppError.Conflict(ReservationErrors.NothingReserved, "Nothing is reserved for this checkout (any more).");

        var committed = await db.InTransactionAsync<int>(async () =>
        {
            foreach (var reservation in active)
            {
                // Another request (a release, the expiry job) may have closed it meanwhile: only the one that claims it acts.
                if (!await ClaimAsync(reservation.Id, StockReservationStatus.Committed, now, cancellationToken))
                    return AppError.Conflict(ReservationErrors.NothingReserved, "A reservation of this checkout ended meanwhile.");
                var item = new StockItem(reservation.ProductId, reservation.VariantId);
                await UnholdAsync(item, reservation.Quantity, cancellationToken);
                var taken = await ledger.TakeAsync(item, reservation.Quantity, StockMovementKind.Sale, saleReference, cancellationToken);
                if (!taken.IsSuccess)
                    return taken.Error!;
            }
            return active.Count;
        }, cancellationToken);
        db.ChangeTracker.Clear();
        return committed;
    }

    /// <summary>Every active reservation, the oldest first, for staff.</summary>
    public async Task<IReadOnlyList<ActiveReservationView>> ListActiveAsync(string? locale, CancellationToken cancellationToken = default)
    {
        var active = await db.StockReservations.AsNoTracking()
            .Where(r => r.Status == StockReservationStatus.Active)
            .OrderBy(r => r.CreatedUtc)
            .ToListAsync(cancellationToken);
        var names = await ItemNames.LoadAsync(db, active.Select(r => new StockItem(r.ProductId, r.VariantId)).ToList(), store.Value.ContentLocale(locale), cancellationToken);
        return active.Where(r => names.ContainsKey(r.ItemId))
            .Select(r =>
            {
                var name = names[r.ItemId];
                return new ActiveReservationView(r.Id, name.ProductNumber, name.Name, name.VariantLabel, name.Sku, r.Quantity, r.Reference, r.CreatedUtc, r.ExpiresUtc);
            })
            .ToList();
    }

    /// <summary>Adds to the reserved counts if the item has that much available; false (nothing changed) when it has not.</summary>
    private async Task<bool> HoldAsync(StockItem item, int quantity, CancellationToken cancellationToken)
    {
        if (item.VariantId is { } variantId)
        {
            var held = await db.ProductVariants
                .Where(v => v.Id == variantId && v.Stock - v.Reserved >= quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Reserved, v => v.Reserved + quantity), cancellationToken);
            if (held == 0)
                return false;
            // The product's count is the sum of its variants': it follows without a condition of its own.
            await db.Products.IgnoreQueryFilters().Where(p => p.Id == item.ProductId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved + quantity), cancellationToken);
            return true;
        }
        return await db.Products
            .Where(p => p.Id == item.ProductId && p.Stock - p.Reserved >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved + quantity), cancellationToken) > 0;
    }

    private async Task UnholdAsync(StockItem item, int quantity, CancellationToken cancellationToken)
    {
        await db.Products.IgnoreQueryFilters().Where(p => p.Id == item.ProductId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved - quantity), cancellationToken);
        if (item.VariantId is { } variantId)
        {
            await db.ProductVariants.IgnoreQueryFilters().Where(v => v.Id == variantId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Reserved, v => v.Reserved - quantity), cancellationToken);
        }
    }

    private async Task<int> CloseAsync(System.Linq.Expressions.Expression<Func<StockReservation, bool>> which, StockReservationStatus status, CancellationToken cancellationToken)
    {
        var closed = await db.InTransactionAsync<int>(async () =>
        {
            var reservations = await db.StockReservations.AsNoTracking().Where(which).ToListAsync(cancellationToken);
            var now = clock.GetUtcNow().UtcDateTime;
            var count = 0;
            foreach (var reservation in reservations)
            {
                if (!await ClaimAsync(reservation.Id, status, now, cancellationToken))
                    continue;
                await UnholdAsync(new StockItem(reservation.ProductId, reservation.VariantId), reservation.Quantity, cancellationToken);
                count++;
            }
            return count;
        }, cancellationToken);
        db.ChangeTracker.Clear();
        return closed.Value;
    }

    /// <summary>Ends an active reservation; false when it was no longer active (someone else ended it first).</summary>
    private async Task<bool> ClaimAsync(Guid id, StockReservationStatus status, DateTime now, CancellationToken cancellationToken) =>
        await db.StockReservations
            .Where(r => r.Id == id && r.Status == StockReservationStatus.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status).SetProperty(r => r.ClosedUtc, now), cancellationToken) == 1;

    private static ReservationView View(StockReservation r) => new(r.Id, r.ItemId, r.Quantity, r.Reference, r.Status, r.ExpiresUtc);
}
