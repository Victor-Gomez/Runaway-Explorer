using System.Buffers.Binary;
using System.Text;
using RunawayExplorer.Core.FileSystem;
using Xunit;

namespace RunawayExplorer.Core.Tests;

/// <summary>Builders for the archive containers, written exactly as docs/formats §2 describes them.</summary>
public static class SyntheticArchives
{
    /// <summary>A scene archive with <paramref name="slots"/> pairs; entries are laid out back to back after the table.</summary>
    public static byte[] Scene(IReadOnlyList<byte[]?> entries, int slots = 40)
    {
        int tableLen = slots * 8;
        using var ms = new MemoryStream();
        var offsets = new uint[slots];
        var sizes = new uint[slots];
        uint cursor = (uint)tableLen;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] is null)
                continue;
            offsets[i] = cursor;
            sizes[i] = (uint)entries[i]!.Length;
            cursor += sizes[i];
        }
        Span<byte> u32 = stackalloc byte[4];
        foreach (uint o in offsets) { BinaryPrimitives.WriteUInt32LittleEndian(u32, o); ms.Write(u32); }
        foreach (uint s in sizes) { BinaryPrimitives.WriteUInt32LittleEndian(u32, s); ms.Write(u32); }
        foreach (byte[]? e in entries)
            if (e is not null) ms.Write(e);
        return ms.ToArray();
    }

    /// <summary>An audio archive: slot 0 empty, then <paramref name="entries"/> back to back.</summary>
    public static byte[] Audio(IReadOnlyList<byte[]?> entries, int slots = 100)
    {
        using var ms = new MemoryStream();
        var offsets = new uint[slots];
        uint cursor = (uint)(slots * 4);
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] is null)
                continue;
            offsets[i + 1] = cursor;
            cursor += (uint)entries[i]!.Length;
        }
        Span<byte> u32 = stackalloc byte[4];
        foreach (uint o in offsets) { BinaryPrimitives.WriteUInt32LittleEndian(u32, o); ms.Write(u32); }
        foreach (byte[]? e in entries)
            if (e is not null) ms.Write(e);
        return ms.ToArray();
    }

    public static byte[] Global(IReadOnlyList<byte[]?> entries)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 3, 1, 6, 1, 1, 1 });
        ms.Write(new byte[14]);
        var offsets = new uint[GlobalArchive.SlotCount];
        var sizes = new uint[GlobalArchive.SlotCount];
        uint cursor = (uint)GlobalArchive.TableEnd;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] is null) continue;
            offsets[i] = cursor; sizes[i] = (uint)entries[i]!.Length; cursor += sizes[i];
        }
        Span<byte> u32 = stackalloc byte[4];
        foreach (uint o in offsets) { BinaryPrimitives.WriteUInt32LittleEndian(u32, o); ms.Write(u32); }
        foreach (uint s in sizes) { BinaryPrimitives.WriteUInt32LittleEndian(u32, s); ms.Write(u32); }
        foreach (byte[]? e in entries) if (e is not null) ms.Write(e);
        return ms.ToArray();
    }

    /// <summary>
    /// A RESOURCE.IFZ interface archive: a plain offset table whose first slot holds the table's own
    /// length, with an entry running to the next distinct offset. A null entry repeats the previous
    /// offset, which is how the real archives alias several slots onto one image.
    /// </summary>
    public static byte[] Interface(IReadOnlyList<byte[]?> entries)
    {
        int slots = entries.Count;
        uint tableLen = (uint)(slots * 4);
        var offsets = new uint[slots];
        using var payload = new MemoryStream();
        uint cursor = tableLen;
        for (int i = 0; i < slots; i++)
        {
            if (entries[i] is null)
            {
                offsets[i] = i > 0 ? offsets[i - 1] : cursor;
                continue;
            }

            offsets[i] = cursor;
            payload.Write(entries[i]!);
            cursor += (uint)entries[i]!.Length;
        }

        using var ms = new MemoryStream();
        Span<byte> u32 = stackalloc byte[4];
        foreach (uint o in offsets) { BinaryPrimitives.WriteUInt32LittleEndian(u32, o); ms.Write(u32); }
        payload.Position = 0;
        payload.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// A Hollywood Monsters audio archive: a plain offset table whose slot 1 holds the table's own length,
    /// terminated by the file length, with stale bytes left in the slots past it -- exactly what defeats
    /// the Runaway 1 reader's "smallest value in the table" rule.
    /// </summary>
    public static byte[] HmAudio(IReadOnlyList<byte[]> clips, int slots = 100, uint tag = 0)
    {
        var offsets = new uint[slots];
        offsets[0] = tag;
        uint cursor = (uint)(slots * 4);
        int slot = 1;
        foreach (byte[] c in clips)
        {
            offsets[slot++] = cursor;
            cursor += (uint)c.Length;
        }
        offsets[slot++] = cursor;                        // terminator: the file length
        for (int i = slot; i < slots; i++)
            offsets[i] = (uint)(2 + i % 3);              // stale bytes, smaller than the table length

        using var ms = new MemoryStream();
        Span<byte> u32 = stackalloc byte[4];
        foreach (uint o in offsets) { BinaryPrimitives.WriteUInt32LittleEndian(u32, o); ms.Write(u32); }
        foreach (byte[] c in clips) ms.Write(c);
        return ms.ToArray();
    }

    /// <summary>A Hollywood Monsters global archive: one leading byte, then 100 offsets and 100 sizes.</summary>
    public static byte[] HmGlobal(IReadOnlyList<byte[]?> entries)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0);
        var offsets = new uint[GlobalArchive.SlotCountHm];
        var sizes = new uint[GlobalArchive.SlotCountHm];
        uint cursor = GlobalArchive.TableEndHm;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] is null) continue;
            offsets[i] = cursor; sizes[i] = (uint)entries[i]!.Length; cursor += sizes[i];
        }
        Span<byte> u32 = stackalloc byte[4];
        foreach (uint o in offsets) { BinaryPrimitives.WriteUInt32LittleEndian(u32, o); ms.Write(u32); }
        foreach (uint s in sizes) { BinaryPrimitives.WriteUInt32LittleEndian(u32, s); ms.Write(u32); }
        foreach (byte[]? e in entries) if (e is not null) ms.Write(e);
        return ms.ToArray();
    }

    /// <summary>
    /// A Hollywood Monsters <c>RESOURCE.003</c>: the decode key, the stage offset table, and one block per
    /// stage. Rows are enciphered on the way out with the same per-column subtraction the game uses, so the
    /// decoder has to undo it rather than just find the text. <paramref name="stages"/> maps a stage index
    /// to its labels, its lines, and the voice clip each line is cued with (0 for none).
    /// </summary>
    public static byte[] HmScript(
        IReadOnlyDictionary<int, (string[] Labels, (string Text, int VoiceClip)[] Lines)> stages)
    {
        // Any key works, as long as the file leads with it; a varying one proves the column indexing.
        var key = new byte[HollywoodScript.KeySize];
        for (int i = 0; i < key.Length; i++)
            key[i] = (byte)(i * 7 + 13);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp850 = Encoding.GetEncoding(850);

        byte[] Row(string text, int size)
        {
            var row = new byte[size];
            byte[] bytes = cp850.GetBytes(text);
            bytes.AsSpan(0, Math.Min(bytes.Length, size - 1)).CopyTo(row);
            for (int c = 0; c < size; c++)
                row[c] = (byte)(row[c] + key[c]);
            return row;
        }

        using var ms = new MemoryStream();
        ms.Write(key);
        long tableAt = ms.Position;
        ms.Write(new byte[HollywoodScript.StageOffsetCount * 4]);

        var offsets = new Dictionary<int, uint>();
        foreach ((int index, (string[] labels, (string Text, int VoiceClip)[] lines)) in stages.OrderBy(s => s.Key))
        {
            offsets[index] = (uint)ms.Position;

            // Cue records sit at frame 0 of successive rows; the rest of the grid stays zero.
            var cues = new byte[HollywoodScript.DescriptorTableSize];
            for (int r = 0; r < lines.Length; r++)
            {
                if (lines[r].VoiceClip == 0)
                    continue;
                int at = r * HollywoodScript.StageCueStride * HollywoodScript.CueRecordSize;
                if (at + HollywoodScript.CueRecordSize > cues.Length)
                    break;
                BinaryPrimitives.WriteUInt16LittleEndian(cues.AsSpan(at), (ushort)(HollywoodScript.LargeRowBaseId + r));
                BinaryPrimitives.WriteUInt16LittleEndian(cues.AsSpan(at + 3), (ushort)lines[r].VoiceClip);
            }

            ms.Write(cues);
            ms.WriteByte((byte)labels.Length);
            var u16 = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(u16, (ushort)lines.Length);
            ms.Write(u16);
            foreach (string label in labels)
                ms.Write(Row(label, HollywoodScript.SmallRowSize));
            foreach ((string text, _) in lines)
                ms.Write(Row(text, HollywoodScript.LargeRowSize));
        }

        byte[] data = ms.ToArray();
        foreach ((int index, uint offset) in offsets)
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan((int)tableAt + index * 4), offset);
        return data;
    }

    /// <summary>The one tableless Hollywood Monsters scene archive: a bare screen followed by its palette.</summary>
    public static byte[] HmHeadlessScreen(byte[] screen, byte[] palette)
    {
        using var ms = new MemoryStream();
        ms.Write(screen);
        ms.Write(palette);
        return ms.ToArray();
    }

    public static byte[] Visemes(IReadOnlyList<byte[]?> tracks)
    {
        using var ms = new MemoryStream();
        var table = new byte[VisemeArchive.TableSize];
        uint cursor = (uint)VisemeArchive.TableSize;
        for (int i = 0; i < tracks.Count; i++)
        {
            if (tracks[i] is null) continue;
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(i * 6), cursor);
            BinaryPrimitives.WriteUInt16LittleEndian(table.AsSpan(i * 6 + 4), (ushort)tracks[i]!.Length);
            cursor += (uint)tracks[i]!.Length;
        }
        ms.Write(table);
        foreach (byte[]? t in tracks) if (t is not null) ms.Write(t);
        return ms.ToArray();
    }

    /// <summary>A voice shard: <paramref name="clips"/> maps clip index → payload; <paramref name="foreign"/> indices get an out-of-file offset (a cross-shard reference).</summary>
    public static byte[] VoiceShard(IReadOnlyDictionary<int, byte[]> clips, IEnumerable<int>? foreign = null)
    {
        var table = new byte[VoiceArchive.TableSize];
        using var ms = new MemoryStream();
        uint cursor = (uint)VoiceArchive.TableSize;
        var payload = new MemoryStream();
        foreach ((int k, byte[] pcm) in clips.OrderBy(kv => kv.Key))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(k * 4), cursor);
            payload.Write(pcm);
            cursor += (uint)pcm.Length;
        }
        foreach (int k in foreign ?? [])
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(k * 4), 0x7FFF_FFF0);
        ms.Write(table);
        payload.Position = 0;
        payload.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// The seven Runaway 1 voice shards as the game's own exporter wrote them: every shard's table
    /// describes every clip, giving its own local offset for the clips it holds and the clip's offset in
    /// the whole collection -- always the larger number -- for the rest.
    /// </summary>
    public static byte[][] VoiceShards(IReadOnlyList<(int Index, int Size, int Shard)> clips, int shardCount)
    {
        var tables = new byte[shardCount][];
        var payloads = new MemoryStream[shardCount];
        var local = new uint[shardCount];
        for (int s = 0; s < shardCount; s++)
        {
            tables[s] = new byte[VoiceArchive.TableSize];
            payloads[s] = new MemoryStream();
            local[s] = (uint)VoiceArchive.TableSize;
        }

        uint global = (uint)VoiceArchive.TableSize;
        foreach ((int index, int size, int shard) in clips.OrderBy(c => c.Index))
        {
            for (int s = 0; s < shardCount; s++)
            {
                uint value = s == shard ? local[shard] : global;
                BinaryPrimitives.WriteUInt32LittleEndian(tables[s].AsSpan(index * 4), value);
            }

            payloads[shard].Write(new byte[size]);
            local[shard] += (uint)size;
            global += (uint)size;
        }

        var result = new byte[shardCount][];
        for (int s = 0; s < shardCount; s++)
        {
            using var ms = new MemoryStream();
            ms.Write(tables[s]);
            payloads[s].Position = 0;
            payloads[s].CopyTo(ms);
            result[s] = ms.ToArray();
        }
        return result;
    }

    /// <summary>A DATAVC00 keyfile carrying the given (name → real 1024-byte header) pairs, XOR-chained.</summary>
    public static byte[] Keyfile(IReadOnlyList<(string Name, byte[] Header)> videos)
    {
        int n = videos.Count;
        uint dataOff = (uint)(8 + n * 15);
        byte seed = (byte)(dataOff & 0xFF);
        var out_ = new byte[dataOff + n * 2048];
        BinaryPrimitives.WriteUInt32LittleEndian(out_, dataOff);
        BinaryPrimitives.WriteUInt32LittleEndian(out_.AsSpan(4), (uint)n);
        for (int i = 0; i < n; i++)
        {
            var rec = new byte[15];
            System.Text.Encoding.ASCII.GetBytes(videos[i].Name).CopyTo(rec, 0);
            XorChainEncode(rec, seed).CopyTo(out_, 8 + i * 15);
            var block = new byte[2048];
            videos[i].Header.CopyTo(block, 0);
            XorChainEncode(block.AsSpan(0, 1024).ToArray(), seed).CopyTo(out_, (int)dataOff + i * 2048);
        }
        return out_;
    }

    /// <summary>Inverse of the keyfile's chain: raw[0] = plain[0] ^ seed, raw[i] = plain[i] ^ raw[i-1].</summary>
    private static byte[] XorChainEncode(byte[] plain, byte seed)
    {
        var raw = new byte[plain.Length];
        if (plain.Length == 0) return raw;
        raw[0] = (byte)(plain[0] ^ seed);
        for (int i = 1; i < plain.Length; i++)
            raw[i] = (byte)(plain[i] ^ raw[i - 1]);
        return raw;
    }
}

