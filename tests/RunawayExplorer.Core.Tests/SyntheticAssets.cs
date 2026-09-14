using System.Buffers.Binary;
using RunawayExplorer.Core.Formats;

namespace RunawayExplorer.Core.Tests;

/// <summary>
/// Builders for byte-exact synthetic entries in each of the three scene-archive image formats, so the
/// decoders can be tested without game data. Every builder writes exactly what docs/formats describes.
/// </summary>
public static class SyntheticAssets
{
    public static ushort Rgb565(int r8, int g8, int b8) => (ushort)(((r8 >> 3) << 11) | ((g8 >> 2) << 5) | (b8 >> 3));

    /// <summary>A raster with a distinct vertical gradient per column, so the stride detector has something to lock onto.</summary>
    public static byte[] Raster(int width, int height, Func<int, int, ushort>? pixel = null)
    {
        pixel ??= (x, y) => Rgb565((x * 7) & 0xFF, (x * 13 + y) & 0xFF, (y * 3) & 0xFF);
        var data = new byte[width * height * 2];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan((y * width + x) * 2), pixel(x, y));
        return data;
    }

    /// <summary>An overlay from explicit row records.</summary>
    public static byte[] Overlay(IReadOnlyList<(int X, int Y, ushort[] Pixels)> rows)
    {
        using var ms = new MemoryStream();
        Span<byte> u16 = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(u16, (ushort)rows.Count); ms.Write(u16);
        foreach ((int x, int y, ushort[] px) in rows)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(u16, (ushort)x); ms.Write(u16);
            BinaryPrimitives.WriteUInt16LittleEndian(u16, (ushort)y); ms.Write(u16);
            BinaryPrimitives.WriteUInt16LittleEndian(u16, (ushort)px.Length); ms.Write(u16);
            foreach (ushort p in px) { BinaryPrimitives.WriteUInt16LittleEndian(u16, p); ms.Write(u16); }
        }
        return ms.ToArray();
    }

    /// <summary>One frame of a sprite: its segments, each an absolute screen run.</summary>
    public sealed record Frame(IReadOnlyList<(int X, int Y, ushort[] Pixels)> Segments);

    /// <summary>
    /// A sprite asset from frames. Each record's box is the frame's own extent (or the union of all
    /// frames when <paramref name="sharedBox"/>), which is how the game files are laid out.
    /// </summary>
    public static byte[] Sprite(IReadOnlyList<Frame> frames, bool sharedBox = true, int descriptorWidth = 0)
    {
        // Frame data first, to know the offsets.
        var frameBytes = new List<byte[]>();
        foreach (Frame f in frames)
        {
            using var ms = new MemoryStream();
            Span<byte> u16 = stackalloc byte[2];
            foreach ((int x, int y, ushort[] px) in f.Segments)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(u16, (ushort)x); ms.Write(u16);
                BinaryPrimitives.WriteUInt16LittleEndian(u16, (ushort)y); ms.Write(u16);
                ms.WriteByte((byte)(px.Length == 1 ? 0 : px.Length)); // count 0 means 1, exercise it
                foreach (ushort p in px) { BinaryPrimitives.WriteUInt16LittleEndian(u16, p); ms.Write(u16); }
            }
            frameBytes.Add(ms.ToArray());
        }

        (int x0, int y0, int x1, int y1) Box(Frame f)
        {
            int bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = 0, by1 = 0;
            foreach ((int x, int y, ushort[] px) in f.Segments)
            {
                bx0 = Math.Min(bx0, x); by0 = Math.Min(by0, y);
                bx1 = Math.Max(bx1, x + px.Length); by1 = Math.Max(by1, y + 1);
            }
            return f.Segments.Count == 0 ? (0, 0, 1, 1) : (bx0, by0, bx1, by1);
        }

        // The union of the non-empty frames. An empty frame has no extent of its own; in the game files
        // its record still carries a box, so here it gets the union (a harmless, in-bounds choice).
        (int ux0, int uy0, int ux1, int uy1) = (int.MaxValue, int.MaxValue, 0, 0);
        foreach (Frame f in frames)
        {
            if (f.Segments.Count == 0) continue;
            var b = Box(f);
            ux0 = Math.Min(ux0, b.x0); uy0 = Math.Min(uy0, b.y0); ux1 = Math.Max(ux1, b.x1); uy1 = Math.Max(uy1, b.y1);
        }
        if (ux0 == int.MaxValue) (ux0, uy0, ux1, uy1) = (0, 0, 1, 1);

        using var output = new MemoryStream();
        Span<byte> w = stackalloc byte[4];
        if (descriptorWidth > 0)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(w, 0); output.Write(w);
            BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)descriptorWidth); output.Write(w[..2]);
            BinaryPrimitives.WriteInt16LittleEndian(w, (short)-(descriptorWidth - 1)); output.Write(w[..2]);
            BinaryPrimitives.WriteUInt16LittleEndian(w, 600); output.Write(w[..2]);
            BinaryPrimitives.WriteUInt16LittleEndian(w, 0); output.Write(w[..2]);
            BinaryPrimitives.WriteUInt16LittleEndian(w, 0); output.Write(w[..2]);
        }

        int offset = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            var b = sharedBox || frames[i].Segments.Count == 0 ? (ux0, uy0, ux1, uy1) : Box(frames[i]);
            BinaryPrimitives.WriteUInt32LittleEndian(w, (uint)offset); output.Write(w);
            BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)b.Item1); output.Write(w[..2]);
            BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)(b.Item3 - b.Item1)); output.Write(w[..2]);
            BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)b.Item2); output.Write(w[..2]);
            BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)(b.Item4 - 1)); output.Write(w[..2]);
            BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)frames[i].Segments.Count); output.Write(w[..2]);
            offset += frameBytes[i].Length;
        }
        foreach (byte[] fb in frameBytes)
            output.Write(fb);
        return output.ToArray();
    }

    /// <summary>Reads back the BGRA pixel at (x, y) of a decoded image as (R, G, B, A).</summary>
    public static (byte R, byte G, byte B, byte A) Pixel(DecodedImage image, int x, int y)
    {
        int i = (y * image.Width + x) * 4;
        return (image.Pixels[i + 2], image.Pixels[i + 1], image.Pixels[i], image.Pixels[i + 3]);
    }
}
