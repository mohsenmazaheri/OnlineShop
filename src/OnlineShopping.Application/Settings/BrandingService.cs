using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Media;
using OnlineShopping.Domain.Settings;

namespace OnlineShopping.Application.Settings;

public static class BrandingErrors
{
    public const string ColorInvalid = "branding.color_invalid";
}

/// <summary>The shop's look as the storefront and the admin see it.</summary>
public sealed record BrandingView(string PrimaryColor, CornerStyle Corners, FontChoice Font, HomeLayout HomeLayout, string? LogoUrl, string? HeroImageUrl);

/// <summary>Which branding image an upload is for.</summary>
public enum BrandingImage
{
    Logo,
    Hero,
}

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
public sealed class BrandingService(IApplicationDbContext db, MediaService media)
{
    public static BrandingView Defaults { get; } = new(StoreBranding.DefaultPrimaryColor, CornerStyle.Soft, FontChoice.Vazirmatn, HomeLayout.Classic, null, null);

    public async Task<BrandingView> GetAsync(CancellationToken cancellationToken = default) =>
        await db.StoreBranding.AsNoTracking()
            .OrderBy(b => b.CreatedUtc)
            .Select(b => new BrandingView(b.PrimaryColor, b.Corners, b.Font, b.HomeLayout, b.LogoUrl, b.HeroImageUrl))
            .FirstOrDefaultAsync(cancellationToken)
        ?? Defaults;

    public async Task<BrandingView> SaveAsync(SaveBrandingRequest request, CancellationToken cancellationToken = default)
    {
        var branding = await RowAsync(cancellationToken);
        branding.PrimaryColor = request.PrimaryColor.ToLowerInvariant();
        branding.Corners = request.Corners;
        branding.Font = request.Font;
        branding.HomeLayout = request.HomeLayout;
        await db.SaveChangesAsync(cancellationToken);
        return View(branding);
    }

    /// <summary>Uploads the logo or the banner image (checked and re-encoded); the previous one is deleted.</summary>
    public async Task<Result<BrandingView>> SetImageAsync(BrandingImage kind, Stream upload, long length, CancellationToken cancellationToken = default)
    {
        var stored = await media.SaveImageAsync(upload, length, cancellationToken);
        if (!stored.IsSuccess)
            return stored.Error!;

        var branding = await RowAsync(cancellationToken);
        // The logo is small in the header: the 300px size is plenty; the banner uses the largest.
        var url = MediaUrls.For(stored.Value.Key, kind == BrandingImage.Logo ? 300 : MediaUrls.Sizes[0]);
        var old = Swap(branding, kind, url);
        await db.SaveChangesAsync(cancellationToken);
        await media.DeleteAsync(old, cancellationToken);
        return View(branding);
    }

    public async Task<BrandingView> RemoveImageAsync(BrandingImage kind, CancellationToken cancellationToken = default)
    {
        var branding = await RowAsync(cancellationToken);
        var old = Swap(branding, kind, null);
        await db.SaveChangesAsync(cancellationToken);
        await media.DeleteAsync(old, cancellationToken);
        return View(branding);
    }

    private static string? Swap(StoreBranding branding, BrandingImage kind, string? url)
    {
        var old = kind == BrandingImage.Logo ? branding.LogoUrl : branding.HeroImageUrl;
        if (kind == BrandingImage.Logo)
            branding.LogoUrl = url;
        else
            branding.HeroImageUrl = url;
        return old;
    }

    private async Task<StoreBranding> RowAsync(CancellationToken cancellationToken)
    {
        var branding = await db.StoreBranding.OrderBy(b => b.CreatedUtc).FirstOrDefaultAsync(cancellationToken);
        if (branding is null)
        {
            branding = new StoreBranding();
            db.StoreBranding.Add(branding);
        }
        return branding;
    }

    private static BrandingView View(StoreBranding b) => new(b.PrimaryColor, b.Corners, b.Font, b.HomeLayout, b.LogoUrl, b.HeroImageUrl);
}
