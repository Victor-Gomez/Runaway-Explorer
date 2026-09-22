using System.Buffers.Binary;

namespace RunawayExplorer.Core.Formats;

/// <summary>
/// Decoder for Runaway 3 sparse antialiased masks (walk-behind occluders, lighting, and depth layers).
/// Format:
/// <code>
/// u16 n (run count)
/// n × {
///     u16 x
///     u16 y
///     u8 flag (2 = solid run of 'count' pixels, 4 = antialiased run with 'count' alpha bytes)
///     u16 count
///     [u8 alpha[count] if flag == 4]
/// }
/// </code>
/// </summary>
public static class SparseMaskDecoder
{
    public static MaskInfo? Detect(ReadOnlySpan<byte> data, int? sceneWidth = null, int? sceneHeight = null)
    {
        if (data.Length < 9)
            return null;

        ushort n = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (n == 0 || n > 50_000)
            return null;

        int p = 2;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
        int runCount = 0;
        Span<bool> seenIds = stackalloc bool[256];
        int uniqueIds = 0;

        for (int i = 0; i < n; i++)
        {
            if (p + 7 > data.Length)
                return null;

            int x = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p));
            int y = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p + 2));
            byte flag = data[p + 4];
            int count = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p + 5));
            p += 7;

            if (flag == 4)
            {
                if (p + count > data.Length)
                    return null;

                for (int k = 0; k < count; k++)
                {
                    byte id = data[p + k];
                    if (!seenIds[id])
                    {
                        seenIds[id] = true;
                        uniqueIds++;
                    }
                }
                p += count;
            }
            else if (flag != 2)
            {
                return null;
            }

            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + count);
            maxY = Math.Max(maxY, y + 1);
            runCount++;
        }

        if (p != data.Length || runCount == 0)
            return null;

        // Sized to enclosing scene background if available and large enough, else default to screen bounds or bounding box
        int w = sceneWidth.HasValue && sceneWidth.Value >= maxX ? sceneWidth.Value : Math.Max(1280, maxX);
        int h = sceneHeight.HasValue && sceneHeight.Value >= maxY ? sceneHeight.Value : Math.Max(720, maxY);
        return new MaskInfo(w, h, runCount, Math.Max(1, uniqueIds));
    }

    public static DecodedImage Decode(ReadOnlySpan<byte> data, int width, int height, byte[]? idMap = null, int? colorSeed = null)
    {
        var palette = RleMaskDecoder.GeneratePalette();
        int colorIndex = colorSeed.HasValue && colorSeed.Value > 0 ? ((colorSeed.Value - 1) % 254) + 1 : 1;
        (byte baseB, byte baseG, byte baseR, _) = palette[colorIndex];
        var pixels = new byte[width * height * 4];

        if (data.Length < 2)
            return new DecodedImage(width, height, pixels);

        ushort n = BinaryPrimitives.ReadUInt16LittleEndian(data);
        int p = 2;

        for (int i = 0; i < n; i++)
        {
            if (p + 7 > data.Length)
                break;

            int x = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p));
            int y = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p + 2));
            byte flag = data[p + 4];
            int count = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p + 5));
            p += 7;

            if (flag == 2)
            {
                for (int k = 0; k < count; k++)
                {
                    int px = x + k;
                    if (px < width && y < height)
                    {
                        int dst = (y * width + px) * 4;
                        pixels[dst + 0] = baseB;
                        pixels[dst + 1] = baseG;
                        pixels[dst + 2] = baseR;
                        pixels[dst + 3] = 255;
                    }
                }
            }
            else if (flag == 4)
            {
                if (p + count > data.Length)
                    break;

                for (int k = 0; k < count; k++)
                {
                    int px = x + k;
                    if (px < width && y < height)
                    {
                        byte alpha = data[p + k];
                        int dst = (y * width + px) * 4;
                        if (alpha > pixels[dst + 3])
                        {
                            pixels[dst + 0] = baseB;
                            pixels[dst + 1] = baseG;
                            pixels[dst + 2] = baseR;
                            pixels[dst + 3] = alpha;
                        }
                    }
                }
                p += count;
            }
        }

        return new DecodedImage(width, height, pixels);
    }

    public static (DecodedImage Image, MaskInfo Info)? TryDecode(ReadOnlySpan<byte> data, byte[]? idMap = null, int? sceneWidth = null, int? sceneHeight = null, int? colorSeed = null)
    {
        if (Detect(data, sceneWidth, sceneHeight) is not { } info)
            return null;
        return (Decode(data, info.Width, info.Height, idMap, colorSeed), info);
    }
}
