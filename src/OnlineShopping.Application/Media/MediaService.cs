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
