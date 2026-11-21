using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Catalog;
using OnlineShopping.Application.Catalog.Admin;
using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Catalog;
using OnlineShopping.Infrastructure.Persistence;
using OnlineShopping.Infrastructure.Persistence.Seeding.Demo;
using OnlineShopping.UnitTests.TestSupport;

namespace OnlineShopping.UnitTests.Application;

/// <summary>Shared set-up: an SQLite database with the demo catalog (default language fa, prices in EUR).</summary>
public abstract class DemoCatalogTestBase : IDisposable
{
    protected SqliteTestDatabase Database { get; } = new();
    protected StoreOptions Store { get; } = new() { Locales = "fa,en,hy,de,ar", DefaultLocale = "fa", Currency = "EUR" };

    public void Dispose()
    {
        Database.Dispose();
        GC.SuppressFinalize(this);
    }

    protected async Task SeedDemoAsync()
    {
        await using var db = Database.CreateContext();
        await new DemoCatalogSeeder(db, Options.Create(Store), Options.Create(new DemoCatalogOptions { Enabled = true }), NullLogger<DemoCatalogSeeder>.Instance).SeedAsync();
        // As at startup: the demo stock becomes the ledger's opening lines.
        await TestLedger.For(db, Store).OpenUntrackedAsync();
    }

    /// <summary>Works through the search outbox, as the index worker does.</summary>
    protected async Task IndexSearchAsync()
    {
        await using var db = Database.CreateContext();
        var index = new OnlineShopping.Application.Search.SearchIndexService(db, Database.Clock, Options.Create(Store), Options.Create(new OnlineShopping.Application.Search.SearchOptions()));
        while (await index.ProcessOutboxAsync() > 0)
        {
        }
    }

    protected async Task<long> NumberOfAsync(string sku)
    {
        await using var db = Database.CreateContext();
        return await db.Products.Where(p => p.SKU == sku).Select(p => p.PublicNumber).SingleAsync();
    }
}

public sealed class BrandAdminServiceTests : DemoCatalogTestBase
{
    private readonly InMemoryFileStorage _files = new();

    private BrandAdminService Service(AppDbContext db) => new(db, _files.Media(), Options.Create(Store));

    private static SaveBrandRequest Texts(string fa, string en = "", string? slug = null) =>
        new([new LocalizedText("fa", fa, slug, "درباره"), new LocalizedText("en", en, null, null)]);

    [Fact]
    public async Task Creates_renames_and_lists_brands_with_their_product_count()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var service = Service(db);

        var created = (await service.CreateAsync(Texts("نوا", "Nova"))).Value;
        Assert.Equal(("نوا", "نوا", "درباره"), (created.Texts[0].Name, created.Texts[0].Slug, created.Texts[0].Description));
        Assert.Equal(("Nova", "nova"), (created.Texts[1].Name, created.Texts[1].Slug));

        var renamed = (await service.UpdateAsync(created.Id, Texts("نوا پلاس", "", "nova-plus"))).Value;
        Assert.Equal(("نوا پلاس", "nova-plus", ""), (renamed.Texts[0].Name, renamed.Texts[0].Slug, renamed.Texts[1].Name));

        var list = await service.ListAsync();
        Assert.Equal(DemoCatalogData.Brands.Count + 1, list.Brands.Count);
        Assert.Equal(DemoCatalogData.AllProducts.Count(p => p.Brand == "Orvia"), list.Brands.Single(b => b.Texts[0].Name == "Orvia").ProductCount);
    }

    [Fact]
    public async Task Slugs_are_unique_and_a_used_brand_cannot_be_deleted()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var service = Service(db);
        var orvia = await db.Brands.SingleAsync(b => b.Name == "Orvia");

        Assert.Equal(CatalogErrors.SlugTaken, (await service.CreateAsync(Texts("اورویا", "", "orvia"))).Error!.Code);
        Assert.Equal(CatalogErrors.HasProducts, (await service.DeleteAsync(orvia.Id)).Error!.Code);

        var unused = (await service.CreateAsync(Texts("تازه"))).Value;
        Assert.True((await service.DeleteAsync(unused.Id)).IsSuccess);
        Assert.Equal(CatalogErrors.NotFound, (await service.UpdateAsync(unused.Id, Texts("x"))).Error!.Code);
    }

    [Fact]
    public async Task A_new_logo_replaces_the_old_files_and_removing_it_deletes_them()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var service = Service(db);
        var brand = (await service.CreateAsync(Texts("لوگو"))).Value;
        var png = TestImages.Png(400, 200);

        var first = (await service.SetLogoAsync(brand.Id, new MemoryStream(png), png.Length)).Value;
        var second = (await service.SetLogoAsync(brand.Id, new MemoryStream(png), png.Length)).Value;

        Assert.EndsWith("/300.webp", second, StringComparison.Ordinal);
        Assert.DoesNotContain(_files.Files.Keys, p => first.Contains(p.Split("/")[0], StringComparison.Ordinal));
        Assert.True((await service.RemoveLogoAsync(brand.Id)).IsSuccess);
        Assert.Empty(_files.Files);
        Assert.Null((await service.ListAsync()).Brands.Single(b => b.Id == brand.Id).LogoUrl);
    }
}

