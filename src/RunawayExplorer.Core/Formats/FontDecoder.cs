using System.Buffers.Binary;

namespace RunawayExplorer.Core.Formats;

/// <summary>
/// Runaway 1's outlined fonts, which live in <c>RESOURCE.000</c> as a pair of slots: a glyph bitmap
/// and, in the slot immediately after it, a glyph table. See <c>docs/formats/global-data.md</c>.
/// <para>
/// The bitmap is one byte per pixel, <c>Width</c> bytes per row, <c>Height</c> rows, with no run-length
/// encoding and no scanline markers. A byte in <c>0x00..0x10</c> is an opaque shade on a 17-step ramp
/// from the outline colour (<c>0x00</c>) to the fill colour (<c>0x10</c>); <c>0x11</c> and <c>0x12</c>
/// are both transparent. The glyphs are outlined, not antialiased, so reading the byte as alpha throws
/// the outline away.
/// </para>
/// </summary>
public static class FontDecoder
{
    /// <summary>Size of one glyph record: <c>{ u16 Offset, u8 Top, u8 Height, u8 Width }</c>.</summary>
    public const int RecordSize = 5;

    /// <summary>The last opaque shade. <c>0x00</c> is pure outline, <see cref="FillShade"/> pure fill.</summary>
    public const byte FillShade = 0x10;

    /// <summary>One glyph's placement in the bitmap and on the line.</summary>
    /// <param name="Offset">Byte offset of the glyph's pixels in the bitmap.</param>
    /// <param name="Top">Blank rows above the glyph on its line.</param>
    public readonly record struct Glyph(int Offset, int Top, int Height, int Width);

    /// <summary>Reads a glyph table. The caller has already checked the length is a multiple of <see cref="RecordSize"/>.</summary>
    public static Glyph[] ReadGlyphTable(ReadOnlySpan<byte> table)
    {
        var glyphs = new Glyph[table.Length / RecordSize];
        for (int i = 0; i < glyphs.Length; i++)
        {
            ReadOnlySpan<byte> r = table.Slice(i * RecordSize, RecordSize);
            glyphs[i] = new Glyph(BinaryPrimitives.ReadUInt16LittleEndian(r), r[2], r[3], r[4]);
        }
        return glyphs;
    }

    /// <summary>
    /// Tells whether <paramref name="table"/> is the glyph table for a bitmap of
    /// <paramref name="bitmapSize"/> bytes. The records tile the bitmap exactly -- each one starts where
    /// the previous ended and the last ends on the bitmap's final byte -- which is the format check, and
    /// nothing else in the archive has been seen to satisfy it by accident.
    /// </summary>
    public static bool IsGlyphTable(ReadOnlySpan<byte> table, long bitmapSize)
    {
        if (table.Length == 0 || table.Length % RecordSize != 0 || bitmapSize <= 0)
            return false;

        long expected = 0;
        foreach (Glyph g in ReadGlyphTable(table))
        {
            if (g.Width == 0 || g.Height == 0 || g.Offset != expected)
                return false;
            expected += (long)g.Width * g.Height;
        }

        return expected == bitmapSize;
    }

    /// <summary>The line height the table implies: the tallest <c>Top + Height</c>.</summary>
    public static int LineHeight(IReadOnlyList<Glyph> glyphs)
    {
        int h = 0;
        foreach (Glyph g in glyphs)
            h = Math.Max(h, g.Top + g.Height);
        return h;
    }

    /// <summary>
    /// Renders every glyph of a font onto one sheet, wrapping at <paramref name="maxWidth"/> pixels, each
    /// glyph sitting on its line at its own <c>Top</c>. Fill is drawn white, outline black, the shades in
    /// between interpolated; everything else is transparent.
    /// </summary>
    public static DecodedImage Decode(ReadOnlySpan<byte> bitmap, IReadOnlyList<Glyph> glyphs, int maxWidth = 640)
    {
        int lineHeight = LineHeight(glyphs);
        if (glyphs.Count == 0 || lineHeight == 0)
            return DecodedImage.Transparent(0, 0);

        // Lay the glyphs out first so the sheet can be sized to what they actually need.
        var placed = new (int X, int Y, Glyph G)[glyphs.Count];
        int x = 0, y = 0, used = 0;
        for (int i = 0; i < glyphs.Count; i++)
        {
            Glyph g = glyphs[i];
            if (x > 0 && x + g.Width > maxWidth)
            {
                x = 0;
                y += lineHeight + 1;
            }
            placed[i] = (x, y, g);
            x += g.Width + 1;
            used = Math.Max(used, x);
        }

        var image = DecodedImage.Transparent(Math.Max(1, Math.Min(used, maxWidth)), y + lineHeight);
        foreach ((int px, int py, Glyph g) in placed)
        {
            for (int row = 0; row < g.Height; row++)
            {
                int dy = py + g.Top + row;
                if (dy < 0 || dy >= image.Height)
                    continue;

                for (int col = 0; col < g.Width; col++)
                {
                    int src = g.Offset + row * g.Width + col;
                    if (src >= bitmap.Length)
                        continue;

                    byte shade = bitmap[src];
                    if (shade > FillShade)
                        continue;

                    int dx = px + col;
                    if (dx < 0 || dx >= image.Width)
                        continue;

                    byte v = (byte)(shade * 255 / FillShade);
                    int di = (dy * image.Width + dx) * 4;
                    image.Pixels[di] = v;
                    image.Pixels[di + 1] = v;
                    image.Pixels[di + 2] = v;
                    image.Pixels[di + 3] = 255;
                }
            }
        }

        return image;
    }
}
