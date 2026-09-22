using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Formats;
using Xunit;
using static RunawayExplorer.Core.Tests.SyntheticAssets;

namespace RunawayExplorer.Core.Tests;

/// <summary>
/// A throwaway install on disk with one of everything: a scene archive holding all three image formats
/// plus a data table, a music and an ambient archive, cinematic audio with an aliased slot, a viseme
/// table, two voice shards, a global archive, an unknown loose file, and a keyfile + video pair.
/// </summary>
public sealed class FakeInstall : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("runaway-install").FullName;

    public byte[] Background { get; } = Raster(204, 120);
    public byte[] Overlay { get; } = SyntheticAssets.Overlay([(320, 80, [Rgb565(255, 0, 0)])]);
    public byte[] Sprite { get; } = SyntheticAssets.Sprite([new Frame([(10, 20, [1, 2])]), new Frame([(11, 21, [3])])]);
    public byte[] VideoHeader { get; } = new byte[1024];

    public FakeInstall()
    {
        string res = Path.Combine(Root, "Resource");
        string dataa = Path.Combine(Root, "Dataa");
        string datav = Path.Combine(Root, "Datav");
        Directory.CreateDirectory(res);
        Directory.CreateDirectory(dataa);
        Directory.CreateDirectory(datav);

        File.WriteAllBytes(Path.Combine(res, "RESOURCE.H09"), SyntheticArchives.Scene([Background, new byte[1536], Overlay, Sprite]));
        File.WriteAllBytes(Path.Combine(res, "Resource.h13"), SyntheticArchives.Scene([Background]));
        File.WriteAllBytes(Path.Combine(res, "RESOURCE.M01"), SyntheticArchives.Audio([new byte[64000], new byte[32000]]));
        File.WriteAllBytes(Path.Combine(res, "Resource.s03"), SyntheticArchives.Audio([new byte[8820]]));
        byte[] cine = SyntheticArchives.Audio([new byte[100]]);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(cine.AsSpan(2 * 4), 400); // slot 2 aliases slot 1
        File.WriteAllBytes(Path.Combine(res, "RESOURCE.002"), cine);
        File.WriteAllBytes(Path.Combine(res, "RESOURCE.004"), SyntheticArchives.Visemes([new byte[] { 0, 1, 2 }, null, new byte[] { 3 }]));
        File.WriteAllBytes(Path.Combine(res, "RESOURCE.000"), SyntheticArchives.Global([new byte[10]]));
        File.WriteAllBytes(Path.Combine(res, "RESOURCE.003"), new byte[321]);

        File.WriteAllBytes(Path.Combine(dataa, "DATAACA0.000"), SyntheticArchives.VoiceShard(new Dictionary<int, byte[]> { [1] = new byte[2205], [600] = new byte[10] }));
        File.WriteAllBytes(Path.Combine(dataa, "DATAACA1.000"), SyntheticArchives.VoiceShard(new Dictionary<int, byte[]> { [2] = new byte[100] }));

        "BIKi"u8.CopyTo(VideoHeader);
        File.WriteAllBytes(Path.Combine(datav, "DATAVC00.000"), SyntheticArchives.Keyfile([("DATAVA01.001", VideoHeader)]));
        var video = new byte[1024 + 50];
        Array.Fill(video, (byte)7, 1024, 50);
        File.WriteAllBytes(Path.Combine(datav, "DATAVA01.001"), video);
        File.WriteAllBytes(Path.Combine(datav, "DATAVZ99.001"), new byte[2000]);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

public class VirtualFileSystemTests
{
    [Fact]
    public void BuildsTheCategorisedTree()
    {
        using var install = new FakeInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);

        Assert.Equal(
            [VirtualFileSystem.ScenesFolder, VirtualFileSystem.MusicFolder, VirtualFileSystem.AmbientFolder, VirtualFileSystem.CinematicFolder,
             VirtualFileSystem.VoiceFolder, VirtualFileSystem.LipSyncFolder, VirtualFileSystem.VideoFolder, VirtualFileSystem.GlobalFolder],
            vfs.Root.Children.Select(c => c.Name));

        FsNode scenes = vfs.Root.Children[0];
        Assert.Equal(["RESOURCE.H09", "Resource.h13"], scenes.Children.Select(c => c.Name));

        FsNode h09 = scenes.Children[0];
        Assert.Equal([EntryKind.Background, EntryKind.Data, EntryKind.Overlay, EntryKind.Animation], h09.Children.Select(c => c.Kind));
        Assert.Equal("overlay 1×1 at 320,80 (e02)", h09.Children[2].DisplayName);
        Assert.Equal("animation, 2 frames, 2×2 (e03)", h09.Children[3].DisplayName);
        Assert.Equal("background 204×120 (e00)", h09.Children[0].DisplayName);

        Assert.Equal(2, vfs.Summary.SceneArchives);
        Assert.Equal((2, 1, 1, 1), (vfs.Summary.Backgrounds, vfs.Summary.Overlays, vfs.Summary.Animations, vfs.Summary.DataEntries));

        // In the tree, items inside a folder/resource are ordered by type with code/data last:
        List<FsNode> files = vfs.GetFiles(h09).ToList();
        Assert.Equal([EntryKind.Background, EntryKind.Overlay, EntryKind.Animation, EntryKind.Data], files.Select(c => c.Kind));
        Assert.Equal("e00", files[0].Name);
        Assert.Equal("e02", files[1].Name);
        Assert.Equal("e03", files[2].Name);
        Assert.Equal("e01", files[3].Name);
    }

    [Fact]
    public void AudioEntriesCarryTheirPcmShape_AndCinematicAliasesAreDeduped()
    {
        using var install = new FakeInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);

        FsNode music = vfs.FindNode(vfs.Root, $"\\{VirtualFileSystem.MusicFolder}\\RESOURCE.M01")!;
        Assert.Equal(2, music.Children.Count);
        Assert.Equal(EntryKind.Music, music.Children[0].Kind);
        Assert.Equal(16_000, music.Children[0].Audio!.SampleRate);
        Assert.Equal("t001  0:01", music.Children[0].DisplayName); // 64000 bytes at 64 KB/s

        FsNode cine = vfs.Root.Children.First(c => c.Name == VirtualFileSystem.CinematicFolder).Children[0];
        Assert.Single(cine.Children);

        FsNode ambient = vfs.Root.Children.First(c => c.Name == VirtualFileSystem.AmbientFolder).Children[0];
        Assert.Equal(22_050, ambient.Children[0].Audio!.SampleRate);
        Assert.Equal(4, vfs.Summary.AudioClips);
    }

    [Fact]
    public void VoiceIsGroupedInBlocks_AndMergedAcrossShards()
    {
        using var install = new FakeInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);

        FsNode voice = vfs.Root.Children.First(c => c.Name == VirtualFileSystem.VoiceFolder);
        Assert.Equal(["00000-00499", "00500-00999"], voice.Children.Select(c => c.Name));
        Assert.Equal(["VOICE_00001", "VOICE_00002"], voice.Children[0].Children.Select(c => c.Name));
        Assert.Equal("VOICE_00001  0:00", voice.Children[0].Children[0].DisplayName);
        Assert.Equal(3, vfs.Summary.VoiceClips);
    }

    [Fact]
    public void VideosNeedAHeaderInTheKeyfile()
    {
        using var install = new FakeInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);

        FsNode video = vfs.Root.Children.First(c => c.Name == VirtualFileSystem.VideoFolder);
        Assert.Equal([EntryKind.Video, EntryKind.RawFile], video.Children.Select(c => c.Kind));
        Assert.Equal(1, vfs.Summary.Videos);

        using var restored = new MemoryStream();
        Assert.True(vfs.RestoreVideo(video.Children[0], restored));
        Assert.Equal(1074, restored.Length);
        Assert.Equal("BIKi"u8.ToArray(), restored.ToArray()[..4]);
        Assert.False(vfs.RestoreVideo(video.Children[1], new MemoryStream()));
    }

    [Fact]
    public void OpenFile_ServesExactlyTheEntryBytes()
    {
        using var install = new FakeInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);
        FsNode overlay = vfs.FindNode(vfs.Root, "\\Scenes\\RESOURCE.H09\\e02")!;

        Assert.Equal(install.Overlay, vfs.ReadBytes(overlay));
        using Stream s = vfs.OpenFile(overlay);
        Assert.Equal(install.Overlay.Length, s.Length);
    }

    [Fact]
    public void SceneBackground_IsEntryZeroOfTheOwningArchive_AndCached()
    {
        using var install = new FakeInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);
        FsNode sprite = vfs.FindNode(vfs.Root, "\\Scenes\\RESOURCE.H09\\e03")!;

        DecodedImage? bg = vfs.SceneBackgroundFor(sprite);
        Assert.NotNull(bg);
        Assert.Equal((204, 120), (bg.Width, bg.Height));
        Assert.Same(bg, vfs.SceneBackgroundFor(sprite.Parent!));
        Assert.Null(vfs.SceneBackgroundFor(vfs.FindNode(vfs.Root, "\\Music\\RESOURCE.M01\\t001")!));
    }

    [Fact]
    public void GlobalFolder_HoldsResource000EntriesAndUnknownFiles()
    {
        using var install = new FakeInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);
        FsNode global = vfs.Root.Children.First(c => c.Name == VirtualFileSystem.GlobalFolder);

        Assert.Equal(["RESOURCE.000", "RESOURCE.003"], global.Children.Select(c => c.Name));
        Assert.Equal(EntryKind.GlobalData, global.Children[0].Children[0].Kind);
        Assert.Equal(EntryKind.RawFile, global.Children[1].Kind);
    }

    [Fact]
    public void FindNode_IsCaseInsensitive_AndPathsRoundTrip()
    {
        using var install = new FakeInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);
        FsNode node = vfs.FindNode(vfs.Root, "\\scenes\\resource.h09\\E03")!;
        Assert.Equal("\\Scenes\\RESOURCE.H09\\e03", node.GetPath());
        Assert.Same(node, vfs.FindNode(vfs.Root, node.GetPath()));
        Assert.Null(vfs.FindNode(vfs.Root, "\\Nope"));
    }

    [Fact]
    public void MissingResourceFolder_IsAClearError()
    {
        string dir = Directory.CreateTempSubdirectory("runaway-empty").FullName;
        try
        {
            Assert.Throws<DirectoryNotFoundException>(() => VirtualFileSystem.Init(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ScanCache_ReplaysClassifications_AndInvalidatesOnChange()
    {
        using var install = new FakeInstall();
        string cachePath = Path.Combine(install.Root, "cache.json");

        ScanCache cache = ScanCache.Load(cachePath);
        VirtualFileSystem first = VirtualFileSystem.Init(install.Root, cache: cache);
        Assert.False(first.Summary.FromCache);
        Assert.True(File.Exists(cachePath));

        ScanCache reloaded = ScanCache.Load(cachePath);
        VirtualFileSystem second = VirtualFileSystem.Init(install.Root, cache: reloaded);
        Assert.True(second.Summary.FromCache);
        Assert.Equal(
            first.FindNode(first.Root, "\\Scenes\\RESOURCE.H09\\e03")!.DisplayName,
            second.FindNode(second.Root, "\\Scenes\\RESOURCE.H09\\e03")!.DisplayName);

        // Modify the archive with new content: its content hash changes, so its cached answer no longer applies and it is rescanned.
        string archive = Path.Combine(install.Root, "Resource", "RESOURCE.H09");
        File.WriteAllBytes(archive, SyntheticArchives.Scene([Raster(100, 100)]));
        ScanCache stale = ScanCache.Load(cachePath);
        Assert.Null(stale.TryGet(archive));
        VirtualFileSystem third = VirtualFileSystem.Init(install.Root, cache: stale);
        Assert.Single(third.FindNode(third.Root, "\\Scenes\\RESOURCE.H09")!.Children);
        Assert.NotNull(stale.TryGet(archive));
    }

    [Fact]
    public void ScanCache_HitsShippedCacheForKnownHash()
    {
        Assert.True(ScanCache.ShippedCount >= 75, $"Expected at least 75 shipped archive hashes, found {ScanCache.ShippedCount}");

        // Load an ephemeral or empty cache with shipped enabled
        string emptyPath = Path.Combine(Path.GetTempPath(), $"empty_cache_{Guid.NewGuid():N}.json");
        ScanCache cache = ScanCache.Load(emptyPath, includeShipped: true);

        // Verify lookup of known Runaway 1 Steam RESOURCE.A00 hash
        const string a00Hash = "04e5d8652091d40aee69899c238b2004d977e09fa8b380515739389c704c5078";
        var entries = cache.TryGetByHash(a00Hash);
        Assert.NotNull(entries);
        Assert.Equal(13, entries.Count);
    }

    [Fact]
    public void ScanCache_PreservesCacheAcrossFileMove()
    {
        using var install = new FakeInstall();
        string cachePath = Path.Combine(install.Root, "cache.json");

        ScanCache cache = ScanCache.Load(cachePath);
        string originalArchive = Path.Combine(install.Root, "Resource", "RESOURCE.H09");
        _ = VirtualFileSystem.Init(install.Root, cache: cache);

        // Copy archive to an entirely different path and change timestamp
        string movedDir = Path.Combine(install.Root, "MovedResource");
        Directory.CreateDirectory(movedDir);
        string movedArchive = Path.Combine(movedDir, "RESOURCE.H09");
        File.Copy(originalArchive, movedArchive);
        File.SetLastWriteTimeUtc(movedArchive, DateTime.UtcNow.AddHours(2));

        // Looking up the moved archive in the same cache should hit via content hash
        var movedEntries = cache.TryGet(movedArchive);
        Assert.NotNull(movedEntries);
        Assert.Equal(4, movedEntries.Count);
    }

    [Fact]
    public void ScanCache_ComputeHash_IsConsistent()
    {
        byte[] testData = "Runaway Explorer Asset Scan Cache"u8.ToArray();
        string fromBytes = ScanCache.ComputeHash(testData);
        using var ms = new MemoryStream(testData);
        string fromStream = ScanCache.ComputeHash(ms);

        Assert.Equal(64, fromBytes.Length);
        Assert.Equal(fromBytes, fromStream);
        Assert.Equal(fromBytes.ToLowerInvariant(), fromBytes);
    }

    [Fact]
    public void RealGameInstall_IfPresent_LoadsFromShippedCacheInstantly()
    {
        const string steamPath = @"F:\Games\Steam\steamapps\common\Runaway A Road Adventure";
        if (!Directory.Exists(steamPath))
            return;

        string tempCache = Path.Combine(Path.GetTempPath(), $"bench_cache_{Guid.NewGuid():N}.json");
        try
        {
            var cache = ScanCache.Load(tempCache, includeShipped: true);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var vfs = VirtualFileSystem.Init(steamPath, cache: cache);
            sw.Stop();

            Assert.True(vfs.Summary.FromCache, "Expected all scene archives to load from shipped cache");
            Assert.Equal(74, vfs.Summary.SceneArchives);
            Assert.True(sw.ElapsedMilliseconds < 2500, $"Expected load in under 2.5s, took {sw.ElapsedMilliseconds} ms");
        }
        finally
        {
            if (File.Exists(tempCache))
                File.Delete(tempCache);
        }
    }

    [Fact]
    public void ScanCache_IgnoresAnUnreadableFile()
    {
        string dir = Directory.CreateTempSubdirectory("runaway-cache").FullName;
        try
        {
            string path = Path.Combine(dir, "cache.json");
            File.WriteAllText(path, "{ not json");
            ScanCache cache = ScanCache.Load(path);
            Assert.Null(cache.TryGet(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ApplyLanguage_UpdatesDisplayNamesAcrossTree()
    {
        using var install = new FakeInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root, language: "en");

        FsNode scenes = vfs.FindNode(vfs.Root, "\\Scenes")!;
        Assert.Equal("Scenes", scenes.DisplayName);

        FsNode h09 = vfs.FindNode(vfs.Root, "\\Scenes\\RESOURCE.H09")!;
        Assert.Contains("Hospital Desk & Nightstand", h09.DisplayName);

        FsNode e00 = vfs.FindNode(vfs.Root, "\\Scenes\\RESOURCE.H09\\e00")!;
        Assert.Equal("background 204×120 (e00)", e00.DisplayName);

        // Switch to Spanish
        vfs.ApplyLanguage("es");

        Assert.Equal("Escenas", scenes.DisplayName);
        Assert.Contains("Escritorio y mesita del hospital", h09.DisplayName);
        Assert.Equal("fondo de escena 204×120 (e00)", e00.DisplayName);
    }
}
