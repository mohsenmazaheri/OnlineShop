using System.Buffers.Binary;
using System.Text;

namespace OnlineShop.Application.Media;

/// <summary>What an accepted MP4 file says about itself.</summary>
public sealed record Mp4Info(double? DurationSeconds);

/// <summary>
/// Checks that a file really is an MP4 (ISO base media) video by walking its box structure: it must start with an
/// "ftyp" box naming an MP4 brand (QuickTime .mov is not MP4), every top-level box must fit in the file, and a "moov"
/// box (the index players need) must exist. The duration is read from "moov/mvhd". The file itself is not changed.
/// </summary>
public static class Mp4Inspector
{
    private const int MaxBoxes = 10_000;

    private static readonly string[] Brands =
        ["isom", "iso2", "iso3", "iso4", "iso5", "iso6", "iso7", "iso8", "iso9", "mp41", "mp42", "avc1", "M4V ", "mmp4", "dash", "MSNV", "XAVC", "f4v "];

    /// <summary>The video's details, or null when the stream is not an MP4 video file.</summary>
    public static Mp4Info? Inspect(Stream stream)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("The stream must be seekable.", nameof(stream));
        var length = stream.Length;
        stream.Position = 0;

        var first = ReadBox(stream, length);
        if (first is not { Type: "ftyp" } ftyp || ftyp.ContentLength < 8)
            return null;
        var brands = ReadBytes(stream, (int)Math.Min(ftyp.ContentLength, 256));
        var major = Encoding.ASCII.GetString(brands, 0, 4);
        var compatible = Enumerable.Range(0, (brands.Length - 8) / 4).Select(i => Encoding.ASCII.GetString(brands, 8 + i * 4, 4));
        if (major == "qt  " || !compatible.Prepend(major).Any(b => Brands.Contains(b)))
            return null;

        stream.Position = ftyp.End;
        double? duration = null;
        var hasMovie = false;
        for (var count = 0; stream.Position < length && count < MaxBoxes; count++)
        {
            var box = ReadBox(stream, length);
            if (box is null)
                return null;
            if (box.Type == "moov")
            {
                hasMovie = true;
                duration = MovieDuration(stream, box, length);
            }
            stream.Position = box.End;
        }
        return hasMovie ? new Mp4Info(duration) : null;
    }

    private sealed record Box(string Type, long Start, long ContentStart, long End)
    {
        public long ContentLength => End - ContentStart;
    }

    /// <summary>The box at the current position (32-bit size, 64-bit "largesize" for size 1, or "to the end" for 0).</summary>
    private static Box? ReadBox(Stream stream, long limit)
    {
        var start = stream.Position;
        if (limit - start < 8)
            return null;
        var header = ReadBytes(stream, 8);
        long size = BinaryPrimitives.ReadUInt32BigEndian(header);
        var type = Encoding.ASCII.GetString(header, 4, 4);
        if (type.Any(c => c < 0x20 || c > 0x7E))
            return null;
        var contentStart = start + 8;
        if (size == 1)
        {
            if (limit - start < 16)
                return null;
            size = (long)BinaryPrimitives.ReadUInt64BigEndian(ReadBytes(stream, 8));
            contentStart += 8;
        }
        else if (size == 0)
        {
            size = limit - start;
        }
        return size < contentStart - start || start + size > limit ? null : new Box(type, start, contentStart, start + size);
    }

    /// <summary>moov/mvhd: version 0 has 32-bit times, version 1 64-bit ones; duration = duration / timescale.</summary>
    private static double? MovieDuration(Stream stream, Box movie, long length)
    {
        stream.Position = movie.ContentStart;
        for (var count = 0; stream.Position < movie.End && count < MaxBoxes; count++)
        {
            var box = ReadBox(stream, Math.Min(movie.End, length));
            if (box is null)
                return null;
            if (box.Type == "mvhd" && box.ContentLength >= 20)
            {
                var data = ReadBytes(stream, (int)Math.Min(box.ContentLength, 32));
                var v1 = data[0] == 1;
                if (v1 && data.Length < 32)
                    return null;
                var timescale = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(v1 ? 20 : 12));
                var duration = v1 ? BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(24)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16));
                return timescale == 0 ? null : Math.Round(duration / (double)timescale, 2);
            }
            stream.Position = box.End;
        }
        return null;
    }

    private static byte[] ReadBytes(Stream stream, int count)
    {
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }
}
