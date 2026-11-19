using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Media;
using OnlineShopping.Infrastructure.Media;
using OnlineShopping.UnitTests.TestSupport;
using SkiaSharp;

namespace OnlineShopping.UnitTests.Infrastructure;

public sealed class ImageSignaturesTests
{
    [Fact]
    public void Recognises_the_accepted_formats_by_their_first_bytes()
    {
        Assert.Equal(ImageFormat.Png, ImageSignatures.Detect(TestImages.Png(2, 2)));
        Assert.Equal(ImageFormat.Jpeg, ImageSignatures.Detect(TestImages.Jpeg(2, 2)));
        Assert.Equal(ImageFormat.WebP, ImageSignatures.Detect("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Equal(ImageFormat.Gif, ImageSignatures.Detect("GIF89a......"u8));
    }

    [Fact]
    public void Rejects_everything_else_whatever_it_claims_to_be()
    {
        Assert.Equal(ImageFormat.Unknown, ImageSignatures.Detect("<svg xmlns=\"http://www.w3.org/2000/svg\">"u8));
        Assert.Equal(ImageFormat.Unknown, ImageSignatures.Detect("%PDF-1.7"u8));
        Assert.Equal(ImageFormat.Unknown, ImageSignatures.Detect("MZ"u8));
        Assert.Equal(ImageFormat.Unknown, ImageSignatures.Detect([]));
    }
}

public sealed class MediaUrlsTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void Builds_and_reads_media_addresses()
    {
        Assert.Equal($"/api/v1/media/{Key}/600.webp", MediaUrls.For(Key, 600));
        Assert.Equal(Key, MediaUrls.KeyOf($"/api/v1/media/{Key}/1200.webp"));
        Assert.Null(MediaUrls.KeyOf("/demo/products/kettle.svg"));
        Assert.Null(MediaUrls.KeyOf(null));
    }

    [Theory]
    [InlineData(Key, "600.webp", true)]
    [InlineData(Key, "601.webp", false)]
    [InlineData(Key, "600.png", false)]
    [InlineData("..", "600.webp", false)]
    [InlineData(Key, "../600.webp", false)]
    [InlineData("not-a-guid", "600.webp", false)]
    public void Only_real_stored_files_are_valid(string key, string file, bool valid) => Assert.Equal(valid, MediaUrls.IsValidFile(key, file));
}

public sealed class SkiaImageProcessorTests
{
    private readonly SkiaImageProcessor _processor = new();

    [Fact]
    public void Makes_three_webp_sizes_keeping_the_aspect_ratio()
    {
        var result = _processor.Process(new MemoryStream(TestImages.Png(2000, 1000)), 40_000_000);

        var image = result.Value;
        Assert.Equal((2000, 1000), (image.Width, image.Height));
        Assert.Equal([(1200, 1200, 600), (600, 600, 300), (300, 300, 150)], image.Variants.Select(v => (v.Size, v.Width, v.Height)));
        Assert.All(image.Variants, v => Assert.Equal(ImageFormat.WebP, ImageSignatures.Detect(v.WebP)));
    }

    [Fact]
    public void Never_enlarges_a_small_image()
    {
        var image = _processor.Process(new MemoryStream(TestImages.Jpeg(400, 200)), 40_000_000).Value;

        Assert.Equal([(400, 200), (400, 200), (300, 150)], image.Variants.Select(v => (v.Width, v.Height)));
    }

    [Fact]
    public void Refuses_too_many_pixels_before_decoding_and_garbage()
    {
        Assert.Equal(MediaErrors.TooManyPixels, _processor.Process(new MemoryStream(TestImages.Png(300, 300)), 50_000).Error!.Code);
        Assert.Equal(MediaErrors.InvalidImage, _processor.Process(new MemoryStream("\x89PNG\r\n\x1a\nnot really"u8.ToArray()), 40_000_000).Error!.Code);
    }

    [Fact]
    public void Removes_everything_but_the_pixels()
    {
        // A PNG with text appended after its end: the re-encoded WebP must not carry it.
        var payload = TestImages.Png(50, 50).Concat("<script>alert(1)</script>"u8.ToArray()).ToArray();

        var variant = _processor.Process(new MemoryStream(payload), 40_000_000).Value.Variants[0];

        Assert.DoesNotContain("<script>", System.Text.Encoding.Latin1.GetString(variant.WebP), StringComparison.Ordinal);
        using var decoded = SKBitmap.Decode(variant.WebP);
        Assert.Equal((50, 50), (decoded.Width, decoded.Height));
    }
}

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "os-media-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests() =>
        _storage = new LocalFileStorage(Options.Create(new StorageOptions { LocalPath = _root }), new HostingEnvironment { ContentRootPath = Path.GetTempPath() });

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Saves_opens_and_deletes_files_in_a_folder()
    {
        await _storage.SaveAsync("abc/600.webp", [1, 2, 3]);

        await using (var stream = await _storage.OpenReadAsync("abc/600.webp"))
        {
            using var copy = new MemoryStream();
            await stream!.CopyToAsync(copy);
            Assert.Equal([1, 2, 3], copy.ToArray());
        }
        await _storage.DeleteFolderAsync("abc");

        Assert.Null(await _storage.OpenReadAsync("abc/600.webp"));
        await _storage.DeleteFolderAsync("missing");
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("a/../../outside.txt")]
    [InlineData("C:/Windows/win.ini")]
    public void Never_leaves_the_storage_folder(string path) => Assert.Throws<ArgumentException>(() => _storage.Resolve(path));
}

public sealed class MediaServiceTests
{
    [Fact]
    public async Task Stores_every_size_under_a_new_key()
    {
        var storage = new InMemoryFileStorage();
        var png = TestImages.Png(800, 600);

        var stored = await storage.Media().SaveImageAsync(new MemoryStream(png), png.Length);

        Assert.Equal(MediaUrls.For(stored.Value.Key, 1200), stored.Value.Url);
        Assert.Equal(["1200.webp", "300.webp", "600.webp"], storage.Files.Keys.Select(k => k.Split('/')[1]).Order());
        Assert.NotNull(await storage.Media().OpenAsync(stored.Value.Key, "600.webp"));
        Assert.Null(await storage.Media().OpenAsync(stored.Value.Key, "../secret"));
    }

    [Fact]
    public async Task Refuses_empty_too_large_and_non_image_files()
    {
        var media = new InMemoryFileStorage().Media(new MediaOptions { MaxUploadBytes = 100 });
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray();

        Assert.Equal(MediaErrors.Empty, (await media.SaveImageAsync(new MemoryStream(), 0)).Error!.Code);
        Assert.Equal(MediaErrors.TooLarge, (await media.SaveImageAsync(new MemoryStream(new byte[101]), 101)).Error!.Code);
        Assert.Equal(MediaErrors.TooLarge, (await media.SaveImageAsync(new MemoryStream(new byte[500]), 50)).Error!.Code);
        Assert.Equal(MediaErrors.TypeNotAllowed, (await media.SaveImageAsync(new MemoryStream(svg), svg.Length)).Error!.Code);
    }

    [Fact]
    public async Task Deletes_only_media_addresses()
    {
        var storage = new InMemoryFileStorage();
        var png = TestImages.Png(10, 10);
        var stored = (await storage.Media().SaveImageAsync(new MemoryStream(png), png.Length)).Value;

        await storage.Media().DeleteAsync("/demo/products/kettle.svg");
        Assert.Equal(3, storage.Files.Count);
        await storage.Media().DeleteAsync(stored.Url);
        Assert.Empty(storage.Files);
    }
}
