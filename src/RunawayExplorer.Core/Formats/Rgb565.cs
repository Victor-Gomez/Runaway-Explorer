using System.Buffers.Binary;

namespace RunawayExplorer.Core.Formats;

/// <summary>
/// The one pixel format every colour image in Runaway uses: little-endian <c>u16</c> per pixel,
/// <c>RRRRRGGG GGGBBBBB</c>, red in the top 5 bits. Expanded to 8 bits by shifting, the same way the
/// game does (no rounding): <c>r = (p &gt;&gt; 11) &lt;&lt; 3</c>.
/// </summary>
public static class Rgb565
{
    /// <summary>The game's screen. Overlay and sprite coordinates are positions on it.</summary>
    public const int ScreenWidth = 1024;

    /// <inheritdoc cref="ScreenWidth"/>
    public const int ScreenHeight = 600;

    /// <summary>Writes one RGB565 pixel as opaque BGRA at <paramref name="dst"/>[<paramref name="offset"/>].</summary>
    public static void ToBgra(ushort p, byte[] dst, int offset)
    {
        dst[offset] = (byte)((p & 0x1F) << 3);
        dst[offset + 1] = (byte)(((p >> 5) & 0x3F) << 2);
        dst[offset + 2] = (byte)((p >> 11) << 3);
        dst[offset + 3] = 255;
    }

    /// <summary>Reads the little-endian pixel at byte offset <paramref name="offset"/>.</summary>
    public static ushort Read(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));

    /// <summary>Converts <paramref name="count"/> consecutive pixels starting at byte offset <paramref name="src"/> into BGRA.</summary>
    public static void CopyRow(ReadOnlySpan<byte> data, int src, byte[] dst, int dstOffset, int count)
    {
        for (int i = 0; i < count; i++)
            ToBgra(Read(data, src + i * 2), dst, dstOffset + i * 4);
    }

    /// <summary>The 6-bit green channel of the pixel at byte offset <paramref name="offset"/>.</summary>
    public static int Green(ReadOnlySpan<byte> data, int offset) => (Read(data, offset) >> 5) & 0x3F;
}
