using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Catalog.Admin;
using OnlineShopping.Application.Catalog.Transfer;
using OnlineShopping.Infrastructure.Persistence;
using OnlineShopping.Infrastructure.Transfer;
using OnlineShopping.UnitTests.TestSupport;

namespace OnlineShopping.UnitTests.Application;

public sealed class TableFileTests
{
    private static readonly TableData Sample = new(
        ["sku", "name:fa", "description:en"],
        [["0012", "کوله‌پشتی، مشکی", "Says \"hi\"\non two lines"], ["B-2", "", " padded "]]);

    [Fact]
    public void Csv_round_trips_quotes_commas_line_breaks_and_starts_with_a_bom()
    {
        var csv = new CsvTableFile();
        var bytes = csv.Write(Sample);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var read = csv.Read(new MemoryStream(bytes), 100);
        Assert.Equal(Sample.Headers, read.Headers);
        Assert.Equal(Sample.Rows[0], read.Rows[0]);
        Assert.Equal(["B-2", "", "padded"], read.Rows[1]);
    }

    [Fact]
    public void Csv_reads_semicolons_and_stops_after_the_limit()
    {
        var csv = new CsvTableFile();
        var read = csv.Read(new MemoryStream(Encoding.UTF8.GetBytes("sku;price\r\nA;1\r\n\r\nB;2\r\nC;3\r\n")), 1);

        Assert.Equal(["sku", "price"], read.Headers);
        // Limit 1: two data rows are read, which tells the caller "too many".
        Assert.Equal(2, read.Rows.Count);
    }

    [Fact]
    public void Excel_round_trips_text_and_keeps_leading_zeros()
    {
        var xlsx = new XlsxTableFile();
        var bytes = xlsx.Write(Sample);

        Assert.Equal("PK"u8.ToArray(), bytes[..2]);
        var read = xlsx.Read(new MemoryStream(bytes), 100);
        Assert.Equal(Sample.Headers, read.Headers);
        Assert.Equal("0012", read.Rows[0][0]);
        Assert.Equal("کوله‌پشتی، مشکی", read.Rows[0][1]);
        Assert.Equal(2, read.Rows.Count);
    }

    [Fact]
    public void The_format_is_found_by_name_or_by_content()
    {
        var files = new TableFiles([new CsvTableFile(), new XlsxTableFile()]);

        Assert.Equal("xlsx", files.ByFormat("XLSX")!.Format);
        Assert.Null(files.ByFormat("pdf"));
        Assert.Equal("xlsx", files.Detect("PK\u0003\u0004"u8).Format);
        Assert.Equal("csv", files.Detect("sku,"u8).Format);
    }
}

public sealed class CellsTests
{
    [Theory]
    [InlineData("1,250,000", 1250000)]
    [InlineData("۱٬۲۵۰٬۰۰۰", 1250000)]
    [InlineData("12٫5", 12.5)]
    [InlineData("12,5", 12.5)]
    [InlineData("-3", -3)]
    [InlineData(" 7 ", 7)]
    public void Numbers_are_read_in_any_script(string cell, double expected)
    {
        var (ok, value) = Cells.Number(cell);
        Assert.True(ok);
        Assert.Equal((decimal)expected, value);
    }

    [Fact]
    public void Empty_and_invalid_numbers_and_yes_no_in_every_language()
    {
        Assert.Equal((true, (decimal?)null), Cells.Number(" "));
        Assert.False(Cells.Number("12 apples").Ok);
        Assert.Equal((true, (bool?)true), Cells.Boolean("بله"));
        Assert.Equal((true, (bool?)false), Cells.Boolean("NEIN"));
        Assert.Equal((true, (bool?)null), Cells.Boolean(""));
        Assert.False(Cells.Boolean("maybe").Ok);
        Assert.Equal("12.5", Cells.Format(12.50m));
    }
}

public sealed class CatalogTransferTests : DemoCatalogTestBase
{
    private static readonly TableFiles Files = new([new CsvTableFile(), new XlsxTableFile()]);

