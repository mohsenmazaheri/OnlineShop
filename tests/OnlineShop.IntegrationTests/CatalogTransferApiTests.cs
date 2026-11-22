using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Catalog.Admin;
using OnlineShop.Application.Catalog.Transfer;

namespace OnlineShop.IntegrationTests;

/// <summary>Export and import of products and stock over HTTP on PostgreSQL (transactions and savepoints included).</summary>
public sealed class CatalogTransferApiTests(CatalogAdminApiTests.AdminFactory factory) : IClassFixture<CatalogAdminApiTests.AdminFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateClient();
        var login = new LoginRequest(CatalogAdminApiTests.AdminFactory.AdminEmail, CatalogAdminApiTests.AdminFactory.AdminPassword, false);
        (await client.PostAsJsonAsync("/api/v1/account/login", login)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<ImportReport> UploadAsync(HttpClient client, string path, string csv)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "sheet.csv");
        var response = await client.PostAsync(path, form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportReport>(Json))!;
    }

    [Fact]
    public async Task Products_and_stock_are_exported_checked_and_imported_as_a_whole()
    {
        using var admin = await AdminAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sheet = string.Join('\n',
            "sku,name:fa,name:en,price,stock,group,brand,published",
            $"IMP-{suffix}-1,لیوان,Mug,12,3,آشپزخانه {suffix} > ظروف > لیوان,Brand {suffix},true",
            $"IMP-{suffix}-2,بشقاب,Plate,9,5,آشپزخانه {suffix} > ظروف > بشقاب,Brand {suffix},true");

        // A check, then a file with one bad row: nothing is saved either time.
        var check = await UploadAsync(admin, "/api/v1/admin/catalog/import/products?dryRun=true", sheet);
        Assert.Equal((false, 2, 0), (check.Applied, check.Created, check.Problems.Count));
        var bad = await UploadAsync(admin, "/api/v1/admin/catalog/import/products", sheet + $"\nIMP-{suffix}-3,کاسه,Bowl,cheap,1,آشپزخانه {suffix} > ظروف > کاسه,,true");
        Assert.Equal([new ImportProblem(4, "price", TransferErrors.NumberInvalid)], bad.Problems);
        var page = await admin.GetFromJsonAsync<ProductPage>($"/api/v1/admin/products?search=IMP-{suffix}", Json);
        Assert.Equal(0, page!.Total);

        var imported = await UploadAsync(admin, "/api/v1/admin/catalog/import/products", sheet);
        Assert.Equal((true, 2), (imported.Applied, imported.Created));

        // Stock by SKU, then both exports.
        var stock = await UploadAsync(admin, "/api/v1/admin/catalog/import/stock", $"sku,stock\nIMP-{suffix}-1,40\n");
        Assert.Equal((true, 1), (stock.Applied, stock.Updated));

        var xlsx = await admin.GetAsync("/api/v1/admin/catalog/export/products?format=xlsx");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType!.MediaType);
        Assert.Equal("PK"u8.ToArray(), (await xlsx.Content.ReadAsByteArrayAsync())[..2]);
        var csv = await admin.GetStringAsync("/api/v1/admin/catalog/export/stock?format=csv");
        Assert.Contains($"IMP-{suffix}-1,Mug,40", csv.Replace("لیوان", "Mug", StringComparison.Ordinal), StringComparison.Ordinal);

        using var visitor = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/v1/admin/catalog/export/products")).StatusCode);
    }
}
