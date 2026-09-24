using System.Buffers.Binary;
using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Formats;
using RunawayExplorer.Core.Settings;
using Xunit;
using static RunawayExplorer.Core.Tests.SyntheticAssets;

namespace RunawayExplorer.Core.Tests;

/// <summary>
/// A throwaway Hollywood Monsters install: one scene archive holding all three indexed image formats plus
/// its own palette block, the shared palette in RESOURCE.000, and one archive of each audio kind.
/// </summary>
public sealed class FakeHmInstall : IDisposable
{
    public const int SceneColors = 176;
    public const int SharedColors = IndexedPalette.Colors - SceneColors;

    public string Root { get; } = Directory.CreateTempSubdirectory("monsters-install").FullName;

    public byte[] ScenePalette { get; } = Palette(SceneColors);
    public byte[] SharedPalette { get; } = Palette(SharedColors, seed: 7);
    public byte[] ResidentSound { get; } = new byte[5826];
    public byte[] Background { get; } = IndexedRaster();
    public byte[] Overlay { get; } = IndexedOverlay([(300, 40, [10, 11, 12]), (300, 41, [13, 14, 15])]);
    public byte[] Sprite { get; } = IndexedSprite(
    [
        new IndexedFrame([(10, 20, [200, 201]), (10, 21, [202, 203])]),
        new IndexedFrame([(11, 20, [204])]),
    ]);

    /// <summary>The second cast palette of the two-palette archive, as RESOURCE.A03 e08 is in the real game.</summary>
    public byte[] SecondScenePalette { get; } = Palette(SceneColors, seed: 31);

    public byte[] SecondSprite { get; } = IndexedSprite(
    [
        new IndexedFrame([(40, 50, [12, 13, 14])]),
    ]);

