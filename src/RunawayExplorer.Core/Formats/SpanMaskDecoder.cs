using System.Buffers.Binary;
using RunawayExplorer.Core.FileSystem;

namespace RunawayExplorer.Core.Formats;

/// <summary>
/// Decodes 6-byte span-list polygon masks in Runaway 1.
/// Stored as:
/// <code>
/// u16 count
/// { u16 x, u16 y, u16 width }[count]
/// </code>
/// Each record defines a horizontal pixel span [x, x + width) on scanline y.
/// </summary>
public static class SpanMaskDecoder
{
    public static bool IsSpanMask(ReadOnlySpan<byte> data) => Detect(data) is not null;

    public static ImageInfo? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || (data.Length - 2) % 6 != 0)
            return null;

        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (count == 0 || 2 + count * 6 != data.Length)
            return null;

        int minX = int.MaxValue, maxX = 0;
        int minY = int.MaxValue, maxY = 0;

        for (int i = 0; i < count; i++)
        {
            int p = 2 + i * 6;
            ushort x = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p));
            ushort y = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p + 2));
            ushort w = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p + 4));

            if (w == 0 || w > 3000 || x > 4000 || y > 3000)
                return null;

            int endX = x + w;
            if (x < minX) minX = x;
            if (endX > maxX) maxX = endX;
            if (y < minY) minY = y;
            if (y + 1 > maxY) maxY = y + 1;
        }

        if (minX >= maxX || minY >= maxY)
            return null;

        int width = maxX - minX;
        int height = maxY - minY;

        return new ImageInfo
        {
            Width = width,
            Height = height,
            X = minX,
            Y = minY,
            IsMask = true,
            Sharpness = 99,
        };
    }

    public static DecodedImage Decode(ReadOnlySpan<byte> data, byte r = 0x00, byte g = 0xC8, byte b = 0xFF, byte a = 0xD8)
    {
        ImageInfo info = Detect(data) ?? throw new InvalidOperationException("Invalid span mask data.");

        int width = info.Width;
        int height = info.Height;
        int originX = info.X;
        int originY = info.Y;

        byte[] pixels = new byte[width * height * 4];
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(data);

        for (int i = 0; i < count; i++)
        {
            int p = 2 + i * 6;
            ushort sx = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p));
            ushort sy = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p + 2));
            ushort sw = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p + 4));

            int localY = sy - originY;
            if (localY < 0 || localY >= height)
                continue;

            int rowOffset = localY * width * 4;
            int startX = sx - originX;
            int endX = Math.Min(width, startX + sw);

            for (int lx = Math.Max(0, startX); lx < endX; lx++)
            {
                int pxOff = rowOffset + lx * 4;
                pixels[pxOff + 0] = b;
                pixels[pxOff + 1] = g;
                pixels[pxOff + 2] = r;
                pixels[pxOff + 3] = a;
            }
        }

        return new DecodedImage(width, height, pixels);
    }
}
