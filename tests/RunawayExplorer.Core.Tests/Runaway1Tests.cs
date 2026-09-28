using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Formats;
using Xunit;

namespace RunawayExplorer.Core.Tests;

/// <summary>
/// <em>Runaway: A Road Adventure</em>'s character sprite library (<c>Resource.001</c>) -- the one format
/// only this game has. Everything else Runaway 1 does is the family default and is tested with the
/// decoder that implements it.
/// </summary>
public class Runaway1CharacterLibraryTests
{
    private const string R1SteamDir = @"F:\Games\Steam\steamapps\common\Runaway A Road Adventure";

    private static string LibraryPath => Path.Combine(R1SteamDir, "Resource", "RESOURCE.001");

    private static string GlobalPath => Path.Combine(R1SteamDir, "Resource", "RESOURCE.000");

    private static byte[] OneEntry(int frames = 3) =>
        SyntheticAssets.CharacterEntry(
            [.. Enumerable.Range(0, frames).Select(i => SyntheticAssets.SolidCharacterFrame(40 + i, 30, 12, 9, (byte)(7 + i)))]);

    // ------------------------------------------------------------------------------------------
    // The container
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void EntriesSpanTheirFrameTableAsWellAsTheirData()
    {
        byte[] entry = OneEntry();
        byte[] file = SyntheticAssets.CharacterLibrary(new Dictionary<int, byte[]> { [0] = entry, [5] = entry });

        List<ArchiveEntry> entries = CharacterSpriteArchive.ReadEntries(file, file.Length);

        Assert.Equal(2, entries.Count);
        Assert.Equal([0, 5], entries.Select(e => e.Index));
        // The slot's size counts frame data only; the entry occupies 3,200 bytes more than that.
        Assert.All(entries, e => Assert.Equal(entry.Length, e.Size));
        Assert.Equal(CharacterSpriteArchive.TableEnd, entries[0].Offset);
    }

