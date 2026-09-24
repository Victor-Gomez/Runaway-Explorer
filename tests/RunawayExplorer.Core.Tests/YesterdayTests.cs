using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Formats;
using RunawayExplorer.Core.Metadata;
using RunawayExplorer.Core.Settings;
using Xunit;

namespace RunawayExplorer.Core.Tests;

public class YesterdayTests
{
    public const string YesterdaySteamDir = @"F:\Games\Steam\steamapps\common\Yesterday";

    [Fact]
    public void GameDetector_DetectsTheSignature()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Yesterday.exe"), "dummy");
            Assert.Equal(GameVersion.Yesterday, GameDetector.Detect(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GameDetector_DetectsARealInstall()
    {
        if (Directory.Exists(YesterdaySteamDir))
            Assert.Equal(GameVersion.Yesterday, GameDetector.Detect(YesterdaySteamDir));
    }

    [Fact]
    public void AppSettings_ManagesTheYesterdayPath()
    {
        var settings = new AppSettings
        {
            YesterdayDir = @"C:\Games\Yesterday",
            ActiveGame = GameVersion.Yesterday
        };

        Assert.Equal(@"C:\Games\Yesterday", settings.GetGameDir(GameVersion.Yesterday));
        Assert.Equal(@"C:\Games\Yesterday", settings.ActiveGameDir);

        settings.SetGameDir(GameVersion.Yesterday, @"D:\Yesterday");
        Assert.Equal(@"D:\Yesterday", settings.GetGameDir(GameVersion.Yesterday));
    }

    [Fact]
    public void SceneArchive_RecognizesYesterdayNames()
    {
        // Scene archives
        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.A00", GameVersion.Yesterday));
        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.H01", GameVersion.Yesterday));
        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.SP8", GameVersion.Yesterday));

        // Non-scene archives
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.DIS", GameVersion.Yesterday));
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.TAB", GameVersion.Yesterday));
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.CRD", GameVersion.Yesterday));
    }

    [Fact]
    public void RealInstall_CanLoadAndScan()
    {
        if (!Directory.Exists(YesterdaySteamDir))
            return;

        var cache = ScanCache.Ephemeral();
        var vfs = VirtualFileSystem.Init(YesterdaySteamDir, cache: cache);
        Assert.Equal(GameVersion.Yesterday, vfs.GameVersion);
        Assert.True(vfs.Summary.SceneArchives > 0);
        Assert.True(vfs.Summary.Backgrounds > 0);
        Assert.True(vfs.Summary.Videos > 0);

        // Second scan with same cache must hit cache
        var vfs2 = VirtualFileSystem.Init(YesterdaySteamDir, cache: cache);
        Assert.True(vfs2.Summary.FromCache);
        Assert.Equal(vfs.Summary.SceneArchives, vfs2.Summary.SceneArchives);
        Assert.Equal(vfs.Summary.Backgrounds, vfs2.Summary.Backgrounds);
        Assert.True(vfs.Summary.Animations > 0, "Must classify Yesterday sprite animations");
        Assert.True(vfs.Summary.Masks > 0, "Must classify Yesterday scene masks");
        Assert.True(vfs.Summary.Overlays > 0, "Must classify Yesterday overlays");
    }

    [Fact]
    public void Yesterday_AssetsDecodeProperly()
    {
        if (!Directory.Exists(YesterdaySteamDir)) return;
        string resA01 = Path.Combine(YesterdaySteamDir, "Resource", "RESOURCE.A01");
        if (!File.Exists(resA01)) return;

        using var stream = File.OpenRead(resA01);
        byte[] tableHeader = new byte[65536];
        int tableRead = stream.Read(tableHeader, 0, tableHeader.Length);
        var entries = SceneArchive.ReadEntries(tableHeader.AsSpan(0, tableRead), stream.Length);

        // 1. Entry 0: JPEG Background (1920x1080)
        var e0 = entries[0];
        byte[] e0Bytes = new byte[e0.Size];
        stream.Seek(e0.Offset, SeekOrigin.Begin);
        stream.ReadExactly(e0Bytes, 0, e0Bytes.Length);

        Assert.True(JpegDecoder.IsJpeg(e0Bytes));
        Assert.True(JpegDecoder.TryGetDimensions(e0Bytes, out int jw, out int jh));
        Assert.Equal(1920, jw);
        Assert.Equal(1080, jh);

        var bgImage = JpegDecoder.Decode(e0Bytes);
        Assert.NotNull(bgImage);
        Assert.Equal(1920, bgImage.Width);
        Assert.Equal(1080, bgImage.Height);
        Assert.Equal(1920 * 1080 * 4, bgImage.Pixels.Length);

        // Classifier classifies Entry 0 as Background
        var c0 = EntryClassifier.Classify(e0Bytes, 1920, 1080, GameVersion.Yesterday);
        Assert.Equal(EntryKind.Background, c0.Kind);

        // 2. Entry 1: 4-byte RLE Mask (1920x1080)
        var e1 = entries[1];
        byte[] e1Bytes = new byte[e1.Size];
        stream.Seek(e1.Offset, SeekOrigin.Begin);
        stream.ReadExactly(e1Bytes, 0, e1Bytes.Length);

        var maskInfo = RleMaskDecoder.Detect(e1Bytes, 1920, 1080);
        Assert.NotNull(maskInfo);
        Assert.Equal(1920, maskInfo.Value.Width);
        Assert.Equal(1080, maskInfo.Value.Height);
        Assert.Equal(4, maskInfo.Value.RunBytes);

        var maskImage = RleMaskDecoder.Decode(e1Bytes, 1920, 1080);
        Assert.Equal(1920, maskImage.Width);
        Assert.Equal(1080, maskImage.Height);

        var c1 = EntryClassifier.Classify(e1Bytes, 1920, 1080, GameVersion.Yesterday);
        Assert.Equal(EntryKind.Mask, c1.Kind);

        // 3. Entry 9: Sprite animation (flag 6 / BGR24)
        var e9 = entries[9];
        byte[] e9Bytes = new byte[e9.Size];
        stream.Seek(e9.Offset, SeekOrigin.Begin);
        stream.ReadExactly(e9Bytes, 0, e9Bytes.Length);

        var sprite = SpriteAsset.Parse(e9Bytes);
        Assert.NotNull(sprite);
        Assert.Equal(2, sprite.FrameCount);
        Assert.Null(sprite.Verify());

        var frame0 = sprite.DecodeFrame(0);
        Assert.NotNull(frame0.Image);
        Assert.True(frame0.Image.Width > 0);
        Assert.True(frame0.Image.Height > 0);

        var c9 = EntryClassifier.Classify(e9Bytes, 1920, 1080, GameVersion.Yesterday);
        Assert.Equal(EntryKind.Animation, c9.Kind);
    }

    [Fact]
    public void Yesterday_ResourceC02_Entry04_DecodesAsPng()
    {
        if (!Directory.Exists(YesterdaySteamDir)) return;
        string resC02 = Path.Combine(YesterdaySteamDir, "Resource", "RESOURCE.C02");
        if (!File.Exists(resC02)) return;

        using var stream = File.OpenRead(resC02);
        byte[] tableHeader = new byte[65536];
        int tableRead = stream.Read(tableHeader, 0, tableHeader.Length);
        var entries = SceneArchive.ReadEntries(tableHeader.AsSpan(0, tableRead), stream.Length);
        Assert.True(entries.Count > 4);

        var e4 = entries[4];
        byte[] e4Bytes = new byte[e4.Size];
        stream.Seek(e4.Offset, SeekOrigin.Begin);
        stream.ReadExactly(e4Bytes, 0, e4Bytes.Length);

        Assert.True(PngDecoder.IsPng(e4Bytes));
        var decoded = PngDecoder.Decode(e4Bytes);
        Assert.NotNull(decoded);
        Assert.Equal(1920, decoded.Width);
        Assert.Equal(1080, decoded.Height);
        Assert.Equal(1920 * 1080 * 4, decoded.Pixels.Length);

        var classification = EntryClassifier.Classify(e4Bytes, 1920, 1080, GameVersion.Yesterday);
        Assert.Equal(EntryKind.Mask, classification.Kind);
        Assert.True(classification.Image!.IsMask);
    }

    [Fact]
    public void Yesterday_ResourceD02_Entries04_05_06_ClassifiedAsMasks()
    {
        if (!Directory.Exists(YesterdaySteamDir)) return;
        string resD02 = Path.Combine(YesterdaySteamDir, "Resource", "RESOURCE.D02");
        if (!File.Exists(resD02)) return;

        using var stream = File.OpenRead(resD02);
        byte[] tableHeader = new byte[65536];
        int tableRead = stream.Read(tableHeader, 0, tableHeader.Length);
        var entries = SceneArchive.ReadEntries(tableHeader.AsSpan(0, tableRead), stream.Length);

        // Entries 4, 5, 6 are grayscale PNG mask layers
        for (int i = 4; i <= 6; i++)
        {
            var e = entries[i];
            byte[] bytes = new byte[e.Size];
            stream.Seek(e.Offset, SeekOrigin.Begin);
            stream.ReadExactly(bytes, 0, bytes.Length);

            Assert.True(PngDecoder.IsPng(bytes));
            var c = EntryClassifier.Classify(bytes, 1920, 1080, GameVersion.Yesterday);
            Assert.Equal(EntryKind.Mask, c.Kind);
            Assert.True(c.Image!.IsMask);
            Assert.Equal(1920, c.Image.Width);
            Assert.Equal(1080, c.Image.Height);

            // ResourceLoader / PngDecoder decodes it successfully
            var img = PngDecoder.Decode(bytes);
            Assert.NotNull(img);
            Assert.Equal(1920, img.Width);
            Assert.Equal(1080, img.Height);
        }
    }

    [Fact]
    public void Yesterday_ResourceE00_Entries00_to_04_ClassifiedAsSprites()
    {
        if (!Directory.Exists(YesterdaySteamDir)) return;
        string resE00 = Path.Combine(YesterdaySteamDir, "Resource", "RESOURCE.E00");
        if (!File.Exists(resE00)) return;

        using var stream = File.OpenRead(resE00);
        byte[] tableHeader = new byte[65536];
        int tableRead = stream.Read(tableHeader, 0, tableHeader.Length);
        var entries = SceneArchive.ReadEntries(tableHeader.AsSpan(0, tableRead), stream.Length);

        // Entries 0 through 4 are object sprite sheets (RGBA PNGs with transparency)
        for (int i = 0; i <= 4; i++)
        {
            var e = entries[i];
            byte[] bytes = new byte[e.Size];
            stream.Seek(e.Offset, SeekOrigin.Begin);
            stream.ReadExactly(bytes, 0, bytes.Length);

            Assert.True(PngDecoder.IsPng(bytes));
            Assert.Equal(6, bytes[25]); // colorType == 6 (RGBA)

            var c = EntryClassifier.Classify(bytes, null, null, GameVersion.Yesterday);
            // Must be classified as Overlay/Sprite, NEVER as Background
            Assert.Equal(EntryKind.Overlay, c.Kind);
            Assert.NotNull(c.Image);
            Assert.Equal(0, c.Image.X);
            Assert.Equal(0, c.Image.Y);

            var node = new FsNode
            {
                Name = $"e{i:D2}",
                Kind = c.Kind,
                Image = c.Image,
            };
            string label = SceneCatalog.FormatSceneEntryLabel(node, "en");
            Assert.StartsWith("sprite ", label);
        }

        // Test VFS scan: RESOURCE.E00 has no scene background
        var vfs = VirtualFileSystem.Init(YesterdaySteamDir, cache: ScanCache.Ephemeral());
        var e00Archive = vfs.Root.Children
            .FirstOrDefault(c => c.Name == VirtualFileSystem.ScenesFolder)?
            .Children.FirstOrDefault(c => c.Name.Equals("RESOURCE.E00", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(e00Archive);
        Assert.Null(vfs.SceneBackgroundFor(e00Archive));

        foreach (var child in e00Archive.Children.Take(5))
        {
            Assert.Equal(EntryKind.Overlay, child.Kind);
            Assert.NotNull(child.Image);
        }
    }

    [Fact]
    public void ResourceD02_ClassifiesAndDecodesCorrectly()
    {
        if (!Directory.Exists(YesterdaySteamDir))
            return;

        var vfs = VirtualFileSystem.Init(YesterdaySteamDir, cache: ScanCache.Ephemeral());
        var d02 = vfs.Root.Children
            .FirstOrDefault(c => c.Name == VirtualFileSystem.ScenesFolder)?
            .Children.FirstOrDefault(c => c.Name.Equals("RESOURCE.D02", StringComparison.OrdinalIgnoreCase));
        if (d02 is null)
            return;

        var e00 = d02.Children.FirstOrDefault(c => c.EntryIndex == 0);
        Assert.NotNull(e00);
        Assert.Equal(EntryKind.Background, e00.Kind);
        Assert.Equal(1920, e00.Image?.Width);

        // Yesterday's D02 has no 1536-byte attribute table.
        Assert.Null(vfs.SceneAttributeTableFor(d02));
    }
}
