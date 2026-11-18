using OnlineShopping.Application.Catalog;
using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Catalog;
using OnlineShopping.UnitTests.TestSupport;

namespace OnlineShopping.UnitTests.Application;

public sealed class CategoryQueriesTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Menu_lists_categories_and_sub_categories_by_name()
    {
        await SeedAsync(
            new Category { Name = "Toys", Slug = "toys" },
            new Category
            {
                Name = "Books",
                Slug = "books",
                SubCategories =
                [
                    new SubCategory { Name = "Poetry", Slug = "poetry" },
                    new SubCategory { Name = "History", Slug = "history" }
                ]
            });

        await using var db = _database.CreateContext();
        var menu = await new CategoryQueries(db).GetMenuAsync();

        Assert.Equal(["Books", "Toys"], menu.Select(c => c.Name));
        Assert.Equal(["History", "Poetry"], menu[0].SubCategories.Select(s => s.Name));
        Assert.Empty(menu[1].SubCategories);
    }

    [Fact]
    public async Task Menu_hides_soft_deleted_categories_and_sub_categories()
    {
        await SeedAsync(
            new Category { Name = "Gone", Slug = "gone", IsDeleted = true },
            new Category
            {
                Name = "Books",
                Slug = "books",
                SubCategories =
                [
                    new SubCategory { Name = "Live", Slug = "live" },
                    new SubCategory { Name = "Deleted", Slug = "deleted", IsDeleted = true }
                ]
            });

        await using var db = _database.CreateContext();
        var menu = await new CategoryQueries(db).GetMenuAsync();

        var books = Assert.Single(menu);
        Assert.Equal(["Live"], books.SubCategories.Select(s => s.Name));
    }

    [Fact]
    public async Task GetBySlug_returns_the_category()
    {
        await SeedAsync(new Category { Name = "Books", Slug = "books" });

        await using var db = _database.CreateContext();
        var result = await new CategoryQueries(db).GetBySlugAsync("books");

        Assert.True(result.IsSuccess);
        Assert.Equal("Books", result.Value.Name);
    }

    [Fact]
    public async Task GetBySlug_reports_not_found_for_unknown_and_deleted_categories()
    {
        await SeedAsync(new Category { Name = "Gone", Slug = "gone", IsDeleted = true });

        await using var db = _database.CreateContext();
        var queries = new CategoryQueries(db);

        foreach (var slug in new[] { "missing", "gone" })
        {
            var result = await queries.GetBySlugAsync(slug);
            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
            Assert.Equal(CategoryQueries.NotFoundCode, result.Error.Code);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetBySlug_rejects_an_empty_slug(string slug)
    {
        await using var db = _database.CreateContext();

        var result = await new CategoryQueries(db).GetBySlugAsync(slug);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(CategoryQueries.SlugRequiredCode, result.Error.Code);
    }

    [Fact]
    public async Task Menu_uses_the_requested_language_and_falls_back_to_the_default_names()
    {
        await SeedAsync(
            new Category
            {
                Name = "کتاب",
                Slug = "books",
                ImageUrl = "/demo/categories/books.svg",
                Translations = [new CatalogTranslation { Locale = "en", Name = "Books" }],
                SubCategories =
                [
                    new SubCategory { Name = "شعر", Slug = "poetry", Translations = [new CatalogTranslation { Locale = "en", Name = "Poetry" }] },
                    new SubCategory { Name = "تاریخ", Slug = "history" },
                ],
            },
            new Category { Name = "اسباب‌بازی", Slug = "toys", Translations = [new CatalogTranslation { Locale = "en", Name = "Toys" }] });

        await using var db = _database.CreateContext();
        var queries = new CategoryQueries(db);
        var english = await queries.GetMenuAsync("en");
        var persian = await queries.GetMenuAsync("fa");

        Assert.Equal(["Books", "Toys"], english.Select(c => c.Name));
        // "تاریخ" has no English name yet: it keeps its default-language name.
        Assert.Equal(["Poetry", "تاریخ"], english[0].SubCategories.Select(s => s.Name));
        Assert.Equal("/demo/categories/books.svg", english[0].ImageUrl);
        Assert.Equal(["اسباب‌بازی", "کتاب"], persian.Select(c => c.Name));
        Assert.Equal("Books", (await queries.GetBySlugAsync("books", "en")).Value.Name);
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("de", "fa")]
    [InlineData(null, "fa")]
    [InlineData("xx", "fa")]
    public void Content_locale_is_the_requested_one_only_when_the_store_offers_it(string? requested, string expected) =>
        Assert.Equal(expected, new StoreOptions { Locales = "fa,en", DefaultLocale = "fa" }.ContentLocale(requested));

    private async Task SeedAsync(params Category[] categories)
    {
        await using var db = _database.CreateContext();
        db.Categories.AddRange(categories);
        await db.SaveChangesAsync();
    }
}