    public FakeHmInstall()
    {
        File.WriteAllText(Path.Combine(Root, "Monsters.exe"), "dummy");
        File.WriteAllBytes(Path.Combine(Root, "RESOURCE.A02"), SyntheticArchives.Scene([Background, ScenePalette, Overlay, Sprite]));
        // Two palette blocks: entries 2 take e01, entries after e03 take e03.
        File.WriteAllBytes(Path.Combine(Root, "RESOURCE.A03"),
            SyntheticArchives.Scene([Background, ScenePalette, Sprite, SecondScenePalette, SecondSprite]));
        // The shared palette sits at slot 0; the resident sound effects occupy the tail, from 0x55.
        byte[]?[] globals = new byte[VirtualFileSystem.ResidentSoundLastEntry + 1][];
        globals[0] = SharedPalette;
        globals[VirtualFileSystem.ResidentSoundFirstEntry] = ResidentSound;
        File.WriteAllBytes(Path.Combine(Root, "RESOURCE.000"), SyntheticArchives.HmGlobal(globals));
        File.WriteAllBytes(Path.Combine(Root, "RESOURCE.M01"), SyntheticArchives.HmAudio([new byte[44100], new byte[22050]]));
        File.WriteAllBytes(Path.Combine(Root, "RESOURCE.S01"), SyntheticArchives.HmAudio([new byte[11025]]));
        File.WriteAllBytes(Path.Combine(Root, "RESOURCE.001"), SyntheticArchives.HmAudio([new byte[2205]]));
        File.WriteAllBytes(Path.Combine(Root, "RESOURCE.002"), SyntheticArchives.HmAudio([new byte[8820]]));
        File.WriteAllBytes(Path.Combine(Root, "RESOURCE.004"), SyntheticArchives.HmAudio([new byte[800], new byte[900], new byte[1000]], slots: 4000));
        File.WriteAllBytes(Path.Combine(Root, "RESOURCE.003"), new byte[321]);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

public class HollywoodMonstersTests
{
    [Fact]
    public void GameDetector_DetectsHollywoodMonsters()
    {
        string dir = Directory.CreateTempSubdirectory("monsters-detect").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Monsters.exe"), "dummy");
            Assert.Equal(GameVersion.HollywoodMonsters, GameDetector.Detect(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AppSettings_ManagesTheHollywoodMonstersPath()
    {
        var settings = new AppSettings { ActiveGame = GameVersion.HollywoodMonsters };
        settings.SetGameDir(GameVersion.HollywoodMonsters, @"C:\Games\Monsters");

        Assert.Equal(@"C:\Games\Monsters", settings.HollywoodMonstersDir);
        Assert.Equal(@"C:\Games\Monsters", settings.GetGameDir(GameVersion.HollywoodMonsters));
        Assert.Equal(@"C:\Games\Monsters", settings.ActiveGameDir);
    }

    // --- Palettes ---

    [Fact]
    public void IndexedPalette_AcceptsSixBitBlocksAndExpandsToTheFullRange()
    {
        IndexedPalette? full = IndexedPalette.TryParse(Palette(IndexedPalette.Colors));
        Assert.NotNull(full);
        Assert.Equal(IndexedPalette.Colors, full.DefinedCount);

        // Colour 21 is { r 21, g 42, b 63 }; 63 must expand to 255, not 252.
        var bgra = new byte[4];
        full.ToBgra(21, bgra, 0);
        Assert.Equal([255, (byte)((42 << 2) | (42 >> 4)), (byte)((21 << 2) | (21 >> 4)), 255], bgra);

        Assert.Null(IndexedPalette.TryParse(Palette(10)));                  // too small to be a palette
        Assert.Null(IndexedPalette.TryParse(new byte[528 + 1]));            // not whole triples
        Assert.False(IndexedPalette.IsPaletteBlock([.. Palette(176), 64])); // 64 is past 6 bits
    }

    [Fact]
    public void IndexedPalette_WithTail_PutsTheSharedColoursAtTheTopOfTheTable()
    {
        IndexedPalette scene = IndexedPalette.TryParse(Palette(176))!;
        IndexedPalette shared = IndexedPalette.TryParse(Palette(80, seed: 7))!;
        Assert.Equal(176, scene.DefinedCount);

        IndexedPalette merged = scene.WithTail(shared);
        Assert.Equal(IndexedPalette.Colors, merged.DefinedCount);
        Assert.Equal(scene.Bgra[..(176 * 4)].ToArray(), merged.Bgra[..(176 * 4)].ToArray());
        Assert.Equal(shared.Bgra[..(80 * 4)].ToArray(), merged.Bgra[(176 * 4)..].ToArray());
    }

    // --- Images ---

    [Fact]
    public void RasterDecoder_DetectsIndexedScreensByTheirExactSize()
    {
        byte[] screen = IndexedRaster();
        RasterInfo info = RasterDecoder.DetectIndexed(screen)!.Value;
        Assert.Equal((RasterDecoder.IndexedScreenWidth, RasterDecoder.IndexedScreenHeight), (info.Width, info.Height));

        Assert.Null(RasterDecoder.DetectIndexed(new byte[491_520 - 1]));
        Assert.Null(RasterDecoder.DetectIndexed(new byte[491_520 + 7]));

        IndexedPalette palette = IndexedPalette.TryParse(Palette(IndexedPalette.Colors))!;
        DecodedImage image = RasterDecoder.DecodeIndexed(screen, info.Width, info.Height, palette);
        var expected = new byte[4];
        palette.ToBgra(DefaultIndexedPixel(5, 6), expected, 0);
        Assert.Equal((expected[2], expected[1], expected[0], expected[3]), Pixel(image, 5, 6));
    }

    [Fact]
    public void SpriteDecoder_ParsesIndexedSpritesAndWalksEveryFrameByteExact()
    {
        using var install = new FakeHmInstall();

        SpriteAsset asset = SpriteAsset.Parse(install.Sprite, bytesPerPixel: 1)!;
        Assert.True(asset.IsIndexed);
        Assert.Null(asset.Verify());
        Assert.Equal(2, asset.FrameCount);
        Assert.Equal((10, 20, 2, 2), asset.Bounds); // the union of both frames

        IndexedPalette palette = IndexedPalette.TryParse(Palette(IndexedPalette.Colors))!;
        asset.Palette = palette;
        SpriteFrame frame = asset.DecodeFrame(0);
        Assert.Equal((10, 20), (frame.X, frame.Y));
        Assert.Equal((2, 2), (frame.Image!.Width, frame.Image.Height));

        var expected = new byte[4];
        palette.ToBgra(203, expected, 0);
        Assert.Equal((expected[2], expected[1], expected[0], expected[3]), Pixel(frame.Image, 1, 1));

        // The same bytes are not a valid RGB565 sprite: the pixel width is told, not guessed.
        Assert.Null(SpriteAsset.Parse(install.Sprite));
    }

    [Fact]
    public void OverlayDecoder_ParsesIndexedOverlays()
    {
        byte[] data = IndexedOverlay([(300, 40, [1, 2, 3]), (300, 41, [4, 5, 6])]);

        OverlayInfo info = OverlayDecoder.Parse(data, bytesPerPixel: 1)!;
        Assert.Equal((300, 40, 3, 2), (info.X, info.Y, info.Width, info.Height));
        Assert.Equal(1, info.BytesPerPixel);
        Assert.True(info.IsRectangular);

        // At two bytes per pixel the records no longer consume the entry exactly.
        Assert.Null(OverlayDecoder.Parse(data));

        IndexedPalette palette = IndexedPalette.TryParse(Palette(IndexedPalette.Colors))!;
        DecodedImage image = OverlayDecoder.Decode(data, info, palette);
        var expected = new byte[4];
        palette.ToBgra(5, expected, 0);
        Assert.Equal((expected[2], expected[1], expected[0], expected[3]), Pixel(image, 1, 1));
    }

    [Fact]
    public void EntryClassifier_TestsPaletteBlocksBeforeMasks()
    {
        using var install = new FakeHmInstall();

        static EntryClassification Classify(byte[] data) =>
            EntryClassifier.Classify(data, 1024, 480, GameVersion.HollywoodMonsters);

        // A block of 6-bit triples is also a whole number of 3-byte RLE mask runs, so it must be tested first.
        Assert.Equal(EntryKind.Data, Classify(install.ScenePalette).Kind);
        Assert.Equal(EntryKind.Background, Classify(install.Background).Kind);
        Assert.Equal(EntryKind.Overlay, Classify(install.Overlay).Kind);
        Assert.Equal(EntryKind.Animation, Classify(install.Sprite).Kind);
        Assert.Equal(EntryKind.Data, Classify(new byte[37]).Kind);
    }

    // --- Containers ---

    [Fact]
    public void SceneArchive_NamesAndTheOneTablelessArchive()
    {
        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.A02", GameVersion.HollywoodMonsters));
        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.I02", GameVersion.HollywoodMonsters));
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.001", GameVersion.HollywoodMonsters));
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.004", GameVersion.HollywoodMonsters));
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.M01", GameVersion.HollywoodMonsters));

        long screen = (long)RasterDecoder.IndexedScreenWidth * RasterDecoder.IndexedScreenHeight;
        Assert.Equal(
            [new ArchiveEntry(0, 0, screen), new ArchiveEntry(1, screen, IndexedPalette.FullBlockBytes)],
            SceneArchive.ReadHeaderlessScreenEntries(screen + IndexedPalette.FullBlockBytes));
        Assert.Empty(SceneArchive.ReadHeaderlessScreenEntries(screen));
    }

    [Fact]
    public void AudioArchive_ReadsTheTableLengthFromSlotOne()
    {
        byte[] archive = SyntheticArchives.HmAudio([new byte[1000], new byte[500], new byte[250]]);

        List<AudioArchiveEntry> entries = AudioArchive.ReadHollywoodMonstersEntries(archive, archive.Length);
        Assert.Equal([1000, 500, 250], entries.Select(e => e.Size));
        Assert.Equal([400, 1400, 1900], entries.Select(e => e.Offset));
        Assert.All(entries, e => Assert.Equal(AudioFormat.RawPcm, e.Format));

        // The stale bytes past the terminator defeat the Runaway 1 reader, which is why this one exists.
        Assert.Empty(AudioArchive.ReadAudioEntries(archive, archive.Length));
    }

    [Fact]
    public void GlobalArchive_ReadsTheSingleByteHollywoodMonstersHeader()
    {
        byte[] block = Palette(80);
        byte[] archive = SyntheticArchives.HmGlobal([block]);

        List<ArchiveEntry> entries = GlobalArchive.ReadEntries(archive, archive.Length, GameVersion.HollywoodMonsters);
        Assert.Equal([new ArchiveEntry(0, GlobalArchive.TableEndHm, block.Length)], entries);

        // The same bytes read as a Runaway 1 archive find nothing: the header sizes do not line up.
        Assert.Empty(GlobalArchive.ReadEntries(archive, archive.Length));
    }

    // --- The tree ---

    [Fact]
    public void VirtualFileSystem_BuildsAHollywoodMonstersTree()
    {
        using var install = new FakeHmInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);

        Assert.Equal(GameVersion.HollywoodMonsters, vfs.GameVersion);
        Assert.Equal(
            [VirtualFileSystem.ScenesFolder, VirtualFileSystem.MusicFolder, VirtualFileSystem.AmbientFolder,
             VirtualFileSystem.CinematicFolder, VirtualFileSystem.VoiceFolder, VirtualFileSystem.DialogueFolder,
             VirtualFileSystem.GlobalFolder],
            vfs.Root.Children.Select(c => c.Name));

        Assert.Equal(["RESOURCE.A02", "RESOURCE.A03"], vfs.Root.Children[0].Children.Select(c => c.Name));

        FsNode scene = vfs.Root.Children[0].Children[0];
        Assert.Equal(
            [EntryKind.Background, EntryKind.Data, EntryKind.Overlay, EntryKind.Animation],
            scene.Children.Select(c => c.Kind));
        Assert.Equal((2, 2, 2, 3), (vfs.Summary.Backgrounds, vfs.Summary.Overlays, vfs.Summary.Animations, vfs.Summary.DataEntries));
        Assert.Equal(0, vfs.Summary.Videos);
    }

    [Fact]
    public void VirtualFileSystem_CompletesScenePalettesFromTheSharedBlock()
    {
        using var install = new FakeHmInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);

        FsNode scene = vfs.Root.Children[0].Children[0];
        IndexedPalette palette = vfs.ScenePaletteFor(scene.Children[3])!;

        Assert.Equal(IndexedPalette.Colors, palette.DefinedCount);
        IndexedPalette shared = IndexedPalette.TryParse(install.SharedPalette)!;
        Assert.Equal(
            shared.Bgra[..(FakeHmInstall.SharedColors * 4)].ToArray(),
            palette.Bgra[(FakeHmInstall.SceneColors * 4)..].ToArray());

        // The characters' colours live at the top of the table: without the tail they would all be black.
        var bgra = new byte[4];
        palette.ToBgra(200, bgra, 0);
        Assert.NotEqual([0, 0, 0, 255], bgra);
    }

    [Fact]
    public void VirtualFileSystem_GivesEachEntryTheNearestPrecedingPalette()
    {
        using var install = new FakeHmInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);

        FsNode scene = vfs.Root.Children[0].Children[1];
        Assert.Equal("RESOURCE.A03", scene.Name);

        FsNode Entry(int index) => scene.Children.First(c => c.EntryIndex == index);

        IndexedPalette first = IndexedPalette.TryParse(install.ScenePalette)!;
        IndexedPalette second = IndexedPalette.TryParse(install.SecondScenePalette)!;
        static byte[] Bottom(IndexedPalette p) => p.Bgra[..(FakeHmInstall.SceneColors * 4)].ToArray();

        // e02 sits between the two blocks and takes the first; e04 follows the second and takes it.
        Assert.Equal(Bottom(first), Bottom(vfs.ScenePaletteFor(Entry(2))!));
        Assert.Equal(Bottom(second), Bottom(vfs.ScenePaletteFor(Entry(4))!));
        Assert.NotEqual(Bottom(first), Bottom(second));

        // The background precedes every block, so it takes the first one; so does the archive node itself.
        Assert.Equal(Bottom(first), Bottom(vfs.ScenePaletteFor(Entry(0))!));
        Assert.Equal(Bottom(first), Bottom(vfs.ScenePaletteFor(scene)!));

        // Both are still completed with the shared tail.
        IndexedPalette shared = IndexedPalette.TryParse(install.SharedPalette)!;
        byte[] tail = shared.Bgra[..(FakeHmInstall.SharedColors * 4)].ToArray();
        Assert.Equal(tail, vfs.ScenePaletteFor(Entry(2))!.Bgra[(FakeHmInstall.SceneColors * 4)..].ToArray());
        Assert.Equal(tail, vfs.ScenePaletteFor(Entry(4))!.Bgra[(FakeHmInstall.SceneColors * 4)..].ToArray());
    }

    [Fact]
    public void VirtualFileSystem_RoutesEachAudioArchiveToItsFolderAndPcmShape()
    {
        using var install = new FakeHmInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);

        FsNode Folder(string name) => vfs.Root.Children.First(c => c.Name == name);

        FsNode music = Folder(VirtualFileSystem.MusicFolder).Children.Single();
        Assert.Equal("RESOURCE.M01", music.Name);
        Assert.Equal(2, music.Children.Count);
        Assert.Equal(EntryKind.Music, music.Children[0].Kind);
        Assert.Equal(
            (11_025, 1, 16),
            (music.Children[0].Audio!.SampleRate, music.Children[0].Audio!.Channels, music.Children[0].Audio!.BitsPerSample));

        // RESOURCE.001 and RESOURCE.S01 are both ambient: 8-bit at the 11,025 Hz effect rate.
        FsNode ambient = Folder(VirtualFileSystem.AmbientFolder);
        Assert.Equal(["RESOURCE.001", "RESOURCE.S01"], ambient.Children.Select(c => c.Name).Order());
        Assert.Equal(
            (11_025, 8),
            (ambient.Children[0].Children[0].Audio!.SampleRate, ambient.Children[0].Children[0].Audio!.BitsPerSample));

        Assert.Single(Folder(VirtualFileSystem.CinematicFolder).Children.Single().Children);

        // RESOURCE.004 is the voice bank, not the lip-sync table it is in Runaway 1.
        FsNode voice = Folder(VirtualFileSystem.VoiceFolder);
        Assert.Equal(3, vfs.Summary.VoiceClips);

        // Voice runs at twice the effect rate, so it carries its own PCM shape rather than sharing one.
        Assert.Equal(
            (22_050, 8),
            (voice.Children[0].Children[0].Audio!.SampleRate, voice.Children[0].Children[0].Audio!.BitsPerSample));

        // RESOURCE.003 is the script, and it belongs under Dialogue rather than with the global data.
        Assert.Contains(Folder(VirtualFileSystem.DialogueFolder).Children, c => c.Name == "RESOURCE.003");
        Assert.DoesNotContain(Folder(VirtualFileSystem.GlobalFolder).Children, c => c.Name == "RESOURCE.003");
        Assert.Equal(5, vfs.Summary.AudioClips); // 2 music + 1 sfx + 1 ambient + 1 cinematic
    }

    [Fact]
    public void VirtualFileSystem_PlaysTheResidentSoundEffectsInTheTailOfResource000()
    {
        using var install = new FakeHmInstall();
        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);

        FsNode archive = vfs.Root.Children.Single(c => c.Name == VirtualFileSystem.GlobalFolder)
                                 .Children.Single(c => c.Name == "RESOURCE.000");

        // The palette block at slot 0 stays data; the tail slots are playable at the sound-effect shape.
        FsNode palette = archive.Children.Single(c => c.EntryIndex == 0);
        Assert.Equal(EntryKind.GlobalData, palette.Kind);
        Assert.Null(palette.Audio);

        FsNode effect = archive.Children.Single(c => c.EntryIndex == VirtualFileSystem.ResidentSoundFirstEntry);
        Assert.Equal(EntryKind.Ambient, effect.Kind);
        Assert.Equal((11_025, 1, 8), (effect.Audio!.SampleRate, effect.Audio.Channels, effect.Audio.BitsPerSample));
    }

