using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Catalog;
using OnlineShopping.Application.Catalog.Browse;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Search;
using OnlineShopping.Domain.Search;
using OnlineShopping.Infrastructure.Persistence;
using OnlineShopping.Infrastructure.Search;

namespace OnlineShopping.UnitTests.Application;

public sealed class SearchTermsTests
{
    [Theory]
    [InlineData("ball b-12345678", "ball b-12345678 b 12345678")]
    [InlineData("x-1000 usb-c/hdmi", "x-1000 usb-c/hdmi x 1000 usb c hdmi")]
    [InlineData("plain words only", "plain words only")]
    [InlineData("a-b a-b", "a-b a-b a b")]
    [InlineData("-- x", "-- x")]
    public void Joined_words_also_get_their_parts_in_the_documents(string text, string expected) =>
        Assert.Equal(expected, SearchIndexService.WithParts(text));

    [Fact]
    public void Words_get_their_synonyms_and_a_phrase_synonym_counts_as_one_word()
    {
        string[][] groups = [["گوشی", "موبایل", "تلفن همراه"], ["laptop", "notebook"]];

        var terms = SearchTerms.From("تلفن همراه سامسونگ", ["تلفن", "همراه", "سامسونگ"], groups);
        Assert.Equal(2, terms.Words.Count);
        Assert.Equal(["تلفن همراه", "گوشی", "موبایل"], terms.Words[0]);
        Assert.Equal(["سامسونگ"], terms.Words[1]);
        Assert.Equal(["notebook", "laptop"], SearchTerms.From("notebook", ["notebook"], groups).Words[0]);
        Assert.True(SearchTerms.From("", [], groups).IsEmpty);
    }

    [Fact]
    public void Text_typed_on_the_wrong_keyboard_is_read_on_the_other_layouts()
    {
        Assert.Equal("مبایل", KeyboardLayouts.Alternatives("lfhdg")[0]);
        Assert.Contains("lfhdg", KeyboardLayouts.Alternatives("مبایل"));
        Assert.Equal("բարեվ", KeyboardLayouts.Alternatives("barev")[1]);
        Assert.Contains("barev", KeyboardLayouts.Alternatives("բարեվ"));
        // "," is the Persian «و» key.
        Assert.Equal("هودی", KeyboardLayouts.Alternatives("i,nd")[0]);
        Assert.Empty(KeyboardLayouts.Alternatives("123"));
    }

    [Fact]
    public void Edit_distance_counts_changes()
    {
        Assert.Equal(0, TextDistance.Levenshtein("hoodie", "hoodie"));
        Assert.Equal(1, TextDistance.Levenshtein("hodie", "hoodie"));
        Assert.Equal(2, TextDistance.Levenshtein("casanna", "kasana"));
        Assert.Equal(3, TextDistance.Levenshtein("", "abc"));
    }

    [Fact]
    public void The_postgres_query_has_prefix_terms_synonyms_and_phrases_and_nothing_else()
    {
        var terms = SearchTerms.From("x", ["x"], []) with
        {
            Words = [["sams"], ["گوشی", "تلفن همراه"], ["a&b|c:*!"], ["!!"]],
        };

        Assert.Equal("sams:* & (گوشی:* | تلفن <-> همراه:*) & a <-> b <-> c:*", PostgresProductSearch.TsQuery(terms));
    }
}

public sealed class SearchIndexTests : DemoCatalogTestBase
{
    private SearchIndexService Index(AppDbContext db) => new(db, Database.Clock, Options.Create(Store), Options.Create(new SearchOptions()));