public class SceneArchiveTests
{
    [Fact]
    public void ReadsOffsetAndSizeHalves_SkippingEmptySlots()
    {
        byte[] a = SyntheticArchives.Scene([new byte[100], null, new byte[7]]);

        List<ArchiveEntry> entries = SceneArchive.ReadEntries(a, a.Length);

        Assert.Equal(2, entries.Count);
        Assert.Equal(new ArchiveEntry(0, 320, 100), entries[0]);
        Assert.Equal(new ArchiveEntry(2, 420, 7), entries[1]);
    }

    [Fact]
    public void FiftySlotTables_Work()
    {
        byte[] a = SyntheticArchives.Scene([new byte[10]], slots: 50);
        List<ArchiveEntry> entries = SceneArchive.ReadEntries(a, a.Length);
        Assert.Single(entries);
        Assert.Equal(400, entries[0].Offset);
    }

    [Fact]
    public void FromStream_ReadsOnlyTheTable()
    {
        byte[] a = SyntheticArchives.Scene([new byte[100], new byte[50]]);
        using var ms = new MemoryStream(a);
        Assert.Equal(2, SceneArchive.ReadEntries(ms).Count);
    }

    [Fact]
    public void EntryPastEndOfFile_IsDropped()
    {
        byte[] a = SyntheticArchives.Scene([new byte[100]]);
        Assert.Empty(SceneArchive.ReadEntries(a, fileLength: 300));
    }

