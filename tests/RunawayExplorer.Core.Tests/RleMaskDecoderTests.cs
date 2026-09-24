using System.Buffers.Binary;
using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Formats;
using Xunit;

namespace RunawayExplorer.Core.Tests;

public class RleMaskDecoderTests
{
    private static byte[] CreateSyntheticMask(int width, int height)
    {
        using var ms = new MemoryStream();
        Span<byte> u16 = stackalloc byte[2];
        for (int y = 0; y < height; y++)
        {
            // Two runs per line: left half ID 1, right half ID 2
            ushort left = (ushort)(width / 2);
            ushort right = (ushort)(width - left);

            ms.WriteByte((byte)(1 + (y % 10)));
            BinaryPrimitives.WriteUInt16LittleEndian(u16, left);
            ms.Write(u16);

            ms.WriteByte((byte)(2 + (y % 10)));
            BinaryPrimitives.WriteUInt16LittleEndian(u16, right);
            ms.Write(u16);
        }
        return ms.ToArray();
    }

    [Fact]
    public void DetectsValidSyntheticMask()
    {
        byte[] data = CreateSyntheticMask(1024, 600);
        MaskInfo? info = RleMaskDecoder.Detect(data);

        Assert.NotNull(info);
        Assert.Equal(1024, info.Value.Width);
        Assert.Equal(600, info.Value.Height);
        Assert.Equal(1200, info.Value.RunCount);
        Assert.True(info.Value.UniqueIdCount >= 2);
    }

    [Fact]
    public void DecodesSyntheticMask_PixelsMatchPalette()
    {
        byte[] data = CreateSyntheticMask(1024, 600);
        var decoded = RleMaskDecoder.Decode(data, 1024, 600);

        Assert.Equal(1024, decoded.Width);
        Assert.Equal(600, decoded.Height);
        Assert.Equal(1024 * 600 * 4, decoded.Pixels.Length);

        var palette = RleMaskDecoder.GeneratePalette();
        // Row 0, pixel 0 should be ID 1
        var expected = palette[1];
        Assert.Equal(expected.B, decoded.Pixels[0]);
        Assert.Equal(expected.G, decoded.Pixels[1]);
        Assert.Equal(expected.R, decoded.Pixels[2]);
        Assert.Equal(255, decoded.Pixels[3]);

        // Row 0, pixel 512 should be ID 2
        int offset512 = 512 * 4;
        var expected2 = palette[2];
        Assert.Equal(expected2.B, decoded.Pixels[offset512 + 0]);
        Assert.Equal(expected2.G, decoded.Pixels[offset512 + 1]);
        Assert.Equal(expected2.R, decoded.Pixels[offset512 + 2]);
        Assert.Equal(255, decoded.Pixels[offset512 + 3]);
    }

    [Fact]
    public void ClassifiesSyntheticMaskAsMask()
    {
        byte[] data = CreateSyntheticMask(1024, 600);
        EntryClassification c = EntryClassifier.Classify(data);

        Assert.Equal(EntryKind.Mask, c.Kind);
        Assert.NotNull(c.Image);
        Assert.Equal(1024, c.Image.Width);
        Assert.Equal(600, c.Image.Height);
        Assert.True(c.Image.IsMask);
    }

    [Fact]
    public void RejectsMalformedStreams()
    {
        // Not multiple of 3
        Assert.Null(RleMaskDecoder.Detect(new byte[100]));

        // Too short
        Assert.Null(RleMaskDecoder.Detect(new byte[3]));

        // Run length 0
        byte[] zeroLen = [0x01, 0x00, 0x00, 0x02, 0x00, 0x04];
        Assert.Null(RleMaskDecoder.Detect(zeroLen));
    }

    [Fact]
    public void RealGameH38e01_ClassifiesAsMask_IfPresent()
    {
        string dir = @"F:\Games\Steam\steamapps\common\Runaway A Road Adventure\Resource";
        string h38Path = Path.Combine(dir, "RESOURCE.H38");
        if (!File.Exists(h38Path))
            return;

        using var fs = File.OpenRead(h38Path);
        var entries = SceneArchive.ReadEntries(fs);
        var e01 = entries.FirstOrDefault(e => e.Index == 1);
        Assert.Equal(20070, e01.Size);

        var data = new byte[e01.Size];
        fs.Position = e01.Offset;
        fs.ReadExactly(data);

        EntryClassification c = EntryClassifier.Classify(data);
        Assert.Equal(EntryKind.Mask, c.Kind);
        Assert.NotNull(c.Image);
        Assert.Equal(1024, c.Image.Width);
        Assert.Equal(600, c.Image.Height);
        Assert.True(c.Image.IsMask);
    }

