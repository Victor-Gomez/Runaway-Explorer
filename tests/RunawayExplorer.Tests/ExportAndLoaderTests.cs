using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Formats;
using RunawayExplorer.Core.Settings;
using RunawayExplorer.Core.Tests;
using RunawayExplorer.Services;
using Xunit;

namespace RunawayExplorer.Tests;

public class ResourceLoaderTests
{
    private static (VirtualFileSystem Vfs, FakeInstall Install, TempFileTracker Temp) Open()
    {
        var install = new FakeInstall();
        return (VirtualFileSystem.Init(install.Root), install, new TempFileTracker());
    }

    [Fact]
    public void DispatchesOnTheScanTimeKind()
    {
        (VirtualFileSystem vfs, FakeInstall install, TempFileTracker temp) = Open();
        using (install)
        {
            var settings = new AppSettings();
            FsNode h09 = vfs.FindNode(vfs.Root, "\\Scenes\\RESOURCE.H09")!;

            var bg = Assert.IsType<ImageResource>(ResourceLoader.Load(h09.Children[0], vfs, settings, temp));
            Assert.Equal((204, 120, false), (bg.Image.Width, bg.Image.Height, bg.Positioned));

            var data = Assert.IsType<TextResource>(ResourceLoader.Load(h09.Children[1], vfs, settings, temp));
            Assert.StartsWith("Scene-archive data entry", data.Text);
            Assert.Contains("byte(s)", data.Text);
            Assert.Contains("0000:0000 |", data.Text);

            var ov = Assert.IsType<ImageResource>(ResourceLoader.Load(h09.Children[2], vfs, settings, temp));
            Assert.Equal((320, 80, true), (ov.X, ov.Y, ov.Positioned));

            var anim = Assert.IsType<AnimationResource>(ResourceLoader.Load(h09.Children[3], vfs, settings, temp));
            Assert.Equal(2, anim.Asset.FrameCount);
        }
    }

    [Fact]
    public void MaskLoadsAsPositionedImageResource()
    {
        (VirtualFileSystem vfs, FakeInstall install, TempFileTracker temp) = Open();
        using (install)
        {
            string tempMaskPath = Path.Combine(install.Root, "mask_test.bin");
            byte[] maskBytes = SyntheticMaskBytes(1024, 600);
            File.WriteAllBytes(tempMaskPath, maskBytes);

            var maskNode = new FsNode
            {
                Name = "e01",
                Kind = EntryKind.Mask,
                ArchivePath = tempMaskPath,
                Offset = 0,
                Size = maskBytes.Length,
                NodeType = FsNodeType.File,
                Image = new ImageInfo { Width = 1024, Height = 600, IsMask = true }
            };

            var res = Assert.IsType<ImageResource>(ResourceLoader.Load(maskNode, vfs, new AppSettings(), temp));
            Assert.True(res.Positioned);
            Assert.Equal("scene mask", res.Kind);
            Assert.Equal(1024, res.Image.Width);
            Assert.Equal(600, res.Image.Height);

            var maskNodeNoInfo = new FsNode
            {
                Name = "e01",
                Kind = EntryKind.Mask,
                ArchivePath = tempMaskPath,
                Offset = 0,
                Size = maskBytes.Length,
                NodeType = FsNodeType.File,
                Image = null
            };

            var resNoInfo = Assert.IsType<ImageResource>(ResourceLoader.Load(maskNodeNoInfo, vfs, new AppSettings(), temp));
            Assert.True(resNoInfo.Positioned);
            Assert.Equal("scene mask", resNoInfo.Kind);
            Assert.Equal(1024, resNoInfo.Image.Width);
            Assert.Equal(600, resNoInfo.Image.Height);
        }
    }

    private static byte[] SyntheticMaskBytes(int width, int height)
    {
        using var ms = new MemoryStream();
        Span<byte> u16 = stackalloc byte[2];
        for (int y = 0; y < height; y++)
        {
            ushort left = (ushort)(width / 2);
            ushort right = (ushort)(width - left);
            ms.WriteByte(1);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(u16, left);
            ms.Write(u16);
            ms.WriteByte(2);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(u16, right);
            ms.Write(u16);
        }
        return ms.ToArray();
    }

