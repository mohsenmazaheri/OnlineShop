using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OnlineShop.Application.Catalog;
using OnlineShop.Application.Catalog.Admin;
using OnlineShop.Application.Common;
using OnlineShop.Application.Seo;
using OnlineShop.Domain.Seo;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Application;

public sealed class ProductCodesTests
{
    [Theory]
    [InlineData("4006381333931", "4006381333931")] // EAN-13
    [InlineData("۴۰۰۶۳۸۱۳۳۳۹۳۱", "4006381333931")] // Persian digits
    [InlineData("4006-3813 33931", "4006381333931")]
    [InlineData("96385074", "96385074")] // EAN-8
    [InlineData("036000291452", "036000291452")] // UPC-A
    [InlineData("10012345678902", "10012345678902")] // GTIN-14
    [InlineData("4006381333932", null)] // wrong check digit
    [InlineData("12345", null)]
    [InlineData("40063813339a1", null)]
    [InlineData("  ", null)]
    public void Gtins_are_digits_with_a_valid_check_digit(string input, string? expected) =>
        Assert.Equal(expected, ProductCodes.NormalizeGtin(input));

    [Fact]
    public void Tags_are_trimmed_and_listed_once()
    {
        // The first spelling of a repeated word stays; Persian commas split too.
        Assert.Equal("گوشی, mobile phone", ProductCodes.NormalizeTags(" گوشی ،mobile  phone, , Mobile phone ", 500));
        Assert.Equal("a, b", ProductCodes.NormalizeTags("a,b,A", 500));
        Assert.Null(ProductCodes.NormalizeTags(" , ", 500));
        Assert.Equal("abc", ProductCodes.NormalizeTags("abc, def", 4));
    }
}

public sealed class SearchTextTests
{
    [Theory]
    [InlineData("كيف  عربي", "کیف عربی")]
    [InlineData("تلفن‌همراه", "تلفن همراه")]
    [InlineData("Galaxy S۲۴", "galaxy s24")]
    [InlineData("مُحَمَّد", "محمد")]
    [InlineData("  ", "")]
    public void Normalizes_spelling_digits_and_spaces(string input, string expected) => Assert.Equal(expected, SearchText.Normalize(input));
}

public sealed class RedirectPathsTests
{
    [Theory]
    [InlineData("/old-page/", "/old-page")]
    [InlineData("https://old.example.com/shop/item.php?id=12#top", "/shop/item.php?id=12")]
    [InlineData("/%D9%85%D8%AD%D8%B5%D9%88%D9%84", "/محصول")]
    [InlineData("/a?", "/a")]
    [InlineData("old-page", null)]
    [InlineData("//evil.example.com", null)]
    [InlineData("/", null)]
    [InlineData("/api/v1/x", null)]
    [InlineData("/_next/static/x.js", null)]
    [InlineData("/has space", null)]
    public void Old_addresses_are_site_paths(string input, string? expected) => Assert.Equal(expected, RedirectPaths.NormalizeFrom(input));

    [Theory]
    [InlineData("/new", "/new")]
    [InlineData("https://new.example.com/x", "https://new.example.com/x")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("ftp://x.example.com/", null)]
    [InlineData("//evil.example.com", null)]
    public void New_addresses_are_paths_or_http_urls(string input, string? expected) => Assert.Equal(expected, RedirectPaths.NormalizeTo(input));
}

