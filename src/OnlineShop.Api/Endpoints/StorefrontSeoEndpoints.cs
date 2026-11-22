using Microsoft.Extensions.Options;
using OnlineShop.Api.Common;
using OnlineShop.Application.Authorization;
using OnlineShop.Application.Catalog;
using OnlineShop.Application.Common;

namespace OnlineShop.Api.Endpoints;

/// <summary>Sitemap data and landing pages (the storefront's SEO pages, phase 6f).</summary>
public static class StorefrontSeoEndpoints
{
    public static IEndpointRouteBuilder MapStorefrontSeoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/catalog/sitemap/{kind}", async (SitemapKind kind, SitemapService sitemap, CancellationToken ct) =>
                TypedResults.Ok(await sitemap.ListAsync(kind, ct)))
            .WithTags("Storefront")
            .WithName("GetSitemapEntries")
            .WithSummary("The pages of one sitemap (Products, Categories, Brands, Landing) with their address in every translated language.");

        app.MapGet("/api/v1/catalog/landing/{slug}", async (string slug, string? locale, LandingPageService pages, IOptions<StoreOptions> store, CancellationToken ct) =>
                (await pages.GetAsync(slug, store.Value.ContentLocale(locale), ct)).ToHttpResult())
            .WithTags("Storefront")
            .WithName("GetLandingPage")
            .WithSummary("A published landing page by its slug (any language), in the requested language, with its fixed browse query.")
            .Produces<LandingPageContent>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var admin = app.MapGroup("/api/v1/admin/landing-pages").WithTags("Admin: content")
            .RequireAuthorization(Permissions.Policy(Permissions.ContentManage));
        admin.MapGet("/", async (LandingPageService pages, CancellationToken ct) => TypedResults.Ok(await pages.ListAsync(ct)))
            .WithName("ListLandingPages").WithSummary("Every landing page with its texts in each shop language.");
        admin.MapPost("/", async (SaveLandingPageRequest request, LandingPageService pages, CancellationToken ct) => (await pages.CreateAsync(request, ct)).ToHttpResult())
            .Validate<SaveLandingPageRequest>()
            .WithName("CreateLandingPage").WithSummary("Create a landing page (the query may be a listing's address).")
            .Produces<LandingPageView>();
        admin.MapPut("/{id:guid}", async (Guid id, SaveLandingPageRequest request, LandingPageService pages, CancellationToken ct) =>
                (await pages.UpdateAsync(id, request, ct)).ToHttpResult())
            .Validate<SaveLandingPageRequest>()
            .WithName("UpdateLandingPage").WithSummary("Change a landing page.")
            .Produces<LandingPageView>();
        admin.MapDelete("/{id:guid}", async (Guid id, LandingPageService pages, CancellationToken ct) => (await pages.DeleteAsync(id, ct)).ToHttpResult())
            .WithName("DeleteLandingPage").WithSummary("Delete a landing page (its address then answers 404; add a redirect if it was popular).");

        return app;
    }
}
