using SkiaSharp;

namespace OnlineShopping.UnitTests.TestSupport;

/// <summary>Small real images made on the fly for upload tests.</summary>
internal static class TestImages
{
    public static byte[] Png(int width, int height) => Encode(width, height, SKEncodedImageFormat.Png);

    public static byte[] Jpeg(int width, int height) => Encode(width, height, SKEncodedImageFormat.Jpeg);

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Teal);
            using var paint = new SKPaint { Color = SKColors.Orange };
            canvas.DrawRect(0, 0, width / 2f, height / 2f, paint);
        }
        using var data = bitmap.Encode(format, 90);
        return data.ToArray();
    }
}