    [Fact]
    public async Task Catalog_changes_queue_their_products_and_the_index_builds_one_document_per_language()
    {
        await SeedDemoAsync();
        await using (var db = Database.CreateContext())
        {
            Assert.True(await db.SearchOutbox.AnyAsync(e => e.Target == SearchTarget.Product));
            Assert.Equal(new SearchIndexStatus(0, await db.SearchOutbox.CountAsync()), await Index(db).StatusAsync());
        }
        await IndexSearchAsync();

        await using var check = Database.CreateContext();
        var products = await check.Products.CountAsync(p => p.IsPublished);
        Assert.Equal(products * 5, await check.SearchDocuments.CountAsync());
        Assert.Equal(0, await check.SearchOutbox.CountAsync());
        var hoodie = await check.Products.SingleAsync(p => p.SKU == "DEMO-FSH-003");
        var en = await check.SearchDocuments.SingleAsync(d => d.ProductId == hoodie.Id && d.Locale == "en");
        Assert.Contains("hoodie", en.Title, StringComparison.Ordinal);
        // The default-language name and the brand, the variant colours and the codes are searchable too.
        Assert.Contains("هودی", en.Keywords, StringComparison.Ordinal);
        Assert.Contains("ranna", en.Keywords, StringComparison.Ordinal);
        Assert.Contains("beige", en.Details, StringComparison.Ordinal);
        Assert.Contains("demo-fsh-003-bg-s", en.Codes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edits_renames_and_unpublishing_reach_the_documents()
    {
        await SeedDemoAsync();
        await IndexSearchAsync();
        await using (var db = Database.CreateContext())
        {
            var shoes = await db.Products.SingleAsync(p => p.SKU == "DEMO-FSH-001");
            shoes.Name = "کفش کوهنوردی";
            var brand = await db.Brands.SingleAsync(b => b.Name == "Casana");
            brand.Name = "Casanova";
            var bag = await db.Products.SingleAsync(p => p.SKU == "DEMO-FSH-002");
            bag.IsPublished = false;
            await db.SaveChangesAsync();
        }
        await IndexSearchAsync();

        await using var check = Database.CreateContext();
        var shoesId = await check.Products.Where(p => p.SKU == "DEMO-FSH-001").Select(p => p.Id).SingleAsync();
        Assert.Equal("کفش کوهنوردی", (await check.SearchDocuments.SingleAsync(d => d.ProductId == shoesId && d.Locale == "fa")).Title);
        var casana = await check.Products.Where(p => p.Brand!.Name == "Casanova").Select(p => p.Id).ToListAsync();
        Assert.All(await check.SearchDocuments.Where(d => casana.Contains(d.ProductId) && d.Locale == "fa").ToListAsync(),
            d => Assert.Contains("casanova", d.Keywords, StringComparison.Ordinal));
        var bagId = await check.Products.Where(p => p.SKU == "DEMO-FSH-002").Select(p => p.Id).SingleAsync();
        Assert.False(await check.SearchDocuments.AnyAsync(d => d.ProductId == bagId));
    }

    [Fact]
    public async Task A_shop_without_documents_gets_a_full_build_once()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        await db.SearchOutbox.ExecuteDeleteAsync();

        Assert.True(await Index(db).EnsureBuiltAsync());
        Assert.False(await Index(db).EnsureBuiltAsync());
        Assert.Equal(SearchTarget.All, (await db.SearchOutbox.SingleAsync()).Target);
        await IndexSearchAsync();
        Assert.True(await db.SearchDocuments.AnyAsync());
        Assert.False(await Index(db).EnsureBuiltAsync());
    }

    [Fact]
    public async Task The_worker_processes_the_queue_and_survives_failures()
    {
        await SeedDemoAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => Database.CreateContext());
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<TimeProvider>(Database.Clock);
        services.AddSingleton(Options.Create(Store));
        services.AddSingleton(Options.Create(new SearchOptions()));
        services.AddScoped<SearchIndexService>();
        await using var provider = services.BuildServiceProvider();

        var worker = new SearchIndexWorker(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new SearchOptions()), Database.Clock, NullLogger<SearchIndexWorker>.Instance);
        await worker.RunAsync(ensureBuilt: true, CancellationToken.None);

        await using var db = Database.CreateContext();
        Assert.Equal(0, await db.SearchOutbox.CountAsync());
        Assert.True(await db.SearchDocuments.AnyAsync());

        await using var empty = new ServiceCollection().BuildServiceProvider();
        await new SearchIndexWorker(empty.GetRequiredService<IServiceScopeFactory>(), Options.Create(new SearchOptions()), Database.Clock, NullLogger<SearchIndexWorker>.Instance)
            .RunAsync(ensureBuilt: true, CancellationToken.None);
    }
}

public sealed class ProductSearchTests : DemoCatalogTestBase
{
    private static ProductTextSearch Search(AppDbContext db) => new(db, new SimpleProductSearch(db));

    private static async Task<string> NameAsync(AppDbContext db, Guid id) => await db.Products.Where(p => p.Id == id).Select(p => p.SKU).SingleAsync();

    [Fact]
    public async Task Every_word_must_match_and_the_title_counts_most()
    {
        await SeedDemoAsync();
        await IndexSearchAsync();
        await using var db = Database.CreateContext();
        var engine = new SimpleProductSearch(db);

        var hoodie = await engine.MatchAsync(SearchTerms.From("hood", ["hood"], []), "en", 10);
        Assert.Equal("DEMO-FSH-003", await NameAsync(db, Assert.Single(hoodie).Key));
        // "ranna" is a brand (keywords, weight 2); with "hoodie" (title, 4) it narrows to the hoodie.
        var ranna = await engine.MatchAsync(SearchTerms.From("ranna", ["ranna"], []), "en", 10);
        Assert.Equal(3, ranna.Count);
        Assert.Single(await engine.MatchAsync(SearchTerms.From("ranna hoodie", ["ranna", "hoodie"], []), "en", 10));
        Assert.Empty(await engine.MatchAsync(SearchTerms.From("oodie", ["oodie"], []), "en", 10));
        Assert.Equal("hoodie", await engine.SimilarWordAsync("hodie", "en"));
        Assert.Null(await engine.SimilarWordAsync("zz", "en"));
    }