public sealed class SpecAdminServiceTests : DemoCatalogTestBase
{
    private SpecAdminService Service(AppDbContext db) => new(db, Options.Create(Store));

    private static SaveSpecRequest Spec(string fa, string en = "", string kind = "Option", string? code = null, params SpecOptionInput[] options) =>
        new(code, kind, IsFilterable: true, IsComparable: true, ShowInSummary: false,
            [new SpecText("fa", fa, "کلی", null), new SpecText("en", en, en.Length > 0 ? "General" : null, null)], options);

    private static SpecOptionInput Option(string fa, string en = "", string? code = null) => new(code, [new OptionText("fa", fa), new OptionText("en", en)]);

    [Fact]
    public async Task Creates_a_spec_with_options_and_codes_made_from_the_names()
    {
        await using var db = Database.CreateContext();

        var spec = (await Service(db).CreateAsync(Spec("جنس بدنه", "Body", options: [Option("فلز", "Metal", "metal"), Option("چوب"), Option("Glass")]))).Value;

        Assert.Equal("spec_1", spec.Code);
        Assert.Equal(["metal", "option_1", "glass"], spec.Options.Select(o => o.Code));
        Assert.Equal(("Body", "General"), (spec.Texts[1].Name, spec.Texts[1].GroupName));
        Assert.Equal("", spec.Options[1].Texts[1].Value);
        Assert.Equal(0, spec.UsageCount);
    }

    [Fact]
    public async Task Only_number_specs_keep_a_better_direction()
    {
        await using var db = Database.CreateContext();

        var weight = (await Service(db).CreateAsync(Spec("وزن", "Weight", kind: "Number") with { Better = SpecBetter.Lower })).Value;
        var colour = (await Service(db).CreateAsync(Spec("رنگ", "Colour", kind: "Text") with { Better = SpecBetter.Higher })).Value;

        Assert.Equal(SpecBetter.Lower, weight.Better);
        Assert.Equal(SpecBetter.None, colour.Better);
    }

    [Theory]
    [InlineData("Screen size", null, "screen_size")]
    [InlineData("اندازه", "Bad Code!", null)]
    [InlineData("اندازه", "panel_type", "panel_type")]
    public void Codes_are_lower_case_latin_and_never_repeat(string name, string? given, string? expected)
    {
        Assert.Equal(expected, SpecAdminService.MakeCode(given, name, ["spec_1"], "spec"));
        Assert.Equal("screen_size_1", SpecAdminService.MakeCode(null, "Screen size", ["screen_size"], "spec"));
        Assert.Equal("spec_2", SpecAdminService.MakeCode(null, "اندازه", ["spec_1"], "spec"));
    }

    [Fact]
    public async Task Used_specs_and_options_are_protected()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var service = Service(db);
        var color = (await service.ListAsync()).Specs.Single(s => s.Code == "color");
        Assert.True(color.UsageCount > 0);
        Assert.True(color.Options.Single(o => o.Code == "beige").UsageCount > 0);

