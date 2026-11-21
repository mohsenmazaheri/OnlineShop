using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Catalog;
using OnlineShopping.Application.Catalog.Admin;
using OnlineShopping.Application.Catalog.Browse;
using OnlineShopping.Application.Search;
using OnlineShopping.Infrastructure.Persistence;
using OnlineShopping.UnitTests.TestSupport;

namespace OnlineShopping.UnitTests.Application;

public sealed class LandingQueryTests
{
    [Theory]
    [InlineData("https://shop.example.com/fa/c/electronics/mobile-phones?brand=orvia&page=2&utm_source=x", "c=electronics&s=mobile-phones&brand=orvia")]
    [InlineData("/en/b/orvia?sort=price_asc&max=300", "b=orvia&max=300&sort=price_asc")]
    [InlineData("?q=hoodie&o.color=beige,gray&stock=1", "q=hoodie&o.color=beige,gray&stock=1")]
    [InlineData("c=electronics&sale=1", "c=electronics&sale=1")]
    [InlineData("/fa/c/%DA%AF%D9%88%D8%B4%DB%8C", "c=%DA%AF%D9%88%D8%B4%DB%8C")]
    public void A_pasted_listing_address_becomes_the_stored_query(string input, string expected) =>
        Assert.Equal(expected, LandingQuery.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("https://shop.example.com/fa/account?page=2")]
    [InlineData("hello")]
    public void Nothing_usable_gives_null(string? input) => Assert.Null(LandingQuery.Normalize(input));
}

public sealed class LandingPageServiceTests : DemoCatalogTestBase
{
    private LandingPageService Service(AppDbContext db) =>
        new(db, new ProductBrowseService(db, new ProductQueries(db), new ProductTextSearch(db, new SimpleProductSearch(db)), new SearchTermLog(db, Database.Clock), Options.Create(Store)), Options.Create(Store));

    private static SaveLandingPageRequest Request(string query, bool published = true, string fa = "گوشی اورویا", string en = "Orvia phones") => new(
        [new LocalizedText("fa", fa, null, "بهترین گوشی‌های اورویا"), new LocalizedText("en", en, null, "The best Orvia phones")],
        [new SeoText("fa", "گوشی اورویا | فروشگاه", null, null), new SeoText("en", null, "Every Orvia phone in stock.", null)],
        query,
        published);

    [Fact]
    public async Task A_landing_page_keeps_texts_per_language_and_a_checked_query()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var pages = Service(db);

        var page = (await pages.CreateAsync(Request("https://shop.test/en/c/electronics/mobile-phones?brand=orvia&page=3"))).Value;
        Assert.Equal("c=electronics&s=mobile-phones&brand=orvia", page.Query);
        Assert.Equal(("گوشی-اورویا", "orvia-phones"), (page.Texts[0].Slug, page.Texts[1].Slug));
        Assert.Equal("Every Orvia phone in stock.", page.Seo.Single(s => s.Locale == "en").MetaDescription);

        var en = (await pages.GetAsync("orvia-phones", "en")).Value;
        Assert.Equal(("Orvia phones", "orvia-phones", "The best Orvia phones", "Every Orvia phone in stock."), (en.Title, en.Slug, en.Intro, en.MetaDescription));
        Assert.Equal([("fa", "گوشی-اورویا"), ("en", "orvia-phones")], en.Alternates.Select(a => (a.Locale, a.Slug)));
        // Found by any language's slug; an untranslated language falls back to the default one.
        var hy = (await pages.GetAsync("orvia-phones", "hy")).Value;
        Assert.Equal(("گوشی اورویا", "گوشی-اورویا"), (hy.Title, hy.Slug));

        Assert.Equal(CatalogErrors.LandingQueryInvalid, (await pages.CreateAsync(Request("c=no-such-category", fa: "دیگر", en: "Other"))).Error!.Code);
        Assert.Equal(CatalogErrors.LandingQueryInvalid, (await pages.CreateAsync(Request("nothing here", fa: "دیگر", en: "Other"))).Error!.Code);
        Assert.Equal(CatalogErrors.LandingSlugTaken, (await pages.CreateAsync(Request("b=orvia", fa: "دیگر", en: "Orvia phones"))).Error!.Code);

        Assert.True((await pages.UpdateAsync(page.Id, Request("b=orvia", published: false))).IsSuccess);
        Assert.Equal(CatalogErrors.LandingNotFound, (await pages.GetAsync("orvia-phones", "en")).Error!.Code);
        Assert.Single((await pages.ListAsync()).Pages);
        Assert.True((await pages.DeleteAsync(page.Id)).IsSuccess);
        Assert.Empty((await pages.ListAsync()).Pages);
        Assert.Equal(CatalogErrors.LandingNotFound, (await pages.DeleteAsync(page.Id)).Error!.Code);
    }
}