public sealed class RedirectServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero));

    public void Dispose() => _database.Dispose();

    private RedirectService Service(out OnlineShop.Infrastructure.Persistence.AppDbContext db)
    {
        db = _database.CreateContext();
        return new RedirectService(db, _time);
    }

    [Fact]
    public async Task Creates_updates_deletes_and_lists_rules()
    {
        var service = Service(out var db);
        await using var _ = db;

        var created = (await service.CreateAsync(new("/old-shoes/", "/fa/c/shoes", RedirectKind.Permanent))).Value;
        Assert.Equal(("/old-shoes", "/fa/c/shoes"), (created.FromPath, created.ToPath));
        var gone = (await service.CreateAsync(new("/discontinued", "/ignored", RedirectKind.Gone))).Value;
        Assert.Null(gone.ToPath);

        var updated = (await service.UpdateAsync(created.Id, new("/old-shoes", "https://new.example.com/shoes", RedirectKind.Temporary))).Value;
        Assert.Equal(RedirectKind.Temporary, updated.Kind);
        Assert.Equal(["/discontinued", "/old-shoes"], (await service.RulesAsync()).Select(r => r.FromPath));
        Assert.True((await service.DeleteAsync(gone.Id)).IsSuccess);
        Assert.Single(await service.ListAsync());
    }

    [Fact]
    public async Task Refuses_bad_paths_duplicates_loops_and_chains()
    {
        var service = Service(out var db);
        await using var _ = db;
        await service.CreateAsync(new("/a", "/b", RedirectKind.Permanent));

        async Task<string?> Error(string from, string? to) => (await service.CreateAsync(new(from, to, RedirectKind.Permanent))).Error?.Code;

        Assert.Equal(SeoErrors.PathInvalid, await Error("a", "/x"));
        Assert.Equal(SeoErrors.TargetInvalid, await Error("/c", "javascript:alert(1)"));
        Assert.Equal(SeoErrors.TargetInvalid, await Error("/c", null));
        Assert.Equal(SeoErrors.RedirectLoop, await Error("/c", "/c/"));
        Assert.Equal(SeoErrors.PathTaken, await Error("/a/", "/z"));
        // "/c → /a" would chain to /b; "/b → /z" would make "/a → /b" a chain.
        Assert.Equal(SeoErrors.RedirectChain, await Error("/c", "/a"));
        Assert.Equal(SeoErrors.RedirectChain, await Error("/b", "/z"));
    }

    [Fact]
    public async Task Imports_csv_lines_and_reports_the_bad_ones()
    {
        var service = Service(out var db);
        await using var _ = db;
        await service.CreateAsync(new("/kept", "/old-target", RedirectKind.Permanent));

        var result = (await service.ImportAsync(new("from,to,status\n/one,/new-one\n\n/kept,/new-target,302\nbad,/x\n/gone,,410\n/two;/new-two;999\n"))).Value;

        Assert.Equal((2, 1), (result.Added, result.Updated));
        Assert.Equal([new ImportLineError(5, SeoErrors.PathInvalid), new ImportLineError(7, SeoErrors.TargetInvalid)], result.Errors);
        var rules = await service.RulesAsync();
        Assert.Contains(rules, r => r is { FromPath: "/kept", ToPath: "/new-target", Kind: RedirectKind.Temporary });
        Assert.Contains(rules, r => r is { FromPath: "/gone", Kind: RedirectKind.Gone });
        Assert.Equal(SeoErrors.ImportTooLarge, (await service.ImportAsync(new(string.Join('\n', Enumerable.Repeat("/x,/y", RedirectService.MaxImportLines + 1))))).Error!.Code);
    }

    [Fact]
    public async Task Hits_are_counted_with_the_time()
    {
        var service = Service(out var db);
        await using var _ = db;
        await service.CreateAsync(new("/old", "/new", RedirectKind.Permanent));

        await service.RecordHitAsync("/old/");
        await service.RecordHitAsync("/old");
        await service.RecordHitAsync("/unknown");

        var row = Assert.Single(await service.ListAsync());
        Assert.Equal((2L, _time.GetUtcNow().UtcDateTime), (row.HitCount, row.LastHitUtc!.Value));
    }
}

public sealed class SynonymServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private SynonymService Service(out OnlineShop.Infrastructure.Persistence.AppDbContext db)
    {
        db = _database.CreateContext();
        return new SynonymService(db, Options.Create(new StoreOptions { Locales = "fa,en", DefaultLocale = "fa" }));
    }

    [Fact]
    public async Task Stores_normalized_words_and_keeps_each_word_in_one_group()
    {
        var service = Service(out var db);
        await using var _ = db;

        var group = (await service.CreateAsync(new("fa", ["گوشي", "موبایل", " تلفن‌همراه ", "موبایل"]))).Value;
        Assert.Equal(["گوشی", "موبایل", "تلفن همراه"], group.Terms);

        Assert.Equal(SeoErrors.TermInOtherGroup, (await service.CreateAsync(new("fa", ["موبايل", "سلولی"]))).Error!.Code);
        // The same word in another language is another group.
        Assert.True((await service.CreateAsync(new("en", ["phone", "mobile"]))).IsSuccess);
        // A group may keep its own words when it changes.
        Assert.True((await service.UpdateAsync(group.Id, new("fa", ["گوشی", "موبایل"]))).IsSuccess);
        Assert.Equal(2, (await service.ListAsync()).Groups.Count);
    }

    [Fact]
    public async Task Refuses_too_few_long_or_foreign_language_groups()
    {
        var service = Service(out var db);
        await using var _ = db;

        Assert.Equal(SeoErrors.TooFewTerms, (await service.CreateAsync(new("fa", ["گوشی", "گوشي"]))).Error!.Code);
        Assert.Equal(SeoErrors.TermTooLong, (await service.CreateAsync(new("fa", ["a", new string('b', 51)]))).Error!.Code);
        Assert.Equal(SeoErrors.LocaleInvalid, (await service.CreateAsync(new("de", ["a", "b"]))).Error!.Code);
        Assert.Equal(SeoErrors.NotFound, (await service.DeleteAsync(Guid.NewGuid())).Error!.Code);
    }

    [Fact]
    public void Validators_limit_the_requests()
    {
        Assert.Contains(new SaveSynonymRequestValidator().Validate(new SaveSynonymRequest("fa", [.. Enumerable.Range(0, 21).Select(i => $"w{i}")])).Errors, e => e.ErrorCode == SeoErrors.TooManyTerms);
        Assert.Contains(new SaveRedirectRequestValidator().Validate(new SaveRedirectRequest("", null, RedirectKind.Permanent)).Errors, e => e.ErrorCode == SeoErrors.PathInvalid);
        Assert.Contains(new SaveRedirectRequestValidator().Validate(new SaveRedirectRequest("/a", null, (RedirectKind)9)).Errors, e => e.ErrorCode == SeoErrors.TargetInvalid);
    }
}