        // Dropping the used "beige" option, changing the kind, deleting the spec or taking another code is refused.
        var withoutBeige = Spec("رنگ", "Colour", code: "color", options: [.. color.Options.Where(o => o.Code != "beige").Select(o => new SpecOptionInput(o.Code, o.Texts))]);
        Assert.Equal(CatalogErrors.OptionInUse, (await service.UpdateAsync(color.Id, withoutBeige)).Error!.Code);
        Assert.Equal(CatalogErrors.SpecInUse, (await service.UpdateAsync(color.Id, Spec("رنگ", kind: "Text", code: "color"))).Error!.Code);
        Assert.Equal(CatalogErrors.SpecInUse, (await service.DeleteAsync(color.Id)).Error!.Code);
        Assert.Equal(CatalogErrors.CodeTaken, (await service.UpdateAsync(color.Id, Spec("رنگ", code: "size"))).Error!.Code);
    }

    [Fact]
    public async Task Options_are_renamed_added_and_unused_ones_removed()
    {
        await using var db = Database.CreateContext();
        var service = Service(db);
        var spec = (await service.CreateAsync(Spec("جنس", options: [Option("فلز", "", "metal"), Option("چوب", "", "wood")]))).Value;

        var updated = (await service.UpdateAsync(spec.Id, Spec("جنس", "Material", code: "material", options: [Option("چوب طبیعی", "Wood", "wood"), Option("سنگ", "", "stone")]))).Value;

        Assert.Equal("material", updated.Code);
        Assert.Equal(["wood", "stone"], updated.Options.Select(o => o.Code));
        Assert.Equal(("چوب طبیعی", "Wood"), (updated.Options[0].Texts[0].Value, updated.Options[0].Texts[1].Value));
        Assert.Equal(CatalogErrors.OptionRepeated, (await service.UpdateAsync(spec.Id, Spec("جنس", options: [Option("a", "", "x"), Option("b", "", "x")]))).Error!.Code);
        Assert.Equal(CatalogErrors.DefaultTextRequired, (await service.UpdateAsync(spec.Id, Spec("جنس", options: [Option("", "Only English")]))).Error!.Code);

        // Another kind drops the options (none used).
        Assert.Empty((await service.UpdateAsync(spec.Id, Spec("جنس", kind: "Text", code: "material", options: [Option("x")]))).Value.Options);
    }

    [Fact]
    public async Task Deleting_an_unused_spec_takes_it_out_of_every_template_and_reorder_moves_it()
    {
        await using var db = Database.CreateContext();
        var service = Service(db);
        var a = (await service.CreateAsync(Spec("الف", kind: "Number", code: "a"))).Value;
        var b = (await service.CreateAsync(Spec("ب", kind: "Number", code: "b"))).Value;
        var category = new Category { Name = "دسته", Slug = "c" };
        category.TemplateSpecs.Add(new TemplateSpec { SpecId = a.Id });
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        Assert.Equal(["b", "a"], (await service.ReorderAsync(new([b.Id]))).Specs.Select(s => s.Code));
        Assert.True((await service.DeleteAsync(a.Id)).IsSuccess);

        await using var check = Database.CreateContext();
        Assert.Empty((await check.Categories.SingleAsync()).TemplateSpecs);
        Assert.Equal(["b"], (await Service(check).ListAsync()).Specs.Select(s => s.Code));
    }
}

public sealed class SpecTemplateTests : DemoCatalogTestBase
{
    [Fact]
    public async Task Templates_are_saved_per_category_and_sub_category_in_the_specs_order()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var service = new CatalogTreeService(db, Options.Create(Store));
        var tree = await service.GetTreeAsync();
        var electronics = tree.Categories.Single(c => c.Texts.Any(t => t.Name == "Electronics"));

        // The demo gives every sub-category the specs of its products.
        Assert.Contains("screen_size", electronics.SubCategories.Single(s => s.Texts.Any(t => t.Name == "Mobile phones")).TemplateSpecs);

        var saved = (await service.SetCategoryTemplateAsync(electronics.Id, new(["warranty", "color", "warranty"]))).Value;
        Assert.Equal(["color", "warranty"], saved);
        Assert.Equal(["color", "warranty"], (await service.GetTreeAsync()).Categories.Single(c => c.Id == electronics.Id).TemplateSpecs);
        Assert.Equal(CatalogErrors.SpecNotFound, (await service.SetSubCategoryTemplateAsync(electronics.SubCategories[0].Id, new(["nope"]))).Error!.Code);
        Assert.Equal(CatalogErrors.NotFound, (await service.SetCategoryTemplateAsync(Guid.NewGuid(), new([]))).Error!.Code);
    }
}

