using System.Buffers.Binary;

namespace RunawayExplorer.Core.FileSystem;

/// <summary>One populated slot of an archive table.</summary>
public readonly record struct ArchiveEntry(int Index, long Offset, long Size);

/// <summary>
/// <c>RESOURCE.&lt;L&gt;&lt;nn&gt;</c> -- one scene each.
/// <code>
/// offset 0        u32 offset[N/2]      absolute file offsets, unused = 0
/// offset N*2      u32 size[N/2]        byte size of the matching entry
/// offset N*4      entry data...
/// </code>
/// <c>offset[0]</c> is where entry 0 begins, which is immediately after the table, so
/// <c>table_len = offset[0]</c> and <c>N = table_len / 4</c>. The sizes are exact, not padded.
/// <para>
/// <b>Do not read the table as a flat list of offsets.</b> That misreads every size value as a pointer
/// into the middle of another entry -- and because entry 0 is a 1024-wide raster, those phantom
/// entries decode as plausible-looking, mis-aligned fragments of real images. Both earlier extractors
/// made this mistake, and it cost the project months chasing a "horizontal shift" that does not exist.
/// </para>
/// </summary>
public static class SceneArchive
{
    /// <summary>Parses the table at the start of <paramref name="data"/> (the whole file, or at least the table).</summary>
    public static List<ArchiveEntry> ReadEntries(ReadOnlySpan<byte> data, long fileLength)
    {
        var entries = new List<ArchiveEntry>();
        if (data.Length < 8)
            return entries;

        uint tbl = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (tbl == 0 || tbl % 8 != 0 || tbl > data.Length)
            return entries;

        int half = (int)(tbl / 8);
        for (int i = 0; i < half; i++)
        {
            uint o = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i * 4));
            uint s = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(half * 4 + i * 4));
            if (o != 0 && s != 0 && (long)o + s <= fileLength)
                entries.Add(new ArchiveEntry(i, o, s));
        }
        return entries;
    }

    /// <summary>Reads just the table from an open archive and parses it.</summary>
    public static List<ArchiveEntry> ReadEntries(Stream archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        archive.Position = 0;
        Span<byte> first = stackalloc byte[4];
        if (archive.Read(first) < 4)
            return [];
        uint tbl = BinaryPrimitives.ReadUInt32LittleEndian(first);
        if (tbl == 0 || tbl % 8 != 0 || tbl > archive.Length || tbl > 1 << 20)
            return [];
        var table = new byte[tbl];
        archive.Position = 0;
        archive.ReadExactly(table);
        return ReadEntries(table, archive.Length);
    }

    /// <summary>True for <c>RESOURCE.A00</c>-style names: a letter and two digits after the dot.</summary>
    public static bool IsSceneArchiveName(string fileName)
    {
        string ext = Path.GetExtension(fileName);
        return Path.GetFileNameWithoutExtension(fileName).Equals("RESOURCE", StringComparison.OrdinalIgnoreCase)
               && ext.Length == 4
               && char.IsLetter(ext[1]) && char.ToUpperInvariant(ext[1]) is not ('M' or 'S')
               && char.IsDigit(ext[2]) && char.IsDigit(ext[3]);
    }
}

/// <summary>
/// <c>RESOURCE.M&lt;nn&gt;</c>, <c>S&lt;nn&gt;</c>, <c>002</c> -- audio. A plain offset table with no size
/// half: slot 0 is always empty, so <c>table_len</c> is the smallest non-zero word, and an entry's size
/// is the gap to the next-highest offset. <c>002</c> has several entries aliasing the same offset.
/// </summary>
public static class AudioArchive
{
    public static List<ArchiveEntry> ReadEntries(ReadOnlySpan<byte> data, long fileLength)
    {
        var entries = new List<ArchiveEntry>();
        if (data.Length < 8)
            return entries;

        uint first = 0;
        int scanLimit = Math.Min(data.Length, 1 << 16);
        for (int i = 0; i + 4 <= scanLimit; i += 4)
        {
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i));
            if (v != 0 && (first == 0 || v < first))
                first = v;
            if (first != 0 && i + 4 >= first)
                break;
        }

        if (first == 0 || first % 4 != 0 || first >= fileLength || first > data.Length)
            return entries;

        int n = (int)(first / 4);
        var offsets = new uint[n];
        for (int i = 0; i < n; i++)
            offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i * 4));

        // Entry i spans [off[i], next offset greater than off[i]) -- the table is not required to be sorted.
        uint[] sorted = offsets.Where(o => o != 0).Distinct().OrderBy(o => o).ToArray();
        for (int i = 0; i < n; i++)
        {
            uint o = offsets[i];
            if (o == 0 || o >= fileLength)
                continue;
            int idx = Array.BinarySearch(sorted, o);
            long end = idx >= 0 && idx + 1 < sorted.Length ? sorted[idx + 1] : fileLength;
            long size = end - o;
            if (size > 0)
                entries.Add(new ArchiveEntry(i, o, size));
        }
        return entries;
    }

    public static List<ArchiveEntry> ReadEntries(Stream archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        archive.Position = 0;
        var head = new byte[(int)Math.Min(archive.Length, 1 << 16)];
        archive.ReadExactly(head);
        return ReadEntries(head, archive.Length);
    }
}

