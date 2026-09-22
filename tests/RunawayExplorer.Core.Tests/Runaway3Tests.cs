using System.Buffers.Binary;
using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Formats;
using RunawayExplorer.Core.Metadata;
using RunawayExplorer.Core.Settings;
using Xunit;

namespace RunawayExplorer.Core.Tests;

public class Runaway3Tests
{
    public const string R3SteamDir = @"F:\Games\Steam\steamapps\common\Runaway A Twist Of Fate";

    #region Synthetic Builders

    /// <summary>
    /// Builds a synthetic Runaway 3 sprite:
    /// Record header (14 bytes per frame):
    ///   u32 fo (absolute offset from start of entry: 2 + fc * 14 + frame_relative_offset)
    ///   u16 x0, u16 w, u16 y0, u16 y1, u16 segCount
    /// Segments (7 bytes + pixel data):
    ///   u16 x, u16 y, u8 flag, u16 count, [pixels]
    ///   flag 0: RGB565 (2 bytes/pixel)
    ///   flag 1: RGB565 + 8-bit alpha (3 bytes/pixel)
    /// </summary>
    public static byte[] SyntheticSpriteR3()
    {
        ushort frameCount = 1;
        int headerSize = 2 + frameCount * 14;

        using var segMs = new MemoryStream();
        Span<byte> u16 = stackalloc byte[2];

        // Segment 0: x=10, y=5, flag=0 (RGB565, 2 bpp), count=2
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 10); segMs.Write(u16);
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 5); segMs.Write(u16);
        segMs.WriteByte(0);
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 2); segMs.Write(u16);
        // 2 pixels: red (0xF800), green (0x07E0)
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 0xF800); segMs.Write(u16);
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 0x07E0); segMs.Write(u16);

        // Segment 1: x=15, y=5, flag=1 (RGB565 + Alpha, 3 bpp), count=1
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 15); segMs.Write(u16);
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 5); segMs.Write(u16);
        segMs.WriteByte(1);
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 1); segMs.Write(u16);
        // 1 pixel: blue (0x001F) + alpha 128
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 0x001F); segMs.Write(u16);
        segMs.WriteByte(128);

        byte[] segData = segMs.ToArray();

        using var ms = new MemoryStream();
        BinaryPrimitives.WriteUInt16LittleEndian(u16, frameCount); ms.Write(u16);

        // Frame 0 record: fo = absolute offset = headerSize
        Span<byte> u32 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)headerSize); ms.Write(u32);
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 10); ms.Write(u16); // x0
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 16); ms.Write(u16); // x1 (x0 + w)
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 5); ms.Write(u16);  // y0
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 5); ms.Write(u16);  // y1
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 2); ms.Write(u16);  // segCount = 2

        ms.Write(segData);
        return ms.ToArray();
    }

    /// <summary>
    /// Builds a synthetic continuous RLE mask:
    /// Runs of 3 bytes: { u8 id, u16 len } summing to width * height.
    /// </summary>
    public static byte[] SyntheticContinuousMask(IReadOnlyList<(byte Id, ushort Length)> runs)
    {
        using var ms = new MemoryStream();
        Span<byte> u16 = stackalloc byte[2];
        foreach (var (id, len) in runs)
        {
            ms.WriteByte(id);
            BinaryPrimitives.WriteUInt16LittleEndian(u16, len);
            ms.Write(u16);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Builds a synthetic sparse antialiased mask:
    /// u16 n records of:
    ///   u16 x, u16 y, u8 flag, u16 count, [count bytes of alpha feathering if flag == 4]
    /// </summary>
    public static byte[] SyntheticSparseMask()
    {
        using var ms = new MemoryStream();
        Span<byte> u16 = stackalloc byte[2];

        ushort n = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(u16, n); ms.Write(u16);

        // Record 1: x=10, y=20, flag=2 (solid run), count=50
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 10); ms.Write(u16);
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 20); ms.Write(u16);
        ms.WriteByte(2);
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 50); ms.Write(u16);

        // Record 2: x=60, y=20, flag=4 (antialiased feathering), count=4, alphas=[192, 128, 64, 16]
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 60); ms.Write(u16);
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 20); ms.Write(u16);
        ms.WriteByte(4);
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 4); ms.Write(u16);
        ms.Write([192, 128, 64, 16]);

        return ms.ToArray();
    }

    #endregion

    #region GameDetector Tests

    [Fact]
    public void GameDetector_DetectsRunaway3Signatures()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "runaway3_detect_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // RATOF.exe
            string exe = Path.Combine(tempDir, "RATOF.exe");
            File.WriteAllBytes(exe, [1, 2, 3]);
            Assert.Equal(GameVersion.Runaway3, GameDetector.Detect(tempDir));
            File.Delete(exe);

            // ratof.config
            string cfg = Path.Combine(tempDir, "ratof.config");
            File.WriteAllBytes(cfg, [1, 2, 3]);
            Assert.Equal(GameVersion.Runaway3, GameDetector.Detect(tempDir));
            File.Delete(cfg);

            // RATOF-Config.exe
            string cfgExe = Path.Combine(tempDir, "RATOF-Config.exe");
            File.WriteAllBytes(cfgExe, [1, 2, 3]);
            Assert.Equal(GameVersion.Runaway3, GameDetector.Detect(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GameDetector_DetectsRealRunaway3()
    {
        if (Directory.Exists(R3SteamDir))
        {
            Assert.Equal(GameVersion.Runaway3, GameDetector.Detect(R3SteamDir));
        }
    }

    #endregion

    #region Format Recognition & Parsing Tests

    [Fact]
    public void SceneArchive_RecognizesRunaway3Names()
    {
        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.002", GameVersion.Runaway3)); // Cutscene archive in R3
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.002", GameVersion.Runaway1)); // Audio in R1
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.002", GameVersion.Runaway2)); // Audio in R2

        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.D01", GameVersion.Runaway3));
        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.001", GameVersion.Runaway3));
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.000", GameVersion.Runaway3)); // Global
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.004", GameVersion.Runaway3)); // Lip-sync
    }

    [Fact]
    public void SpriteDecoder_ParsesRunaway3Sprite()
    {
        byte[] spriteData = SyntheticSpriteR3();
        var asset = SpriteAsset.Parse(spriteData);

        Assert.NotNull(asset);
        Assert.True(asset.IsRunaway3);
        Assert.False(asset.IsRunaway2);
        Assert.Equal(1, asset.FrameCount);
        Assert.Null(asset.Verify());

        // Check frame bounds and segments
        var frameRec = asset.Records[0];
        Assert.Equal(10, frameRec.X0);
        Assert.Equal(6, frameRec.W);
        Assert.Equal(5, frameRec.Y0);
        Assert.Equal(5, frameRec.Y1);
        Assert.Equal(2, frameRec.SegmentCount);

        // Decode frame to RGBA8888
        var segments = asset.ReadSegments(0);
        Assert.Equal(2, segments.Count);

        // Segment 0: 2 pixels
        Assert.Equal(10, segments[0].X);
        Assert.Equal(5, segments[0].Y);
        Assert.Equal(2, segments[0].Count);
        Assert.False(segments[0].HasAlpha);

        // Segment 1: 1 pixel with alpha
        Assert.Equal(15, segments[1].X);
        Assert.Equal(5, segments[1].Y);
        Assert.Equal(1, segments[1].Count);
        Assert.True(segments[1].HasAlpha);

        SpriteFrame frame = asset.DecodeFrame(0);
        Assert.NotNull(frame.Image);
        Assert.Equal(6, frame.Image.Width);
        Assert.Equal(1, frame.Image.Height);

        byte[] rgba = frame.Image.Pixels;
        // Verify pixel 0 (red: 0xF800 -> B=0, G=0, R=248, A=255)
        Assert.Equal(0, rgba[0]);   // B
        Assert.Equal(0, rgba[1]);   // G
        Assert.True(rgba[2] > 240); // R
        Assert.Equal(255, rgba[3]); // A

        // Verify pixel 5 (blue with alpha 128: 0x001F -> B=248, G=0, R=0, A=128)
        int px5 = 5 * 4;
        Assert.True(rgba[px5] > 240); // B
        Assert.Equal(0, rgba[px5 + 1]); // G
        Assert.Equal(0, rgba[px5 + 2]); // R
        Assert.Equal(128, rgba[px5 + 3]); // A
    }

    [Fact]
    public void ContinuousMaskDecoder_DetectsAndDecodes()
    {
        // 1280x720 image: 921,600 pixels total
        int remaining = 1280 * 720;
        var exactRuns = new List<(byte Id, ushort Length)>();
        byte currentId = 1;
        while (remaining > 0)
        {
            ushort step = (ushort)Math.Min(remaining, 50000);
            exactRuns.Add((currentId, step));
            remaining -= step;
            currentId = (byte)(currentId == 1 ? 2 : 1);
        }

        byte[] maskData = SyntheticContinuousMask(exactRuns);

        var maskInfo = RleMaskDecoder.DetectContinuous(maskData);
        Assert.NotNull(maskInfo);
        Assert.Equal(1280, maskInfo.Value.Width);
        Assert.Equal(720, maskInfo.Value.Height);

        var decoded = RleMaskDecoder.Decode(maskData, 1280, 720);
        Assert.Equal(1280 * 720 * 4, decoded.Pixels.Length);
        Assert.True(decoded.Pixels[3] > 0); // Alpha > 0
    }

    [Fact]
    public void SparseMaskDecoder_DetectsAndDecodes()
    {
        byte[] sparseData = SyntheticSparseMask();

        var maskInfo = SparseMaskDecoder.Detect(sparseData);
        Assert.NotNull(maskInfo);
        Assert.Equal(2, maskInfo.Value.RunCount);

        // Decode into 100x50 canvas
        DecodedImage image = SparseMaskDecoder.Decode(sparseData, 100, 50, colorSeed: 1);
        Assert.Equal(100 * 50 * 4, image.Pixels.Length);

        // Record 1: x=10..59 at y=20 has flag 2 (solid run, alpha=255)
        for (int x = 10; x < 60; x++)
        {
            int idx = (20 * 100 + x) * 4;
            Assert.Equal(255, image.Pixels[idx + 3]);
        }

        // Record 2: x=60..63 at y=20 has flag 4 (antialiased feathering: [192, 128, 64, 16])
        Assert.Equal(192, image.Pixels[(20 * 100 + 60) * 4 + 3]);
        Assert.Equal(128, image.Pixels[(20 * 100 + 61) * 4 + 3]);
        Assert.Equal(64, image.Pixels[(20 * 100 + 62) * 4 + 3]);
        Assert.Equal(16, image.Pixels[(20 * 100 + 63) * 4 + 3]);

        // Pixels outside the mask should be transparent (alpha == 0)
        Assert.Equal(0, image.Pixels[(20 * 100 + 9) * 4 + 3]);
        Assert.Equal(0, image.Pixels[(20 * 100 + 64) * 4 + 3]);
        Assert.Equal(0, image.Pixels[(19 * 100 + 20) * 4 + 3]);
    }

    [Fact]
    public void SceneCatalog_HasRunaway3Metadata()
    {
        Assert.Equal(7, SceneCatalog.ChaptersR3.Count);
        Assert.Equal("Brian Basco Is Dead", SceneCatalog.ChaptersR3[0].TitleEn);
        Assert.Equal("Epilogue & Flashbacks", SceneCatalog.ChaptersR3[6].TitleEn);

        var ch1 = SceneCatalog.GetChapter(1, GameVersion.Runaway3);
        Assert.NotNull(ch1);
        Assert.Equal("Brian Basco Is Dead", ch1.TitleEn);

        string a01Title = SceneCatalog.GetSceneTitle("RESOURCE.A01", "en", GameVersion.Runaway3);
        Assert.Contains("Brian's Grave", a01Title);

        string d01Title = SceneCatalog.GetSceneTitle("RESOURCE.D01", "en", GameVersion.Runaway3);
        Assert.Contains("Autopsy Lab", d01Title);

        string cutTitle = SceneCatalog.GetSceneTitle("RESOURCE.002", "en", GameVersion.Runaway3);
        Assert.Contains("Cinematic Cutscene", cutTitle);
    }

    #endregion

    #region Real Installation Integration Tests

    [Fact]
    public void RealInstall_SceneArchivesAndEntries()
    {
        if (!Directory.Exists(R3SteamDir)) return;

        string resDir = Path.Combine(R3SteamDir, "Resource");
        if (!Directory.Exists(resDir)) return;

        // Verify RESOURCE.D01
        string d01Path = Path.Combine(resDir, "RESOURCE.D01");
        if (File.Exists(d01Path))
        {
            using var f = File.OpenRead(d01Path);
            var entries = SceneArchive.ReadEntries(f);
            Assert.NotEmpty(entries);

            // Entry 0 should be raster: D01 is a 2-story tall scene 1508x1540
            var e0 = entries[0];
            byte[] e0Bytes = new byte[e0.Size];
            f.Position = e0.Offset;
            Assert.Equal(e0.Size, f.Read(e0Bytes));
            var rasterInfo = RasterDecoder.Detect(e0Bytes);
            Assert.NotNull(rasterInfo);
            Assert.Equal(1508, rasterInfo.Value.Width);
            Assert.Equal(1540, rasterInfo.Value.Height);

            // Entry 1 should be continuous mask matching D01 dimensions
            var e1 = entries[1];
            byte[] e1Bytes = new byte[e1.Size];
            f.Position = e1.Offset;
            Assert.Equal(e1.Size, f.Read(e1Bytes));
            var maskInfo = RleMaskDecoder.DetectContinuous(e1Bytes, 1508, 1540);
            Assert.NotNull(maskInfo);
            Assert.Equal(1508, maskInfo.Value.Width);
            Assert.Equal(1540, maskInfo.Value.Height);
        }

        // Verify RESOURCE.A02: vertical 2-screen tall scene 1280x1440
        string a02Path = Path.Combine(resDir, "RESOURCE.A02");
        if (File.Exists(a02Path))
        {
            using var f = File.OpenRead(a02Path);
            var entries = SceneArchive.ReadEntries(f);
            Assert.NotEmpty(entries);

            // Entry 0: 1280x1440 background
            var e0 = entries[0];
            byte[] e0Bytes = new byte[e0.Size];
            f.Position = e0.Offset;
            Assert.Equal(e0.Size, f.Read(e0Bytes));
            var rasterInfo = RasterDecoder.Detect(e0Bytes);
            Assert.NotNull(rasterInfo);
            Assert.Equal(1280, rasterInfo.Value.Width);
            Assert.Equal(1440, rasterInfo.Value.Height);

            // Entry 1: 1280x1440 continuous mask
            var e1 = entries[1];
            byte[] e1Bytes = new byte[e1.Size];
            f.Position = e1.Offset;
            Assert.Equal(e1.Size, f.Read(e1Bytes));
            var maskInfo = RleMaskDecoder.Detect(e1Bytes, 1280, 1440);
            Assert.NotNull(maskInfo);
            Assert.Equal(1280, maskInfo.Value.Width);
            Assert.Equal(1440, maskInfo.Value.Height);

            // Entry 8: sparse mask on 1280x1440 scene
            var e8 = entries[8];
            byte[] e8Bytes = new byte[e8.Size];
            f.Position = e8.Offset;
            Assert.Equal(e8.Size, f.Read(e8Bytes));
            var sparseInfo = SparseMaskDecoder.Detect(e8Bytes, 1280, 1440);
            Assert.NotNull(sparseInfo);
            Assert.Equal(1280, sparseInfo.Value.Width);
            Assert.Equal(1440, sparseInfo.Value.Height);
        }

        // Verify RESOURCE.002 is a scene archive in R3
        string r002Path = Path.Combine(resDir, "RESOURCE.002");
        if (File.Exists(r002Path))
        {
            using var f = File.OpenRead(r002Path);
            var entries = SceneArchive.ReadEntries(f);
            Assert.Equal(14, entries.Count); // 14 animations
        }

        // Verify Audio RESOURCE.S00
        string s00Path = Path.Combine(resDir, "RESOURCE.S00");
        if (File.Exists(s00Path))
        {
            using var f = File.OpenRead(s00Path);
            var entries = AudioArchive.ReadEntries(f);
            Assert.NotEmpty(entries);
        }

        // Verify Lip-sync RESOURCE.004
        string r004Path = Path.Combine(resDir, "RESOURCE.004");
        if (File.Exists(r004Path))
        {
            using var f = File.OpenRead(r004Path);
            byte[] r004Bytes = new byte[f.Length];
            Assert.Equal((int)f.Length, f.Read(r004Bytes));
            var entries = VisemeArchive.ReadEntries(r004Bytes, f.Length);
            Assert.NotEmpty(entries);
        }
    }

    [Fact]
    public void RealInstall_VoiceArchive()
    {
        if (!Directory.Exists(R3SteamDir)) return;

        string voicePath = Path.Combine(R3SteamDir, "Dataa", "DATAAA.000");
        if (!File.Exists(voicePath)) return;

        using var f = File.OpenRead(voicePath);
        var clips = VoiceArchive.ReadSingleArchiveClips(f, voicePath);
        Assert.Equal(7228, clips.Count);
    }

    [Fact]
    public void RealInstall_VirtualFileSystem_FullScan()
    {
        if (!Directory.Exists(R3SteamDir)) return;

        var vfs = VirtualFileSystem.Init(R3SteamDir, cache: ScanCache.Ephemeral(includeShipped: true));

        Assert.Equal(GameVersion.Runaway3, vfs.GameVersion);
        Assert.True(vfs.Summary.FromCache);
        Assert.True(vfs.Summary.SceneArchives > 0);
        Assert.True(vfs.Summary.AudioClips > 0);
        Assert.True(vfs.Summary.VoiceClips > 0);
        Assert.True(vfs.Summary.Videos > 0);

        // Verify scenes folder exists
        var scenesNode = vfs.Root.Children.FirstOrDefault(c => c.Name == VirtualFileSystem.ScenesFolder);
        Assert.NotNull(scenesNode);
        Assert.True(scenesNode.Children.Count >= 50);

        // Verify cutscenes from RESOURCE.002 are present
        Assert.Contains(scenesNode.Children, s => s.Name == "RESOURCE.002");
    }

    [Fact]
    public void RealInstall_PopulateShippedScanCacheR3()
    {
        if (!Directory.Exists(R3SteamDir)) return;

        // Locate repository root
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "RunawayExplorer.slnx")) && !Directory.Exists(Path.Combine(dir, ".git")))
        {
            dir = Path.GetDirectoryName(dir)!;
        }
        Assert.False(string.IsNullOrEmpty(dir), "Could not locate solution directory");

        string shippedJsonPath = Path.Combine(dir, "src", "RunawayExplorer.Core", "Resources", "shipped-scan-cache.json");
        Assert.True(File.Exists(shippedJsonPath), $"shipped-scan-cache.json not found at {shippedJsonPath}");

        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
        };

        var shipped = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<ScanCache.CachedEntry>>>(
            File.ReadAllText(shippedJsonPath), options) ?? new(StringComparer.OrdinalIgnoreCase);

        int initialCount = shipped.Count;
        Assert.True(initialCount >= 145, $"Expected at least 145 R1+R2 hashes in shipped cache, found {initialCount}");

        string resDir = Path.Combine(R3SteamDir, "Resource");
        if (!Directory.Exists(resDir))
            resDir = R3SteamDir;

        var sceneFiles = Directory.GetFiles(resDir)
            .Where(f => SceneArchive.IsSceneArchiveName(Path.GetFileName(f), GameVersion.Runaway3))
            .ToList();

        Assert.True(sceneFiles.Count >= 60, $"Expected >= 60 R3 scene archives, found {sceneFiles.Count}");

        var newHashes = new System.Collections.Concurrent.ConcurrentDictionary<string, List<ScanCache.CachedEntry>>(StringComparer.OrdinalIgnoreCase);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 8)
        };

        Parallel.ForEach(sceneFiles, parallelOptions, file =>
        {
            string hash = ScanCache.ComputeHash(file);
            var localCache = ScanCache.Ephemeral(includeShipped: false);
            VirtualFileSystem.BuildSceneArchive(file, localCache, "en", GameVersion.Runaway3);
            var entries = localCache.TryGet(file);
            if (entries is not null && entries.Count > 0)
            {
                newHashes[hash] = entries;
            }
        });

        foreach (var (h, entries) in newHashes)
        {
            shipped[h] = entries;
        }

        File.WriteAllText(shippedJsonPath, System.Text.Json.JsonSerializer.Serialize(shipped, options));

        Assert.True(shipped.Count >= 145 + 60, $"Expected >= 205 total hashes after R3 merge, found {shipped.Count}");
    }

    [Fact]
    public void SpriteDecoder_ParsesRunaway3RealSprite()
    {
        string path = Path.Combine(R3SteamDir, "Resource", "RESOURCE.A05");
        if (!File.Exists(path)) return;

        using var f = File.OpenRead(path);
        var entries = SceneArchive.ReadEntries(f);
        var e15 = entries[15];
        byte[] bytes = new byte[e15.Size];
        f.Position = e15.Offset;
        f.ReadExactly(bytes);

        var asset = SpriteAsset.Parse(bytes);
        Assert.NotNull(asset);
        Assert.Equal(985, asset.Bounds.X);
        Assert.Equal(362, asset.Bounds.Y);
        Assert.Equal(183, asset.Bounds.Width);
        Assert.Equal(239, asset.Bounds.Height);
        Assert.Equal(38, asset.FrameCount);
        Assert.Null(asset.Verify());
    }

    [Fact]
    public void SparseMaskDecoder_DecodesRealRunaway3Mask()
    {
        string path = Path.Combine(R3SteamDir, "Resource", "RESOURCE.C10");
        if (!File.Exists(path)) return;
        using var fs = File.OpenRead(path);
        var ents = SceneArchive.ReadEntries(fs);
        var me = ents[7];
        byte[] data = new byte[me.Size];
        fs.Position = me.Offset;
        fs.ReadExactly(data);

        var decoded = SparseMaskDecoder.Decode(data, 1380, 720, colorSeed: 7);
        Assert.NotNull(decoded);
        Assert.Equal(1380, decoded.Width);
        Assert.Equal(720, decoded.Height);

        int solidPixels = 0;
        int antialiasedPixels = 0;
        int emptyPixels = 0;

        for (int p = 0; p < decoded.Pixels.Length; p += 4)
        {
            byte a = decoded.Pixels[p + 3];
            if (a == 255) solidPixels++;
            else if (a > 0) antialiasedPixels++;
            else emptyPixels++;
        }

        Console.WriteLine($"Decoded e07: solid={solidPixels}, antialiased={antialiasedPixels}, empty={emptyPixels}");
        Assert.True(solidPixels > 10_000, $"Expected solidPixels > 10,000, got {solidPixels}");
        Assert.True(antialiasedPixels > 100, $"Expected antialiasedPixels > 100, got {antialiasedPixels}");
    }

    [Fact]
    public void SceneMask_DecodesRunaway3ContinuousRleMaskAndAttributeTable()
    {
        string path = Path.Combine(R3SteamDir, "Resource", "RESOURCE.C10");
        if (!File.Exists(path)) return;
        using var fs = File.OpenRead(path);
        var ents = SceneArchive.ReadEntries(fs);

        // e01: RLE mask
        var e1 = ents[1];
        byte[] b1 = new byte[e1.Size];
        fs.Position = e1.Offset;
        fs.ReadExactly(b1);

        var maskInfo = RleMaskDecoder.Detect(b1, 1380, 720);
        Assert.NotNull(maskInfo);
        Assert.Equal(1380, maskInfo.Value.Width);
        Assert.Equal(720, maskInfo.Value.Height);

        // e02: 1536-byte attribute table (6 attributes * 256 IDs)
        var e2 = ents[2];
        Assert.Equal(1536, e2.Size);
        byte[] b2 = new byte[e2.Size];
        fs.Position = e2.Offset;
        fs.ReadExactly(b2);

        byte[]? map = RleMaskDecoder.ExtractObjectMapping(b2);
        Assert.NotNull(map);
        Assert.Equal(256, map.Length);
    }

    #endregion
}