    private ProductExportService Export(AppDbContext db) => new(db, Files, Options.Create(Store));

    private ProductImportService Import(AppDbContext db)
    {
        var storeOptions = Options.Create(Store);
        return new ProductImportService(
            db,
            new ProductAdminService(db, storeOptions, TestLedger.For(db, Store)),
            new CatalogTreeService(db, storeOptions),
            new BrandAdminService(db, new InMemoryFileStorage().Media(), storeOptions),
            Files,
            new SaveProductRequestValidator(),
            storeOptions,
            TestLedger.For(db, Store));
    }

    private static MemoryStream Csv(string text) => new(Encoding.UTF8.GetBytes(text));

    private async Task<ImportReport> ImportCsvAsync(string text, bool dryRun = false)
    {
        await using var db = Database.CreateContext();
        var stream = Csv(text);
        return (await Import(db).ProductsAsync(stream, stream.Length, dryRun)).Value;
    }

    [Fact]
    public async Task Exports_every_product_with_texts_in_every_language_and_used_specs()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();

        var file = (await Export(db).ProductsAsync("csv")).Value;

        Assert.Equal(("products.csv", "text/csv; charset=utf-8"), (file.FileName, file.ContentType));
        var table = new CsvTableFile().Read(new MemoryStream(file.Content), 1000);
        Assert.Equal(ProductColumns.Fixed, table.Headers.Take(ProductColumns.Fixed.Length));
        Assert.Contains("name:fa", table.Headers);
        Assert.Contains("tags:ar", table.Headers);
        Assert.Contains("spec:color", table.Headers);
        var bag = table.Rows.Single(r => r[1] == "DEMO-FSH-002");
        Assert.Equal("Ranna", bag[table.Headers.ToList().IndexOf("brand")]);
        Assert.Contains(" > ", bag[table.Headers.ToList().IndexOf("group")], StringComparison.Ordinal);
        Assert.Equal("مشکی", bag[table.Headers.ToList().IndexOf("spec:color")]);
        Assert.Equal(TransferErrors.FormatUnknown, CatalogErrorsOf(await Export(db).ProductsAsync("pdf")));
    }

    [Fact]
    public async Task An_exported_sheet_imports_back_without_changes_or_problems()
    {
        await SeedDemoAsync();
        byte[] content;
        await using (var db = Database.CreateContext())
            content = (await Export(db).ProductsAsync("xlsx")).Value.Content;

        await using var import = Database.CreateContext();
        var report = (await Import(import).ProductsAsync(new MemoryStream(content), content.Length, dryRun: false)).Value;

        Assert.Empty(report.Problems);
        Assert.True(report.Applied);
        Assert.Equal((0, report.Rows), (report.Created, report.Updated));
        await using var check = Database.CreateContext();
        Assert.Equal(report.Rows, await check.Products.CountAsync());
    }

    [Fact]
    public async Task A_sheet_with_only_some_columns_changes_only_those()
    {
        await SeedDemoAsync();
        decimal oldPrice;
        await using (var db = Database.CreateContext())
            oldPrice = (await db.Products.SingleAsync(p => p.SKU == "DEMO-FSH-001")).Price;

        var report = await ImportCsvAsync("sku,price,spec:color\nDEMO-FSH-002,۱۲۰٬۰۰۰,blue\n");

        Assert.True(report.Applied);
        await using var check = Database.CreateContext();
        var bag = await check.Products.Include(p => p.SpecValues).ThenInclude(v => v.Option).Include(p => p.Translations).SingleAsync(p => p.SKU == "DEMO-FSH-002");
        Assert.Equal(120_000m, bag.Price);
        Assert.Equal("blue", bag.SpecValues.Single(v => v.Option != null && v.Option.Code == "blue").Option!.Code);
        Assert.Equal(4, bag.Translations.Count);
        Assert.Equal(oldPrice, (await check.Products.SingleAsync(p => p.SKU == "DEMO-FSH-001")).Price);
    }

    [Fact]
    public async Task New_products_create_their_category_path_and_brand_and_a_check_saves_nothing()
    {
        await SeedDemoAsync();
        const string Sheet = "sku,name:fa,name:en,price,group,brand,published,gtin\nNEW-1,چراغ قوه,Torch,25,ابزار > روشنایی > چراغ قوه,Lumo,yes,4006381333931\n";

        var check = await ImportCsvAsync(Sheet, dryRun: true);
        Assert.Equal((false, 1, 0), (check.Applied, check.Created, check.Updated));
        await using (var db = Database.CreateContext())
        {
            Assert.False(await db.Products.AnyAsync(p => p.SKU == "NEW-1"));
            Assert.False(await db.Brands.AnyAsync(b => b.Name == "Lumo"));
        }

        var report = await ImportCsvAsync(Sheet);
        Assert.True(report.Applied);
        await using var after = Database.CreateContext();
        var torch = await after.Products.Include(p => p.Brand).Include(p => p.ProductGroup).ThenInclude(g => g.SubCategory).ThenInclude(s => s.Category).SingleAsync(p => p.SKU == "NEW-1");
        Assert.Equal(("Lumo", "ابزار", "روشنایی", "چراغ قوه"), (torch.Brand!.Name, torch.ProductGroup.SubCategory.Category.Name, torch.ProductGroup.SubCategory.Name, torch.ProductGroup.Name));
        Assert.True(torch.IsPublished);
        Assert.Equal("4006381333931", torch.Gtin);
    }

    [Fact]
    public async Task One_bad_row_keeps_the_whole_file_out_and_every_problem_is_reported()
    {
        await SeedDemoAsync();

        var report = await ImportCsvAsync(string.Join('\n',
            "sku,price,stock,spec:color,group,gtin,published",
            "DEMO-FSH-001,10,,,,,",
            "DEMO-FSH-002,ten,,,,,",
            "DEMO-FSH-003,10,1.5,,,,",
            "DEMO-PHN-001,10,,purple,,,",
            "NEW-2,10,,,Only > Two,,",
            "DEMO-FSH-001,10,,,,,",
            ",10,,,,,",
            "DEMO-PHN-002,10,,,,123,",
            "DEMO-PHN-003,10,,,,,perhaps"));

        Assert.False(report.Applied);
        Assert.Equal(
            [
                new ImportProblem(3, "price", TransferErrors.NumberInvalid),
                new ImportProblem(4, "stock", TransferErrors.NumberInvalid),
                new ImportProblem(5, "spec:color", TransferErrors.OptionUnknown),
                new ImportProblem(6, "group", TransferErrors.GroupInvalid),
                new ImportProblem(7, "sku", TransferErrors.SkuRepeated),
                new ImportProblem(8, "sku", TransferErrors.SkuRequired),
                new ImportProblem(9, null, CatalogErrors.GtinInvalid),
                new ImportProblem(10, "published", TransferErrors.BooleanInvalid),
            ],
            report.Problems);
        await using var db = Database.CreateContext();
        Assert.NotEqual(10m, (await db.Products.SingleAsync(p => p.SKU == "DEMO-FSH-001")).Price);
    }

    [Fact]
    public async Task Header_problems_stop_before_any_row()
    {
        await SeedDemoAsync();

        var report = await ImportCsvAsync("price,colour,spec:nope,price\n10,x,y,z\n");

        Assert.Equal(
            [
                new ImportProblem(1, "colour", TransferErrors.ColumnUnknown),
                new ImportProblem(1, "price", TransferErrors.ColumnRepeated),
                new ImportProblem(1, "sku", TransferErrors.ColumnMissing),
                new ImportProblem(1, "spec:nope", TransferErrors.ColumnUnknown),
            ],
            report.Problems);
    }

    [Fact]
    public async Task The_number_column_lets_a_product_change_its_sku()
    {
        await SeedDemoAsync();
        long number;
        await using (var db = Database.CreateContext())
            number = (await db.Products.SingleAsync(p => p.SKU == "DEMO-FSH-002")).PublicNumber;

        Assert.True((await ImportCsvAsync($"number,sku\n{number},BAG-25\n")).Applied);
        Assert.Equal([new ImportProblem(2, "number", TransferErrors.NumberUnknown)], (await ImportCsvAsync("number,sku\n999999,X\n")).Problems);

        await using var check = Database.CreateContext();
        Assert.Equal("BAG-25", (await check.Products.SingleAsync(p => p.PublicNumber == number)).SKU);
    }

    [Fact]
    public async Task Files_that_are_empty_too_big_or_too_long_are_refused()
    {
        await using var db = Database.CreateContext();
        var import = Import(db);

        Assert.Equal(TransferErrors.FileEmpty, (await import.ProductsAsync(Csv(""), 0, false)).Error!.Code);
        Assert.Equal(TransferErrors.FileTooLarge, (await import.ProductsAsync(Csv("x"), ProductImportService.MaxBytes + 1, false)).Error!.Code);
        var tooLong = Csv("sku\n" + string.Join('\n', Enumerable.Range(0, ProductImportService.MaxRows + 1).Select(i => $"S{i}")));
        Assert.Equal(TransferErrors.TooManyRows, (await import.ProductsAsync(tooLong, tooLong.Length, false)).Error!.Code);
        var broken = new MemoryStream([0x50, 0x4B, 0x03, 0x04, 1, 2, 3]);
        Assert.Equal(TransferErrors.FileUnreadable, (await import.ProductsAsync(broken, broken.Length, false)).Error!.Code);
    }

    [Fact]
    public async Task Stock_is_set_by_product_or_variant_sku()
    {
        await SeedDemoAsync();
        await using (var db = Database.CreateContext())
        {
            var file = (await Export(db).StockAsync("csv")).Value;
            var table = new CsvTableFile().Read(new MemoryStream(file.Content), 1000);
            Assert.Equal(["sku", "name", "stock"], table.Headers);
            Assert.Contains(table.Rows, r => r[0] == "DEMO-FSH-003-BG-S");
            Assert.DoesNotContain(table.Rows, r => r[0] == "DEMO-FSH-003");
        }

        await using var import = Database.CreateContext();
        var bad = Csv("sku,stock\nDEMO-FSH-003,5\nNOPE,1\nDEMO-FSH-001,-2\n");
        Assert.Equal(
            [
                new ImportProblem(2, "sku", TransferErrors.StockOnVariants),
                new ImportProblem(3, "sku", TransferErrors.SkuNotFound),
                new ImportProblem(4, "stock", TransferErrors.NumberInvalid),
            ],
            (await Import(import).StockAsync(bad, bad.Length, false)).Value.Problems);

        var good = Csv("sku,name,stock\nDEMO-FSH-001,whatever,۷\nDEMO-FSH-003-BG-S,,20\n");
        var report = (await Import(import).StockAsync(good, good.Length, false)).Value;
        Assert.Equal((true, 2), (report.Applied, report.Updated));

        await using var check = Database.CreateContext();
        Assert.Equal(7, (await check.Products.SingleAsync(p => p.SKU == "DEMO-FSH-001")).Stock);
        // The hoodie's stock is its variants' sum again: 60 - 8 + 20.
        Assert.Equal(72, (await check.Products.SingleAsync(p => p.SKU == "DEMO-FSH-003")).Stock);
        // Both changes are adjustment lines of the ledger: 33 → 7 and 8 → 20.
        Assert.Equal([-26, 12], (await check.StockMovements.Where(m => m.Reference == OnlineShopping.Application.Inventory.StockReferences.Import).OrderBy(m => m.Quantity).Select(m => m.Quantity).ToListAsync()));
    }

    private static string? CatalogErrorsOf<T>(OnlineShopping.Application.Common.Result<T> result) => result.Error?.Code;
}