    [Theory]
    [InlineData("RESOURCE.A00", true)]
    [InlineData("Resource.h13", true)]
    [InlineData("RESOURCE.M01", false)]
    [InlineData("Resource.s04", false)]
    [InlineData("RESOURCE.000", false)]
    [InlineData("RESOURCE.004", false)]
    [InlineData("DATAACA0.000", false)]
    public void SceneArchiveNames(string name, bool expected) => Assert.Equal(expected, SceneArchive.IsSceneArchiveName(name));
}

public class AudioArchiveTests
{
    [Fact]
    public void SizesAreTheGapToTheNextOffset()
    {
        byte[] a = SyntheticArchives.Audio([new byte[30], new byte[20], null, new byte[5]]);

        List<ArchiveEntry> entries = AudioArchive.ReadEntries(a, a.Length);

        Assert.Equal(3, entries.Count);
        Assert.Equal(new ArchiveEntry(1, 400, 30), entries[0]);
        Assert.Equal(new ArchiveEntry(2, 430, 20), entries[1]);
        Assert.Equal(new ArchiveEntry(4, 450, 5), entries[2]);
    }

    [Fact]
    public void AliasedSlots_ShareOffsetAndSize()
    {
        byte[] a = SyntheticArchives.Audio([new byte[30]]);
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(2 * 4), 400); // slot 2 aliases slot 1
        List<ArchiveEntry> entries = AudioArchive.ReadEntries(a, a.Length);
        Assert.Equal(2, entries.Count);
        Assert.Equal(entries[0].Size, entries[1].Size);
    }
}

