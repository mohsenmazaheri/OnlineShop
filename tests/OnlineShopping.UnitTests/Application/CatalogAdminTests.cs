using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Catalog.Admin;
using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Catalog;
using OnlineShopping.Infrastructure.Persistence;
using OnlineShopping.Infrastructure.Persistence.Seeding.Demo;
using OnlineShopping.UnitTests.TestSupport;

namespace OnlineShopping.UnitTests.Application;

public sealed class LocalizedTextsTests
{
    private static readonly StoreOptions Store = new() { Locales = "fa,en,de", DefaultLocale = "fa" };

    [Fact]
    public void Validation_needs_a_default_name_and_only_shop_languages_once()
    {
        Assert.Null(LocalizedTexts.Validate([new("fa", "کتاب", null, null), new("en", "", null, null)], Store));
        Assert.Equal(CatalogErrors.DefaultTextRequired, LocalizedTexts.Validate([new("fa", " ", null, null)], Store)!.Code);
        Assert.Equal(CatalogErrors.LocaleInvalid, LocalizedTexts.Validate([new("fa", "x", null, null), new("hy", "y", null, null)], Store)!.Code);
        Assert.Equal(CatalogErrors.LocaleRepeated, LocalizedTexts.Validate([new("fa", "x", null, null), new("fa", "y", null, null)], Store)!.Code);
    }

    [Fact]
    public void Translations_are_added_changed_and_removed_by_an_empty_name()
    {
        var translations = new List<CatalogTranslation> { new() { Locale = "de", Name = "Alt" } };

        LocalizedTexts.ApplyTranslations(
            [new("fa", "کتاب", null, null), new("en", " Books ", "My Books!", "About"), new("de", "", null, null)],
            "fa", translations, withSlug: true, withDescription: true);

        var en = Assert.Single(translations);
        Assert.Equal(("en", "Books", "my-books", "About"), (en.Locale, en.Name, en.Slug, en.Description));
    }

    [Fact]
    public void Read_lists_the_default_first_and_every_shop_language()
    {
        var texts = LocalizedTexts.Read(Store, "کتاب", "books", null, [new CatalogTranslation { Locale = "de", Name = "Bücher", Slug = "bucher" }]);

        Assert.Equal(["fa", "en", "de"], texts.Select(t => t.Locale));
        Assert.Equal("", texts[1].Name);
        Assert.Equal(["en"], LocalizedTexts.Missing(Store, ["de"]));
    }
}

