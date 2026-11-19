using System.Text.RegularExpressions;

namespace OnlineShopping.Application.Media;

/// <summary>Error codes of uploads (translated by the web app under <c>admin.mediaErrors</c>).</summary>
public static class MediaErrors
{
    public const string Empty = "media.empty";
    public const string TooLarge = "media.too_large";
    public const string TypeNotAllowed = "media.type_not_allowed";
    public const string InvalidImage = "media.invalid_image";
    public const string TooManyPixels = "media.too_many_pixels";
    public const string NotFound = "media.not_found";
    public const string InvalidVideo = "media.invalid_video";
    public const string VideoTooLarge = "media.video_too_large";
}

/// <summary>Section <c>Media</c>: upload limits.</summary>
public sealed class MediaOptions
{
    public const string SectionName = "Media";

    /// <summary>Largest accepted upload (10 MB by default).</summary>
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Largest accepted width × height before decoding, against "decompression bombs" (40 megapixels).</summary>
    public long MaxPixels { get; set; } = 40_000_000;

    /// <summary>Largest accepted video (50 MB by default; the web proxy and nginx accept a little more).</summary>
    public long MaxVideoBytes { get; set; } = 50 * 1024 * 1024;
}

/// <summary>Image formats accepted for upload, recognised by their first bytes (never by name or claimed type).</summary>
public enum ImageFormat
{
    Unknown,
    Jpeg,
    Png,
    WebP,
    Gif,
}

public static class ImageSignatures
{
    /// <summary>Bytes needed to recognise every accepted format.</summary>
    public const int HeaderLength = 12;

    public static ImageFormat Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return ImageFormat.Jpeg;
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return ImageFormat.Png;
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return ImageFormat.WebP;
        if (header.Length >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)))
            return ImageFormat.Gif;
        return ImageFormat.Unknown;
    }
}

/// <summary>One stored size of an image (WebP).</summary>
public sealed record ImageVariant(int Size, int Width, int Height, byte[] WebP);

/// <summary>The re-encoded image: every size, plus the original dimensions.</summary>
public sealed record ProcessedImage(int Width, int Height, IReadOnlyList<ImageVariant> Variants);

/// <summary>A stored image: its key and the public address of its largest size.</summary>
public sealed record StoredImage(string Key, string Url, int Width, int Height);

/// <summary>A stored video: its key, public address, size in bytes and duration (when the file states it).</summary>
public sealed record StoredVideo(string Key, string Url, long SizeBytes, double? DurationSeconds);

/// <summary>Decodes an image and re-encodes it as WebP in each size (stripping metadata); errors carry media.* codes.</summary>
public interface IImageProcessor
{
    Common.Result<ProcessedImage> Process(Stream image, long maxPixels);
}

/// <summary>Where uploaded files live (local disk; an S3-compatible store can implement the same interface).</summary>
public interface IFileStorage
{
    Task SaveAsync(string path, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>Writes a large file (a video) without holding it in memory.</summary>
    Task SaveAsync(string path, Stream content, CancellationToken cancellationToken = default);

    /// <summary>The file, or null when it does not exist.</summary>
    Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Removes a folder and everything in it (no error when it does not exist).</summary>
    Task DeleteFolderAsync(string folder, CancellationToken cancellationToken = default);
}

/// <summary>Addresses of stored images: /api/v1/media/{key}/{size}.webp (the key is a GUID, the size one of <see cref="Sizes"/>).</summary>
public static partial class MediaUrls
{
    /// <summary>Longest side of each stored size, largest first.</summary>
    public static IReadOnlyList<int> Sizes { get; } = [1200, 600, 300];

    public const string Prefix = "/api/v1/media/";

    public static string For(string key, int size) => $"{Prefix}{key}/{size}.webp";

    /// <summary>A product video is stored as it was uploaded (MP4, checked, not re-encoded).</summary>
    public const string VideoFile = "video.mp4";

    public static string Video(string key) => $"{Prefix}{key}/{VideoFile}";

    /// <summary>The content type a stored file is served with.</summary>
    public static string ContentTypeOf(string file) => file == VideoFile ? "video/mp4" : "image/webp";

    /// <summary>The storage key of a media address, or null for any other address (demo SVGs, external URLs).</summary>
    public static string? KeyOf(string? url)
    {
        var match = url is null ? null : MediaUrl().Match(url);
        return match is { Success: true } ? match.Groups[1].Value : null;
    }

    /// <summary>A valid request path inside the store: "{guid}/{size}.webp" (nothing else is ever opened).</summary>
    public static bool IsValidFile(string key, string file) =>
        Guid.TryParseExact(key, "N", out _)
        && (file == VideoFile || (FileName().IsMatch(file) && Sizes.Contains(int.Parse(file[..^5], System.Globalization.CultureInfo.InvariantCulture))));

    [GeneratedRegex(@"^/api/v1/media/([0-9a-f]{32})/(\d+\.webp|video\.mp4)$")]
    private static partial Regex MediaUrl();

    [GeneratedRegex(@"^\d{3,4}\.webp$")]
    private static partial Regex FileName();
}
