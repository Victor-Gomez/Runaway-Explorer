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
    public void FirstShardWins_AndForeignOffsetsAreSkipped()
    {
        string dir = Directory.CreateTempSubdirectory("runaway-voice").FullName;
        try
        {
            string s0 = Path.Combine(dir, "DATAACA0.000");
            string s1 = Path.Combine(dir, "DATAACA1.000");
            File.WriteAllBytes(s0, SyntheticArchives.VoiceShard(new Dictionary<int, byte[]> { [5] = new byte[10], [7] = new byte[20] }, foreign: [9]));
            File.WriteAllBytes(s1, SyntheticArchives.VoiceShard(new Dictionary<int, byte[]> { [7] = new byte[99], [9] = new byte[4] }));

            var clips = VoiceArchive.ReadClips([s0, s1]);

            Assert.Equal([5, 7, 9], clips.Keys);
            Assert.Equal((s0, 10L), (clips[5].ShardPath, clips[5].Size));
            Assert.Equal((s0, 20L), (clips[7].ShardPath, clips[7].Size)); // shard 0 wins over shard 1's copy
            Assert.Equal((s1, 4L), (clips[9].ShardPath, clips[9].Size));  // shard 0's entry was a cross-shard reference
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
