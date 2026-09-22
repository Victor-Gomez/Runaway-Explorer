using System.Buffers.Binary;

namespace RunawayExplorer.Core.Formats;

/// <summary>Result of <see cref="RleMaskDecoder.Detect"/>: geometry and statistics of an RLE scene mask.</summary>
public readonly record struct MaskInfo(int Width, int Height, int RunCount, int UniqueIdCount);

/// <summary>
/// Decoder for scene-archive RLE mask entries (e.g. entry 1 across 18 scene archives).
/// Stored as a stream of 3-byte records: { u8 id, u16 length_little_endian }.
/// Runs are scanline-bounded and sum to exactly the scene width per row.
/// </summary>
public static class RleMaskDecoder
{
    private static readonly int[] CandidateWidths =
    [
        1024, 1224, 1236, 1280, 1372, 1380, 1444, 1468, 1508, 1560, 1564, 1584, 1600, 1612, 1624, 1648, 1650, 1688, 1740, 1784, 1824, 1828, 1852, 1920, 1924, 1988, 2048, 2592, 2616, 2740, 3288
    ];

    /// <summary>
    /// Checks whether the data is a valid scanline-bounded or continuous RLE scene mask.
    /// </summary>
    public static MaskInfo? Detect(ReadOnlySpan<byte> data, int? sceneWidth = null, int? sceneHeight = null)
    {
        if (data.Length < 6 || data.Length % 3 != 0)
            return null;

        if (sceneWidth.HasValue && sceneWidth.Value > 0)
        {
            if (DetectWithWidth(data, sceneWidth.Value) is { } matchW)
                return matchW;
        }

        foreach (int w in CandidateWidths)
        {
            if (DetectWithWidth(data, w) is { } info)
                return info;
        }

        if (DetectContinuous(data, sceneWidth, sceneHeight) is { } continuousInfo)
            return continuousInfo;

        return null;
    }

    /// <summary>
    /// Tests whether the RLE stream represents a continuous mask (as in Runaway 3) where runs wrap across scanlines.
    /// </summary>
    public static MaskInfo? DetectContinuous(ReadOnlySpan<byte> data, int? sceneWidth = null, int? sceneHeight = null)
    {
        if (data.Length < 6 || data.Length % 3 != 0)
            return null;

        long totalPx = 0;
        int runCount = 0;
        Span<bool> seenIds = stackalloc bool[256];
        int uniqueIds = 0;

        for (int p = 0; p + 3 <= data.Length; p += 3)
        {
            byte id = data[p];
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p + 1, 2));
            if (len == 0)
                return null;

            totalPx += len;
            runCount++;

