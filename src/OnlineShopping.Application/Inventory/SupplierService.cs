using Microsoft.EntityFrameworkCore;
using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Inventory;

namespace OnlineShopping.Application.Inventory;

/// <summary>The shop's suppliers. One with purchase receipts is never deleted (the receipts point at it), only deactivated.</summary>
public sealed class SupplierService(IApplicationDbContext db)
{
    public async Task<IReadOnlyList<SupplierView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var receipts = await db.PurchaseReceipts.AsNoTracking()
            .GroupBy(r => r.SupplierId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        var suppliers = await db.Suppliers.AsNoTracking().OrderByDescending(s => s.IsActive).ThenBy(s => s.Name).ToListAsync(cancellationToken);
        return suppliers.Select(s => View(s, receipts.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<Result<SupplierView>> CreateAsync(SaveSupplierRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var supplier = new Supplier { Name = "" };
        Apply(supplier, request);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(cancellationToken);
        return View(supplier, 0);
    }

    public async Task<Result<SupplierView>> UpdateAsync(Guid id, SaveSupplierRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (supplier is null)
            return NotFound();
        Apply(supplier, request);
        await db.SaveChangesAsync(cancellationToken);
        return View(supplier, await db.PurchaseReceipts.CountAsync(r => r.SupplierId == id, cancellationToken));
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (supplier is null)
            return NotFound();
        if (await db.PurchaseReceipts.IgnoreQueryFilters().AnyAsync(r => r.SupplierId == id, cancellationToken))
            return AppError.Conflict(PurchasingErrors.SupplierUsed, "A supplier with receipts is deactivated, not deleted.");
        db.Suppliers.Remove(supplier);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void Apply(Supplier supplier, SaveSupplierRequest request)
    {
        supplier.Name = request.Name.Trim();
        supplier.ContactName = Clean(request.ContactName);
        supplier.Phone = Clean(request.Phone);
        supplier.Email = Clean(request.Email);
        supplier.Address = Clean(request.Address);
        supplier.Note = Clean(request.Note);
        supplier.IsActive = request.IsActive;
    }

    private static SupplierView View(Supplier s, int receipts) =>
        new(s.Id, s.Name, s.ContactName, s.Phone, s.Email, s.Address, s.Note, s.IsActive, receipts);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static AppError NotFound() => AppError.NotFound(PurchasingErrors.SupplierNotFound, "No such supplier.");
}
