using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Sales;

namespace OnlineShop.Application.Sales;

/// <summary>Stable error codes of the admin's discount code list (translated under admin.discountErrors).</summary>
public static class DiscountErrors
{
    public const string NotFound = "discount.not_found";
    public const string CodeInvalid = "discount.code_invalid";
    public const string CodeTaken = "discount.code_taken";
    public const string ValueInvalid = "discount.value_invalid";
    public const string DatesInvalid = "discount.dates_invalid";
    public const string LimitInvalid = "discount.limit_invalid";
}

public sealed record DiscountCodeView(
    Guid Id,
    string Code,
    DiscountKind Kind,
    decimal Value,
    decimal? MinSubtotal,
    decimal? MaxDiscount,
    DateTime? StartsUtc,
    DateTime? EndsUtc,
    int? UsageLimit,
    int UsedCount,
    bool OncePerCustomer,
    bool IsActive,
    string? Note);

public sealed record SaveDiscountCodeRequest(
    string Code,
    DiscountKind Kind,
    decimal Value,
    decimal? MinSubtotal,
    decimal? MaxDiscount,
    DateTime? StartsUtc,
    DateTime? EndsUtc,
    int? UsageLimit,
    bool OncePerCustomer,
    bool IsActive,
    string? Note);

public sealed partial class SaveDiscountCodeRequestValidator : AbstractValidator<SaveDiscountCodeRequest>
{
    public SaveDiscountCodeRequestValidator()
    {
        RuleFor(r => r.Code).Must(c => c is not null && CodePattern().IsMatch(c.Trim())).WithErrorCode(DiscountErrors.CodeInvalid);
        RuleFor(r => r.Kind).IsInEnum();
        RuleFor(r => r.Value).InclusiveBetween(0.01m, 100).WithErrorCode(DiscountErrors.ValueInvalid).When(r => r.Kind == DiscountKind.Percent);
        RuleFor(r => r.Value).InclusiveBetween(0.01m, 1_000_000_000_000m).WithErrorCode(DiscountErrors.ValueInvalid).When(r => r.Kind == DiscountKind.Amount);
        RuleFor(r => r.MinSubtotal!.Value).InclusiveBetween(0, 1_000_000_000_000m).WithErrorCode(DiscountErrors.ValueInvalid).When(r => r.MinSubtotal is not null);
        RuleFor(r => r.MaxDiscount!.Value).InclusiveBetween(0.01m, 1_000_000_000_000m).WithErrorCode(DiscountErrors.ValueInvalid).When(r => r.MaxDiscount is not null);
        RuleFor(r => r).Must(r => r.StartsUtc is null || r.EndsUtc is null || r.StartsUtc < r.EndsUtc).WithErrorCode(DiscountErrors.DatesInvalid)
            .OverridePropertyName(nameof(SaveDiscountCodeRequest.EndsUtc));
        RuleFor(r => r.UsageLimit!.Value).InclusiveBetween(1, 1_000_000).WithErrorCode(DiscountErrors.LimitInvalid).When(r => r.UsageLimit is not null);
        RuleFor(r => r.Note!).MaximumLength(300).When(r => r.Note is not null);
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9-]{3,40}$")]
    private static partial System.Text.RegularExpressions.Regex CodePattern();
}

/// <summary>Who is ordering, for "once per customer" codes (any of them matching an earlier order counts).</summary>
public sealed record DiscountCustomer(string? UserId, string? Email, string? PhoneNumber);

/// <summary>Discount codes: the admin's list, and the checks and usage count of checkout.</summary>
public sealed class DiscountCodeService(IApplicationDbContext db, TimeProvider clock)
{
    public static string Normalize(string code) => code.Trim().ToUpperInvariant();

    public async Task<IReadOnlyList<DiscountCodeView>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.DiscountCodes.AsNoTracking().OrderByDescending(d => d.CreatedUtc)
            .Select(d => new DiscountCodeView(d.Id, d.Code, d.Kind, d.Value, d.MinSubtotal, d.MaxDiscount, d.StartsUtc, d.EndsUtc,
                d.UsageLimit, d.UsedCount, d.OncePerCustomer, d.IsActive, d.Note))
            .ToListAsync(cancellationToken);

    public async Task<Result<DiscountCodeView>> CreateAsync(SaveDiscountCodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var code = new DiscountCode { Code = Normalize(request.Code) };
        if (await TakenAsync(code.Code, null, cancellationToken))
            return AppError.Conflict(DiscountErrors.CodeTaken, "The shop already has this code.");
        Apply(code, request);
        db.DiscountCodes.Add(code);
        await db.SaveChangesAsync(cancellationToken);
        return View(code);
    }