public sealed class ProductSeoFieldsTests : DemoCatalogTestBase
{
    [Fact]
    public async Task Barcode_weight_and_search_texts_are_saved_per_language_and_shown_by_the_storefront()
    {
        await SeedDemoAsync();
        var number = await NumberOfAsync("DEMO-FSH-002");
        await using var db = Database.CreateContext();
        var admin = new ProductAdminService(db, Options.Create(Store), TestLedger.For(db, Store));
        var bag = (await admin.GetAsync(number)).Value;
        var request = new SaveProductRequest(bag.Sku, bag.Price, bag.CompareAtPrice, bag.Stock, true, bag.ProductGroupId, bag.Texts, bag.Specs, bag.BrandId,
            Gtin: "۴۰۰۶۳۸۱۳۳۳۹۳۱", Mpn: " RB-25 ", WeightGrams: 720,
            Seo: [new("fa", "کوله‌پشتی ۲۵ لیتری", "توضیح کوتاه", "کوله, کیف لپ‌تاپ"), new("en", "25 L laptop backpack", null, "backpack,bag,Bag")]);

        var saved = (await admin.UpdateAsync(number, request)).Value;

        Assert.Equal(("4006381333931", "RB-25", 720), (saved.Gtin, saved.Mpn, saved.WeightGrams));
        Assert.Equal(new SeoText("en", "25 L laptop backpack", null, "backpack, bag"), saved.Seo.Single(s => s.Locale == "en"));
        Assert.Equal(CatalogErrors.GtinInvalid, (await admin.UpdateAsync(number, request with { Gtin = "123" })).Error!.Code);

        await using var read = Database.CreateContext();
        var queries = new ProductQueries(read);
        var en = (await queries.GetAsync(number, "en", "fa")).Value;
        Assert.Equal(("4006381333931", "25 L laptop backpack", (string?)null), (en.Gtin, en.MetaTitle, en.MetaDescription));
        Assert.Equal("کوله‌پشتی ۲۵ لیتری", (await queries.GetAsync(number, "fa", "fa")).Value.MetaTitle);
    }

    [Fact]
    public async Task Variants_may_have_their_own_valid_barcode()
    {
        await SeedDemoAsync();
        var number = await NumberOfAsync("DEMO-FSH-003");
        await using var db = Database.CreateContext();
        var service = new ProductVariantService(db, TestLedger.For(db, Store));
        var hoodie = (await new ProductAdminService(db, Options.Create(Store), TestLedger.For(db, Store)).GetAsync(number)).Value;
        VariantInput Input(VariantView v, string? gtin) => new(v.Id, v.Sku, v.Price, v.CompareAtPrice, v.Stock, v.Options, gtin);

        var saved = (await service.SaveAsync(number, new(hoodie.Variants.SpecCodes, [Input(hoodie.Variants.Variants[0], "96385074")]))).Value;
        Assert.Equal("96385074", saved.Variants[0].Gtin);
        Assert.Equal(CatalogErrors.GtinInvalid, (await service.SaveAsync(number, new(hoodie.Variants.SpecCodes, [Input(hoodie.Variants.Variants[0], "96385075")]))).Error!.Code);

        await using var read = Database.CreateContext();
        Assert.Equal("96385074", (await new ProductQueries(read).GetAsync(number, "en", "fa")).Value.Variants[0].Gtin);
    }
}
