namespace RunawayExplorer.Core.Formats;

/// <summary>
/// A 256-entry colour table for the 8-bit indexed images of Hollywood Monsters.
/// <para>
/// The game stores palettes as plain 6-bit VGA triples -- <c>{ u8 r, u8 g, u8 b }</c> with every component
/// in 0..63 -- and a scene archive rarely carries all 256 of them. Most scenes ship 176 colours (a 528-byte
/// entry) and borrow the remaining 80 from a shared block in <c>RESOURCE.000</c>: that top range is where
/// the characters live, which is why a sprite decoded against the scene palette alone comes out black.
/// A few scenes ship all 256 (a 768-byte entry) and need no tail.
/// </para>
/// </summary>
public sealed class IndexedPalette
{
    /// <summary>The number of colours a palette block can hold.</summary>
    public const int Colors = 256;

    /// <summary>A palette block is this many bytes per colour: <c>{ r, g, b }</c>.</summary>
    public const int BytesPerColor = 3;

    /// <summary>Full 256-colour blocks are this big.</summary>
    public const int FullBlockBytes = Colors * BytesPerColor;

    /// <summary>The smallest block accepted as a palette, so that small data entries are not mistaken for one.</summary>
    public const int MinBlockBytes = 96;

    /// <summary>The highest value a 6-bit VGA component can take.</summary>
    public const int MaxComponent = 63;

    private readonly byte[] _bgra;

    private IndexedPalette(byte[] bgra, int definedCount)
    {
        _bgra = bgra;
        DefinedCount = definedCount;
    }

    /// <summary>How many of the 256 slots were filled from an actual palette block; the rest are opaque black.</summary>
    public int DefinedCount { get; }

    /// <summary>The expanded table: 256 BGRA quadruples, straight alpha, always opaque.</summary>
    public ReadOnlySpan<byte> Bgra => _bgra;

    /// <summary>A neutral ramp, used when an install has no palette for a scene so images stay readable.</summary>
    public static IndexedPalette Grayscale { get; } = BuildGrayscale();

    /// <summary>True when <paramref name="data"/> has the shape of a palette block: whole 6-bit VGA triples.</summary>
    public static bool IsPaletteBlock(ReadOnlySpan<byte> data)
    {
        if (data.Length < MinBlockBytes || data.Length > FullBlockBytes || data.Length % BytesPerColor != 0)
            return false;

        foreach (byte b in data)
        {
            if (b > MaxComponent)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Builds a palette from a block of 6-bit VGA triples filling slots 0..n-1.
    /// <see langword="null"/> when <paramref name="data"/> is not a palette block.
    /// </summary>
    public static IndexedPalette? TryParse(ReadOnlySpan<byte> data)
    {
        if (!IsPaletteBlock(data))
            return null;

        int count = data.Length / BytesPerColor;
        var bgra = new byte[Colors * 4];
        for (int i = 0; i < count; i++)
            WriteColor(bgra, i, data[i * 3], data[i * 3 + 1], data[i * 3 + 2]);
        for (int i = count; i < Colors; i++)
            bgra[i * 4 + 3] = 255;
        return new IndexedPalette(bgra, count);
    }

    /// <summary>
    /// A copy of this palette with <paramref name="tail"/>'s colours moved to the top of the table, so a
    /// 176-colour scene block plus an 80-colour shared block fills all 256 slots.
    /// </summary>
    public IndexedPalette WithTail(IndexedPalette tail)
    {
        ArgumentNullException.ThrowIfNull(tail);
        if (tail.DefinedCount == 0 || tail.DefinedCount >= Colors)
            return this;

        int start = Colors - tail.DefinedCount;
        var bgra = (byte[])_bgra.Clone();
        Array.Copy(tail._bgra, 0, bgra, start * 4, tail.DefinedCount * 4);
        return new IndexedPalette(bgra, Math.Max(DefinedCount, Colors));
    }

    /// <summary>Writes the colour of <paramref name="index"/> as opaque BGRA at <paramref name="offset"/>.</summary>
    public void ToBgra(byte index, byte[] dst, int offset)
    {
        ArgumentNullException.ThrowIfNull(dst);
        int src = index * 4;
        dst[offset] = _bgra[src];
        dst[offset + 1] = _bgra[src + 1];
        dst[offset + 2] = _bgra[src + 2];
        dst[offset + 3] = 255;
    }

    /// <summary>Converts <paramref name="count"/> consecutive indices starting at <paramref name="src"/> into BGRA.</summary>
    public void CopyRow(ReadOnlySpan<byte> data, int src, byte[] dst, int dstOffset, int count)
    {
        for (int i = 0; i < count; i++)
            ToBgra(data[src + i], dst, dstOffset + i * 4);
    }

    /// <summary>Expands a 6-bit component to 8 bits so 63 maps to 255 rather than 252.</summary>
    private static byte Expand(byte v) => (byte)((v << 2) | (v >> 4));

    private static void WriteColor(byte[] bgra, int index, byte r, byte g, byte b)
    {
        int p = index * 4;
        bgra[p] = Expand(b);
        bgra[p + 1] = Expand(g);
        bgra[p + 2] = Expand(r);
        bgra[p + 3] = 255;
    }

    private static IndexedPalette BuildGrayscale()
    {
        var bgra = new byte[Colors * 4];
        for (int i = 0; i < Colors; i++)
        {
            bgra[i * 4] = (byte)i;
            bgra[i * 4 + 1] = (byte)i;
            bgra[i * 4 + 2] = (byte)i;
            bgra[i * 4 + 3] = 255;
        }
        return new IndexedPalette(bgra, Colors);
    }
}
