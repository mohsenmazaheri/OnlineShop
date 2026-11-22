using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Catalog;
using OnlineShop.Application.Content;
using OnlineShop.Domain.Content;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Application;

public sealed class ContentServiceTests : DemoCatalogTestBase
{
    private readonly InMemoryFileStorage _files = new();

    private ContentService Service(AppDbContext db) => new(db, new ProductQueries(db), _files.Media(), Database.Clock, Options.Create(Store));

    private static SaveContentRequest Post(string fa, string en = "", string? tags = "phones, tips", IReadOnlyList<long>? related = null, bool published = true, DateTime? at = null) => new(
        ContentKind.Post,
        [
            new ContentText("fa", fa, null, "خلاصه", "متن فارسی", null, null, "گوشی، راهنما"),
            new ContentText("en", en, null, en.Length > 0 ? "Summary" : null, en.Length > 0 ? "English text" : "", null, "Read before you buy.", tags),
        ],
        "Sara",
        related ?? [],
        published,
        at,
        false);

    [Fact]
    public async Task A_post_is_shown_in_its_languages_with_its_products_and_lists_newest_first()
    {
        await SeedDemoAsync();
        var phone = await NumberOfAsync("DEMO-PHN-001");
        await using var db = Database.CreateContext();
        var content = Service(db);

        var guide = (await content.CreateAsync(Post("راهنمای خرید گوشی", "Phone buying guide", related: [phone, 999_999]))).Value;
        Assert.Equal(("راهنمای-خرید-گوشی", "phone-buying-guide"), (guide.Texts[0].Slug, guide.Texts[1].Slug));
        Assert.Equal(Database.Clock.GetUtcNow().UtcDateTime, guide.PublishedUtc);
        Database.Clock.Advance(TimeSpan.FromHours(1));
        await content.CreateAsync(Post("خبر تازه"));

        var en = (await content.GetAsync(ContentKind.Post, "phone-buying-guide", "en")).Value;
        Assert.Equal(("Phone buying guide", "English text", "Read before you buy."), (en.Title, en.Body, en.MetaDescription));
        Assert.Equal(["phones", "tips"], en.Tags);
        Assert.Equal([phone], en.RelatedProducts.Select(p => p.Number));
        Assert.Equal([("fa", "راهنمای-خرید-گوشی"), ("en", "phone-buying-guide")], en.Alternates.Select(a => (a.Locale, a.Slug)));
        // Found by another language's slug; an untranslated language shows the default one.
        Assert.Equal("راهنمای خرید گوشی", (await content.GetAsync(ContentKind.Post, "phone-buying-guide", "hy")).Value.Title);
        Assert.Equal(ContentErrors.NotFound, (await content.GetAsync(ContentKind.Page, "phone-buying-guide", "en")).Error!.Code);

        var list = await content.ListPostsAsync("en", 1, null);
        Assert.Equal(["خبر تازه", "Phone buying guide"], list.Items.Select(i => i.Title));
        // The untranslated post brings its default-language tags (split at the Persian comma too).
        Assert.Equal(["phones", "tips", "راهنما", "گوشی"], list.Tags);
        Assert.Equal(["Phone buying guide"], (await content.ListPostsAsync("en", 1, "tips")).Items.Select(i => i.Title));
    }