public class GlobalAndVisemeArchiveTests
{
    [Fact]
    public void GlobalArchive_HasTwentyByteHeaderThen500Pairs()
    {
        byte[] a = SyntheticArchives.Global([new byte[10], null, new byte[3]]);
        List<ArchiveEntry> entries = GlobalArchive.ReadEntries(a, a.Length);
        Assert.Equal(2, entries.Count);
        Assert.Equal(new ArchiveEntry(0, 0xFB4, 10), entries[0]);
        Assert.Equal(new ArchiveEntry(2, 0xFB4 + 10, 3), entries[1]);
    }

    [Fact]
    public void GlobalArchive_IsNotMistakenForRunaway2_WhenTheWholeHeaderIsRead()
    {
        // Runaway 1's entry 0 begins right after the 500-slot table, so the u32 at offset 20 reads as
        // 4020 -- which is also a valid Runaway 2 table_half_bytes. Handing the reader 8,192 bytes, as
        // the stream overload does, used to be enough for the Runaway 2 branch to take the file and
        // shatter it into hundreds of plausible fragments.
        byte[] a = SyntheticArchives.Global([new byte[10], null, new byte[3]]);
        var padded = new byte[Math.Max(a.Length, 9000)];
        a.CopyTo(padded, 0);

        List<ArchiveEntry> entries = GlobalArchive.ReadEntries(new MemoryStream(padded));

        Assert.Equal(2, entries.Count);
        Assert.Equal(new ArchiveEntry(0, 0xFB4, 10), entries[0]);
        Assert.Equal(new ArchiveEntry(2, 0xFB4 + 10, 3), entries[1]);
    }

