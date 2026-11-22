using OnlineShop.Api.Common;
using OnlineShop.Application.Authorization;
using OnlineShop.Application.Search;
using OnlineShop.Application.Seo;

namespace OnlineShop.Api.Endpoints;

/// <summary>Search synonyms and redirects: managed by staff with catalog.manage; the rules are read by the storefront.</summary>
public static class SeoEndpoints
{
    public static IEndpointRouteBuilder MapSeoEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/seo").WithTags("Admin: SEO")
            .RequireAuthorization(Permissions.Policy(Permissions.CatalogManage));

        admin.MapGet("/synonyms", async (SynonymService synonyms, CancellationToken ct) => TypedResults.Ok(await synonyms.ListAsync(ct)))
            .WithName("ListSynonyms").WithSummary("Every search synonym group, with the shop's languages.");
        admin.MapPost("/synonyms", async (SaveSynonymRequest request, SynonymService synonyms, CancellationToken ct) => (await synonyms.CreateAsync(request, ct)).ToHttpResult())
            .Validate<SaveSynonymRequest>().WithName("CreateSynonym").WithSummary("Add a group of words that mean the same.").Produces<SynonymView>();
        admin.MapPut("/synonyms/{id:guid}", async (Guid id, SaveSynonymRequest request, SynonymService synonyms, CancellationToken ct) =>
                (await synonyms.UpdateAsync(id, request, ct)).ToHttpResult())
            .Validate<SaveSynonymRequest>().WithName("UpdateSynonym").WithSummary("Change a synonym group.").Produces<SynonymView>();
        admin.MapDelete("/synonyms/{id:guid}", async (Guid id, SynonymService synonyms, CancellationToken ct) => (await synonyms.DeleteAsync(id, ct)).ToHttpResult())
            .WithName("DeleteSynonym").WithSummary("Delete a synonym group.");

        admin.MapGet("/redirects", async (RedirectService redirects, CancellationToken ct) => TypedResults.Ok(await redirects.ListAsync(ct)))
            .WithName("ListRedirects").WithSummary("Every redirect with how often it was used.");
        admin.MapPost("/redirects", async (SaveRedirectRequest request, RedirectService redirects, CancellationToken ct) => (await redirects.CreateAsync(request, ct)).ToHttpResult())
            .Validate<SaveRedirectRequest>().WithName("CreateRedirect").WithSummary("Add a redirect of an old address.").Produces<RedirectView>();
        admin.MapPut("/redirects/{id:guid}", async (Guid id, SaveRedirectRequest request, RedirectService redirects, CancellationToken ct) =>
                (await redirects.UpdateAsync(id, request, ct)).ToHttpResult())
            .Validate<SaveRedirectRequest>().WithName("UpdateRedirect").WithSummary("Change a redirect.").Produces<RedirectView>();
        admin.MapDelete("/redirects/{id:guid}", async (Guid id, RedirectService redirects, CancellationToken ct) => (await redirects.DeleteAsync(id, ct)).ToHttpResult())
            .WithName("DeleteRedirect").WithSummary("Delete a redirect.");
        admin.MapPost("/redirects/import", async (ImportRedirectsRequest request, RedirectService redirects, CancellationToken ct) =>
                (await redirects.ImportAsync(request, ct)).ToHttpResult())
            .WithName("ImportRedirects").WithSummary("Add or update redirects from CSV lines (old,new[,301|302|410]).").Produces<ImportRedirectsResult>();

        var storefront = app.MapGroup("/api/v1/redirects").WithTags("Storefront");
        storefront.MapGet("/", async (RedirectService redirects, CancellationToken ct) => TypedResults.Ok(await redirects.RulesAsync(ct)))
            .WithName("GetRedirectRules").WithSummary("Every redirect rule; the storefront keeps them in memory for a minute.");
        storefront.MapPost("/hits", async (RedirectHitRequest request, RedirectService redirects, CancellationToken ct) =>
            {
                await redirects.RecordHitAsync(request.FromPath, ct);
                return TypedResults.NoContent();
            })
            .WithName("RecordRedirectHit").WithSummary("Count a use of a redirect (sent by the storefront).");

        admin.MapGet("/search-index", async (SearchIndexService index, SearchEngineSyncService engine, CancellationToken ct) =>
                TypedResults.Ok(await index.StatusAsync(ct) with { Engine = await engine.StatusAsync(ct) }))
            .WithName("GetSearchIndexStatus").WithSummary("How many search documents there are, how many catalog changes wait to reach them, and the search engine's state.");
        admin.MapPost("/search-index/rebuild", async (SearchIndexService index, SearchEngineSyncService engine, CancellationToken ct) =>
            {
                await index.EnqueueAllAsync(ct);
                return TypedResults.Ok(await index.StatusAsync(ct) with { Engine = await engine.StatusAsync(ct) });
            })
            .WithName("RebuildSearchIndex").WithSummary("Queue a rebuild of every product's search documents and, with a search engine, of its indexes (done in the background).");

        return app;
    }
}