public sealed class SitemapServiceTests : DemoCatalogTestBase
{
    [Fact]
    public async Task Sitemaps_list_published_pages_in_their_translated_languages_with_images()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var sitemap = new SitemapService(db, Options.Create(Store));

        var products = await sitemap.ListAsync(SitemapKind.Products);
        Assert.Equal(await db.Products.CountAsync(p => p.IsPublished), products.Count);
        var phone = await db.Products.AsNoTracking().SingleAsync(p => p.SKU == "DEMO-PHN-001");
        var entry = products.Single(p => p.Links[0].Path.StartsWith($"p/{phone.PublicNumber}/", StringComparison.Ordinal));
        Assert.Equal(Store.LocaleList, entry.Links.Select(l => l.Locale));
        Assert.Equal($"p/{phone.PublicNumber}/{phone.Slug}", entry.Links[0].Path);
        Assert.NotEmpty(entry.Images);

        // A product without an English translation is listed in English no more.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM product_translations WHERE locale = 'en' AND product_id = {0}", phone.Id);
        await db.Products.Where(p => p.SKU == "DEMO-PHN-002").ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPublished, false));
        products = await sitemap.ListAsync(SitemapKind.Products);
        Assert.DoesNotContain("en", products.Single(p => p.Links[0].Path.StartsWith($"p/{phone.PublicNumber}/", StringComparison.Ordinal)).Links.Select(l => l.Locale));
        Assert.Equal(await db.Products.CountAsync(p => p.IsPublished), products.Count);

        var categories = await sitemap.ListAsync(SitemapKind.Categories);
        Assert.Contains(categories, c => c.Links[0].Path == "c/electronics");
        var sub = categories.Single(c => c.Links[0].Path == "c/electronics/mobile-phones");
        Assert.Equal(Store.LocaleList.Count, sub.Links.Count);

        Assert.Contains(await sitemap.ListAsync(SitemapKind.Brands), b => b.Links[0].Path.StartsWith("b/", StringComparison.Ordinal));
        Assert.Empty(await sitemap.ListAsync(SitemapKind.Landing));
    }
}

public sealed class SpecialOfferTests : DemoCatalogTestBase
{
    [Fact]
    public async Task An_offer_ends_by_restoring_the_old_price_and_running_offers_are_listed()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var now = Database.Clock.GetUtcNow().UtcDateTime;
        var admin = new ProductAdminService(db, Options.Create(Store), TestLedger.For(db, Store));
        var number = await NumberOfAsync("DEMO-PHN-001");
        var view = (await admin.GetAsync(number)).Value;

        var request = new SaveProductRequest(view.Sku, 80, 100, view.Stock, true, view.ProductGroupId, view.Texts, view.Specs, view.BrandId, view.Gtin, view.Mpn,
            view.WeightGrams, view.Seo, now.AddHours(2));
        Assert.Equal(now.AddHours(2), (await admin.UpdateAsync(number, request)).Value.SaleEndsUtc);
        Assert.Contains(new SaveProductRequestValidator().Validate(request with { CompareAtPrice = null }).Errors, e => e.ErrorCode == CatalogErrors.SaleNeedsOldPrice);

        var offers = await new ProductQueries(db).ListAsync(ProductList.SpecialOffers, "en", 10);
        Assert.Equal(number, Assert.Single(offers).Number);
        Assert.Equal(now.AddHours(2), offers[0].SaleEndsUtc);

        var expiry = new SaleExpiryService(db, Database.Clock);
        Assert.Equal(0, await expiry.EndExpiredAsync());
        Database.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(1, await expiry.EndExpiredAsync());
        var ended = await db.Products.AsNoTracking().SingleAsync(p => p.PublicNumber == number);
        Assert.Equal((100m, (decimal?)null, (DateTime?)null), (ended.Price, ended.CompareAtPrice, ended.SaleEndsUtc));
        Assert.Empty(await new ProductQueries(db).ListAsync(ProductList.SpecialOffers, "en", 10));
    }

    [Fact]
    public async Task Variants_with_their_own_old_price_get_it_back_too()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var hoodie = await db.Products.SingleAsync(p => p.SKU == "DEMO-FSH-003");
        var variant = await db.ProductVariants.FirstAsync(v => v.ProductId == hoodie.Id);
        variant.Price = 30;
        variant.CompareAtPrice = 45;
        hoodie.CompareAtPrice ??= hoodie.Price + 10;
        hoodie.SaleEndsUtc = Database.Clock.GetUtcNow().UtcDateTime.AddMinutes(-1);
        await db.SaveChangesAsync();

        Assert.Equal(1, await new SaleExpiryService(db, Database.Clock).EndExpiredAsync());
        var restored = await db.ProductVariants.AsNoTracking().SingleAsync(v => v.Id == variant.Id);
        Assert.Equal((45m, (decimal?)null), (restored.Price!.Value, restored.CompareAtPrice));
    }
}