    [Fact]
    public void InterfaceArchive_SizesRunToTheNextDistinctOffset()
    {
        byte[] a = SyntheticArchives.Interface([new byte[10], new byte[3], new byte[7]]);
        List<ArchiveEntry> entries = InterfaceArchive.ReadEntries(a, a.Length);

        Assert.Equal(3, entries.Count);
        Assert.Equal(new ArchiveEntry(0, 12, 10), entries[0]);
        Assert.Equal(new ArchiveEntry(1, 22, 3), entries[1]);
        Assert.Equal(new ArchiveEntry(2, 25, 7), entries[2]);
    }

    [Fact]
    public void InterfaceArchive_AliasedSlotsAllGetTheImage()
    {
        // Yesterday points 48 consecutive slots at one PNG. Sizing by the gap to the next slot rather
        // than to the next distinct offset would leave every alias but the last one empty.
        byte[] a = SyntheticArchives.Interface([new byte[10], null, null, new byte[4]]);
        List<ArchiveEntry> entries = InterfaceArchive.ReadEntries(a, a.Length);

        Assert.Equal(4, entries.Count);
        Assert.All(entries.Take(3), e => Assert.Equal(16, e.Offset));
        Assert.All(entries.Take(3), e => Assert.Equal(10, e.Size));
        Assert.Equal(new ArchiveEntry(3, 26, 4), entries[3]);
    }

