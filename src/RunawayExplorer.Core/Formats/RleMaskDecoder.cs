using System.Buffers.Binary;

namespace RunawayExplorer.Core.Formats;

/// <summary>Result of <see cref="RleMaskDecoder.Detect"/>: geometry and statistics of an RLE scene mask.</summary>
public readonly record struct MaskInfo(int Width, int Height, int RunCount, int UniqueIdCount, int RunBytes = 3);

/// <summary>
/// Bitflags specifying which functional mask layers to decode or display.
/// </summary>
[Flags]
public enum MaskLayers
{
    None = 0,
    Walk = 1 << 0,
    Hotspot = 1 << 1,
    Depth = 1 << 2,
    Material = 1 << 3,
    Occluder = 1 << 4,
    All = Walk | Hotspot | Depth | Material | Occluder
}

/// <summary>
/// Indicates which attributes are populated in a scene's 1,536-byte attribute table.
/// </summary>
public readonly record struct MaskAttributePresence(bool HasWalk, bool HasHotspot, bool HasDepth, bool HasMaterial);

/// <summary>
/// Decoder for scene-archive RLE mask entries (e.g. entry 1 across 18 scene archives).
/// Stored as a stream of 3-byte ({ u8 id, u16 len }) or 4-byte ({ u16 id, u16 len }) records.
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
        if (data.Length < 6)
            return null;

        if (sceneWidth.HasValue && sceneWidth.Value > 0)
        {
            if (data.Length % 4 == 0 && DetectWithWidth(data, sceneWidth.Value, 4) is { } m4)
                return m4;
            if (data.Length % 3 == 0 && DetectWithWidth(data, sceneWidth.Value, 3) is { } matchW)
                return matchW;
        }

        if (data.Length % 4 == 0)
        {
            foreach (int w in CandidateWidths)
            {
                if (DetectWithWidth(data, w, 4) is { } info)
                    return info;
            }
        }

        if (data.Length % 3 == 0)
        {
            foreach (int w in CandidateWidths)
            {
                if (DetectWithWidth(data, w, 3) is { } info)
                    return info;
            }

            if (DetectContinuous(data, sceneWidth, sceneHeight) is { } continuousInfo)
                return continuousInfo;
        }

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
    public static MaskInfo? DetectWithWidth(ReadOnlySpan<byte> data, int width, int runBytes = 3)
    {
        if (data.Length < runBytes * 2 || data.Length % runBytes != 0 || width <= 0)
            return null;

        int pos = 0;
        int curRowPx = 0;
        int rowCount = 0;
        int runCount = 0;
        int uniqueIds = 0;
        Span<bool> seenIds = stackalloc bool[256];
        HashSet<int>? extraIds = null;

        while (pos + runBytes <= data.Length)
        {
            int id = runBytes == 4
                ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos, 2))
                : data[pos];
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos + (runBytes - 2), 2));
            pos += runBytes;
            runCount++;

            if (id < 256)
            {
                if (!seenIds[id])
                {
                    seenIds[id] = true;
                    uniqueIds++;
                }
            }
            else
            {
                extraIds ??= [];
                if (extraIds.Add(id))
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

        return new MaskInfo(width, rowCount, runCount, uniqueIds, runBytes);
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
    /// <summary>
    /// Checks which mask attributes (walkboxes, hotspots, depth bands, footstep materials)
    /// are present in the scene's 1,536-byte attribute table (Entry 2).
    /// </summary>
    public static MaskAttributePresence DetectAttributePresence(ReadOnlySpan<byte> table1536)
    {
        if (table1536.Length < 1536)
            return default;

        bool walk = false, hot = false, depth = false, mat = false;
        for (int i = 1; i < 256; i++)
        {
            if (table1536[0 * 256 + i] != 0) walk = true;
            if (table1536[1 * 256 + i] != 0) hot = true;
            if (table1536[3 * 256 + i] != 0) depth = true;
            if (table1536[5 * 256 + i] != 0) mat = true;
        }
        return new MaskAttributePresence(walk, hot, depth, mat);
    }

    /// <summary>
    /// Decodes the RLE mask into a 32-bit BGRA image using high-contrast deterministic colors.
    /// Optionally maps IDs through <paramref name="idMap"/> to unify object sub-zones and intersections,
    /// and filters visible layers using <paramref name="table1536"/> and <paramref name="layers"/>.
    /// </summary>
    public static DecodedImage Decode(
        ReadOnlySpan<byte> data,
        int width,
        int height,
        byte[]? idMap = null,
        byte[]? table1536 = null,
        MaskLayers layers = MaskLayers.All,
        bool transparentBackground = false)
    {
        var pixels = new byte[width * height * 4];
        if (layers == MaskLayers.None)
            return new DecodedImage(width, height, pixels);

        var defaultPalette = GeneratePalette();
        var walkPalette = GenerateWalkboxPalette();
        var depthPalette = GenerateDepthPalette();
        var matPalette = GenerateMaterialPalette();

        bool hasTable = table1536 is not null && table1536.Length >= 1536;
        bool hasTableAttributes = false;
        if (hasTable)
        {
            for (int i = 0; i < 1536; i++)
            {
                if (table1536![i] != 0)
                {
                    hasTableAttributes = true;
                    break;
                }
            }
        }

        bool showWalk = layers.HasFlag(MaskLayers.Walk);
        bool showHotspot = layers.HasFlag(MaskLayers.Hotspot);
        bool showDepth = layers.HasFlag(MaskLayers.Depth);
        bool showMat = layers.HasFlag(MaskLayers.Material);

        // Precompute a 256-entry lookup table for the active layer configuration
        var lut = new (byte B, byte G, byte R, byte A)[256];
        lut[0] = (hasTable || layers != MaskLayers.All || !showWalk || transparentBackground)
            ? ((byte)0, (byte)0, (byte)0, (byte)0)
            : defaultPalette[0];

        for (int id = 1; id < 256; id++)
        {
            if (!hasTableAttributes)
            {
                if (showWalk)
                {
                    byte displayId = idMap is not null ? idMap[id] : (byte)id;
                    lut[id] = defaultPalette[displayId];
                }
                else
                {
                    lut[id] = ((byte)0, (byte)0, (byte)0, (byte)0);
                }
                continue;
            }

            byte walk = table1536![0 * 256 + id];
            byte hot = table1536[1 * 256 + id];
            byte depth = table1536[3 * 256 + id];
            byte mat = table1536[5 * 256 + id];

            bool matchWalk = showWalk && walk != 0;
            bool matchHot = showHotspot && hot != 0;
            bool matchDepth = showDepth && depth != 0;
            bool matchMat = showMat && mat != 0;

            if (!matchWalk && !matchHot && !matchDepth && !matchMat)
            {
                if (walk == 0 && hot == 0 && depth == 0 && mat == 0 &&
                    showWalk && showHotspot && showDepth && showMat)
                {
                    byte displayId = idMap is not null ? idMap[id] : (byte)id;
                    lut[id] = defaultPalette[displayId];
                }
                else
                {
                    lut[id] = ((byte)0, (byte)0, (byte)0, (byte)0);
                }
                continue;
            }

            // Layer-specific styling
            if (matchHot)
            {
                lut[id] = defaultPalette[hot];
            }
            else if (matchWalk)
            {
                lut[id] = walkPalette[walk % walkPalette.Length];
            }
            else if (matchDepth)
            {
                lut[id] = depthPalette[Math.Clamp((int)depth, 0, depthPalette.Length - 1)];
            }
            else if (matchMat)
            {
                lut[id] = matPalette[Math.Clamp((int)mat, 0, matPalette.Length - 1)];
            }
            else
            {
                byte displayId = idMap is not null ? idMap[id] : (byte)id;
                lut[id] = defaultPalette[displayId];
            }
        }

        int runBytes = (data.Length % 4 == 0 && (data.Length % 3 != 0 || width >= 1920)) ? 4 : 3;
        int pos = 0;
        int pxIndex = 0;
        int totalPx = width * height;

        while (pos + runBytes <= data.Length && pxIndex < totalPx)
        {
            int id = runBytes == 4
                ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos, 2))
                : data[pos];
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos + (runBytes - 2), 2));
            pos += runBytes;

            int count = Math.Min((int)len, totalPx - pxIndex);
            (byte b, byte g, byte r, byte a) = lut[id % 256];

            if (a > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    int dst = (pxIndex + i) * 4;
                    pixels[dst + 0] = b;
                    pixels[dst + 1] = g;
                    pixels[dst + 2] = r;
                    pixels[dst + 3] = a;
                }
            }

            pxIndex += count;
        }

        return new DecodedImage(width, height, pixels);
    }

    /// <summary>Detect + decode in one step. Returns null if not a valid RLE mask.</summary>
    public static (DecodedImage Image, MaskInfo Info)? TryDecode(
        ReadOnlySpan<byte> data,
        byte[]? idMap = null,
        int? sceneWidth = null,
        int? sceneHeight = null,
        byte[]? table1536 = null,
        MaskLayers layers = MaskLayers.All)
    {
        if (Detect(data, sceneWidth, sceneHeight) is not { } info)
            return null;
        return (Decode(data, info.Width, info.Height, idMap, table1536, layers), info);
    }

    /// <summary>
    /// Generates a palette of vibrant greens, teals, and limestones tailored for walkboxes.
    /// </summary>
    public static (byte B, byte G, byte R, byte A)[] GenerateWalkboxPalette()
    {
        var palette = new (byte B, byte G, byte R, byte A)[256];
        palette[0] = ((byte)0, (byte)0, (byte)0, (byte)0);

        (byte r, byte g, byte b)[] colors =
        [
            (46, 204, 113),  // Emerald
            (26, 188, 156),  // Teal
            (52, 152, 219),  // Light Blue
            (130, 224, 170), // Mint
            (40, 180, 99),   // Jade
            (93, 173, 226),  // Sky Blue
            (125, 206, 160), // Pastel Green
            (22, 160, 133),  // Deep Teal
        ];

        for (int i = 1; i < 256; i++)
        {
            var (r, g, b) = colors[(i - 1) % colors.Length];
            palette[i] = (b, g, r, 255);
        }
        return palette;
    }

    /// <summary>
    /// Generates a continuous perspective rainbow gradient for depth scaling ladder values 1..24.
    /// </summary>
    public static (byte B, byte G, byte R, byte A)[] GenerateDepthPalette()
    {
        var palette = new (byte B, byte G, byte R, byte A)[256];
        palette[0] = ((byte)0, (byte)0, (byte)0, (byte)0);

        for (int d = 1; d < 256; d++)
        {
            float t = Math.Clamp((d - 1) / 23f, 0f, 1f);
            float hue = (1f - t) * 240f; // 240 (deep blue) down to 0 (foreground red)
            (byte r, byte g, byte b) = HsvToRgb(hue, 0.90f, 0.95f);
            palette[d] = (b, g, r, 255);
        }
        return palette;
    }

    /// <summary>
    /// Generates recognizable acoustic material tones for footstep surface types.
    /// </summary>
    public static (byte B, byte G, byte R, byte A)[] GenerateMaterialPalette()
    {
        var palette = new (byte B, byte G, byte R, byte A)[256];
        palette[0] = ((byte)0, (byte)0, (byte)0, (byte)0);

        palette[1] = (195, 180, 165, 255); // Stone / Concrete (slate blue-grey)
        palette[2] = (50, 130, 205, 255);  // Wood (warm amber oak)
        palette[3] = (55, 110, 150, 255);  // Dirt / Ground (earth ochre)
        palette[4] = (235, 215, 95, 255);  // Metal (metallic cyan/silver)
        palette[5] = (245, 140, 35, 255);  // Water (aquatic azure)

        for (int i = 6; i < 256; i++)
        {
            float hue = (i * 137.507764f) % 360f;
            (byte r, byte g, byte b) = HsvToRgb(hue, 0.75f, 0.85f);
            palette[i] = (b, g, r, 255);
        }
        return palette;
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

