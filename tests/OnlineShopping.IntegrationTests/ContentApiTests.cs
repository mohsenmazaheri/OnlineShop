using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnlineShopping.Application.Accounts;
using OnlineShopping.Application.Catalog;
using OnlineShopping.Application.Content;
using OnlineShopping.Domain.Content;

namespace OnlineShopping.IntegrationTests;

/// <summary>The blog and content pages on PostgreSQL.</summary>
public sealed class ContentApiTests(CatalogAdminApiTests.AdminFactory factory) : IClassFixture<CatalogAdminApiTests.AdminFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    [Fact]
    public async Task Posts_and_pages_are_written_per_language_and_shown_once_published()
    {
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/v1/account/login",
            new LoginRequest(CatalogAdminApiTests.AdminFactory.AdminEmail, CatalogAdminApiTests.AdminFactory.AdminPassword, false))).EnsureSuccessStatusCode();
        var word = Guid.NewGuid().ToString("N")[..6];

        var post = await ReadAsync<ContentView>(await admin.PostAsJsonAsync("/api/v1/admin/content", new SaveContentRequest(ContentKind.Post,
            [
                new ContentText("fa", $"راهنما {word}", null, "خلاصه", "## عنوان\n\nمتن", null, null, "گوشی، خرید"),
                new ContentText("en", $"Guide {word}", null, "Summary", "## Title\n\nText", "Guide | Shop", null, "phones, buying"),
            ],
            "Sara", [], true, null, false)));
        await ReadAsync<ContentView>(await admin.PostAsJsonAsync("/api/v1/admin/content", new SaveContentRequest(ContentKind.Page,
            [new ContentText("fa", $"درباره {word}", $"about-{word}", null, "ما فروشگاهی کوچک هستیم.", null, null, null)], null, [], true, null, true)));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/admin/content", new SaveContentRequest(ContentKind.Page,
            [new ContentText("fa", "حساب", "account", null, "متن", null, null, null)], null, [], true, null, false))).StatusCode);

        using var visitor = factory.CreateClient();
        var article = await ReadAsync<ContentArticle>(await visitor.GetAsync($"/api/v1/content/Post/guide-{word}?locale=en"));
        Assert.Equal(($"Guide {word}", "Sara", "Guide | Shop"), (article.Title, article.AuthorName, article.MetaTitle));
        Assert.Equal(["phones", "buying"], article.Tags);
        var posts = await ReadAsync<PostList>(await visitor.GetAsync("/api/v1/content/posts?locale=fa"));
        Assert.Contains(posts.Items, p => p.Title == $"راهنما {word}");
        Assert.Contains("گوشی", posts.Tags);
        Assert.Contains(await ReadAsync<List<FooterPage>>(await visitor.GetAsync("/api/v1/content/footer?locale=en")), p => p.Slug == $"about-{word}");
        Assert.Contains(await ReadAsync<List<SitemapEntry>>(await visitor.GetAsync("/api/v1/catalog/sitemap/Posts")), e => e.Links.Any(l => l.Path == $"blog/guide-{word}"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/v1/admin/content")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/v1/admin/content/{post.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/v1/content/Post/guide-{word}?locale=en")).StatusCode);
    }
}