public sealed class ProductVariantServiceTests : DemoCatalogTestBase
{
    private ProductVariantService Service(AppDbContext db) => new(db, TestLedger.For(db, Store));

    /// <summary>The demo bag has its own colour; variants by colour need it gone first.</summary>
    private async Task<long> BagWithoutColourAsync()
    {
        await using var db = Database.CreateContext();
        var product = await db.Products.Include(p => p.SpecValues).ThenInclude(v => v.Spec).SingleAsync(p => p.SKU == "DEMO-FSH-002");
        db.ProductSpecValues.Remove(product.SpecValues.Single(v => v.Spec.Code == "color"));
        await db.SaveChangesAsync();
        return product.PublicNumber;
    }

    private static VariantInput Variant(string sku, int stock, string color, string size, decimal? price = null, decimal? compareAt = null, Guid? id = null) =>
        new(id, sku, price, compareAt, stock, [new("color", color), new("size", size)]);

    [Fact]
    public async Task Saves_variants_and_the_product_stock_becomes_their_sum()
    {
        await SeedDemoAsync();
        var number = await BagWithoutColourAsync();
        await using var db = Database.CreateContext();

        var saved = (await Service(db).SaveAsync(number, new(["size", "color"],
            [Variant("BAG-BK-M", 3, "black", "m"), Variant("BAG-BK-L", 4, "black", "l", price: 55, compareAt: 60)]))).Value;

        // Axes are listed in the specs' order, whatever order they were sent in.
        Assert.Equal(["color", "size"], saved.SpecCodes);
        Assert.Equal(["BAG-BK-M", "BAG-BK-L"], saved.Variants.Select(v => v.Sku));
        Assert.Equal([new VariantOptionInput("color", "black"), new VariantOptionInput("size", "l")], saved.Variants[1].Options);
        Assert.Equal(7, (await db.Products.SingleAsync(p => p.PublicNumber == number)).Stock);
    }

    [Fact]
    public async Task Updating_keeps_ids_and_removed_variants_leave()
    {
        await SeedDemoAsync();
        var number = await NumberOfAsync("DEMO-FSH-003");
        await using var db = Database.CreateContext();
        var hoodie = (await new ProductAdminService(db, Options.Create(Store), TestLedger.For(db, Store)).GetAsync(number)).Value;
        Assert.Equal(8, hoodie.Variants.Variants.Count);
        var keep = hoodie.Variants.Variants[0];

        // The first variant changes its stock, a row without id reuses the SKU of the second, the rest are removed.
        var saved = (await Service(db).SaveAsync(number, new(["color", "size"],
            [new VariantInput(keep.Id, keep.Sku, null, null, 2, keep.Options),
             Variant(hoodie.Variants.Variants[1].Sku, 1, "gray", "xl")]))).Value;

        Assert.Equal(keep.Id, saved.Variants[0].Id);
        Assert.Equal(hoodie.Variants.Variants[1].Id, saved.Variants[1].Id);
        Assert.Equal(3, (await db.Products.SingleAsync(p => p.PublicNumber == number)).Stock);
        Assert.Equal(2, await db.ProductVariants.CountAsync(v => v.Product.PublicNumber == number));
    }

