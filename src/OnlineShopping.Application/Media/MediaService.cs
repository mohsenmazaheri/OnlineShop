using Microsoft.Extensions.Options;
using OnlineShopping.Application.Common;

namespace OnlineShopping.Application.Media;

/// <summary>
/// Accepts an uploaded image safely and stores it: size limit, format by its first bytes (JPEG, PNG, WebP, GIF), a
/// pixel limit before decoding, then a full decode and WebP re-encode in every size (so nothing of the original file
/// — scripts, metadata, location data — is kept). Files are stored under a new random key.
/// </summary>
public sealed class MediaService(IImageProcessor images, IFileStorage storage, IOptions<MediaOptions> options)
{
    public async Task<Result<StoredImage>> SaveImageAsync(Stream upload, long length, CancellationToken cancellationToken = default)
    {
        if (length <= 0)
            return AppError.Validation(MediaErrors.Empty, "The file is empty.");
        if (length > options.Value.MaxUploadBytes)
            return AppError.Validation(MediaErrors.TooLarge, $"The file is larger than {options.Value.MaxUploadBytes} bytes.");

        using var buffer = new MemoryStream();
        await upload.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > options.Value.MaxUploadBytes)
            return AppError.Validation(MediaErrors.TooLarge, "The file is too large.");
        if (ImageSignatures.Detect(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, ImageSignatures.HeaderLength))) == ImageFormat.Unknown)
            return AppError.Validation(MediaErrors.TypeNotAllowed, "Only JPEG, PNG, WebP and GIF images are accepted.");

        buffer.Position = 0;
        var processed = images.Process(buffer, options.Value.MaxPixels);
        if (!processed.IsSuccess)
            return processed.Error!;

        var key = Guid.NewGuid().ToString("N");
        foreach (var variant in processed.Value.Variants)
            await storage.SaveAsync($"{key}/{variant.Size}.webp", variant.WebP, cancellationToken);

        return new StoredImage(key, MediaUrls.For(key, MediaUrls.Sizes[0]), processed.Value.Width, processed.Value.Height);
    }

    /// <summary>
    /// Stores an uploaded MP4 video under a new key after checking it really is one (<see cref="Mp4Inspector"/>). Videos
    /// are not re-encoded in v1: they are served as video/mp4 with nosniff, so a browser never runs them as anything else.
    /// </summary>
    public async Task<Result<StoredVideo>> SaveVideoAsync(Stream upload, long length, CancellationToken cancellationToken = default)
    {
        if (length <= 0)
            return AppError.Validation(MediaErrors.Empty, "The file is empty.");
        if (length > options.Value.MaxVideoBytes)
            return AppError.Validation(MediaErrors.VideoTooLarge, $"The video is larger than {options.Value.MaxVideoBytes} bytes.");

        var key = Guid.NewGuid().ToString("N");
        var path = $"{key}/{MediaUrls.VideoFile}";
        await storage.SaveAsync(path, upload, cancellationToken);

        Mp4Info? info = null;
        long size = 0;
        await using (var stored = await storage.OpenReadAsync(path, cancellationToken))
        {
            if (stored is not null)
            {
                size = stored.Length;
                info = size <= options.Value.MaxVideoBytes ? Mp4Inspector.Inspect(stored) : null;
            }
        }
        if (info is null)
        {
            await storage.DeleteFolderAsync(key, cancellationToken);
            return size > options.Value.MaxVideoBytes
                ? AppError.Validation(MediaErrors.VideoTooLarge, "The video is too large.")
                : AppError.Validation(MediaErrors.InvalidVideo, "Only MP4 videos are accepted.");
        }
        return new StoredVideo(key, MediaUrls.Video(key), size, info.DurationSeconds);
    }

    /// <summary>Deletes the files behind a media address; other addresses (demo images, external) are left alone.</summary>
    public async Task DeleteAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (MediaUrls.KeyOf(url) is { } key)
            await storage.DeleteFolderAsync(key, cancellationToken);
    }

    /// <summary>A stored file for the public media endpoint, or null for anything that is not a valid media path.</summary>
    public Task<Stream?> OpenAsync(string key, string file, CancellationToken cancellationToken = default) =>
        MediaUrls.IsValidFile(key, file) ? storage.OpenReadAsync($"{key}/{file}", cancellationToken) : Task.FromResult<Stream?>(null);
}
