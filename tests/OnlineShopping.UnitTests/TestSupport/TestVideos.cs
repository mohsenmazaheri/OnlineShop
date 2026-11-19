using System.Buffers.Binary;
using System.Text;

namespace OnlineShopping.UnitTests.TestSupport;

/// <summary>Minimal MP4 files (ftyp + moov/mvhd + mdat boxes) for upload tests; enough for the box walk, not for playing.</summary>
internal static class TestVideos
{
    public static byte[] Mp4(double seconds = 12.5, string brand = "isom", bool withMovie = true, bool version1 = false, int payloadBytes = 64)
    {
        var parts = new List<byte[]> { Box("ftyp", [.. Ascii(brand), 0, 0, 2, 0, .. Ascii(brand), .. Ascii("mp41")]) };
        if (withMovie)
            parts.Add(Box("moov", Box("mvhd", MovieHeader(seconds, version1))));
        parts.Add(Box("mdat", new byte[payloadBytes]));
        return [.. parts.SelectMany(p => p)];
    }

    /// <summary>A box with a 64-bit size ("largesize"), as long files use.</summary>
    public static byte[] LargeBox(string type, byte[] content)
    {
        var box = new byte[16 + content.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, 1);
        Ascii(type).CopyTo(box, 4);
        BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8), (ulong)box.Length);
        content.CopyTo(box, 16);
        return box;
    }

    public static byte[] Box(string type, byte[] content)
    {
        var box = new byte[8 + content.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Ascii(type).CopyTo(box, 4);
        content.CopyTo(box, 8);
        return box;
    }

    private static byte[] MovieHeader(double seconds, bool version1)
    {
        const uint timescale = 1000;
        var duration = (ulong)(seconds * timescale);
        if (!version1)
        {
            var v0 = new byte[100];
            BinaryPrimitives.WriteUInt32BigEndian(v0.AsSpan(12), timescale);
            BinaryPrimitives.WriteUInt32BigEndian(v0.AsSpan(16), (uint)duration);
            return v0;
        }
        var v1 = new byte[112];
        v1[0] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(v1.AsSpan(20), timescale);
        BinaryPrimitives.WriteUInt64BigEndian(v1.AsSpan(24), duration);
        return v1;
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
}
