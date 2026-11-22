using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Catalog.Admin;

namespace OnlineShop.IntegrationTests;

/// <summary>Product videos over HTTP: upload (checked MP4), playback with Range requests, the product page, deletion.</summary>
public sealed class ProductVideoApiTests(CatalogAdminApiTests.AdminFactory factory) : IClassFixture<CatalogAdminApiTests.AdminFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static byte[] Box(string type, byte[] content)
    {
        var box = new byte[8 + content.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        content.CopyTo(box, 8);
        return box;
    }

    /// <summary>A minimal MP4: ftyp, moov/mvhd (20 s) and 4 KB of media data.</summary>
    private static byte[] Mp4()
    {
        var mvhd = new byte[100];
        BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(12), 1000);
        BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(16), 20_000);
        return [.. Box("ftyp", [.. "isom"u8.ToArray(), 0, 0, 2, 0, .. "isom"u8.ToArray()]), .. Box("moov", Box("mvhd", mvhd)), .. Box("mdat", new byte[4096])];
    }

    private static MultipartFormDataContent File(byte[] bytes, string name, string type)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", name);
        return form;
    }

    [Fact]
    public async Task A_video_is_uploaded_played_with_ranges_shown_on_the_product_and_deleted()
    {
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/v1/account/login", new LoginRequest(CatalogAdminApiTests.AdminFactory.AdminEmail, CatalogAdminApiTests.AdminFactory.AdminPassword, false))).EnsureSuccessStatusCode();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await (await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories", new SaveCatalogNodeRequest([new("fa", $"ویدئو {suffix}", null, null)]))).Content.ReadFromJsonAsync<CatalogNodeView>(Json);
        var sub = await (await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category!.Id}/subcategories", new SaveCatalogNodeRequest([new("fa", "زیر", null, null)]))).Content.ReadFromJsonAsync<CatalogNodeView>(Json);
        var group = await (await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub!.Id}/groups", new SaveCatalogNodeRequest([new("fa", "گروه", null, null)]))).Content.ReadFromJsonAsync<CatalogNodeView>(Json);
        var product = await (await admin.PostAsJsonAsync("/api/v1/admin/products", new SaveProductRequest($"VID-{suffix}", 5, null, 1, true, group!.Id, [new("fa", "کالای ویدئودار", null, null)], []), Json)).Content.ReadFromJsonAsync<ProductEditView>(Json);

        // A text file named .mp4 is refused; a real MP4 is stored with its duration.
        using var fake = File("<script>alert(1)</script>"u8.ToArray(), "evil.mp4", "video/mp4");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/v1/admin/products/{product!.Number}/videos", fake)).StatusCode);
        using var upload = File(Mp4(), "tour.mp4", "video/mp4");
        var response = await admin.PostAsync($"/api/v1/admin/products/{product.Number}/videos", upload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var video = (await response.Content.ReadFromJsonAsync<ProductVideoView>(Json))!;
        Assert.Equal(20, video.DurationSeconds);

        // Players ask for ranges: 206 with the requested bytes.
        using var visitor = factory.CreateClient();
        using var range = new HttpRequestMessage(HttpMethod.Get, video.Url);
        range.Headers.Range = new RangeHeaderValue(0, 99);
        var partial = await visitor.SendAsync(range);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal("video/mp4", partial.Content.Headers.ContentType!.MediaType);
        Assert.Equal(100, (await partial.Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal(Mp4().Length, partial.Content.Headers.ContentRange!.Length);

        using var page = JsonDocument.Parse(await visitor.GetStringAsync($"/api/v1/catalog/products/{product.Number}?locale=fa"));
        Assert.Equal(video.Url, page.RootElement.GetProperty("videos")[0].GetProperty("url").GetString());

        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/v1/admin/products/{product.Number}/videos/{video.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(video.Url)).StatusCode);
    }
}