    [Fact]
    public void AFileWhoseEntriesDoNotTileItIsNotTheLibrary()
    {
        byte[] entry = OneEntry();
        byte[] file = SyntheticAssets.CharacterLibrary(new Dictionary<int, byte[]> { [0] = entry });

        // One byte of slack at the end and the tiling no longer closes on the last byte.
        Assert.Empty(CharacterSpriteArchive.ReadEntries(file, file.Length + 1));
        // Sizing the entries without their frame tables leaves a gap the size of one table.
        byte[] shrunk = (byte[])file.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            shrunk.AsSpan(CharacterSpriteArchive.SlotCount * 4),
            (uint)(entry.Length - CharacterSpriteAsset.FrameTableSize - 1));
        Assert.Empty(CharacterSpriteArchive.ReadEntries(shrunk, shrunk.Length));
    }

    [Fact]
    public void TheLibraryIsThreeBlocksOfSeventyTwoSlotsOnePerPalette()
    {
        Assert.Equal(11, CharacterSpriteArchive.PaletteSlotFor(0));
        Assert.Equal(11, CharacterSpriteArchive.PaletteSlotFor(71));
        Assert.Equal(45, CharacterSpriteArchive.PaletteSlotFor(72));
        Assert.Equal(45, CharacterSpriteArchive.PaletteSlotFor(143));
        Assert.Equal(79, CharacterSpriteArchive.PaletteSlotFor(144));
        Assert.Equal(79, CharacterSpriteArchive.PaletteSlotFor(215));
    }

    [Fact]
    public void OnlyRunaway1HasALibraryUnderThisName()
    {
        Assert.True(CharacterSpriteArchive.IsLibraryName("RESOURCE.001", GameVersion.Runaway1));
        Assert.True(CharacterSpriteArchive.IsLibraryName("Resource.001", GameVersion.Runaway1));
        // Hollywood Monsters' RESOURCE.001 is ambient audio, and Runaway 2's is a scene archive.
        Assert.False(CharacterSpriteArchive.IsLibraryName("RESOURCE.001", GameVersion.HollywoodMonsters));
        Assert.False(CharacterSpriteArchive.IsLibraryName("RESOURCE.001", GameVersion.Runaway2));
    }

    // ------------------------------------------------------------------------------------------
    // The frames
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void AnEntryParsesToItsFramesAndVerifiesClean()
    {
        CharacterSpriteAsset asset = Assert.IsType<CharacterSpriteAsset>(CharacterSpriteAsset.Parse(OneEntry(4)));

        Assert.Equal(4, asset.FrameCount);
        Assert.Null(asset.Verify());
        Assert.Equal(0, asset.Records[0].DataOffset);
        Assert.All(asset.Records, r => Assert.Equal(r.EdgeCountA + r.EdgeCountB, r.EdgeRunCount));
    }

    [Fact]
    public void RunCoordinatesArePositionsOnACanvasNotInsideTheBox()
    {
        CharacterSpriteAsset asset = CharacterSpriteAsset.Parse(OneEntry(3))!;

        // Frame i's runs start at x = 40 + i, and that is where the frame goes -- not at 0.
        for (int i = 0; i < asset.FrameCount; i++)
        {
            Assert.Equal((40 + i, 30), asset.OriginOf(i));
            Assert.Equal(40 + i, asset.DecodeFrame(i).X);
            Assert.Equal(30, asset.DecodeFrame(i).Y);
        }

        // The canvas is the union of the frames' extents, so it is wider than any single frame.
        Assert.Equal((40, 30, 14, 9), asset.Bounds);
    }

    [Fact]
    public void TheFeetLandOnOneCanvasPointThroughoutAnAnimation()
    {
        CharacterSpriteAsset asset = CharacterSpriteAsset.Parse(OneEntry(5))!;

        for (int i = 0; i < asset.FrameCount; i++)
        {
            (int x, int y) = asset.OriginOf(i);
            Assert.Equal(SyntheticAssets.CharacterFeetX, x + asset.Records[i].AnchorX);
            Assert.Equal(SyntheticAssets.CharacterFeetY, y + asset.Records[i].AnchorY);
        }
    }

    [Fact]
    public void TheBodyGivesTheColourAndTheCoverageStreamGivesTheAlpha()
    {
        byte[] entry = SyntheticAssets.CharacterEntry([SyntheticAssets.SolidCharacterFrame(10, 20, 6, 4, index: 3)]);
        CharacterSpriteAsset asset = CharacterSpriteAsset.Parse(entry)!;
        asset.Palette = IndexedPalette.TryParseRgb565(SyntheticAssets.Rgb565Palette());

        DecodedImage image = asset.DecodeFrame(0).Image!;

        // Rows 0..2 are body, fully covered by kind-0 runs: the palette colour at full alpha.
        (byte r, byte g, byte b, byte a) = SyntheticAssets.Pixel(image, 0, 0);
        Assert.Equal(255, a);
        Assert.True(r != 0 || g != 0 || b != 0);
        // The last row is coverage with no body behind it: the shadow, black and translucent.
        (byte sr, byte sg, byte sb, byte sa) = SyntheticAssets.Pixel(image, 0, 3);
        Assert.Equal((0, 0, 0), (sr, sg, sb));
        Assert.Equal(128 * CharacterSpriteAsset.ShadowAlpha / 255, sa);
    }

    [Fact]
    public void PuttingTheCoverageBytesThroughThePaletteIsNotWhatHappens()
    {
        // Coverage 128 against a palette whose entry 128 is a bright colour: if the byte were an index
        // the shadow row would take that colour. It stays black, at 128 scaled by the shadow strength.
        byte[] entry = SyntheticAssets.CharacterEntry([SyntheticAssets.SolidCharacterFrame(0, 0, 4, 3, index: 1)]);
        CharacterSpriteAsset asset = CharacterSpriteAsset.Parse(entry)!;
        asset.Palette = IndexedPalette.TryParseRgb565(SyntheticAssets.Rgb565Palette());

        (byte r, byte g, byte b, _) = SyntheticAssets.Pixel(asset.DecodeFrame(0).Image!, 1, 2);
        Assert.Equal((0, 0, 0), (r, g, b));
    }

    [Fact]
    public void AnEntryWhoseStreamsDoNotTileTheFrameIsRejected()
    {
        byte[] entry = OneEntry(2);
        // Claim one body run more than the frame holds: the streams then overrun their space.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), 999);
        Assert.Null(CharacterSpriteAsset.Parse(entry));
    }

    [Fact]
    public void RunawayOnesPalettesAreRgb565NotSixBitTriples()
    {
        byte[] block = SyntheticAssets.Rgb565Palette();

        Assert.NotNull(IndexedPalette.TryParseRgb565(block));
        // The Hollywood Monsters reader must not claim it: 512 bytes is not a whole number of triples.
        Assert.Null(IndexedPalette.TryParse(block));
        Assert.Null(IndexedPalette.TryParseRgb565(SyntheticAssets.Palette(256)));
    }

    // ------------------------------------------------------------------------------------------
    // Against the real install
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void RealInstall_EveryEntryTilesTheFileAndEveryFrameWalksClean()
    {
        if (!File.Exists(LibraryPath)) return;

        using FileStream f = File.OpenRead(LibraryPath);
        List<ArchiveEntry> entries = CharacterSpriteArchive.ReadEntries(f);

        Assert.Equal(168, entries.Count);
        Assert.Equal(108_761_688, f.Length);

        int frames = 0;
        foreach (ArchiveEntry e in entries)
        {
            var data = new byte[e.Size];
            f.Position = e.Offset;
            f.ReadExactly(data);

            CharacterSpriteAsset asset = Assert.IsType<CharacterSpriteAsset>(CharacterSpriteAsset.Parse(data));
            Assert.Null(asset.Verify());
            frames += asset.FrameCount;
        }
        Assert.Equal(2166, frames);
    }

    [Fact]
    public void RealInstall_TheThreePalettesAreWhereTheLibraryExpectsThem()
    {
        if (!File.Exists(GlobalPath)) return;

        using FileStream f = File.OpenRead(GlobalPath);
        List<ArchiveEntry> entries = GlobalArchive.ReadEntries(f);
        var bySlot = entries.ToDictionary(e => e.Index);

        var tables = new Dictionary<int, IndexedPalette>();
        foreach (int slot in (int[])[11, 28, 45, 62, 79, 96])
        {
            ArchiveEntry e = bySlot[slot];
            Assert.Equal(IndexedPalette.Rgb565BlockBytes, e.Size);
            var block = new byte[e.Size];
            f.Position = e.Offset;
            f.ReadExactly(block);
            tables[slot] = Assert.IsType<IndexedPalette>(IndexedPalette.TryParseRgb565(block));
        }

        // Six slots, three tables: 11 = 28, 45 = 62, 79 = 96, and no two of the three are alike.
        static byte[] Bytes(IndexedPalette p) => p.Bgra.ToArray();
        Assert.Equal(Bytes(tables[11]), Bytes(tables[28]));
        Assert.Equal(Bytes(tables[45]), Bytes(tables[62]));
        Assert.Equal(Bytes(tables[79]), Bytes(tables[96]));
        Assert.NotEqual(Bytes(tables[11]), Bytes(tables[45]));
        Assert.NotEqual(Bytes(tables[11]), Bytes(tables[79]));
        Assert.NotEqual(Bytes(tables[45]), Bytes(tables[79]));
    }

    [Fact]
    public void RealInstall_EveryEntryMatchesItsOwnBlocksPaletteAndNoOther()
    {
        if (!File.Exists(LibraryPath) || !File.Exists(GlobalPath)) return;

        // The palette an entry wants is not stored, so the claim under test is the three-block layout.
        // Score each entry's index histogram against the three candidate palettes' own histograms: the
        // art drawn for one costume uses that costume's colours and barely touches the others'.
        var reference = new Dictionary<int, double[]>();
        var libraryHistograms = new Dictionary<int, double[]>();

        using (FileStream f = File.OpenRead(LibraryPath))
        {
            foreach (ArchiveEntry e in CharacterSpriteArchive.ReadEntries(f))
            {
                var data = new byte[e.Size];
                f.Position = e.Offset;
                f.ReadExactly(data);
                libraryHistograms[e.Index] = FirstFrameHistogram(CharacterSpriteAsset.Parse(data)!, data);
            }
        }

        foreach (int slot in (int[])[11, 45, 79])
            reference[slot] = libraryHistograms.First(kv => CharacterSpriteArchive.PaletteSlotFor(kv.Key) == slot).Value;

        Assert.Equal(168, libraryHistograms.Count);
        foreach ((int index, double[] histogram) in libraryHistograms)
        {
            int own = CharacterSpriteArchive.PaletteSlotFor(index);
            double mine = Cosine(histogram, reference[own]);
            foreach (int other in (int[])[11, 45, 79])
            {
                if (other == own)
                    continue;
                Assert.True(
                    mine > Cosine(histogram, reference[other]) + 0.5,
                    $"entry {index} is not clearly in block {own}: {mine:F3} against {Cosine(histogram, reference[other]):F3} for {other}");
            }
        }
    }

    [Fact]
    public void RealInstall_TheLibraryIsItsOwnFolderAndItsEntriesDecodeInColour()
    {
        if (!Directory.Exists(R1SteamDir)) return;

        var vfs = VirtualFileSystem.Init(R1SteamDir, cache: ScanCache.Ephemeral(includeShipped: true));

        FsNode characters = vfs.Root.Children.Single(c => c.Name == VirtualFileSystem.CharactersFolder);
        FsNode library = characters.Children.Single();
        Assert.Equal(168, library.Children.Count);
        Assert.All(library.Children, c => Assert.Equal(EntryKind.CharacterAnimation, c.Kind));
        Assert.Equal(168, vfs.Summary.CharacterAnimations);

        // One entry from each of the three blocks, decoded the way the viewer decodes it.
        foreach (int index in (int[])[0, 100, 200])
        {
            FsNode node = library.Children.Single(c => c.EntryIndex == index);
            IndexedPalette palette = Assert.IsType<IndexedPalette>(vfs.CharacterPaletteFor(node));

            CharacterSpriteAsset asset = Assert.IsType<CharacterSpriteAsset>(CharacterSpriteAsset.Parse(vfs.ReadBytes(node)));
            asset.Palette = palette;
            Assert.Null(asset.Verify());

            DecodedImage image = asset.DecodeFrame(0).Image!;
            // A character, not a silhouette: the body comes out in many colours at full alpha, and the
            // coverage mask reaches past it to leave a translucent shadow.
            var opaque = new HashSet<uint>();
            bool translucent = false;
            for (int i = 0; i < image.Width * image.Height; i++)
            {
                byte a = image.Pixels[i * 4 + 3];
                if (a == 255)
                    opaque.Add(BitConverter.ToUInt32(image.Pixels, i * 4));
                else if (a > 0)
                    translucent = true;
            }
            Assert.True(opaque.Count > 20, $"entry {index} decoded to {opaque.Count} opaque colours");
            Assert.True(translucent, $"entry {index} has no anti-aliased edge or shadow");
        }
    }

    /// <summary>How often each palette index appears in an entry's first frame, as a unit-sum vector.</summary>
    private static double[] FirstFrameHistogram(CharacterSpriteAsset asset, byte[] entry)
    {
        var counts = new double[256];
        int p = CharacterSpriteAsset.FrameTableSize;
        for (int i = 0; i < asset.Records[0].BodyRunCount; i++)
        {
            int n = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(entry.AsSpan(p + 4));
            p += 6;
            for (int k = 0; k < n; k++)
                counts[entry[p + k]]++;
            p += n;
        }
        double total = counts.Sum();
        return total == 0 ? counts : [.. counts.Select(c => c / total)];
    }

    private static double Cosine(double[] a, double[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / Math.Sqrt(na * nb);
    }
}