    [Fact]
    public void ExtractObjectMapping_UsesAttribute1_WhenPopulated()
    {
        // Build a 1536-byte table: Attribute 1 (offset 256..511) has object IDs.
        var table = new byte[1536];

        // IDs 3, 6, 11 all belong to object 2 (like the H18 door example).
        table[256 + 3] = 2;
        table[256 + 6] = 2;
        table[256 + 11] = 2;
        // ID 5 belongs to object 5.
        table[256 + 5] = 5;

        byte[]? map = RleMaskDecoder.ExtractObjectMapping(table);

        Assert.NotNull(map);
        Assert.Equal(256, map.Length);
        Assert.Equal(2, map[3]);
        Assert.Equal(2, map[6]);
        Assert.Equal(2, map[11]);
        Assert.Equal(5, map[5]);
        // Unmapped IDs stay identity.
        Assert.Equal(0, map[0]);
        Assert.Equal(1, map[1]);
        Assert.Equal(4, map[4]);
    }

    [Fact]
    public void ExtractObjectMapping_FallsBackToAttribute5_WhenAttribute1IsAllZero()
    {
        // Attribute 1 (offset 256..511) all zero; Attribute 5 (offset 1280..1535) has mappings.
        var table = new byte[1536];

        // IDs 7, 8 map to object 5 (like the H10 carpet example).
        table[1280 + 7] = 5;
        table[1280 + 8] = 5;

        byte[]? map = RleMaskDecoder.ExtractObjectMapping(table);

        Assert.NotNull(map);
        Assert.Equal(5, map[7]);
        Assert.Equal(5, map[8]);
        Assert.Equal(1, map[1]); // identity for unmapped
    }

    [Fact]
    public void ExtractObjectMapping_RejectsShortTable()
    {
        byte[]? map = RleMaskDecoder.ExtractObjectMapping(new byte[100]);
        Assert.Null(map);
    }

    [Fact]
    public void Decode_WithIdMap_UnifiesColors()
    {
        // Synthetic mask: 4 pixels wide, 1 row. IDs: [3, 3, 6, 6]
        // Without idMap, IDs 3 and 6 produce different colors.
        // With idMap mapping both to object 2, all 4 pixels share the same color.
        byte[] data =
        [
            3, 0x02, 0x00,  // ID 3, length 2
            6, 0x02, 0x00,  // ID 6, length 2
        ];

        // Decode without idMap
        var noMap = RleMaskDecoder.Decode(data, 4, 1);
        // Pixel 0 (ID 3) vs pixel 2 (ID 6) should differ.
        Assert.NotEqual(noMap.Pixels[0], noMap.Pixels[8]);  // B channels differ

        // Decode with idMap: both 3 and 6 → 2
        var idMap = new byte[256];
        for (int i = 0; i < 256; i++) idMap[i] = (byte)i;
        idMap[3] = 2;
        idMap[6] = 2;

        var withMap = RleMaskDecoder.Decode(data, 4, 1, idMap);
        // All 4 pixels should have identical BGRA.
        for (int px = 1; px < 4; px++)
        {
            Assert.Equal(withMap.Pixels[0], withMap.Pixels[px * 4 + 0]); // B
            Assert.Equal(withMap.Pixels[1], withMap.Pixels[px * 4 + 1]); // G
            Assert.Equal(withMap.Pixels[2], withMap.Pixels[px * 4 + 2]); // R
            Assert.Equal(withMap.Pixels[3], withMap.Pixels[px * 4 + 3]); // A
        }

        // And the color should be palette[2].
        var palette = RleMaskDecoder.GeneratePalette();
        Assert.Equal(palette[2].B, withMap.Pixels[0]);
        Assert.Equal(palette[2].G, withMap.Pixels[1]);
        Assert.Equal(palette[2].R, withMap.Pixels[2]);
    }

    [Fact]
    public void DetectAttributePresence_IdentifiesAttributes()
    {
        var table = new byte[1536];
        // Table 0: walkbox
        table[0 * 256 + 1] = 5;
        // Table 1: hotspot
        table[1 * 256 + 2] = 8;
        // Table 3: depth
        table[3 * 256 + 3] = 12;
        // Table 5: footstep material
        table[5 * 256 + 4] = 2;

        var presence = RleMaskDecoder.DetectAttributePresence(table);
        Assert.True(presence.HasWalk);
        Assert.True(presence.HasHotspot);
        Assert.True(presence.HasDepth);
        Assert.True(presence.HasMaterial);
    }

