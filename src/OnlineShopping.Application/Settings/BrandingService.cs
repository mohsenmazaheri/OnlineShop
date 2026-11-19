using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Settings;

namespace OnlineShopping.Application.Settings;

public static class BrandingErrors
{
    public const string ColorInvalid = "branding.color_invalid";
}

/// <summary>The shop's look as the storefront and the admin see it.</summary>
public sealed record BrandingView(string PrimaryColor, CornerStyle Corners, FontChoice Font, HomeLayout HomeLayout);

public sealed record SaveBrandingRequest(string PrimaryColor, CornerStyle Corners, FontChoice Font, HomeLayout HomeLayout);

public sealed partial class SaveBrandingRequestValidator : AbstractValidator<SaveBrandingRequest>
{
    public SaveBrandingRequestValidator()
    {
        // Strictly #rrggbb: the value is written into the storefront's CSS, so nothing else may get through.
        RuleFor(r => r.PrimaryColor).Must(c => c is not null && HexColor().IsMatch(c)).WithErrorCode(BrandingErrors.ColorInvalid);
        RuleFor(r => r.Corners).IsInEnum();
        RuleFor(r => r.Font).IsInEnum();
        RuleFor(r => r.HomeLayout).IsInEnum();
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    internal static partial Regex HexColor();
}

/// <summary>Reads and saves the single branding row (the defaults apply until the admin saves once).</summary>
public sealed class BrandingService(IApplicationDbContext db)
{
    public static BrandingView Defaults { get; } = new(StoreBranding.DefaultPrimaryColor, CornerStyle.Soft, FontChoice.Vazirmatn, HomeLayout.Classic);

    public async Task<BrandingView> GetAsync(CancellationToken cancellationToken = default) =>
        await db.StoreBranding.AsNoTracking()
            .OrderBy(b => b.CreatedUtc)
            .Select(b => new BrandingView(b.PrimaryColor, b.Corners, b.Font, b.HomeLayout))
            .FirstOrDefaultAsync(cancellationToken)
        ?? Defaults;

    public async Task<BrandingView> SaveAsync(SaveBrandingRequest request, CancellationToken cancellationToken = default)
    {
        var branding = await db.StoreBranding.OrderBy(b => b.CreatedUtc).FirstOrDefaultAsync(cancellationToken);
        if (branding is null)
        {
            branding = new StoreBranding();
            db.StoreBranding.Add(branding);
        }

        branding.PrimaryColor = request.PrimaryColor.ToLowerInvariant();
        branding.Corners = request.Corners;
        branding.Font = request.Font;
        branding.HomeLayout = request.HomeLayout;
        await db.SaveChangesAsync(cancellationToken);
        return new BrandingView(branding.PrimaryColor, branding.Corners, branding.Font, branding.HomeLayout);
    }
}