/// <summary>
/// <c>RESOURCE.000</c> -- global data (fonts, UI atlas, localised bitmaps).
/// <code>
/// 0x000   byte[20]    header, starts 03 01 06 01 01 01 ...
/// 0x014   u32[500]    offsets
/// 0x7F4   u32[500]    sizes
/// 0xFB4   entry data
/// </code>
/// </summary>
public static class GlobalArchive
{
    public const int HeaderSize = 20;
    public const int SlotCount = 500;
    public const int TableEnd = HeaderSize + SlotCount * 8;

    public static List<ArchiveEntry> ReadEntries(ReadOnlySpan<byte> data, long fileLength)
    {
        var entries = new List<ArchiveEntry>();
        if (data.Length < TableEnd)
            return entries;

        for (int i = 0; i < SlotCount; i++)
        {
            uint o = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(HeaderSize + i * 4));
            uint s = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(HeaderSize + SlotCount * 4 + i * 4));
            if (o != 0 && s != 0 && (long)o + s <= fileLength)
                entries.Add(new ArchiveEntry(i, o, s));
        }
        return entries;
    }

    public static List<ArchiveEntry> ReadEntries(Stream archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.Length < TableEnd)
            return [];
        archive.Position = 0;
        var table = new byte[TableEnd];
        archive.ReadExactly(table);
        return ReadEntries(table, archive.Length);
    }
}

/// <summary>
/// <c>RESOURCE.004</c> -- lip-sync viseme tracks. <c>{ u32 offset, u16 size }[6000]</c>, then data.
/// Each entry is one byte per animation tick, values 0–5 (six mouth shapes). Entry <c>k</c> pairs with
/// voice clip <c>k</c>.
/// </summary>
public static class VisemeArchive
{
    public const int SlotCount = 6000;
    public const int TableSize = SlotCount * 6;

    public static List<ArchiveEntry> ReadEntries(ReadOnlySpan<byte> data, long fileLength)
    {
        var entries = new List<ArchiveEntry>();
        if (data.Length < TableSize)
            return entries;

        for (int i = 0; i < SlotCount; i++)
        {
            uint o = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i * 6));
            ushort s = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(i * 6 + 4));
            if (o != 0 && s != 0 && (long)o + s <= fileLength)
                entries.Add(new ArchiveEntry(i, o, s));
        }
        return entries;
    }

    public static List<ArchiveEntry> ReadEntries(Stream archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.Length < TableSize)
            return [];
        archive.Position = 0;
        var table = new byte[TableSize];
        archive.ReadExactly(table);
        return ReadEntries(table, archive.Length);
    }

    /// <summary>Renders a viseme track as one integer per line.</summary>
    public static string ToText(ReadOnlySpan<byte> track)
    {
        var sb = new System.Text.StringBuilder(track.Length * 2);
        foreach (byte b in track)
            sb.Append(b).Append('\n');
        return sb.ToString();
    }
}

/// <summary>
/// <c>Dataa/DATAACA&lt;0-6&gt;.000</c> -- voice lines. Seven independent archives, each a plain offset
/// table of 12,000 slots. A clip index may appear in more than one shard; an offset ≥ that shard's file
/// size is a reference to another shard and is skipped. Payload is raw 8-bit unsigned mono PCM.
/// </summary>
public static class VoiceArchive
{
    public const int SlotCount = 12_000;
    public const int TableSize = SlotCount * 4;
    public const int ShardCount = 7;

    /// <summary>One resolved clip: which shard holds it and where.</summary>
    public readonly record struct VoiceClip(int Index, string ShardPath, long Offset, long Size);

    /// <summary>The shard file names, in the order the first-hit-wins rule consults them.</summary>
    public static IEnumerable<string> ShardNames() =>
        Enumerable.Range(0, ShardCount).Select(i => $"DATAACA{i}.000");

    /// <summary>
    /// Resolves every clip across <paramref name="shardPaths"/> (in order). The first shard that holds a
    /// clip locally wins; the same index in a later shard is ignored.
    /// </summary>
    public static SortedDictionary<int, VoiceClip> ReadClips(IReadOnlyList<string> shardPaths)
    {
        ArgumentNullException.ThrowIfNull(shardPaths);
        var clips = new SortedDictionary<int, VoiceClip>();

        foreach (string path in shardPaths)
        {
            using FileStream f = File.OpenRead(path);
            if (f.Length < TableSize)
                continue;

            var table = new byte[TableSize];
            f.ReadExactly(table);
            long fileSize = f.Length;

            var local = new List<(int Index, uint Offset)>();
            for (int k = 0; k < SlotCount; k++)
            {
                uint o = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(k * 4));
                if (o > 0 && o < fileSize)
                    local.Add((k, o));
            }

            local.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            for (int i = 0; i < local.Count; i++)
            {
                (int k, uint off) = local[i];
                if (clips.ContainsKey(k))
                    continue;
                long end = i + 1 < local.Count ? local[i + 1].Offset : fileSize;
                long size = end - off;
                if (size > 0)
                    clips[k] = new VoiceClip(k, path, off, size);
            }
        }

        return clips;
    }
}