    private static byte[] ScriptFile() => SyntheticArchives.HmScript(new Dictionary<int, (string[], (string, int)[])>
    {
        [101] = (["  llamador", " puerta"],
                 [("Bien, ya estamos en la mansión Hannover.", 2227),
                  ("Conducen a la mansión.", 2228),
                  ("Está vacía, no sé dónde andará el perro.", 0)]),
        [702] = ([], [("Una sala sin etiquetas.", 91)]),
    });

    [Fact]
    public void HollywoodScript_DeciphersRowsAndPairsLinesWithTheirVoiceClips()
    {
        using var stream = new MemoryStream(ScriptFile());
        List<HollywoodScript.Stage> stages = HollywoodScript.ReadStages(stream);

        Assert.Equal([101, 702], stages.Select(s => s.Index));
        HollywoodScript.Stage first = stages[0];
        Assert.Equal(1010, first.SceneNumber);

        // Labels keep their leading spaces: the game stores them padded and the explorer shows them as stored.
        Assert.Equal(["  llamador", " puerta"], first.Labels.Select(l => l.Text));
        Assert.Equal([1, 2], first.Labels.Select(l => l.Id));

        // Line ids start at 500, and the accented text proves CP850 rather than Windows-1252 decoding.
        Assert.Equal([500, 501, 502], first.Lines.Select(l => l.Id));
        Assert.Equal("Bien, ya estamos en la mansión Hannover.", first.Lines[0].Text);
        Assert.Equal("Está vacía, no sé dónde andará el perro.", first.Lines[2].Text);

        // A cued line carries its clip; an uncued one says so rather than guessing a neighbour's.
        Assert.Equal([2227, 2228, -1], first.Lines.Select(l => l.VoiceClipId));
    }