    [Fact]
    public void SoundsBecomeWavTempFiles_WithTheVoiceRateFromSettings()
    {
        (VirtualFileSystem vfs, FakeInstall install, TempFileTracker temp) = Open();
        using (install)
        {
            var settings = new AppSettings { VoiceSampleRate = 11025 };
            FsNode music = vfs.FindNode(vfs.Root, "\\Music\\RESOURCE.M01\\t001")!;
            FsNode voice = vfs.FindNode(vfs.Root, "\\Voice\\00000-00499\\VOICE_00001")!;

            var m = Assert.IsType<SoundResource>(ResourceLoader.Load(music, vfs, settings, temp));
            Assert.Equal(16_000, m.Pcm.SampleRate);
            Assert.Equal(44 + 64000, new FileInfo(m.TempFilePath).Length);
            Assert.Equal(1.0, m.DurationSeconds, 3);

            var v = Assert.IsType<SoundResource>(ResourceLoader.Load(voice, vfs, settings, temp));
            Assert.Equal(11025, v.Pcm.SampleRate);
            Assert.Equal(8, v.Pcm.BitsPerSample);
            Assert.Equal(0.2, v.DurationSeconds, 3);
        }
    }

    [Fact]
    public void VideosAreRestoredToTempBikFiles()
    {
        (VirtualFileSystem vfs, FakeInstall install, TempFileTracker temp) = Open();
        using (install)
        {
            FsNode video = vfs.FindNode(vfs.Root, "\\Video\\DATAVA01.001")!;
            var v = Assert.IsType<VideoResource>(ResourceLoader.Load(video, vfs, new AppSettings(), temp));
            Assert.EndsWith(".bik", v.TempFilePath);
            Assert.Equal("BIKi"u8.ToArray(), File.ReadAllBytes(v.TempFilePath)[..4]);
        }
    }

    [Fact]
    public void VisemesAreTextOneValuePerLine()
    {
        (VirtualFileSystem vfs, FakeInstall install, TempFileTracker temp) = Open();
        using (install)
        {
            FsNode track = vfs.FindNode(vfs.Root, "\\Lip-sync\\RESOURCE.004\\v00000")!;
            var t = Assert.IsType<TextResource>(ResourceLoader.Load(track, vfs, new AppSettings(), temp));
            Assert.EndsWith("0\n1\n2\n", t.Text);
            Assert.Contains("VOICE_00000", t.Text);
        }
    }

    [Fact]
    public void SceneFolder_SummarisesAndCarriesTheBackground()
    {
        (VirtualFileSystem vfs, FakeInstall install, TempFileTracker temp) = Open();
        using (install)
        {
            FsNode h09 = vfs.FindNode(vfs.Root, "\\Scenes\\RESOURCE.H09")!;
            SceneResource scene = ResourceLoader.LoadScene(h09, vfs);
            Assert.NotNull(scene.Background);
            Assert.Contains("1 background(s), 1 overlay(s), 1 animation(s) with 2 frame(s), 1 data entry", scene.Summary);
        }
    }
}

public class BatchExporterTests
{
    [Fact]
    public void ExportsEveryDecodableKind_PreservingTheTreeLayout()
    {
        using var install = new FakeInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);
        string outDir = Path.Combine(install.Root, "out");
        var options = new BatchExportOptions(AnimationFps: 12, VoiceSampleRate: 22050, AnimationFrames: true);

        var seen = new List<BatchExportProgress>();
        BatchExportSummary summary = BatchExporter.ExportSubtree(vfs.Root, vfs, outDir, options, seen.Add);

        Assert.Equal(0, summary.FailedCount);
        // Everything but the two data-ish entries (the 1536-byte table, RESOURCE.000's entry, RESOURCE.003, the unknown video).
        Assert.Equal(4, summary.SkippedCount);
        Assert.Equal(seen.Count, summary.ExportedCount + summary.SkippedCount + summary.FailedCount);
        Assert.Equal(seen.Count, seen[^1].Total);