public sealed class CatalogTreeServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly AppDbContext _db;
    private readonly CatalogTreeService _service;

    public CatalogTreeServiceTests()
    {
        _db = _database.CreateContext();
        _service = new CatalogTreeService(_db, Options.Create(new StoreOptions { Locales = "fa,en", DefaultLocale = "fa" }));
    }

    public void Dispose()
    {
        _db.Dispose();
        _database.Dispose();
    }

    private static SaveCatalogNodeRequest Texts(string fa, string en = "", string? slug = null) =>
        new([new LocalizedText("fa", fa, slug, null), new LocalizedText("en", en, null, null)]);

    [Fact]
    public async Task Builds_a_tree_of_categories_sub_categories_and_groups_with_translations()
    {
        var category = (await _service.CreateCategoryAsync(Texts("کتاب", "Books", "books"))).Value;
        var sub = (await _service.CreateSubCategoryAsync(category.Id, Texts("رمان", "Novels"))).Value;
        await _service.CreateGroupAsync(sub.Id, Texts("رمان ایرانی"));

        var tree = await new CatalogTreeService(_database.CreateContext(), Options.Create(new StoreOptions { Locales = "fa,en", DefaultLocale = "fa" })).GetTreeAsync();

        var node = Assert.Single(tree.Categories);
        Assert.Equal(("کتاب", "books"), (node.Texts[0].Name, node.Texts[0].Slug));
        Assert.Equal("Books", node.Texts[1].Name);
        var group = Assert.Single(Assert.Single(node.SubCategories).Groups);
        Assert.Equal(("رمان ایرانی", ""), (group.Texts[0].Name, group.Texts[1].Name));
        Assert.Equal(0, group.ProductCount);
        Assert.Equal(["fa", "en"], tree.Locales);
    }

    [Fact]
    public async Task Category_slugs_are_unique_and_made_from_the_name_when_empty()
    {
        var first = await _service.CreateCategoryAsync(Texts("کالای دیجیتال"));
        var duplicate = await _service.CreateCategoryAsync(Texts("کالای دیجیتال"));

        Assert.Equal("کالای-دیجیتال", first.Value.Texts[0].Slug);
        Assert.Equal(CatalogErrors.SlugTaken, duplicate.Error!.Code);
    }

    [Fact]
    public async Task Renaming_updates_and_removes_translations()
    {
        var category = (await _service.CreateCategoryAsync(Texts("کتاب", "Books"))).Value;

        var renamed = await _service.UpdateCategoryAsync(category.Id, Texts("کتاب‌ها", ""));

        Assert.Equal("کتاب‌ها", renamed.Value.Texts[0].Name);
        Assert.Equal("", renamed.Value.Texts[1].Name);
        Assert.Empty(_db.Categories.AsNoTracking().Single().Translations);
    }

    [Fact]
    public async Task Only_empty_items_can_be_deleted()
    {
        var category = (await _service.CreateCategoryAsync(Texts("کتاب"))).Value;
        var sub = (await _service.CreateSubCategoryAsync(category.Id, Texts("رمان"))).Value;
        var group = (await _service.CreateGroupAsync(sub.Id, Texts("گروه"))).Value;
        _db.Products.Add(new Product { Name = "p", SKU = "S1", PublicNumber = 7001, ProductGroupId = group.Id });
        await _db.SaveChangesAsync();

        Assert.Equal(CatalogErrors.HasChildren, (await _service.DeleteCategoryAsync(category.Id)).Error!.Code);
        Assert.Equal(CatalogErrors.HasChildren, (await _service.DeleteSubCategoryAsync(sub.Id)).Error!.Code);
        Assert.Equal(CatalogErrors.HasProducts, (await _service.DeleteGroupAsync(group.Id)).Error!.Code);
        Assert.Equal(CatalogErrors.NotFound, (await _service.DeleteGroupAsync(Guid.NewGuid())).Error!.Code);
        Assert.Equal(CatalogErrors.NotFound, (await _service.CreateSubCategoryAsync(Guid.NewGuid(), Texts("x"))).Error!.Code);
    }
}