    [Fact]
    public async Task Drafts_and_scheduled_posts_stay_hidden_and_pages_never_take_the_shops_own_addresses()
    {
        await using var db = Database.CreateContext();
        var content = Service(db);
        var draft = (await content.CreateAsync(Post("پیش‌نویس", published: false))).Value;
        Assert.Null(draft.PublishedUtc);
        var later = (await content.CreateAsync(Post("بعدا", at: Database.Clock.GetUtcNow().UtcDateTime.AddDays(1)))).Value;
        Assert.Empty((await content.ListPostsAsync("fa", 1, null)).Items);
        Database.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(["بعدا"], (await content.ListPostsAsync("fa", 1, null)).Items.Select(i => i.Title));
        Assert.True((await content.GetAsync(ContentKind.Post, later.Texts[0].Slug!, "fa")).IsSuccess);

        Assert.Equal(ContentErrors.SlugTaken, (await content.CreateAsync(Post("بعدا"))).Error!.Code);
        var page = new SaveContentRequest(ContentKind.Page, [new ContentText("fa", "درباره ما", "about", null, "ما فروشگاهی کوچک هستیم.", null, null, null)], null, [], true, null, true);
        var about = (await content.CreateAsync(page)).Value;
        Assert.Equal("about", about.Texts[0].Slug);
        // A post may share a page's address: they live under different paths.
        Assert.True((await content.CreateAsync(page with { Kind = ContentKind.Post })).IsSuccess);
        Assert.Equal(ContentErrors.SlugReserved, (await content.CreateAsync(page with { Texts = [page.Texts[0] with { Slug = "account" }] })).Error!.Code);
        Assert.Equal(new FooterPage("درباره ما", "about"), Assert.Single(await content.FooterAsync("en")));

        Assert.Equal(ContentErrors.TitleRequired, (await content.CreateAsync(page with { Texts = [page.Texts[0] with { Title = " " }] })).Error!.Code);
        Assert.Equal(ContentErrors.BodyRequired, (await content.CreateAsync(page with { Texts = [page.Texts[0] with { Title = "x", Body = "" }] })).Error!.Code);
        Assert.Equal(ContentErrors.LocaleInvalid, (await content.CreateAsync(page with { Texts = [page.Texts[0], page.Texts[0] with { Locale = "xx" }] })).Error!.Code);

        Assert.True((await content.DeleteAsync(about.Id)).IsSuccess);
        Assert.Empty(await content.FooterAsync("en"));
        Assert.Equal(ContentErrors.NotFound, (await content.UpdateAsync(about.Id, page)).Error!.Code);
        Assert.Equal(3, (await content.ListAsync()).Items.Count);
    }

    [Fact]
    public async Task A_cover_image_is_stored_as_webp_and_replaced_cleanly()
    {
        await using var db = Database.CreateContext();
        var content = Service(db);
        var post = (await content.CreateAsync(Post("عکس‌دار"))).Value;

        await using (var png = new MemoryStream(TestImages.Png(800, 600)))
            Assert.EndsWith("/1200.webp", (await content.SetCoverAsync(post.Id, png, png.Length)).Value, StringComparison.Ordinal);
        var files = _files.Files.Count;
        await using (var png = new MemoryStream(TestImages.Png(800, 600)))
            await content.SetCoverAsync(post.Id, png, png.Length);
        Assert.Equal(files, _files.Files.Count);
        Assert.True((await content.RemoveCoverAsync(post.Id)).IsSuccess);
        Assert.Null((await db.ContentPages.AsNoTracking().SingleAsync()).CoverImageUrl);
    }

    [Fact]
    public async Task The_sitemaps_list_published_posts_and_pages()
    {
        await using var db = Database.CreateContext();
        var content = Service(db);
        await content.CreateAsync(Post("راهنما", "Guide"));
        await content.CreateAsync(Post("پیش‌نویس", published: false));
        await content.CreateAsync(new SaveContentRequest(ContentKind.Page, [new ContentText("fa", "درباره ما", "about", null, "متن", null, null, null)], null, [], true, null, false));

        var sitemap = new SitemapService(db, Options.Create(Store), Database.Clock);
        var posts = await sitemap.ListAsync(SitemapKind.Posts);
        Assert.Equal([("fa", "blog/راهنما"), ("en", "blog/guide")], Assert.Single(posts).Links.Select(l => (l.Locale, l.Path)));
        Assert.Equal("about", Assert.Single(await sitemap.ListAsync(SitemapKind.Pages)).Links[0].Path);
    }
}