    [Fact]
    public void VisemeArchive_SixByteCatalogue()
    {
        byte[] a = SyntheticArchives.Visemes([new byte[] { 0, 1, 2 }, null, new byte[] { 5 }]);
        List<ArchiveEntry> entries = VisemeArchive.ReadEntries(a, a.Length);
        Assert.Equal(2, entries.Count);
        Assert.Equal(new ArchiveEntry(0, 36000, 3), entries[0]);
        Assert.Equal("0\n1\n2\n", VisemeArchive.ToText(new byte[] { 0, 1, 2 }));
    }
}

public class VoiceArchiveTests
{
    [Fact]
    public void TheSmallestOfTheSevenOffsetsNamesTheShardThatHoldsTheClip()
    {
        string dir = Directory.CreateTempSubdirectory("runaway-voice").FullName;
        try
        {
            // Clip 7 lives in shard 1 and is the trap: its placeholder in shard 0 (48,010) is a valid
            // offset inside shard 0, so taking the first shard that merely fits reads shard 0's own
            // audio instead. Its local offset in shard 1 is smaller, which is what identifies it.
            byte[][] shards = SyntheticArchives.VoiceShards(
                [(5, 10, 0), (7, 20, 1), (9, 4000, 1), (11, 30, 0)], shardCount: 2);

            string s0 = Path.Combine(dir, "DATAACA0.000");
            string s1 = Path.Combine(dir, "DATAACA1.000");
            File.WriteAllBytes(s0, shards[0]);
            File.WriteAllBytes(s1, shards[1]);

            var clips = VoiceArchive.ReadClips([s0, s1]);

            Assert.Equal([5, 7, 9, 11], clips.Keys);
            Assert.Equal((s0, 10L), (clips[5].ShardPath, clips[5].Size));
            Assert.Equal((s1, 20L), (clips[7].ShardPath, clips[7].Size));
            Assert.Equal((s1, 4000L), (clips[9].ShardPath, clips[9].Size));
            Assert.Equal((s0, 30L), (clips[11].ShardPath, clips[11].Size));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RealInstall_ResolvesEveryClipAndTilesEveryShard()
    {
        const string dataa = @"F:\Games\Steam\steamapps\common\Runaway A Road Adventure\Dataa";
        if (!Directory.Exists(dataa)) return;

        List<string> shards = VoiceArchive.ShardNames().Select(n => Path.Combine(dataa, n)).ToList();
        if (!shards.All(File.Exists)) return;

        var clips = VoiceArchive.ReadClips(shards);

        // The live run is slots 1..5591 with nothing before or after it.
        Assert.Equal(5591, clips.Count);
        Assert.Equal(Enumerable.Range(1, 5591), clips.Keys);

        // Every shard is tiled from the end of its table to its last byte, with no gap and no overlap.
        foreach (IGrouping<string, VoiceArchive.VoiceClip> shard in clips.Values.GroupBy(c => c.ShardPath))
        {
            List<VoiceArchive.VoiceClip> ordered = shard.OrderBy(c => c.Offset).ToList();
            Assert.Equal(VoiceArchive.TableSize, ordered[0].Offset);
            for (int i = 1; i < ordered.Count; i++)
                Assert.Equal(ordered[i - 1].Offset + ordered[i - 1].Size, ordered[i].Offset);
            Assert.Equal(new FileInfo(shard.Key).Length, ordered[^1].Offset + ordered[^1].Size);
        }

        // All seven hold clips, and the big shards hold the most: reading the first shard whose value
        // merely fits would have answered 385 of these from the wrong one.
        Assert.Equal(7, clips.Values.Select(c => c.ShardPath).Distinct().Count());
        Assert.Equal(1576, clips.Values.Count(c => c.ShardPath.EndsWith("DATAACA6.000", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void LeftoverValuesPastTheLiveRunAreNotClips()
    {
        string dir = Directory.CreateTempSubdirectory("runaway-voice-junk").FullName;
        try
        {
            byte[][] shards = SyntheticArchives.VoiceShards([(1, 100, 0), (2, 100, 0)], shardCount: 1);

            // What the real tables carry past the last clip: the exporter's buffer, read as offsets.
            // One lands inside the table, one past the live run but backwards, one past the file.
            BinaryPrimitives.WriteUInt32LittleEndian(shards[0].AsSpan(900 * 4), 16_858);
            BinaryPrimitives.WriteUInt32LittleEndian(shards[0].AsSpan(901 * 4), (uint)VoiceArchive.TableSize + 50);
            BinaryPrimitives.WriteUInt32LittleEndian(shards[0].AsSpan(902 * 4), 0x7FFF_FFF0);

            string s0 = Path.Combine(dir, "DATAACA0.000");
            File.WriteAllBytes(s0, shards[0]);

            Assert.Equal([1, 2], VoiceArchive.ReadClips([s0]).Keys);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class VideoKeyfileTests
{
    [Fact]
    public void DecodesNamesAndHeaders_ThroughTheXorChain()
    {
        var header = new byte[1024];
        "BIKi"u8.CopyTo(header);
        for (int i = 4; i < header.Length; i++) header[i] = (byte)(i * 31);
        byte[] key = SyntheticArchives.Keyfile([("DATAVA01.001", header), ("DATAVB02.003", new byte[1024])]);

        VideoKeyfile? kf = VideoKeyfile.Parse(key);

        Assert.NotNull(kf);
        Assert.Equal(2, kf.Count);
        Assert.Equal((byte)((8 + 2 * 15) & 0xFF), kf.Seed);
        Assert.Equal(header, kf.HeaderFor("datava01.001"));
        Assert.Null(kf.HeaderFor("DATAVZ99.000"));
    }

    [Fact]
    public void WrongShape_IsRejected()
    {
        Assert.Null(VideoKeyfile.Parse(new byte[8]));
        byte[] key = SyntheticArchives.Keyfile([("DATAVA01.001", new byte[1024])]);
        Assert.Null(VideoKeyfile.Parse(key[..^1]));
    }

    [Fact]
    public void Restore_ReplacesTheFirstKilobyte()
    {
        string dir = Directory.CreateTempSubdirectory("runaway-video").FullName;
        try
        {
            var header = new byte[1024];
            "BIKi"u8.CopyTo(header);
            VideoKeyfile kf = VideoKeyfile.Parse(SyntheticArchives.Keyfile([("DATAVA01.001", header)]))!;

            string video = Path.Combine(dir, "DATAVA01.001");
            var junkThenBody = new byte[1024 + 100];
            Array.Fill(junkThenBody, (byte)0xEE, 0, 1024);
            Array.Fill(junkThenBody, (byte)0x42, 1024, 100);
            File.WriteAllBytes(video, junkThenBody);

            using var restored = new MemoryStream();
            Assert.True(kf.Restore(video, restored));
            byte[] r = restored.ToArray();
            Assert.Equal(1124, r.Length);
            Assert.Equal("BIKi"u8.ToArray(), r[..4]);
            Assert.All(r[1024..], b => Assert.Equal(0x42, b));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class ArchiveWindowStreamTests
{
    [Fact]
    public void ReadsOnlyItsWindow()
    {
        var inner = new MemoryStream(Enumerable.Range(0, 100).Select(i => (byte)i).ToArray());
        using var window = new ArchiveWindowStream(inner, 10, 5);

        Assert.Equal(5, window.Length);
        var buf = new byte[10];
        Assert.Equal(5, window.Read(buf, 0, 10));
        Assert.Equal(new byte[] { 10, 11, 12, 13, 14 }, buf[..5]);
        Assert.Equal(0, window.Read(buf, 0, 10));

        window.Position = 3;
        Assert.Equal(2, window.Read(buf, 0, 10));
        Assert.Equal(13, buf[0]);
    }

    [Fact]
    public void WindowOutsideTheStream_IsRejected()
    {
        var inner = new MemoryStream(new byte[10]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchiveWindowStream(inner, 8, 5));
    }
}
