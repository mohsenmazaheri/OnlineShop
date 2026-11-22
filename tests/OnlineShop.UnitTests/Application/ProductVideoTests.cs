using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Catalog;
using OnlineShop.Application.Catalog.Admin;
using OnlineShop.Application.Media;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Application;

public sealed class Mp4InspectorTests
{
    private static Mp4Info? Inspect(byte[] bytes) => Mp4Inspector.Inspect(new MemoryStream(bytes));

    [Fact]
    public void Reads_the_duration_of_an_mp4_in_both_header_versions()
    {
        Assert.Equal(12.5, Inspect(TestVideos.Mp4(12.5))!.DurationSeconds);
        Assert.Equal(3600, Inspect(TestVideos.Mp4(3600, version1: true))!.DurationSeconds);
        Assert.NotNull(Inspect(TestVideos.Mp4(brand: "mp42")));
    }

    [Fact]
    public void Accepts_64_bit_box_sizes()
    {
        var ftyp = TestVideos.Box("ftyp", [.. "isom"u8.ToArray(), 0, 0, 2, 0, .. "isom"u8.ToArray()]);
        var moov = TestVideos.Box("moov", TestVideos.Box("mvhd", new byte[100]));
        Assert.NotNull(Inspect([.. ftyp, .. moov, .. TestVideos.LargeBox("mdat", new byte[32])]));
    }

    [Fact]
    public void Refuses_quicktime_files_without_an_index_and_anything_else()
    {
        Assert.Null(Inspect(TestVideos.Mp4(brand: "qt  ")));
        Assert.Null(Inspect(TestVideos.Mp4(withMovie: false)));
        Assert.Null(Inspect("<html><script>alert(1)</script></html>"u8.ToArray()));
        Assert.Null(Inspect([]));
        // A box claiming more bytes than the file has.
        var truncated = TestVideos.Mp4();
        Assert.Null(Inspect(truncated[..^10]));
        // A JPEG renamed to .mp4.
        Assert.Null(Inspect(TestImages.Jpeg(10, 10)));
    }
}

public sealed class VideoMediaTests
{
    [Fact]
    public void Video_addresses_are_recognised_and_served_as_mp4()
    {
        var key = Guid.NewGuid().ToString("N");
        Assert.Equal($"/api/v1/media/{key}/video.mp4", MediaUrls.Video(key));
        Assert.Equal(key, MediaUrls.KeyOf(MediaUrls.Video(key)));
        Assert.True(MediaUrls.IsValidFile(key, "video.mp4"));
        Assert.False(MediaUrls.IsValidFile(key, "video.mov"));
        Assert.Equal("video/mp4", MediaUrls.ContentTypeOf("video.mp4"));
        Assert.Equal("image/webp", MediaUrls.ContentTypeOf("600.webp"));
    }

    [Fact]
    public async Task Stores_a_checked_video_and_removes_a_refused_one()
    {
        var storage = new InMemoryFileStorage();
        var media = storage.Media(new MediaOptions { MaxVideoBytes = 10_000 });
        var mp4 = TestVideos.Mp4(8);

        var stored = (await media.SaveVideoAsync(new MemoryStream(mp4), mp4.Length)).Value;
        Assert.Equal((mp4.Length, 8.0), (stored.SizeBytes, stored.DurationSeconds!.Value));
        Assert.Equal(mp4, storage.Files[$"{stored.Key}/video.mp4"]);

        var fake = "not a video at all, just text"u8.ToArray();
        Assert.Equal(MediaErrors.InvalidVideo, (await media.SaveVideoAsync(new MemoryStream(fake), fake.Length)).Error!.Code);
        Assert.Single(storage.Files);
        Assert.Equal(MediaErrors.VideoTooLarge, (await media.SaveVideoAsync(new MemoryStream(mp4), 10_001)).Error!.Code);
        // The declared length can lie: the stored size counts too.
        var big = TestVideos.Mp4(payloadBytes: 20_000);
        Assert.Equal(MediaErrors.VideoTooLarge, (await media.SaveVideoAsync(new MemoryStream(big), 100)).Error!.Code);
        Assert.Equal(MediaErrors.Empty, (await media.SaveVideoAsync(new MemoryStream(), 0)).Error!.Code);
        Assert.Single(storage.Files);
    }
}

public sealed class CatalogVideoServiceTests : DemoCatalogTestBase
{
    private readonly InMemoryFileStorage _files = new();