    [Fact]
    public async Task Invalid_variant_sets_are_refused()
    {
        await SeedDemoAsync();
        var bag = await BagWithoutColourAsync();
        var shoes = await NumberOfAsync("DEMO-FSH-001");
        await using var db = Database.CreateContext();
        var service = Service(db);

        async Task<string?> Error(long number, SaveVariantsRequest request) => (await service.SaveAsync(number, request)).Error?.Code;

        Assert.Equal(CatalogErrors.VariantRepeated, await Error(bag, new(["color", "size"], [Variant("A", 1, "black", "m"), Variant("B", 1, "black", "m")])));
        Assert.Equal(CatalogErrors.VariantOptionsInvalid, await Error(bag, new(["color", "size"], [new(null, "A", null, null, 1, [new("color", "black")])])));
        Assert.Equal(CatalogErrors.VariantOptionsInvalid, await Error(bag, new(["color", "size"], [Variant("A", 1, "black", "huge")])));
        Assert.Equal(CatalogErrors.VariantSpecInvalid, await Error(bag, new(["weight"], [])));
        Assert.Equal(CatalogErrors.VariantSpecInvalid, await Error(bag, new([], [Variant("A", 1, "black", "m")])));
        Assert.Equal(CatalogErrors.SkuTaken, await Error(bag, new(["color", "size"], [Variant("DEMO-FSH-001", 1, "black", "m")])));
        Assert.Equal(CatalogErrors.SkuTaken, await Error(bag, new(["color", "size"], [Variant("DEMO-FSH-003-BG-S", 1, "black", "m")])));
        Assert.Equal(CatalogErrors.SkuTaken, await Error(bag, new(["color", "size"], [Variant("A", 1, "black", "m"), Variant("A", 1, "black", "l")])));
        Assert.Equal(CatalogErrors.CompareAtInvalid, await Error(bag, new(["color", "size"], [Variant("A", 1, "black", "m", price: 50, compareAt: 50)])));
        // The shoes have their own colour value: a colour axis would contradict it.
        Assert.Equal(CatalogErrors.SpecIsVariant, await Error(shoes, new(["color", "size"], [Variant("A", 1, "black", "m")])));
        Assert.Equal(CatalogErrors.NotFound, await Error(999_999, new([], [])));
    }

    [Fact]
    public async Task The_product_editor_keeps_variant_specs_and_stock_to_the_variants()
    {
        await SeedDemoAsync();
        var number = await NumberOfAsync("DEMO-FSH-003");
        await using var db = Database.CreateContext();
        var admin = new ProductAdminService(db, Options.Create(Store), TestLedger.For(db, Store));
        var hoodie = (await admin.GetAsync(number)).Value;
        var request = new SaveProductRequest(hoodie.Sku, hoodie.Price, hoodie.CompareAtPrice, 999, true, hoodie.ProductGroupId, hoodie.Texts, hoodie.Specs, hoodie.BrandId);

        var saved = (await admin.UpdateAsync(number, request)).Value;
        Assert.Equal(60, saved.Stock);
        Assert.NotNull(saved.BrandId);

        var withColour = request with { Specs = [.. hoodie.Specs, new ProductSpecInput("color", null, null, null, "beige")] };
        Assert.Equal(CatalogErrors.SpecIsVariant, (await admin.UpdateAsync(number, withColour)).Error!.Code);
        Assert.Equal(CatalogErrors.SkuTaken, (await admin.UpdateAsync(number, request with { Sku = "DEMO-FSH-003-BG-S" })).Error!.Code);
        Assert.Equal(CatalogErrors.BrandNotFound, (await admin.UpdateAsync(number, request with { BrandId = Guid.NewGuid() })).Error!.Code);
    }
}

public sealed class ProductDetailVariantsTests : DemoCatalogTestBase
{
    [Fact]
    public async Task The_product_page_gets_the_brand_and_variants_with_their_prices()
    {
        await SeedDemoAsync();
        var number = await NumberOfAsync("DEMO-FSH-003");
        await using (var db = Database.CreateContext())
        {
            var variant = await db.ProductVariants.SingleAsync(v => v.Sku == "DEMO-FSH-003-GR-M");
            variant.Price = 45;
            await db.SaveChangesAsync();
        }
        await using var read = Database.CreateContext();

        var detail = (await new ProductQueries(read).GetAsync(number, "de", "fa")).Value;

        Assert.Equal(("Ranna", "ranna"), (detail.Brand!.Name, detail.Brand.Slug));
        Assert.Equal(["color", "size"], detail.VariantAxes.Select(a => a.Code));
        // Only the options the variants use, in the spec's option order.
        Assert.Equal("Farbe", detail.VariantAxes[0].Name);
        Assert.Equal([("gray", "Grau"), ("beige", "Beige")], detail.VariantAxes[0].Options.Select(o => (o.Code, o.Value)));
        Assert.Equal(["s", "m", "l", "xl"], detail.VariantAxes[1].Options.Select(o => o.Code));
        var beigeS = detail.Variants[0];
        Assert.Equal((39m, 52m, true), (beigeS.Price, beigeS.CompareAtPrice, beigeS.InStock));
        Assert.Equal([new VariantChoice("color", "beige"), new VariantChoice("size", "s")], beigeS.Options);
        // Its own price: no inherited "old price"; sold out: not in stock.
        Assert.Equal((45m, (decimal?)null), (detail.Variants.Single(v => v.Sku == "DEMO-FSH-003-GR-M").Price, detail.Variants.Single(v => v.Sku == "DEMO-FSH-003-GR-M").CompareAtPrice));
        Assert.False(detail.Variants.Single(v => v.Sku == "DEMO-FSH-003-GR-XL").InStock);
        Assert.DoesNotContain(detail.Specs, s => s.Code == "color");
    }

