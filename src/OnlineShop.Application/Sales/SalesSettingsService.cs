using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Settings;

namespace OnlineShop.Application.Sales;

/// <summary>The seller's details on invoices and the return period, as the admin edits them.</summary>
public sealed record SalesSettingsView(string? SellerName, string? SellerAddress, string? SellerTaxNumber, string? InvoiceNote, int ReturnDays);

public sealed class SalesSettingsViewValidator : AbstractValidator<SalesSettingsView>
{
    public const int MaxReturnDays = 365;

    public SalesSettingsViewValidator()
    {
        RuleFor(r => r.SellerName!).MaximumLength(200).WithErrorCode(ShippingErrors.SellerTooLong).When(r => r.SellerName is not null);
        RuleFor(r => r.SellerAddress!).MaximumLength(500).WithErrorCode(ShippingErrors.SellerTooLong).When(r => r.SellerAddress is not null);
        RuleFor(r => r.SellerTaxNumber!).MaximumLength(100).WithErrorCode(ShippingErrors.SellerTooLong).When(r => r.SellerTaxNumber is not null);
        RuleFor(r => r.InvoiceNote!).MaximumLength(500).WithErrorCode(ShippingErrors.SellerTooLong).When(r => r.InvoiceNote is not null);
        RuleFor(r => r.ReturnDays).InclusiveBetween(0, MaxReturnDays).WithErrorCode(ShippingErrors.ReturnDaysInvalid);
    }
}

/// <summary>Reads and saves the single sales settings row (the defaults apply until the admin saves once).</summary>
public sealed class SalesSettingsService(IApplicationDbContext db)
{
    public static SalesSettingsView Defaults { get; } = new(null, null, null, null, SalesSettings.DefaultReturnDays);

    public async Task<SalesSettingsView> GetAsync(CancellationToken cancellationToken = default) =>
        await db.SalesSettings.AsNoTracking().OrderBy(s => s.CreatedUtc)
            .Select(s => new SalesSettingsView(s.SellerName, s.SellerAddress, s.SellerTaxNumber, s.InvoiceNote, s.ReturnDays))
            .FirstOrDefaultAsync(cancellationToken)
        ?? Defaults;

    public async Task<SalesSettingsView> SaveAsync(SalesSettingsView request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var row = await db.SalesSettings.OrderBy(s => s.CreatedUtc).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = new SalesSettings();
            db.SalesSettings.Add(row);
        }
        static string? clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        row.SellerName = clean(request.SellerName);
        row.SellerAddress = clean(request.SellerAddress);
        row.SellerTaxNumber = clean(request.SellerTaxNumber);
        row.InvoiceNote = clean(request.InvoiceNote);
        row.ReturnDays = request.ReturnDays;
        await db.SaveChangesAsync(cancellationToken);
        return new SalesSettingsView(row.SellerName, row.SellerAddress, row.SellerTaxNumber, row.InvoiceNote, row.ReturnDays);
    }
}