    private CatalogVideoService Service(AppDbContext db) => new(db, _files.Media(), Options.Create(Store));

    private static MemoryStream Video() => new(TestVideos.Mp4(30));

    [Fact]
    public async Task Videos_are_added_titled_ordered_and_limited()
    {
        await SeedDemoAsync();
        var number = await NumberOfAsync("DEMO-FSH-002");
        await using var db = Database.CreateContext();
        var service = Service(db);

        var first = (await service.AddAsync(number, Video(), Video().Length)).Value;
        var second = (await service.AddAsync(number, Video(), Video().Length)).Value;
        await service.AddAsync(number, Video(), Video().Length);
        Assert.Equal(CatalogErrors.TooManyVideos, (await service.AddAsync(number, Video(), Video().Length)).Error!.Code);
        Assert.Equal(30, first.DurationSeconds);

        var third = (await new ProductAdminService(db, Options.Create(Store), TestLedger.For(db, Store)).GetAsync(number)).Value.Videos[2];
        var saved = (await service.SaveAsync(number, new([
            new(second.Id, [new("fa", "معرفی کوله"), new("en", "Backpack tour")]),
            new(first.Id, []),
            new(third.Id, []),
        ]))).Value;

        Assert.Equal([second.Id, first.Id, third.Id], saved.Select(v => v.Id));
        Assert.Equal(("معرفی کوله", "Backpack tour", ""), (saved[0].Titles[0].Title, saved[0].Titles[1].Title, saved[0].Titles[2].Title));
        Assert.Equal(CatalogErrors.VideosMismatch, (await service.SaveAsync(number, new([new(first.Id, [])]))).Error!.Code);
        Assert.Equal(CatalogErrors.LocaleInvalid, (await service.SaveAsync(number, new([new(second.Id, [new("xx", "t")]), new(first.Id, []), new(third.Id, [])]))).Error!.Code);
    }

    [Fact]
    public async Task A_poster_is_set_and_deleting_a_video_removes_every_file()
    {
        await SeedDemoAsync();
        var number = await NumberOfAsync("DEMO-FSH-002");
        await using var db = Database.CreateContext();
        var service = Service(db);
        var video = (await service.AddAsync(number, Video(), Video().Length)).Value;
        var png = TestImages.Png(640, 360);

        var poster = (await service.SetPosterAsync(number, video.Id, new MemoryStream(png), png.Length)).Value;
        Assert.EndsWith("/1200.webp", poster, StringComparison.Ordinal);
        Assert.Equal(4, _files.Files.Count); // the video and three poster sizes

        Assert.True((await service.DeleteAsync(number, video.Id)).IsSuccess);
        Assert.Empty(_files.Files);
        Assert.Equal(CatalogErrors.NotFound, (await service.DeleteAsync(number, video.Id)).Error!.Code);
        // The video belongs to this product only.
        var other = (await service.AddAsync(number, Video(), Video().Length)).Value;
        Assert.Equal(CatalogErrors.NotFound, (await service.RemovePosterAsync(await NumberOfAsync("DEMO-FSH-001"), other.Id)).Error!.Code);
    }

    [Fact]
    public async Task The_storefront_gets_videos_with_titles_in_its_language()
    {
        await SeedDemoAsync();
        var number = await NumberOfAsync("DEMO-FSH-002");
        await using (var db = Database.CreateContext())
        {
            var service = Service(db);
            var video = (await service.AddAsync(number, Video(), Video().Length)).Value;
            await service.SaveAsync(number, new([new(video.Id, [new("fa", "معرفی"), new("en", "Tour")])]));
        }
        await using var read = Database.CreateContext();
        var queries = new ProductQueries(read);

        var en = (await queries.GetAsync(number, "en", "fa")).Value.Videos.Single();
        Assert.Equal(("Tour", 30.0), (en.Title, en.DurationSeconds!.Value));
        Assert.EndsWith("/video.mp4", en.Url, StringComparison.Ordinal);
        Assert.Equal("معرفی", (await queries.GetAsync(number, "de", "fa")).Value.Videos.Single().Title);
        Assert.Empty((await queries.GetAsync(await NumberOfAsync("DEMO-FSH-001"), "en", "fa")).Value.Videos);
        Assert.True(await read.ProductVideos.AnyAsync());
    }
}