    [Fact]
    public async Task A_code_opens_the_product_and_synonyms_find_their_partners()
    {
        await SeedDemoAsync();
        await IndexSearchAsync();
        await using var db = Database.CreateContext();
        var number = await NumberOfAsync("DEMO-FSH-003");

        Assert.Equal(number, (await Search(db).SearchAsync("DEMO-FSH-003-BG-S", "en")).ExactNumber);
        Assert.Equal(number, (await Search(db).SearchAsync(number.ToString(System.Globalization.CultureInfo.InvariantCulture), "en")).ExactNumber);
        Assert.Null(await ProductCodeLookup.FindAsync(db, "no such code"));

        db.SearchSynonyms.Add(new OnlineShopping.Domain.Seo.SearchSynonym { Locale = "en", Terms = "sweatshirt, hoodie" });
        await db.SaveChangesAsync();
        var outcome = await Search(db).SearchAsync("sweatshirt", "en");
        Assert.Equal("DEMO-FSH-003", await NameAsync(db, Assert.Single(outcome.Scores).Key));
    }

    [Fact]
    public async Task Nothing_found_tries_the_other_keyboard_then_offers_the_closest_words()
    {
        await SeedDemoAsync();
        await IndexSearchAsync();
        await using var db = Database.CreateContext();
        var persianHoodie = (await db.SearchDocuments.Where(d => d.Locale == "fa").Select(d => d.Title).ToListAsync()).First(t => t.Contains("هودی", StringComparison.Ordinal));
        // "i,nd" is "هودی" typed on an English keyboard.
        var layout = await Search(db).SearchAsync("i,nd", "fa");
        Assert.Equal("هودی", layout.CorrectedText);
        Assert.NotEmpty(layout.Scores);
        Assert.Contains("هودی", persianHoodie, StringComparison.Ordinal);

        var typo = await Search(db).SearchAsync("hodie", "en");
        Assert.Empty(typo.Scores);
        Assert.Equal("hoodie", typo.DidYouMean);
        Assert.Null((await Search(db).SearchAsync("qqqqqqq", "en")).DidYouMean);
    }

    [Fact]
    public async Task Searches_are_counted_and_suggestions_offer_products_places_brands_and_phrases()
    {
        await SeedDemoAsync();
        await IndexSearchAsync();
        await using var db = Database.CreateContext();
        var log = new SearchTermLog(db, Database.Clock);
        await log.RecordAsync("en", "Hoodie", true);
        await log.RecordAsync("en", "hoodie ", true);
        await log.RecordAsync("en", "hoodie zip", false);
        await log.RecordAsync("en", new string('x', 101), false);

        var stats = await db.SearchTermStats.AsNoTracking().OrderBy(t => t.Term).ToListAsync();
        Assert.Equal([("hoodie", 2, 0), ("hoodie zip", 1, 1)], stats.Select(s => (s.Term, s.Searches, s.ZeroResults)));

        var suggestions = await new SearchSuggestionService(db, new SimpleProductSearch(db), new ProductQueries(db), Options.Create(Store)).SuggestAsync("ho", "en");
        Assert.Contains(suggestions.Products, p => p.Name.Contains("Hoodie", StringComparison.Ordinal));
        Assert.Contains(suggestions.Categories, c => c.Name.StartsWith("Ho", StringComparison.Ordinal) && c.Path.StartsWith("c/", StringComparison.Ordinal));
        // Only phrases that found something, and not the one typed.
        Assert.Equal(["hoodie"], suggestions.Terms);
        var brands = await new SearchSuggestionService(db, new SimpleProductSearch(db), new ProductQueries(db), Options.Create(Store)).SuggestAsync("orv", "en");
        Assert.Equal("Orvia", Assert.Single(brands.Brands).Name);
        Assert.Empty((await new SearchSuggestionService(db, new SimpleProductSearch(db), new ProductQueries(db), Options.Create(Store)).SuggestAsync("h", "en")).Products);
    }

    [Fact]
    public async Task A_search_listing_carries_its_help_and_how_to_relax_the_filters()
    {
        await SeedDemoAsync();
        await IndexSearchAsync();
        await using var db = Database.CreateContext();
        var browse = new ProductBrowseService(db, new ProductQueries(db), Search(db), new SearchTermLog(db, Database.Clock), Options.Create(Store));

        var exact = (await browse.BrowseAsync(new BrowseQuery { Text = "DEMO-FSH-001" }, "en")).Value;
        Assert.Equal(await NumberOfAsync("DEMO-FSH-001"), exact.Search!.ExactNumber);
        Assert.Equal(1, await db.SearchTermStats.CountAsync());

        // Ranna's three products, none of them both on sale and out of stock.
        var none = (await browse.BrowseAsync(new BrowseQuery { Text = "ranna", Discounted = true, MaxPrice = 1 }, "en")).Value;
        Assert.Equal(0, none.Total);
        Assert.Contains(none.Relaxations, r => r.Filter == "price" && r.Count > 0);
        Assert.Equal(1, await db.SearchTermStats.CountAsync());

        var typo = (await browse.BrowseAsync(new BrowseQuery { Text = "hodie" }, "en")).Value;
        Assert.Equal("hoodie", typo.Search!.DidYouMean);
        Assert.Equal(1, (await db.SearchTermStats.SingleAsync(t => t.Term == "hodie")).ZeroResults);
        Assert.Null((await browse.BrowseAsync(new BrowseQuery(), "en")).Value.Search);
    }
}
