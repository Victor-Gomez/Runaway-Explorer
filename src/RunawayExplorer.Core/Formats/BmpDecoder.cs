using System.Buffers.Binary;

namespace RunawayExplorer.Core.Formats;

/// <summary>
/// Windows BMP, which is what <em>Runaway 3</em> keeps its interface art in: cursors, panel widgets and
/// full-screen menu artwork all sit in <c>RESOURCE.000</c> as whole <c>BM</c> files rather than as the raw
/// RGB565 rasters the earlier games use.
/// <para>
/// Only what the game actually ships is decoded: a 40-byte <c>BITMAPINFOHEADER</c>, uncompressed
/// (<c>BI_RGB</c>), 24 or 32 bits per pixel. Rows are padded to four bytes and stored bottom-up unless the
/// height is negative. A 32-bit BMP has no alpha channel by definition, but these carry one in the fourth
/// byte, so it is honoured -- except when every pixel reads zero, which is the "unused" convention and
/// would otherwise decode the image as fully transparent.
/// </para>
/// </summary>
public static class BmpDecoder
{
    /// <summary>The file header plus a <c>BITMAPINFOHEADER</c>.</summary>
    public const int MinHeaderSize = 54;

    /// <summary>
    /// Reads the size out of a BMP header, and the length the whole file claims. Only the header has to
    /// be present, which is what lets the scan classify a multi-megabyte entry from its first 54 bytes;
    /// whether the pixels are all there is <see cref="IsBmp"/>'s question.
    /// </summary>
    public static bool TryReadHeader(ReadOnlySpan<byte> data, out int width, out int height, out long fileSize)
    {
        width = height = 0;
        fileSize = 0;
        if (data.Length < MinHeaderSize || data[0] != 'B' || data[1] != 'M')
            return false;

        uint claimed = BinaryPrimitives.ReadUInt32LittleEndian(data[2..]);
        uint pixelOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[10..]);
        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data[14..]);
        int w = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
        int h = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
        ushort bpp = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]);
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(data[30..]);

        if (headerSize < 40 || compression != 0 || (bpp != 24 && bpp != 32))
            return false;
        if (w <= 0 || w > 1 << 15 || h == 0 || Math.Abs((long)h) > 1 << 15)
            return false;
        if (pixelOffset < MinHeaderSize)
            return false;

        long stride = ((long)w * bpp / 8 + 3) & ~3L;
        if (claimed < pixelOffset + stride * Math.Abs((long)h))
            return false;

        width = w;
        height = Math.Abs(h);
        fileSize = claimed;
        return true;
    }

    /// <inheritdoc cref="TryReadHeader(ReadOnlySpan{byte}, out int, out int, out long)"/>
    public static bool TryReadHeader(ReadOnlySpan<byte> data, out int width, out int height) =>
        TryReadHeader(data, out width, out height, out _);

    /// <summary>Whether the bytes are a whole BMP this decoder can read, pixels included.</summary>
    public static bool IsBmp(ReadOnlySpan<byte> data) =>
        TryReadHeader(data, out _, out _, out long fileSize) && fileSize <= data.Length;

    /// <summary>Decodes a BMP, or returns null if it is not one this decoder reads.</summary>
    public static DecodedImage? Decode(ReadOnlySpan<byte> data)
    {
        if (!TryReadHeader(data, out int width, out int height))
            return null;

        uint pixelOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[10..]);
        ushort bpp = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]);
        bool bottomUp = BinaryPrimitives.ReadInt32LittleEndian(data[22..]) > 0;
        int bytesPerPixel = bpp / 8;
        int stride = (width * bytesPerPixel + 3) & ~3;

        // A 32-bit BMP whose fourth byte is zero everywhere is storing padding, not transparency.
        bool hasAlpha = false;
        if (bpp == 32)
        {
            for (int y = 0; y < height && !hasAlpha; y++)
            {
                int row = (int)pixelOffset + y * stride;
                for (int x = 3; x < width * 4; x += 4)
                {
                    if (data[row + x] != 0)
                    {
                        hasAlpha = true;
                        break;
                    }
                }
            }
        }

        var image = DecodedImage.Transparent(width, height);
        for (int y = 0; y < height; y++)
        {
            int src = (int)pixelOffset + (bottomUp ? height - 1 - y : y) * stride;
            int dst = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int s = src + x * bytesPerPixel;
                int d = dst + x * 4;
                image.Pixels[d] = data[s];
                image.Pixels[d + 1] = data[s + 1];
                image.Pixels[d + 2] = data[s + 2];
                image.Pixels[d + 3] = bpp == 32 && hasAlpha ? data[s + 3] : (byte)255;
            }
        }

        return image;
    }
}