public sealed class ProductAdminServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly StoreOptions _store = new() { Locales = "fa,en,hy,de,ar", DefaultLocale = "fa", Currency = "EUR" };

    public void Dispose() => _database.Dispose();

    private ProductAdminService Service(AppDbContext db) => new(db, Options.Create(_store), TestLedger.For(db, _store));

    private async Task SeedDemoAsync()
    {
        await using var db = _database.CreateContext();
        await new DemoCatalogSeeder(db, Options.Create(_store), Options.Create(new DemoCatalogOptions { Enabled = true }), NullLogger<DemoCatalogSeeder>.Instance).SeedAsync();
    }

    private async Task<Guid> AnyGroupAsync()
    {
        await using var db = _database.CreateContext();
        return await db.ProductGroups.Select(g => g.Id).FirstAsync();
    }

    private static SaveProductRequest Request(Guid group, string sku = "NEW-1", params ProductSpecInput[] specs) =>
        new(sku, 100, 120, 5, true, group,
            [new LocalizedText("fa", "کالای تازه", null, "توضیح"), new LocalizedText("en", "New item", "", "About")],
            specs);

    [Fact]
    public async Task Creates_a_product_with_the_next_number_texts_and_specs()
    {
        await SeedDemoAsync();
        var group = await AnyGroupAsync();
        await using var db = _database.CreateContext();

        var created = await Service(db).CreateAsync(Request(group, "NEW-1",
            new ProductSpecInput("color", null, null, null, "red"), new ProductSpecInput("weight", 120, null, null, null)));

        var product = created.Value;
        Assert.Equal(1001 + DemoCatalogData.AllProducts.Count(), product.Number);
        Assert.Equal(("کالای تازه", "کالای-تازه"), (product.Texts[0].Name, product.Texts[0].Slug));
        Assert.Equal(("New item", "new-item"), (product.Texts[1].Name, product.Texts[1].Slug));
        Assert.Equal("", product.Texts[2].Name);
        Assert.Equal(["color", "weight"], product.Specs.Select(s => s.Code).Order());
    }

    [Fact]
    public async Task Update_replaces_spec_values_and_rejects_a_taken_sku()
    {
        await SeedDemoAsync();
        var group = await AnyGroupAsync();
        long number;
        await using (var db = _database.CreateContext())
            number = (await Service(db).CreateAsync(Request(group, "NEW-1", new ProductSpecInput("color", null, null, null, "red")))).Value.Number;

        await using (var db = _database.CreateContext())
        {
            var updated = await Service(db).UpdateAsync(number, Request(group, "NEW-1",
                new ProductSpecInput("color", null, null, null, null), new ProductSpecInput("network_5g", null, true, null, null)));
            Assert.Equal(["network_5g"], updated.Value.Specs.Select(s => s.Code));

            var taken = await Service(db).UpdateAsync(number, Request(group, "DEMO-PHN-001"));
            Assert.Equal(CatalogErrors.SkuTaken, taken.Error!.Code);
        }

        await using var check = _database.CreateContext();
        Assert.Equal(["network_5g"], (await Service(check).GetAsync(number)).Value.Specs.Select(s => s.Code));
    }

    [Theory]
    [InlineData("color", null, null, "purple-ish")]
    [InlineData("weight", null, true, null)]
    [InlineData("unknown", null, null, null)]
    public async Task Spec_values_must_fit_their_kind(string code, decimal? number, bool? boolean, string? option)
    {
        await SeedDemoAsync();
        var group = await AnyGroupAsync();
        await using var db = _database.CreateContext();

        var result = await Service(db).CreateAsync(Request(group, "X", new ProductSpecInput(code, number, boolean, code == "unknown" ? "x" : null, option)));

        Assert.Contains(result.Error!.Code, new[] { CatalogErrors.SpecValueInvalid, CatalogErrors.SpecNotFound });
    }

    [Fact]
    public async Task List_searches_by_name_sku_or_number_and_shows_missing_languages()
    {
        await SeedDemoAsync();
        var group = await AnyGroupAsync();
        await using var db = _database.CreateContext();
        var created = (await Service(db).CreateAsync(Request(group))).Value;

        var byName = await Service(db).ListAsync("new item", null, 1, "en");
        var bySku = await Service(db).ListAsync("demo-phn", null, 1, "fa");
        var byNumber = await Service(db).ListAsync(created.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), null, 1, "fa");
        var all = await Service(db).ListAsync(null, null, 2, "fa");

        var item = Assert.Single(byName.Items);
        Assert.Equal("New item", item.Name);
        Assert.Equal(["hy", "de", "ar"], item.MissingLocales);
        Assert.Equal(3, bySku.Total);
        Assert.Equal(created.Number, Assert.Single(byNumber.Items).Number);
        Assert.Equal(DemoCatalogData.AllProducts.Count() + 1, all.Total);
        Assert.Equal(all.Total - ProductAdminService.PageSize, all.Items.Count);
    }

    [Fact]
    public async Task Delete_hides_the_product_and_its_number_is_never_reused()
    {
        await SeedDemoAsync();
        var group = await AnyGroupAsync();
        await using var db = _database.CreateContext();
        var first = (await Service(db).CreateAsync(Request(group, "A"))).Value.Number;

        Assert.True((await Service(db).DeleteAsync(first)).IsSuccess);
        var second = (await Service(db).CreateAsync(Request(group, "B"))).Value.Number;

        Assert.Equal(first + 1, second);
        Assert.Equal(CatalogErrors.NotFound, (await Service(db).GetAsync(first)).Error!.Code);
    }

    [Fact]
    public async Task Spec_choices_are_named_in_the_admins_language()
    {
        await SeedDemoAsync();
        await using var db = _database.CreateContext();

        var specs = await Service(db).GetSpecsAsync("de");

        var color = specs.Single(s => s.Code == "color");
        Assert.Equal(("Farbe", "Option"), (color.Name, color.Kind));
        Assert.Contains(color.Options, o => o.Code == "black" && o.Value == "Schwarz");
    }
}
