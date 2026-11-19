using OnlineShopping.Api.Common;
using OnlineShopping.Application.Authorization;
using OnlineShopping.Application.Settings;

namespace OnlineShopping.Api.Endpoints;

/// <summary>The shop's look (colour, corners, font, home layout); the storefront reads it from GET /store.</summary>
public static class BrandingEndpoints
{
    public static IEndpointRouteBuilder MapBrandingEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/branding").WithTags("Admin: branding")
            .RequireAuthorization(Permissions.Policy(Permissions.SettingsManage));

        admin.MapGet("/", async (BrandingService branding, CancellationToken ct) => TypedResults.Ok(await branding.GetAsync(ct)))
            .WithName("GetBranding")
            .WithSummary("The shop's main colour, corner style, font and home page layout.");

        admin.MapPut("/", async (SaveBrandingRequest request, BrandingService branding, CancellationToken ct) =>
                TypedResults.Ok(await branding.SaveAsync(request, ct)))
            .Validate<SaveBrandingRequest>()
            .WithName("SaveBranding")
            .WithSummary("Change the shop's look; applied by the storefront at runtime, no rebuild.");

        return app;
    }
}
