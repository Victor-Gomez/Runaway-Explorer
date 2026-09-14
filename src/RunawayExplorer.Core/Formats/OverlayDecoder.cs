using System.Buffers.Binary;

namespace RunawayExplorer.Core.Formats;

/// <summary>One row record of an overlay: <paramref name="Count"/> pixels painted at screen (<paramref name="X"/>, <paramref name="Y"/>).</summary>
public readonly record struct OverlayRecord(int X, int Y, int Count, int PixelOffset);

/// <summary>Geometry of a parsed overlay: the records plus the bounding box of everything they paint.</summary>
public sealed class OverlayInfo
{
    public required IReadOnlyList<OverlayRecord> Records { get; init; }

    /// <summary>Top-left of the painted area on the screen.</summary>
    public int X { get; init; }
    public int Y { get; init; }

    /// <summary>Size of the painted area (the cropped image).</summary>
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>True when every record has the same x and count -- a plain rectangle (title card, icon) rather than a sparse prop.</summary>
    public bool IsRectangular { get; init; }
}

/// <summary>
/// Format 2 of the scene archives: positioned row records.
/// <code>
/// u16 n
/// n × { u16 x, u16 y, u16 count, u16 rgb565[count] }
/// </code>
/// Records consume the entry exactly -- that is the format check, and it is byte-exact rather than a
/// heuristic. <c>(x, y)</c> are positions on the 1024×600 screen (a few sit past 1024 on wide scenes).
/// This is the record shape consumed by the game's blitter at RVA 0x32ea0.
/// </summary>
public static class OverlayDecoder
{
    /// <summary>Parses the record table. <see langword="null"/> unless the records consume <paramref name="data"/> exactly.</summary>
    public static OverlayInfo? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            return null;

        int n = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (n == 0)
            return null;

        var records = new List<OverlayRecord>(n);
        int p = 2;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
        bool rectangular = true;
        int firstX = -1, firstCount = -1;

        for (int i = 0; i < n; i++)
        {
            if (p + 6 > data.Length)
                return null;

            int x = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p));
            int y = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p + 2));
            int c = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p + 4));
            p += 6;
            if (c == 0 || p + 2 * c > data.Length)
                return null;

            records.Add(new OverlayRecord(x, y, c, p));
            p += 2 * c;

            if (i == 0) { firstX = x; firstCount = c; }
            else if (x != firstX || c != firstCount) rectangular = false;

            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + c);
            maxY = Math.Max(maxY, y + 1);
        }

        if (p != data.Length)
            return null;

        return new OverlayInfo
        {
            Records = records,
            X = minX,
            Y = minY,
            Width = maxX - minX,
            Height = maxY - minY,
            IsRectangular = rectangular,
        };
    }

    /// <summary>
    /// Paints the records into an RGBA image cropped to their bounding box. Everything not covered by
    /// a record is transparent. Place the result at (<see cref="OverlayInfo.X"/>, <see cref="OverlayInfo.Y"/>)
    /// on the screen.
    /// </summary>
    public static DecodedImage Decode(ReadOnlySpan<byte> data, OverlayInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        var image = DecodedImage.Transparent(info.Width, info.Height);
        foreach (OverlayRecord r in info.Records)
        {
            int dst = ((r.Y - info.Y) * info.Width + (r.X - info.X)) * 4;
            Rgb565.CopyRow(data, r.PixelOffset, image.Pixels, dst, r.Count);
        }
        return image;
    }

    /// <summary>Parse + decode in one step. <see langword="null"/> when the bytes are not an overlay.</summary>
    public static (DecodedImage Image, OverlayInfo Info)? TryDecode(ReadOnlySpan<byte> data)
    {
        if (Parse(data) is not { } info)
            return null;
        return (Decode(data, info), info);
    }
}