    [Fact]
    public void DecodesWithMaskLayers_FiltersLayersAccurately()
    {
        // 4 pixels:
        // ID 1: walkbox 1, no hotspot
        // ID 2: hotspot 2, no walkbox
        // ID 3: depth 10, no walkbox, no hotspot
        // ID 4: material 2 (wood), no others
        byte[] data =
        [
            1, 0x01, 0x00,
            2, 0x01, 0x00,
            3, 0x01, 0x00,
            4, 0x01, 0x00,
        ];

        var table = new byte[1536];
        table[0 * 256 + 1] = 1; // ID 1 is Walkbox 1
        table[1 * 256 + 2] = 2; // ID 2 is Hotspot 2
        table[3 * 256 + 3] = 10; // ID 3 is Depth 10
        table[5 * 256 + 4] = 2;  // ID 4 is Material 2

        // Test 1: MaskLayers.None -> all transparent (alpha = 0)
        var noneImg = RleMaskDecoder.Decode(data, 4, 1, table1536: table, layers: MaskLayers.None);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(0, noneImg.Pixels[i * 4 + 3]);
        }

        // Test 2: MaskLayers.Walk only -> only pixel 0 is opaque
        var walkImg = RleMaskDecoder.Decode(data, 4, 1, table1536: table, layers: MaskLayers.Walk);
        Assert.Equal(255, walkImg.Pixels[0 * 4 + 3]);
        Assert.Equal(0, walkImg.Pixels[1 * 4 + 3]);
        Assert.Equal(0, walkImg.Pixels[2 * 4 + 3]);
        Assert.Equal(0, walkImg.Pixels[3 * 4 + 3]);

        // Test 3: MaskLayers.Hotspot only -> only pixel 1 is opaque
        var hotImg = RleMaskDecoder.Decode(data, 4, 1, table1536: table, layers: MaskLayers.Hotspot);
        Assert.Equal(0, hotImg.Pixels[0 * 4 + 3]);
        Assert.Equal(255, hotImg.Pixels[1 * 4 + 3]);
        Assert.Equal(0, hotImg.Pixels[2 * 4 + 3]);
        Assert.Equal(0, hotImg.Pixels[3 * 4 + 3]);

        // Test 4: Walk | Hotspot -> pixels 0 and 1 are opaque, pixels 2 and 3 are transparent
        var comboImg = RleMaskDecoder.Decode(data, 4, 1, table1536: table, layers: MaskLayers.Walk | MaskLayers.Hotspot);
        Assert.Equal(255, comboImg.Pixels[0 * 4 + 3]);
        Assert.Equal(255, comboImg.Pixels[1 * 4 + 3]);
        Assert.Equal(0, comboImg.Pixels[2 * 4 + 3]);
        Assert.Equal(0, comboImg.Pixels[3 * 4 + 3]);

        // Test 5: All -> all 4 pixels are opaque
        var allImg = RleMaskDecoder.Decode(data, 4, 1, table1536: table, layers: MaskLayers.All);
        Assert.Equal(255, allImg.Pixels[0 * 4 + 3]);
        Assert.Equal(255, allImg.Pixels[1 * 4 + 3]);
        Assert.Equal(255, allImg.Pixels[2 * 4 + 3]);
        Assert.Equal(255, allImg.Pixels[3 * 4 + 3]);
    }

    [Fact]
    public void Decode_WithoutAttributeTable_TogglesWalkMaskOff()
    {
        // 4 pixels: ID 1 (run of 4)
        byte[] data = [1, 4, 0];

        // Walk on -> pixels are opaque
        var walkOn = RleMaskDecoder.Decode(data, 4, 1, layers: MaskLayers.Walk, transparentBackground: true);
        Assert.Equal(255, walkOn.Pixels[0 * 4 + 3]);

        // Walk off -> pixels are transparent
        var walkOff = RleMaskDecoder.Decode(data, 4, 1, layers: MaskLayers.None, transparentBackground: true);
        Assert.Equal(0, walkOff.Pixels[0 * 4 + 3]);

        // If layers has only Hotspot (Walk toggled off) -> pixels are transparent
        var hotspotOnly = RleMaskDecoder.Decode(data, 4, 1, layers: MaskLayers.Hotspot, transparentBackground: true);
        Assert.Equal(0, hotspotOnly.Pixels[0 * 4 + 3]);
    }
}