    [Fact]
    public async Task A_product_without_variants_or_brand_has_empty_lists()
    {
        await SeedDemoAsync();
        var number = await NumberOfAsync("DEMO-FSH-002");
        await using (var db = Database.CreateContext())
        {
            (await db.Products.SingleAsync(p => p.PublicNumber == number)).BrandId = null;
            await db.SaveChangesAsync();
        }
        await using var read = Database.CreateContext();

        var detail = (await new ProductQueries(read).GetAsync(number, "en", "fa")).Value;

        Assert.Null(detail.Brand);
        Assert.Empty(detail.VariantAxes);
        Assert.Empty(detail.Variants);
    }
}

public sealed class CatalogLibraryValidatorTests
{
    private static List<string> Codes<T>(FluentValidation.AbstractValidator<T> validator, T request) =>
        validator.Validate(request).Errors.Select(e => e.ErrorCode).Distinct().ToList();

    [Fact]
    public void Brand_texts_have_length_limits()
    {
        Assert.Empty(Codes(new SaveBrandRequestValidator(), new SaveBrandRequest([new LocalizedText("fa", "نوا", null, null)])));
        Assert.Equal([CatalogErrors.DefaultTextRequired], Codes(new SaveBrandRequestValidator(), new SaveBrandRequest([])));
        Assert.Equal([CatalogErrors.NameTooLong], Codes(new SaveBrandRequestValidator(), new SaveBrandRequest([new LocalizedText("fa", new string('x', 201), null, null)])));
    }

    [Fact]
    public void Spec_codes_texts_and_options_have_limits()
    {
        var ok = new SaveSpecRequest("size", "Option", true, true, false, [new SpecText("fa", "سایز", null, null)], [new SpecOptionInput("s", [new OptionText("fa", "S")])]);
        Assert.Empty(Codes(new SaveSpecRequestValidator(), ok));
        Assert.Equal([CatalogErrors.CodeInvalid], Codes(new SaveSpecRequestValidator(), ok with { Code = new string('a', 65) }));
        Assert.Equal([CatalogErrors.NameTooLong], Codes(new SaveSpecRequestValidator(), ok with { Texts = [new SpecText("fa", "سایز", null, new string('u', 33))] }));
        Assert.Equal([CatalogErrors.DefaultTextRequired], Codes(new SaveSpecRequestValidator(), ok with { Texts = [] }));
    }

    [Fact]
    public void Variant_sets_have_limits_and_valid_numbers()
    {
        static VariantInput V(string sku = "A", decimal? price = null, int stock = 1) => new(null, sku, price, null, stock, []);
        Assert.Empty(Codes(new SaveVariantsRequestValidator(), new SaveVariantsRequest(["color"], [V()])));
        Assert.Equal([CatalogErrors.VariantSpecInvalid], Codes(new SaveVariantsRequestValidator(), new SaveVariantsRequest(["a", "b", "c", "d"], [])));
        Assert.Equal([CatalogErrors.TooManyVariants], Codes(new SaveVariantsRequestValidator(), new SaveVariantsRequest(["a"], [.. Enumerable.Range(0, 101).Select(i => V($"S{i}"))])));
        Assert.Equal(
            [CatalogErrors.SkuRequired, CatalogErrors.PriceInvalid, CatalogErrors.StockInvalid],
            Codes(new SaveVariantsRequestValidator(), new SaveVariantsRequest(["a"], [V("", -1, -1)])));
    }
}