        Assert.True(File.Exists(Path.Combine(outDir, "Scenes", "RESOURCE.H09", "e00_204x120.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "Scenes", "RESOURCE.H09", "e02_1x1_at_320_80.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "Scenes", "RESOURCE.H09", "e03.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "Scenes", "RESOURCE.H09", "e03_frames", "frame_0000_x10_y20.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "Scenes", "RESOURCE.H09", "e03_frames", "frame_0001_x11_y21.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "Music", "RESOURCE.M01", "t001.wav")));
        Assert.True(File.Exists(Path.Combine(outDir, "Voice", "00000-00499", "VOICE_00001.wav")));
        Assert.True(File.Exists(Path.Combine(outDir, "Video", "DATAVA01_001.bik")));
        Assert.True(File.Exists(Path.Combine(outDir, "Lip-sync", "RESOURCE.004", "v00000.txt")));
        Assert.False(File.Exists(Path.Combine(outDir, "Scenes", "RESOURCE.H09", "e01.bin")));
    }

    [Fact]
    public void FramesCanBeLeftOut_AndFpsReachesTheApng()
    {
        using var install = new FakeInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);
        FsNode anim = vfs.FindNode(vfs.Root, "\\Scenes\\RESOURCE.H09\\e03")!;
        string outDir = Path.Combine(install.Root, "out");

        BatchExporter.ExportSubtree(anim, vfs, outDir, new BatchExportOptions(24, 22050, AnimationFrames: false));

        Assert.True(File.Exists(Path.Combine(outDir, "e03.png")));
        Assert.False(Directory.Exists(Path.Combine(outDir, "e03_frames")));
        // 24 fps -> 42/1000 s per frame in the first fcTL.
        byte[] png = File.ReadAllBytes(Path.Combine(outDir, "e03.png"));
        int fctl = IndexOf(png, "fcTL"u8);
        Assert.True(fctl > 0);
        int delayNum = (png[fctl + 4 + 20] << 8) | png[fctl + 4 + 21];
        Assert.Equal(42, delayNum);
    }

    [Fact]
    public void Cancellation_StopsTheWalk()
    {
        using var install = new FakeInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => BatchExporter.ExportSubtree(
            vfs.Root, vfs, Path.Combine(install.Root, "out"), new BatchExportOptions(12, 22050), cancellationToken: cts.Token));
    }

    [Theory]
    [InlineData(EntryKind.Background, "e00_204x120.png")]
    [InlineData(EntryKind.Overlay, "e00_204x120_at_5_6.png")]
    [InlineData(EntryKind.Animation, "e00.png")]
    [InlineData(EntryKind.Music, "e00.wav")]
    [InlineData(EntryKind.Viseme, "e00.txt")]
    [InlineData(EntryKind.Data, "e00.bin")]
    public void ExportFileNames(EntryKind kind, string expected)
    {
        var node = new FsNode { Name = "e00", Kind = kind, Image = new ImageInfo { Width = 204, Height = 120, X = 5, Y = 6 } };
        Assert.Equal(expected, BatchExporter.ExportFileName(node));
    }

    [Fact]
    public void ExportImageSequence_WritesIndividualFrames()
    {
        using var install = new FakeInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);
        FsNode animNode = vfs.FindNode(vfs.Root, "\\Scenes\\RESOURCE.H09\\e03")!;
        byte[] data = vfs.ReadBytes(animNode);
        SpriteAsset asset = SpriteAsset.Parse(data)!;

        string outDir = Path.Combine(install.Root, "frames_out");
        int written = BatchExporter.ExportImageSequence(asset, outDir, "custom_frame");

        Assert.True(written > 0);
        Assert.Equal(asset.FrameCount, written);
        string[] files = Directory.GetFiles(outDir, "custom_frame_*.png");
        Assert.Equal(written, files.Length);
    }

    [Fact]
    public void ResourceLoader_LoadsPngAndJpegBackgrounds()
    {
        using var install = new FakeInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);
        var settings = new AppSettings();
        var temp = new TempFileTracker();

        // Synthetic PNG background
        var decoded = new DecodedImage(4, 4, new byte[4 * 4 * 4]);
        byte[] pngBytes = PngWriter.ToBytes(decoded);
        string pngFile = Path.Combine(install.Root, "test_bg.png");
        File.WriteAllBytes(pngFile, pngBytes);

        var pngNode = new FsNode
        {
            Name = "e00",
            Kind = EntryKind.Background,
            ArchivePath = pngFile,
            Offset = 0,
            Size = pngBytes.Length,
            NodeType = FsNodeType.File,
            Image = new ImageInfo { Width = 4, Height = 4 }
        };

        var pngRes = Assert.IsType<ImageResource>(ResourceLoader.Load(pngNode, vfs, settings, temp));
        Assert.Equal(4, pngRes.Image.Width);
        Assert.Equal(4, pngRes.Image.Height);
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                return i;
        }
        return -1;
    }
}
