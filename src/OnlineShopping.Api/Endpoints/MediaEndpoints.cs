using Microsoft.Net.Http.Headers;
using OnlineShopping.Application.Media;

namespace OnlineShopping.Api.Endpoints;

/// <summary>Public, read-only access to uploaded images (re-encoded WebP, so always safe to show).</summary>
public static class MediaEndpoints
{
    /// <summary>Largest accepted upload request (a 10 MB file plus the multipart overhead).</summary>
    public const long MaxRequestBytes = 11 * 1024 * 1024;

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/media/{key}/{file}", async (string key, string file, MediaService media, HttpContext http, CancellationToken ct) =>
            {
                var stream = await media.OpenAsync(key, file, ct);
                if (stream is null)
                    return Results.NotFound();
                // A key is never reused for other content: browsers and proxies may keep the file for a year.
                http.Response.Headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable";
                return Results.Stream(stream, "image/webp");
            })
            .WithTags("Storefront")
            .WithName("GetMedia")
            .WithSummary("An uploaded image in one of its sizes (/api/v1/media/{key}/{1200|600|300}.webp).")
            .Produces(StatusCodes.Status200OK, contentType: "image/webp")
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
