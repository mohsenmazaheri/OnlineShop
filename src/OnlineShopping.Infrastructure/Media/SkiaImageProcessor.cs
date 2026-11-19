using OnlineShopping.Application.Common;
using OnlineShopping.Application.Media;
using SkiaSharp;

namespace OnlineShopping.Infrastructure.Media;

/// <summary>
/// Decodes with SkiaSharp (JPEG, PNG, WebP, GIF first frame), honours the EXIF orientation, and re-encodes every size as
/// WebP. Only the pixels survive: metadata, colour-profile tricks and appended data are dropped by the re-encode.
/// </summary>
public sealed class SkiaImageProcessor : IImageProcessor
{
    private const int Quality = 82;

    public Result<ProcessedImage> Process(Stream image, long maxPixels)
    {
        using var data = SKData.Create(image);
        if (data is null)
            return AppError.Validation(MediaErrors.InvalidImage, "The file could not be read.");
        using var codec = SKCodec.Create(data);
        if (codec is null)
            return AppError.Validation(MediaErrors.InvalidImage, "The file is not an image.");

        // Check the size from the header before decoding a single pixel.
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
            return AppError.Validation(MediaErrors.InvalidImage, "The image has no size.");
        if ((long)info.Width * info.Height > maxPixels)
            return AppError.Validation(MediaErrors.TooManyPixels, "The image has too many pixels.");

        using var decoded = SKBitmap.Decode(codec, info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul));
        if (decoded is null)
            return AppError.Validation(MediaErrors.InvalidImage, "The image could not be decoded.");
        using var upright = Orient(decoded, codec.EncodedOrigin);

        var variants = new List<ImageVariant>();
        foreach (var size in MediaUrls.Sizes)
        {
            var scale = Math.Min(1.0, (double)size / Math.Max(upright.Width, upright.Height));
            var width = Math.Max(1, (int)Math.Round(upright.Width * scale));
            var height = Math.Max(1, (int)Math.Round(upright.Height * scale));
            using var resized = scale < 1.0
                ? upright.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.Mitchell))
                : upright.Copy();
            if (resized is null)
                return AppError.Validation(MediaErrors.InvalidImage, "The image could not be resized.");
            using var encoded = resized.Encode(SKEncodedImageFormat.Webp, Quality);
            variants.Add(new ImageVariant(size, width, height, encoded.ToArray()));
        }

        return new ProcessedImage(upright.Width, upright.Height, variants);
    }

    /// <summary>A copy turned upright according to the camera orientation (phones store most photos rotated).</summary>
    private static SKBitmap Orient(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
            return bitmap.Copy();

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
        using var canvas = new SKCanvas(result);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, bitmap.Width / 2f, 0); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, bitmap.Width / 2f, bitmap.Height / 2f); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, bitmap.Height / 2f); break;
            case SKEncodedOrigin.LeftTop: canvas.Translate(result.Width, 0); canvas.RotateDegrees(90); canvas.Scale(1, -1, 0, bitmap.Height / 2f); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(result.Width, 0); canvas.RotateDegrees(90); break;
            // Transverse: turn a quarter clockwise, then mirror top to bottom (the mirror is set first, so it applies last).
            case SKEncodedOrigin.RightBottom: canvas.Scale(1, -1, 0, result.Height / 2f); canvas.Translate(result.Width, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, result.Height); canvas.RotateDegrees(270); break;
        }
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
        return result;
    }
}
