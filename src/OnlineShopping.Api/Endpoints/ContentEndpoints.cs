using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OnlineShopping.Api.Common;
using OnlineShopping.Application.Authorization;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Content;
using OnlineShopping.Domain.Content;

namespace OnlineShopping.Api.Endpoints;

/// <summary>The blog and content pages: public reads, and the admin's editing (content.manage).</summary>
public static class ContentEndpoints
{
    public static IEndpointRouteBuilder MapContentEndpoints(this IEndpointRouteBuilder app)
    {
        var storefront = app.MapGroup("/api/v1/content").WithTags("Storefront");
        storefront.MapGet("/posts", async (string? locale, int? page, string? tag, ContentService content, IOptions<StoreOptions> store, CancellationToken ct) =>
                TypedResults.Ok(await content.ListPostsAsync(store.Value.ContentLocale(locale), page ?? 1, tag, ct)))
            .WithName("ListPosts").WithSummary("A page of published blog posts (newest first), optionally one tag's, with every tag in use.");
        storefront.MapGet("/{kind}/{slug}", async (ContentKind kind, string slug, string? locale, ContentService content, IOptions<StoreOptions> store, CancellationToken ct) =>
                (await content.GetAsync(kind, slug, store.Value.ContentLocale(locale), ct)).ToHttpResult())
            .WithName("GetContent").WithSummary("A published post or page by its slug in any language, in the requested language.")
            .Produces<ContentArticle>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        storefront.MapGet("/footer", async (string? locale, ContentService content, IOptions<StoreOptions> store, CancellationToken ct) =>
                TypedResults.Ok(await content.FooterAsync(store.Value.ContentLocale(locale), ct)))
            .WithName("GetFooterPages").WithSummary("Published pages listed in the shop's footer.");

        var admin = app.MapGroup("/api/v1/admin/content").WithTags("Admin: content").RequireAuthorization(Permissions.Policy(Permissions.ContentManage));
        admin.MapGet("/", async (ContentService content, CancellationToken ct) => TypedResults.Ok(await content.ListAsync(ct)))
            .WithName("ListContent").WithSummary("Every post and page with its texts in each shop language.");
        admin.MapPost("/", async (SaveContentRequest request, ContentService content, CancellationToken ct) => (await content.CreateAsync(request, ct)).ToHttpResult())
            .Validate<SaveContentRequest>()
            .WithName("CreateContent").WithSummary("Create a post or page.")
            .Produces<ContentView>();
        admin.MapPut("/{id:guid}", async (Guid id, SaveContentRequest request, ContentService content, CancellationToken ct) => (await content.UpdateAsync(id, request, ct)).ToHttpResult())
            .Validate<SaveContentRequest>()
            .WithName("UpdateContent").WithSummary("Change a post or page.")
            .Produces<ContentView>();
        admin.MapDelete("/{id:guid}", async (Guid id, ContentService content, CancellationToken ct) => (await content.DeleteAsync(id, ct)).ToHttpResult())
            .WithName("DeleteContent").WithSummary("Delete a post or page (its address then answers 404; add a redirect if it was popular).");
        admin.MapPost("/{id:guid}/cover", async (Guid id, IFormFile file, ContentService content, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await content.SetCoverAsync(id, stream, file.Length, ct)).ToHttpResult();
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MediaEndpoints.MaxRequestBytes))
            .WithName("SetContentCover").WithSummary("Upload a post's or page's cover image (multipart field \"file\").");
        admin.MapDelete("/{id:guid}/cover", async (Guid id, ContentService content, CancellationToken ct) => (await content.RemoveCoverAsync(id, ct)).ToHttpResult())
            .WithName("RemoveContentCover").WithSummary("Remove the cover image.");

        return app;
    }
}