    [Fact]
    public void HollywoodScript_AcceptsAStageThatCarriesNoLabels()
    {
        // Scene 7020 ships 12 lines and no labels at all, so labels cannot be what qualifies a stage.
        using var stream = new MemoryStream(ScriptFile());
        HollywoodScript.Stage stage = HollywoodScript.ReadStages(stream).Single(s => s.Index == 702);

        Assert.Empty(stage.Labels);
        Assert.Equal("Una sala sin etiquetas.", Assert.Single(stage.Lines).Text);
        Assert.Equal(91, stage.Lines[0].VoiceClipId);
    }

    [Fact]
    public void HollywoodScript_SkipsStageSlotsThatDoNotDecipherToText()
    {
        // Most of the 1,021 slots are zero or stale. A stale one points at real bytes that decipher to
        // noise, so pointing somewhere plausible must not be enough to be listed as a stage.
        byte[] data = ScriptFile();
        int slot = HollywoodScript.KeySize + 300 * 4;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(slot), (uint)(HollywoodScript.KeySize + 8));

        using var stream = new MemoryStream(data);
        Assert.Equal([101, 702], HollywoodScript.ReadStages(stream).Select(s => s.Index));
    }

    [Fact]
    public void VirtualFileSystem_ShowsTheScriptAsScenesOfLabelsAndLines()
    {
        using var install = new FakeHmInstall();
        File.WriteAllBytes(Path.Combine(install.Root, "RESOURCE.003"), ScriptFile());

        VirtualFileSystem vfs = VirtualFileSystem.Init(install.Root);

        FsNode script = vfs.Root.Children.Single(c => c.Name == VirtualFileSystem.DialogueFolder)
                                .Children.Single(c => c.Name == "RESOURCE.003");

        Assert.Equal(["scene 1010", "scene 7020"], script.Children.Select(c => c.Name));

        FsNode scene = script.Children[0];
        Assert.Equal(5, scene.Children.Count); // 2 labels + 3 lines
        Assert.All(scene.Children, c => Assert.Equal(EntryKind.Dialogue, c.Kind));

        FsNode line = scene.Children.Single(c => c.Name == "line 0500");
        Assert.Equal("Bien, ya estamos en la mansión Hannover.", line.Subtitle);
        Assert.Equal(2227, line.VoiceClipIndex);

        // The uncued line still shows, just without a clip to point at.
        Assert.Equal(-1, scene.Children.Single(c => c.Name == "line 0502").VoiceClipIndex);
        Assert.Equal(6, vfs.Summary.Phrases);
    }
}