    public async Task<Result<DiscountCodeView>> UpdateAsync(Guid id, SaveDiscountCodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var code = await db.DiscountCodes.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (code is null)
            return AppError.NotFound(DiscountErrors.NotFound, "Discount code not found.");
        var text = Normalize(request.Code);
        if (await TakenAsync(text, id, cancellationToken))
            return AppError.Conflict(DiscountErrors.CodeTaken, "The shop already has this code.");
        code.Code = text;
        Apply(code, request);
        await db.SaveChangesAsync(cancellationToken);
        return View(code);
    }

    /// <summary>Soft delete: orders keep their copy of the code.</summary>
    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var code = await db.DiscountCodes.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (code is null)
            return AppError.NotFound(DiscountErrors.NotFound, "Discount code not found.");
        db.DiscountCodes.Remove(code);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// The code if it may be used now for goods worth <paramref name="subtotal"/> by this customer. Unknown, inactive,
    /// not yet started and ended codes all answer discount_invalid (no hint which).
    /// </summary>
    public async Task<Result<DiscountCode>> CheckAsync(string code, decimal subtotal, DiscountCustomer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        var text = Normalize(code ?? "");
        var now = clock.GetUtcNow().UtcDateTime;
        var found = await db.DiscountCodes.AsNoTracking().FirstOrDefaultAsync(d => d.Code == text, cancellationToken);
        if (found is null || !found.IsActive || (found.StartsUtc is { } starts && starts > now) || (found.EndsUtc is { } ends && ends <= now))
            return AppError.Validation(CheckoutErrors.DiscountInvalid, "The discount code is not valid.");
        if (found.UsageLimit is { } limit && found.UsedCount >= limit)
            return AppError.Conflict(CheckoutErrors.DiscountUsedUp, "The discount code has been used up.");
        if (found.MinSubtotal is { } minimum && subtotal < minimum)
            return AppError.Validation(CheckoutErrors.DiscountMinSubtotal, $"The code needs goods worth at least {minimum}.");
        if (found.OncePerCustomer && await UsedByAsync(found.Id, customer, cancellationToken))
            return AppError.Conflict(CheckoutErrors.DiscountAlreadyUsed, "This customer has already used the code.");
        return found;
    }

    /// <summary>Counts one use, unless the limit was reached meanwhile (false): one atomic UPDATE.</summary>
    public async Task<bool> ClaimAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.DiscountCodes
            .Where(d => d.Id == id && (d.UsageLimit == null || d.UsedCount < d.UsageLimit))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.UsedCount, d => d.UsedCount + 1), cancellationToken) == 1;

    /// <summary>Gives a use back (its order was canceled before payment); never below zero.</summary>
    public async Task UnclaimAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.DiscountCodes
            .Where(d => d.Id == id && d.UsedCount > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.UsedCount, d => d.UsedCount - 1), cancellationToken);

    private async Task<bool> UsedByAsync(Guid id, DiscountCustomer customer, CancellationToken cancellationToken)
    {
        var orders = db.Orders.Where(o => o.DiscountCodeId == id && o.Status != OrderStatus.Canceled);
        return (customer.UserId is { } userId && await orders.AnyAsync(o => o.CustomerId == userId, cancellationToken))
            || (customer.Email is { Length: > 0 } email && await orders.AnyAsync(o => o.Email == email, cancellationToken))
            || (customer.PhoneNumber is { Length: > 0 } phone && await orders.AnyAsync(o => o.PhoneNumber == phone, cancellationToken));
    }

    private Task<bool> TakenAsync(string code, Guid? except, CancellationToken cancellationToken) =>
        db.DiscountCodes.AnyAsync(d => d.Code == code && d.Id != except, cancellationToken);

    private static void Apply(DiscountCode code, SaveDiscountCodeRequest request)
    {
        code.Kind = request.Kind;
        code.Value = request.Kind == DiscountKind.FreeShipping ? 0 : request.Value;
        code.MinSubtotal = request.MinSubtotal;
        code.MaxDiscount = request.Kind == DiscountKind.Percent ? request.MaxDiscount : null;
        code.StartsUtc = request.StartsUtc?.ToUniversalTime();
        code.EndsUtc = request.EndsUtc?.ToUniversalTime();
        code.UsageLimit = request.UsageLimit;
        code.OncePerCustomer = request.OncePerCustomer;
        code.IsActive = request.IsActive;
        code.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
    }

    private static DiscountCodeView View(DiscountCode d) => new(
        d.Id, d.Code, d.Kind, d.Value, d.MinSubtotal, d.MaxDiscount, d.StartsUtc, d.EndsUtc, d.UsageLimit, d.UsedCount, d.OncePerCustomer, d.IsActive, d.Note);
}