            if (!seenIds[id])
            {
                seenIds[id] = true;
                uniqueIds++;
            }
        }

        if (uniqueIds < 2)
            return null;

        // If enclosing scene background dimensions are known and match totalPx, that is the exact geometry.
        if (sceneWidth.HasValue && sceneHeight.HasValue && (long)sceneWidth.Value * sceneHeight.Value == totalPx)
        {
            return new MaskInfo(sceneWidth.Value, sceneHeight.Value, runCount, uniqueIds);
        }

        // Standard screen width of 1280 with standard or multi-screen heights (1280x720, 1280x960, 1280x1080, 1280x1352, 1280x1440):
        if (totalPx % 1280 == 0)
        {
            int h = (int)(totalPx / 1280);
            if (h is 720 or 960 or 1080 or 1352 or 1440 || (h >= 600 && h <= 2160))
                return new MaskInfo(1280, h, runCount, uniqueIds);
        }

        // Standard screen height of 720 with widescreen or panoramic widths:
        if (totalPx % 720 == 0)
        {
            int w = (int)(totalPx / 720);
            if (w >= 1000 && w <= 2500)
                return new MaskInfo(w, 720, runCount, uniqueIds);
        }

        // Known multi-screen and custom scene aspect ratios:
        foreach (int w in new[] { 1852, 1508, 1784, 1624, 1380, 1024 })
        {
            if (totalPx % w == 0)
            {
                int h = (int)(totalPx / w);
                if (h >= 600 && h <= 2160)
                    return new MaskInfo(w, h, runCount, uniqueIds);
            }
        }

        // Fallback for standard 600-height scenes:
        if (totalPx % 600 == 0)
        {
            int w = (int)(totalPx / 600);
            if (w >= 1000 && w <= 4000)
                return new MaskInfo(w, 600, runCount, uniqueIds);
        }

        return null;
    }

    /// <summary>
    /// Tests whether the RLE stream partitions into exact rows of <paramref name="width"/> pixels.
    /// </summary>
    public static MaskInfo? DetectWithWidth(ReadOnlySpan<byte> data, int width)
    {
        if (data.Length < 6 || data.Length % 3 != 0 || width <= 0)
            return null;

        int pos = 0;
        int curRowPx = 0;
        int rowCount = 0;
        int runCount = 0;
        Span<bool> seenIds = stackalloc bool[256];
        int uniqueIds = 0;

        while (pos + 3 <= data.Length)
        {
            byte id = data[pos];
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos + 1, 2));
            pos += 3;
            runCount++;

            if (!seenIds[id])
            {
                seenIds[id] = true;
                uniqueIds++;
            }

            if (len == 0 || len > width)
                return null;

            curRowPx += len;
            if (curRowPx == width)
            {
                curRowPx = 0;
                rowCount++;
            }
            else if (curRowPx > width)
            {
                return null;
            }
        }

        if (curRowPx != 0 || rowCount < 10 || uniqueIds < 2)
            return null;

        return new MaskInfo(width, rowCount, runCount, uniqueIds);
    }

    /// <summary>
    /// Extracts an object ID mapping from the scene archive's 1536-byte attribute table (Entry 2).
    /// Returns a 256-byte array where map[id] is the canonical object ID, grouping split sub-zones
    /// and intersection slices into their parent object. Returns null if the table is invalid.
    /// </summary>
    public static byte[]? ExtractObjectMapping(ReadOnlySpan<byte> table1536)
    {
        if (table1536.Length < 1536)
            return null;

        // Check Attribute 1 (offset 256..511): primary object/entity ID.
        int nonZeroA1 = 0;
        for (int i = 1; i < 256; i++)
        {
            if (table1536[256 + i] != 0)
                nonZeroA1++;
        }

        var map = new byte[256];
        for (int i = 0; i < 256; i++)
            map[i] = (byte)i;

        if (nonZeroA1 > 0)
        {
            // Use Attribute 1 when populated.
            for (int i = 1; i < 256; i++)
            {
                byte a1 = table1536[256 + i];
                if (a1 != 0)
                    map[i] = a1;
            }
        }
        else
        {
            // Fallback to Attribute 5 (offset 1280..1535) for scenes where Attribute 1 is 0 (e.g. H10, H23, H29, H42).
            for (int i = 1; i < 256; i++)
            {
                byte a5 = table1536[1280 + i];
                if (a5 != 0)
                    map[i] = a5;
            }
        }

        return map;
    }

    /// <summary>
    /// Decodes the RLE mask into a 32-bit BGRA image using high-contrast deterministic colors.
    /// Optionally maps IDs through <paramref name="idMap"/> to unify object sub-zones and intersections.
    /// </summary>
    public static DecodedImage Decode(ReadOnlySpan<byte> data, int width, int height, byte[]? idMap = null)
    {
        var palette = GeneratePalette();
        var pixels = new byte[width * height * 4];

        int pos = 0;
        int pxIndex = 0;
        int totalPx = width * height;

        while (pos + 3 <= data.Length && pxIndex < totalPx)
        {
            byte id = data[pos];
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos + 1, 2));
            pos += 3;

            byte displayId = idMap is not null ? idMap[id] : id;
            int count = Math.Min((int)len, totalPx - pxIndex);
            (byte b, byte g, byte r, byte a) = palette[displayId];

            for (int i = 0; i < count; i++)
            {
                int dst = (pxIndex + i) * 4;
                pixels[dst + 0] = b;
                pixels[dst + 1] = g;
                pixels[dst + 2] = r;
                pixels[dst + 3] = a;
            }

            pxIndex += count;
        }

        return new DecodedImage(width, height, pixels);
    }

    /// <summary>Detect + decode in one step. Returns null if not a valid RLE mask.</summary>
    public static (DecodedImage Image, MaskInfo Info)? TryDecode(ReadOnlySpan<byte> data, byte[]? idMap = null, int? sceneWidth = null, int? sceneHeight = null)
    {
        if (Detect(data, sceneWidth, sceneHeight) is not { } info)
            return null;
        return (Decode(data, info.Width, info.Height, idMap), info);
    }

    /// <summary>
    /// Generates a deterministic, high-contrast palette for all 256 possible zone IDs.
    /// Uses golden angle hue distribution so adjacent IDs are visually distinct.
    /// </summary>
    public static (byte B, byte G, byte R, byte A)[] GeneratePalette()
    {
        var palette = new (byte B, byte G, byte R, byte A)[256];
        // ID 0: black background / empty
        palette[0] = (20, 20, 20, 255);

        for (int id = 1; id < 256; id++)
        {
            // Golden angle: 137.507764 degrees
            float hue = (id * 137.507764f) % 360f;
            float sat = (id % 2 == 0) ? 0.80f : 0.95f;
            float val = (id % 3 == 0) ? 0.85f : 1.0f;
            (byte r, byte g, byte b) = HsvToRgb(hue, sat, val);
            palette[id] = (b, g, r, 255);
        }

        return palette;
    }

    private static (byte R, byte G, byte B) HsvToRgb(float hue, float sat, float val)
    {
        float c = val * sat;
        float x = c * (1 - Math.Abs((hue / 60f) % 2 - 1));
        float m = val - c;

        float r1, g1, b1;
        if (hue < 60f) { r1 = c; g1 = x; b1 = 0; }
        else if (hue < 120f) { r1 = x; g1 = c; b1 = 0; }
        else if (hue < 180f) { r1 = 0; g1 = c; b1 = x; }
        else if (hue < 240f) { r1 = 0; g1 = x; b1 = c; }
        else if (hue < 300f) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        return (
            (byte)Math.Round((r1 + m) * 255),
            (byte)Math.Round((g1 + m) * 255),
            (byte)Math.Round((b1 + m) * 255)
        );
    }
}
