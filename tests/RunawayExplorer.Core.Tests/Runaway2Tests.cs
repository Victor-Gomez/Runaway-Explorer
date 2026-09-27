using System.Buffers.Binary;
using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Formats;
using RunawayExplorer.Core.Metadata;
using RunawayExplorer.Core.Settings;
using Xunit;

namespace RunawayExplorer.Core.Tests;

public class Runaway2ContainerTests
{
    private const string R1SteamDir = @"F:\Games\Steam\steamapps\common\Runaway A Road Adventure";
    private const string R2SteamDir = @"F:\Games\Steam\steamapps\common\Runaway The Dream of the Turtle";

    #region Synthetic Builders

    /// <summary>Builds a synthetic Runaway 2 scene archive: 4-byte header (table half-bytes), then offsets, then sizes, then entries.</summary>
    public static byte[] SyntheticSceneR2(IReadOnlyList<byte[]?> entries)
    {
        int count = entries.Count;
        int tableHalfBytes = count * 4;
        int tableTotalBytes = 4 + count * 8;
        using var ms = new MemoryStream();

        Span<byte> u32 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)tableHalfBytes);
        ms.Write(u32);

        var offsets = new uint[count];
        var sizes = new uint[count];
        uint cursor = (uint)tableTotalBytes;
        for (int i = 0; i < count; i++)
        {
            if (entries[i] is null) continue;
            offsets[i] = cursor;
            sizes[i] = (uint)entries[i]!.Length;
            cursor += sizes[i];
        }

        foreach (uint o in offsets) { BinaryPrimitives.WriteUInt32LittleEndian(u32, o); ms.Write(u32); }
        foreach (uint s in sizes) { BinaryPrimitives.WriteUInt32LittleEndian(u32, s); ms.Write(u32); }
        foreach (byte[]? e in entries) if (e is not null) ms.Write(e);

        return ms.ToArray();
    }

    /// <summary>Builds a synthetic Runaway 2 audio archive: 4-byte count, then 9-byte records (offset, size, format).</summary>
    public static byte[] SyntheticAudioR2(IReadOnlyList<(byte[]? Data, byte Format)> entries)
    {
        int count = entries.Count;
        int headerSize = 4 + count * 9;
        using var ms = new MemoryStream();

        Span<byte> u32 = stackalloc byte[4];
        Span<byte> emptyRecord = stackalloc byte[9];
        emptyRecord.Clear();

        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)count);
        ms.Write(u32);

        uint cursor = (uint)headerSize;
        for (int i = 0; i < count; i++)
        {
            var (data, fmt) = entries[i];
            if (data is null)
            {
                ms.Write(emptyRecord);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(u32, cursor);
                ms.Write(u32);
                BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)data.Length);
                ms.Write(u32);
                ms.WriteByte(fmt);
                cursor += (uint)data.Length;
            }
        }

        foreach (var (data, _) in entries)
        {
            if (data is not null) ms.Write(data);
        }

        return ms.ToArray();
    }

    /// <summary>Builds a synthetic Runaway 2 single voice archive (Dataaa.000): 4-byte count, then 9-byte records.</summary>
    public static byte[] SyntheticVoiceR2(IReadOnlyList<byte[]?> clips)
    {
        int count = clips.Count;
        int headerSize = 4 + count * 9;
        using var ms = new MemoryStream();

        Span<byte> u32 = stackalloc byte[4];
        Span<byte> emptyRecord = stackalloc byte[9];
        emptyRecord.Clear();

        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)count);
        ms.Write(u32);

        uint cursor = (uint)headerSize;
        for (int i = 0; i < count; i++)
        {
            var data = clips[i];
            if (data is null)
            {
                ms.Write(emptyRecord);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(u32, cursor);
                ms.Write(u32);
                BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)data.Length);
                ms.Write(u32);
                ms.WriteByte(1); // 1 = MP3
                cursor += (uint)data.Length;
            }
        }

        foreach (var data in clips)
        {
            if (data is not null) ms.Write(data);
        }

        return ms.ToArray();
    }

    /// <summary>Builds a synthetic Runaway 2 viseme archive (RESOURCE.004): 10,500 7-byte records.</summary>
    public static byte[] SyntheticVisemesR2(IReadOnlyList<(int Index, byte[] Data)> tracks)
    {
        int count = VisemeArchive.SlotCountR2;
        int tableSize = VisemeArchive.TableSizeR2;
        var table = new byte[tableSize];
        using var ms = new MemoryStream();

        uint cursor = (uint)tableSize;
        foreach (var (idx, data) in tracks)
        {
            if (idx >= count) continue;
            int p = idx * 7;
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(p), cursor);
            BinaryPrimitives.WriteUInt16LittleEndian(table.AsSpan(p + 4), (ushort)data.Length);
            table[p + 6] = 1; // flag
            cursor += (uint)data.Length;
        }

        // Ensure slot 0 has offset = TableSizeR2 (required by R2 detection)
        if (BinaryPrimitives.ReadUInt32LittleEndian(table) == 0)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(table, (uint)tableSize);
        }

        ms.Write(table);
        foreach (var (_, data) in tracks)
        {
            ms.Write(data);
        }

        return ms.ToArray();
    }

    /// <summary>Builds a synthetic Runaway 2 paired sprite (header + segments combined).</summary>
    public static byte[] SyntheticSpriteR2()
    {
        // 1 frame: 2 segments
        // Segment 0: (x=10, y=5, flag=0 [no alpha, 2bpp], count=2) -> 4 bytes pixels
        // Segment 1: (x=15, y=5, flag=1 [alpha, 3bpp], count=1) -> 3 bytes pixels
        ushort frameCount = 1;

        using var segMs = new MemoryStream();
        // Seg 0
        Span<byte> u16 = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 10); segMs.Write(u16); // x
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 5); segMs.Write(u16);  // y
        segMs.WriteByte(0); // flag = no alpha
        segMs.WriteByte(2); // count = 2
        // 2 pixels 16-bit: red (0xF800), green (0x07E0)
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 0xF800); segMs.Write(u16);
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 0x07E0); segMs.Write(u16);

        // Seg 1
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 15); segMs.Write(u16); // x
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 5); segMs.Write(u16);  // y
        segMs.WriteByte(1); // flag = with alpha
        segMs.WriteByte(1); // count = 1
        // 1 pixel: blue (0x001F) + alpha 128
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 0x001F); segMs.Write(u16);
        segMs.WriteByte(128); // alpha

        byte[] segData = segMs.ToArray();

        using var ms = new MemoryStream();
        BinaryPrimitives.WriteUInt16LittleEndian(u16, frameCount); ms.Write(u16);
        // SpriteRecord: fo=0, x0=10, w=6, y0=5, y1=5, c=2
        Span<byte> u32 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(u32, 0); ms.Write(u32); // fo
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 10); ms.Write(u16); // x0
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 6); ms.Write(u16);  // w
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 5); ms.Write(u16);  // y0
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 5); ms.Write(u16);  // y1
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 2); ms.Write(u16);  // c = 2 segments
        ms.Write(segData);

        return ms.ToArray();
    }

    #endregion

    #region GameDetector Tests

    [Fact]
    public void GameDetector_DetectsFromSignatures()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "runaway_detect_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Empty defaults to Runaway1
            Assert.Equal(GameVersion.Runaway1, GameDetector.Detect(tempDir));

            // R1 signature
            string r1File = Path.Combine(tempDir, "RESOURCE.001");
            File.WriteAllBytes(r1File, [1, 2, 3]);
            Assert.Equal(GameVersion.Runaway1, GameDetector.Detect(tempDir));
            File.Delete(r1File);

            // R2 signature (RESOURCE.SP1)
            string r2File = Path.Combine(tempDir, "RESOURCE.SP1");
            File.WriteAllBytes(r2File, [1, 2, 3]);
            Assert.Equal(GameVersion.Runaway2, GameDetector.Detect(tempDir));
            File.Delete(r2File);

            // R2 signature (RunawayTDOTT.exe)
            string exeFile = Path.Combine(tempDir, "RunawayTDOTT.exe");
            File.WriteAllBytes(exeFile, [1, 2, 3]);
            Assert.Equal(GameVersion.Runaway2, GameDetector.Detect(tempDir));
            File.Delete(exeFile);

            // R2 Dataa/Dataaa.000
            string dataaDir = Path.Combine(tempDir, "Dataa");
            Directory.CreateDirectory(dataaDir);
            File.WriteAllBytes(Path.Combine(dataaDir, "Dataaa.000"), [1, 2, 3]);
            Assert.Equal(GameVersion.Runaway2, GameDetector.Detect(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GameDetector_RealDirectories()
    {
        if (Directory.Exists(R1SteamDir))
        {
            Assert.Equal(GameVersion.Runaway1, GameDetector.Detect(R1SteamDir));
        }

        if (Directory.Exists(R2SteamDir))
        {
            Assert.Equal(GameVersion.Runaway2, GameDetector.Detect(R2SteamDir));
        }
    }

    #endregion

    #region SceneArchive Tests

    [Fact]
    public void SceneArchive_RecognizesRunaway2Names()
    {
        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.H01"));
        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.B04A"));
        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.SP1"));
        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.SP5"));
        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.001", GameVersion.Runaway2)); // R2 intro scene
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.001", GameVersion.Runaway1)); // R1 string data
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.000")); // Global archive
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.004")); // Viseme archive
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.M01"));
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.S01"));
    }

    [Fact]
    public void SceneArchive_ReadsSyntheticR2Archive()
    {
        byte[] payload1 = [0xAA, 0xBB, 0xCC];
        byte[] payload2 = [0x11, 0x22, 0x33, 0x44];
        byte[] arcData = SyntheticSceneR2([payload1, null, payload2]);

        using var ms = new MemoryStream(arcData);
        var entries = SceneArchive.ReadEntries(ms);

        Assert.Equal(2, entries.Count);
        Assert.Equal(0, entries[0].Index);
        Assert.Equal(payload1.Length, entries[0].Size);
        Assert.Equal(2, entries[1].Index);
        Assert.Equal(payload2.Length, entries[1].Size);

        // Verify entry 0 data
        var buf0 = new byte[entries[0].Size];
        ms.Position = entries[0].Offset;
        ms.ReadExactly(buf0);
        Assert.Equal(payload1, buf0);

        // Verify entry 2 data
        var buf2 = new byte[entries[1].Size];
        ms.Position = entries[1].Offset;
        ms.ReadExactly(buf2);
        Assert.Equal(payload2, buf2);
    }

    [Fact]
    public void SceneArchive_RealR2Archives_ReadCorrectly()
    {
        if (!Directory.Exists(R2SteamDir)) return;

        string h01 = Path.Combine(R2SteamDir, "RESOURCE.H01");
        if (File.Exists(h01))
        {
            using var fs = File.OpenRead(h01);
            var entries = SceneArchive.ReadEntries(fs);
            Assert.True(entries.Count > 0);
            Assert.All(entries, e =>
            {
                Assert.True(e.Offset > 0);
                Assert.True(e.Size > 0);
                Assert.True(e.Offset + e.Size <= fs.Length);
            });
        }

        string b04a = Path.Combine(R2SteamDir, "RESOURCE.B04A");
        if (File.Exists(b04a))
        {
            using var fs = File.OpenRead(b04a);
            var entries = SceneArchive.ReadEntries(fs);
            Assert.True(entries.Count > 0);
        }
    }

    #endregion

    #region AudioArchive Tests

    [Fact]
    public void AudioArchive_ReadsSyntheticR2WithFormats()
    {
        byte[] wavData = "RIFF1234WAVE"u8.ToArray();
        byte[] mp3Data = [0xFF, 0xFB, 0x90, 0x64, 0x00];

        byte[] arcData = SyntheticAudioR2([
            (wavData, (byte)0),  // WAV
            (null, (byte)0),     // empty
            (mp3Data, (byte)1),  // MP3
        ]);

        using var ms = new MemoryStream(arcData);
        var entries = AudioArchive.ReadAudioEntries(ms);

        Assert.Equal(2, entries.Count);

        Assert.Equal(0, entries[0].Index);
        Assert.Equal(AudioFormat.Wav, entries[0].Format);
        Assert.Equal(wavData.Length, entries[0].Size);

        Assert.Equal(2, entries[1].Index);
        Assert.Equal(AudioFormat.Mp3, entries[1].Format);
        Assert.Equal(mp3Data.Length, entries[1].Size);

        // Also verify backward compatible ReadEntries
        ms.Position = 0;
        var compat = AudioArchive.ReadEntries(ms);
        Assert.Equal(2, compat.Count);
        Assert.Equal(entries[0].Offset, compat[0].Offset);
        Assert.Equal(entries[0].Size, compat[0].Size);
    }

    [Fact]
    public void AudioArchive_RealR2Audio_ReadsWavAndMp3()
    {
        if (!Directory.Exists(R2SteamDir)) return;

        string m05 = Path.Combine(R2SteamDir, "RESOURCE.M05");
        if (File.Exists(m05))
        {
            using var fs = File.OpenRead(m05);
            var entries = AudioArchive.ReadAudioEntries(fs);
            Assert.True(entries.Count > 0);
            Assert.Contains(entries, e => e.Format == AudioFormat.Wav);
            Assert.Contains(entries, e => e.Format == AudioFormat.Mp3);
        }
    }

    #endregion

    #region VoiceArchive Tests

    [Fact]
    public void VoiceArchive_ReadsSingleArchiveClips()
    {
        byte[] clip0 = [1, 2, 3, 4];
        byte[] clip1 = [5, 6, 7];
        byte[] arcData = SyntheticVoiceR2([clip0, clip1]);

        using var ms = new MemoryStream(arcData);
        var clips = VoiceArchive.ReadSingleArchiveClips(ms);

        Assert.Equal(2, clips.Count);
        Assert.Equal(0, clips[0].Index);
        Assert.Equal(clip0.Length, clips[0].Size);
        Assert.Equal(1, clips[1].Index);
        Assert.Equal(clip1.Length, clips[1].Size);
    }

    [Fact]
    public void VoiceArchive_RealDataaa_Reads10454Clips()
    {
        if (!Directory.Exists(R2SteamDir)) return;

        string dataaa = Path.Combine(R2SteamDir, "Dataa", "Dataaa.000");
        if (File.Exists(dataaa))
        {
            using var fs = File.OpenRead(dataaa);
            var clips = VoiceArchive.ReadSingleArchiveClips(fs);
            Assert.Equal(10454, clips.Count);
            Assert.All(clips.Values, c =>
            {
                Assert.True(c.Offset > 0);
                Assert.True(c.Size > 0);
                Assert.True(c.Offset + c.Size <= fs.Length);
            });
        }
    }

    #endregion

    #region VisemeArchive Tests

    [Fact]
    public void VisemeArchive_ReadsR2Visemes()
    {
        byte[] t0 = [10, 20, 30];
        byte[] t1 = [40, 50];
        byte[] arcData = SyntheticVisemesR2([(0, t0), (1, t1)]);

        using var ms = new MemoryStream(arcData);
        var entries = VisemeArchive.ReadEntries(ms);

        Assert.Equal(2, entries.Count);
        Assert.Equal(0, entries[0].Index);
        Assert.Equal(t0.Length, entries[0].Size);
        Assert.Equal(1, entries[1].Index);
        Assert.Equal(t1.Length, entries[1].Size);
    }

    [Fact]
    public void VisemeArchive_RealResource004_ReadsEntries()
    {
        if (!Directory.Exists(R2SteamDir)) return;

        string res004 = Path.Combine(R2SteamDir, "RESOURCE.004");
        if (File.Exists(res004))
        {
            using var fs = File.OpenRead(res004);
            var entries = VisemeArchive.ReadEntries(fs);
            Assert.True(entries.Count > 10000);
        }
    }

    #endregion

    #region SpriteDecoder Tests

    [Fact]
    public void SpriteDecoder_ParsesAndDecodesR2PairedSprite()
    {
        byte[] data = SyntheticSpriteR2();

        SpriteAsset? asset = SpriteAsset.Parse(data);
        Assert.NotNull(asset);
        Assert.True(asset.IsRunaway2);
        Assert.Equal(1, asset.FrameCount);
        Assert.Equal((10, 5, 6, 1), asset.Bounds);

        SpriteFrame frame = asset.DecodeFrame(0);
        Assert.NotNull(frame.Image);
        Assert.Equal(10, frame.X);
        Assert.Equal(5, frame.Y);
        Assert.Equal(6, frame.Image.Width);
        Assert.Equal(1, frame.Image.Height);

        // Check pixel (x=10, y=5) -> Red, opaque (alpha 255)
        byte[] px0 = [
            frame.Image.Pixels[0],
            frame.Image.Pixels[1],
            frame.Image.Pixels[2],
            frame.Image.Pixels[3]
        ];
        Assert.Equal(255, px0[3]); // Alpha = 255
        Assert.True(px0[2] > 200);  // Red component high

        // Check pixel (x=15, y=5) -> Blue, alpha 128
        int idx15 = (15 - 10) * 4;
        byte[] px15 = [
            frame.Image.Pixels[idx15],
            frame.Image.Pixels[idx15 + 1],
            frame.Image.Pixels[idx15 + 2],
            frame.Image.Pixels[idx15 + 3]
        ];
        Assert.Equal(128, px15[3]); // Alpha = 128
        Assert.True(px15[0] > 200);  // Blue component high
    }

    #endregion

    #region SceneCatalog Tests

    [Fact]
    public void SceneCatalog_Runaway2Metadata()
    {
        var ch1 = SceneCatalog.GetChapter(1, GameVersion.Runaway2);
        Assert.NotNull(ch1);
        Assert.Equal("Mala Island", ch1.TitleEn);

        var ch6 = SceneCatalog.GetChapter(6, GameVersion.Runaway2);
        Assert.NotNull(ch6);
        Assert.Equal("The Hidden Temple", ch6.TitleEn);

        string titleEn = SceneCatalog.GetSceneTitle("RESOURCE.B01", "en", GameVersion.Runaway2);
        Assert.Contains("Beach", titleEn);

        string titleEs = SceneCatalog.GetSceneTitle("RESOURCE.B01", "es", GameVersion.Runaway2);
        Assert.Contains("Playa", titleEs);

        string audioEn = SceneCatalog.GetAudioTitle("RESOURCE.M05", "en", GameVersion.Runaway2);
        Assert.False(string.IsNullOrEmpty(audioEn));
    }

    #endregion

    #region AppSettings Tests

    [Fact]
    public void AppSettings_ManagesBothGames()
    {
        var settings = new AppSettings
        {
            Runaway1Dir = @"C:\Games\R1",
            Runaway2Dir = @"C:\Games\R2",
            ActiveGame = GameVersion.Runaway2
        };

        Assert.Equal(@"C:\Games\R2", settings.ActiveGameDir);
        Assert.Equal(@"C:\Games\R1", settings.GetGameDir(GameVersion.Runaway1));
        Assert.Equal(@"C:\Games\R2", settings.GetGameDir(GameVersion.Runaway2));

        settings.SetGameDir(GameVersion.Runaway1, @"D:\NewR1");
        Assert.Equal(@"D:\NewR1", settings.Runaway1Dir);

        settings.ActiveGame = GameVersion.Runaway1;
        Assert.Equal(@"D:\NewR1", settings.ActiveGameDir);
    }

    #endregion

    #region Real Installation Integration Tests

    [Fact]
    public void RealInstall_Runaway1_LoadsCompleteVfs()
    {
        if (!Directory.Exists(R1SteamDir)) return;

        var vfs = VirtualFileSystem.Init(R1SteamDir, cache: ScanCache.Ephemeral(includeShipped: true));
        Assert.Equal(GameVersion.Runaway1, vfs.GameVersion);
        Assert.True(vfs.Summary.SceneArchives > 0);
        Assert.True(vfs.Summary.AudioClips > 0);
        Assert.True(vfs.Summary.VoiceClips > 0);
        Assert.True(vfs.Summary.Videos > 0);

        // Its global archive is not only interface art: 11 of its slots are sprite animations in the
        // scene archives' format -- the pointing hands and the menu widgets.
        FsNode global = vfs.Root.Children.Single(c => c.Name == VirtualFileSystem.GlobalFolder)
            .Children.Single(c => c.Name.Equals("Resource.000", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(11, global.Children.Count(c => c.Kind == EntryKind.Animation));
        Assert.Equal(6, global.Children.Count(c => c.Kind == EntryKind.Font));
    }

    [Fact]
    public void GenerateAndVerify_ShippedScanCache_IncludesRunaway2()
    {
        if (!Directory.Exists(R2SteamDir)) return;

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
        Assert.True(initialCount >= 75, $"Expected at least 75 R1 hashes in shipped cache, found {initialCount}");

        string resDir = Path.Combine(R2SteamDir, "Resource");
        if (!Directory.Exists(resDir))
            resDir = R2SteamDir;

        var sceneFiles = Directory.GetFiles(resDir)
            .Where(f => SceneArchive.IsSceneArchiveName(Path.GetFileName(f), GameVersion.Runaway2))
            .ToList();

        Assert.True(sceneFiles.Count >= 70, $"Expected >= 70 R2 scene archives, found {sceneFiles.Count}");

        var newHashes = new System.Collections.Concurrent.ConcurrentDictionary<string, List<ScanCache.CachedEntry>>(StringComparer.OrdinalIgnoreCase);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 8)
        };

        Parallel.ForEach(sceneFiles, parallelOptions, file =>
        {
            string hash = ScanCache.ComputeHash(file);
            if (shipped.ContainsKey(hash))
                return;

            var localCache = ScanCache.Ephemeral(includeShipped: false);
            VirtualFileSystem.BuildSceneArchive(file, localCache, "en", GameVersion.Runaway2);
            var entries = localCache.TryGet(file);
            if (entries is not null && entries.Count > 0)
            {
                newHashes[hash] = entries;
            }
        });

        int added = 0;
        foreach (var (h, entries) in newHashes)
        {
            if (!shipped.ContainsKey(h))
            {
                shipped[h] = entries;
                added++;
            }
        }

        if (added > 0)
        {
            File.WriteAllText(shippedJsonPath, System.Text.Json.JsonSerializer.Serialize(shipped, options));
        }

        Assert.True(shipped.Count >= 75 + 70, $"Expected >= 145 total hashes after R2 merge, found {shipped.Count}");
    }

    [Fact]
    public void RealInstall_Runaway2_LoadsCompleteVfs()
    {
        if (!Directory.Exists(R2SteamDir)) return;

        var vfs = VirtualFileSystem.Init(R2SteamDir, cache: ScanCache.Ephemeral(includeShipped: true));
        Assert.Equal(GameVersion.Runaway2, vfs.GameVersion);
        Assert.True(vfs.Summary.SceneArchives >= 70);
        Assert.True(vfs.Summary.AudioClips > 0);
        Assert.Equal(10454, vfs.Summary.VoiceClips);
        Assert.True(vfs.Summary.Videos > 0);

        // Verify scene categories and nodes
        var sceneFolder = vfs.Root.Children.FirstOrDefault(c => c.Name == VirtualFileSystem.ScenesFolder);
        Assert.NotNull(sceneFolder);
        Assert.True(sceneFolder.Children.Count >= 70);

        // Verify Voice folder
        var voiceFolder = vfs.Root.Children.FirstOrDefault(c => c.Name == VirtualFileSystem.VoiceFolder);
        Assert.NotNull(voiceFolder);
        Assert.NotEmpty(voiceFolder.Children);

        // Verify Audio has both WAV and MP3
        var musicFolder = vfs.Root.Children.FirstOrDefault(c => c.Name == VirtualFileSystem.MusicFolder);
        Assert.NotNull(musicFolder);
        var allAudioTracks = musicFolder.Children.SelectMany(c => c.Children).Where(c => c.Audio is not null).ToList();
        Assert.Contains(allAudioTracks, t => t.Audio!.Format == AudioFormat.Mp3);
        Assert.Contains(allAudioTracks, t => t.Audio!.Format == AudioFormat.Wav);
    }

    #endregion
}

/// <summary>
/// Runaway 2's interface art, which sits in the same RESOURCE.000 slots and the same formats as Runaway
/// 1's: cursors in a 904x540 raster over the key colour, screens at 1024x600, UI strips at 700x74. Its
/// fonts are not in this shape -- no slot pair chains as a glyph table -- so they use some other scheme.
/// </summary>
public class Runaway2InterfaceTests
{
    private const string R2SteamDir = @"F:\Games\Steam\steamapps\common\Runaway The Dream of the Turtle";

    [Fact]
    public void RealInstall_FontsArePairedSlotsWithWiderGlyphRecords()
    {
        // Runaway 2's fonts are Runaway 1's with a 32-bit offset in each glyph record: bitmap in one
        // slot, table in the next, the records tiling the bitmap to its last byte.
        if (!Directory.Exists(R2SteamDir)) return;

        var vfs = VirtualFileSystem.Init(R2SteamDir, cache: ScanCache.Ephemeral(includeShipped: true));
        FsNode global = vfs.Root.Children.Single(c => c.Name == VirtualFileSystem.GlobalFolder)
            .Children.Single(c => c.Name.Equals("RESOURCE.000", StringComparison.OrdinalIgnoreCase));

        List<FsNode> fonts = global.Children.Where(c => c.Kind == EntryKind.Font).ToList();
        Assert.Equal([123, 124, 125, 126, 146, 147, 161, 162], fonts.Select(c => c.EntryIndex));

        const int recordSize = FontDecoder.RecordSizeLater;
        foreach ((int bitmapSlot, int glyphs) in new[] { (123, 247), (125, 191), (146, 191), (161, 150) })
        {
            byte[] bitmap = vfs.ReadBytes(global.Children.Single(c => c.EntryIndex == bitmapSlot));
            byte[] table = vfs.ReadBytes(global.Children.Single(c => c.EntryIndex == bitmapSlot + 1));

            Assert.True(FontDecoder.IsGlyphTable(table, bitmap.Length, recordSize));
            Assert.Equal(glyphs, table.Length / recordSize);

            DecodedImage sheet = FontDecoder.Decode(bitmap, FontDecoder.ReadGlyphTable(table, recordSize));
            Assert.True(sheet.Width > 0 && sheet.Height > 0);
        }
    }

    [Fact]
    public void RealInstall_RastersOfTheirOwnSizeAreFoundByTheStrideSweep()
    {
        // Not everything in the global archive is one of the screen geometries: e155 and e156 are a
        // 264x104 pair, and their width is recovered the way a scene entry's is.
        if (!Directory.Exists(R2SteamDir)) return;

        var vfs = VirtualFileSystem.Init(R2SteamDir, cache: ScanCache.Ephemeral(includeShipped: true));
        FsNode global = vfs.Root.Children.Single(c => c.Name == VirtualFileSystem.GlobalFolder)
            .Children.Single(c => c.Name.Equals("RESOURCE.000", StringComparison.OrdinalIgnoreCase));

        foreach (int slot in new[] { 155, 156 })
        {
            FsNode node = global.Children.Single(c => c.EntryIndex == slot);
            Assert.Equal(EntryKind.Background, node.Kind);
            Assert.Equal((264, 104), (node.Image!.Width, node.Image.Height));
        }
    }

    [Fact]
    public void RealInstall_GlobalArchiveHoldsSixCursorAtlasesAndNoRunaway1Fonts()
    {
        string path = Path.Combine(R2SteamDir, "Resource", "RESOURCE.000");
        if (!File.Exists(path)) return;

        using FileStream fs = File.OpenRead(path);
        List<ArchiveEntry> entries = GlobalArchive.ReadEntries(fs, GameVersion.Runaway2);
        var byIndex = entries.ToDictionary(e => e.Index);

        var atlases = new List<int>();
        int glyphTables = 0;
        foreach (ArchiveEntry e in entries)
        {
            var data = new byte[Math.Min(e.Size, 20_000)];
            fs.Position = e.Offset;
            fs.ReadExactly(data);

            if (e.Size == CursorAtlasDecoder.AtlasWidth * CursorAtlasDecoder.AtlasHeight * 2
                && CursorAtlasDecoder.LooksLikeAtlas(data))
            {
                atlases.Add(e.Index);
            }

            if (e.Size % FontDecoder.RecordSize == 0 && byIndex.TryGetValue(e.Index - 1, out ArchiveEntry prev)
                && FontDecoder.IsGlyphTable(data, prev.Size))
            {
                glyphTables++;
            }
        }

        Assert.Equal([112, 113, 114, 115, 116, 117], atlases);
        Assert.Equal(0, glyphTables);
    }

    [Fact]
    public void RealInstall_GlobalAnimationsAreFoundAndWalkByteExact()
    {
        // Slots 87-94, 105-107 and 150/152 are the scene archives' sprite format sitting in the global
        // archive: a 14-byte frame record table followed by the frames, self-contained rather than split
        // over a pair of slots. Their repeating record tables are what a hex view shows as stripes.
        if (!Directory.Exists(R2SteamDir)) return;

        var vfs = VirtualFileSystem.Init(R2SteamDir, cache: ScanCache.Ephemeral(includeShipped: true));
        FsNode global = vfs.Root.Children.Single(c => c.Name == VirtualFileSystem.GlobalFolder)
            .Children.Single(c => c.Name.Equals("RESOURCE.000", StringComparison.OrdinalIgnoreCase));

        List<FsNode> animations = global.Children.Where(c => c.Kind == EntryKind.Animation).ToList();
        Assert.Equal(33, animations.Count);

        FsNode e087 = global.Children.Single(c => c.Name == "e087");
        Assert.Equal(EntryKind.Animation, e087.Kind);
        Assert.Equal(71, e087.Image!.Frames);
        Assert.Equal((354, 213), (e087.Image.Width, e087.Image.Height));

        // Every frame of every one of them walks byte-exact and paints something.
        foreach (FsNode node in animations)
        {
            SpriteAsset sprite = Assert.IsType<SpriteAsset>(SpriteAsset.Parse(vfs.ReadBytes(node)));
            Assert.Null(sprite.Verify());
            Assert.Contains(Enumerable.Range(0, sprite.FrameCount), i => !sprite.DecodeFrame(i).IsEmpty);
        }
    }

    [Fact]
    public void RealInstall_TheDataStreamCutIntoScreenSizedSlotsIsNotShownAsPictures()
    {
        // Slots 289-291 hold one long data stream cut into pieces of exactly 1024x600x2 bytes -- the
        // stream runs straight across the slot boundaries and into e292, which is a different size and
        // so was never in danger. Read as pixels they are noise, and a size match alone would show them
        // in the tree as screens.
        if (!Directory.Exists(R2SteamDir)) return;

        var vfs = VirtualFileSystem.Init(R2SteamDir, cache: ScanCache.Ephemeral(includeShipped: true));
        FsNode global = vfs.Root.Children.Single(c => c.Name == VirtualFileSystem.GlobalFolder)
            .Children.Single(c => c.Name.Equals("RESOURCE.000", StringComparison.OrdinalIgnoreCase));

        FsNode Entry(int index) => global.Children.Single(c => c.Name == $"e{index:000}");

        Assert.All([289, 290, 291], i => Assert.Equal(EntryKind.GlobalData, Entry(i).Kind));

        // The real screens at that same size still classify, and are still labelled by their geometry.
        Assert.All([80, 140, 269], i => Assert.Equal(EntryKind.Background, Entry(i).Kind));
        Assert.Equal((1024, 600), (Entry(140).Image!.Width, Entry(140).Image!.Height));
    }

    [Fact]
    public void RealInstall_TheFirstAtlasHoldsTheCursors()
    {
        string path = Path.Combine(R2SteamDir, "Resource", "RESOURCE.000");
        if (!File.Exists(path)) return;

        using FileStream fs = File.OpenRead(path);
        ArchiveEntry atlas = GlobalArchive.ReadEntries(fs, GameVersion.Runaway2).First(e => e.Index == 112);
        var data = new byte[atlas.Size];
        fs.Position = atlas.Offset;
        fs.ReadExactly(data);

        IReadOnlyList<CursorAtlasDecoder.SpriteBounds> cursors = CursorAtlasDecoder.FindCursors(data);
        Assert.InRange(cursors.Count, 8, 24);
        Assert.All(cursors, c => Assert.InRange(c.Width, 8, 64));

        DecodedImage image = CursorAtlasDecoder.DecodeAtlas(data);
        Assert.Equal((0, 0, 0, 0), SyntheticAssets.Pixel(image, 0, 0));
    }
}
