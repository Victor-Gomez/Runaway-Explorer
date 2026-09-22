using System.Buffers.Binary;

namespace RunawayExplorer.Core.FileSystem;

/// <summary>One populated slot of an archive table.</summary>
public readonly record struct ArchiveEntry(int Index, long Offset, long Size);

/// <summary>One populated slot of an audio archive table, including format information.</summary>
public readonly record struct AudioArchiveEntry(int Index, long Offset, long Size, AudioFormat Format);

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

        uint word0 = BinaryPrimitives.ReadUInt32LittleEndian(data);
        uint word1 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));

        // Runaway 2 table: word0 is table_half_bytes (count * 4), and word1 is entry 0 offset (4 + table_half_bytes * 2)
        if (word0 > 0 && word0 % 4 == 0 && word1 == 4 + word0 * 2)
        {
            int count = (int)(word0 / 4);
            int tableEnd = (int)(4 + word0 * 2);
            if (tableEnd > data.Length)
                return entries;

            for (int i = 0; i < count; i++)
            {
                uint o = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4 + i * 4));
                uint s = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4 + (int)word0 + i * 4));
                if (o != 0 && s != 0 && (long)o + s <= fileLength)
                    entries.Add(new ArchiveEntry(i, o, s));
            }
            return entries;
        }

        // Runaway 1 table: word0 is table_len (count * 8), and offsets start at 0
        if (word0 == 0 || word0 % 8 != 0 || word0 > data.Length)
            return entries;

        int half = (int)(word0 / 8);
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
        Span<byte> first = stackalloc byte[8];
        if (archive.Read(first) < 8)
            return [];
        uint word0 = BinaryPrimitives.ReadUInt32LittleEndian(first);
        uint word1 = BinaryPrimitives.ReadUInt32LittleEndian(first.Slice(4));

        uint tableLen;
        if (word0 > 0 && word0 % 4 == 0 && word1 == 4 + word0 * 2)
        {
            tableLen = 4 + word0 * 2;
        }
        else
        {
            tableLen = word0;
        }

        if (tableLen == 0 || tableLen > archive.Length || tableLen > 1 << 20)
            return [];
        var table = new byte[tableLen];
        archive.Position = 0;
        archive.ReadExactly(table);
        return ReadEntries(table, archive.Length);
    }

    /// <summary>True for scene archive names in Runaway 1 (e.g. RESOURCE.A00) and Runaway 2 (e.g. RESOURCE.B04A, RESOURCE.SP1).</summary>
    public static bool IsSceneArchiveName(string fileName, GameVersion game = GameVersion.Runaway1)
    {
        if (!Path.GetFileNameWithoutExtension(fileName).Equals("RESOURCE", StringComparison.OrdinalIgnoreCase))
            return false;

        string ext = Path.GetExtension(fileName);
        if (ext.Length < 4 || ext.Length > 5)
            return false;

        string code = ext[1..].ToUpperInvariant();
        // Audio archives start with M or S (followed by digits)
        if (code.StartsWith("M", StringComparison.Ordinal) && code.Length == 3 && char.IsDigit(code[1]))
            return false;
        if (code.StartsWith("S", StringComparison.Ordinal) && code.Length == 3 && char.IsDigit(code[1]))
            return false;
        // Non-scene resource files
        if (code is "000" or "003" or "004" or "005")
            return false;
        if (code == "002" && game != GameVersion.Runaway3)
            return false;
        if (code == "001" && game == GameVersion.Runaway1)
            return false;

        return true;
    }
}

/// <summary>
/// <c>RESOURCE.M&lt;nn&gt;</c>, <c>S&lt;nn&gt;</c>, <c>002</c> -- audio.
/// In Runaway 1: plain offset table with no size half.
/// In Runaway 2: table has <c>u32 count</c> followed by 9-byte records: <c>{ u32 offset, u32 size, u8 format }</c>.
/// </summary>
public static class AudioArchive
{
    public static List<AudioArchiveEntry> ReadAudioEntries(ReadOnlySpan<byte> data, long fileLength)
    {
        var entries = new List<AudioArchiveEntry>();
        if (data.Length < 8)
            return entries;

        // Runaway 2 audio table: word0 is count, word1 is firstOffset (4 + count * 9)
        uint countR2 = BinaryPrimitives.ReadUInt32LittleEndian(data);
        uint firstOffR2 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));
        if (countR2 > 0 && countR2 < 100_000 && firstOffR2 == 4 + countR2 * 9 && data.Length >= firstOffR2)
        {
            for (int i = 0; i < countR2; i++)
            {
                int p = 4 + i * 9;
                uint o = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p));
                uint s = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p + 4));
                byte fmt = data[p + 8];
                AudioFormat format = fmt == 0 ? AudioFormat.Wav : AudioFormat.Mp3;
                if (o != 0 && s != 0 && (long)o + s <= fileLength)
                    entries.Add(new AudioArchiveEntry(i, o, s, format));
            }
            return entries;
        }

        // Runaway 1 audio table: plain offsets
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
                entries.Add(new AudioArchiveEntry(i, o, size, AudioFormat.RawPcm));
        }
        return entries;
    }

    public static List<AudioArchiveEntry> ReadAudioEntries(Stream archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        archive.Position = 0;
        var head = new byte[(int)Math.Min(archive.Length, 1 << 16)];
        archive.ReadExactly(head);
        return ReadAudioEntries(head, archive.Length);
    }

    public static List<ArchiveEntry> ReadEntries(ReadOnlySpan<byte> data, long fileLength)
    {
        var audioEntries = ReadAudioEntries(data, fileLength);
        var list = new List<ArchiveEntry>(audioEntries.Count);
        foreach (AudioArchiveEntry e in audioEntries)
            list.Add(new ArchiveEntry(e.Index, e.Offset, e.Size));
        return list;
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
/// Runaway 1: 20-byte header, 500 slots.
/// Runaway 2: 24-byte header (table_half_bytes at 20 is 1248), 312 slots.
/// </summary>
public static class GlobalArchive
{
    public const int HeaderSizeR1 = 20;
    public const int SlotCountR1 = 500;
    public const int TableEndR1 = HeaderSizeR1 + SlotCountR1 * 8;

    public const int HeaderSize = HeaderSizeR1;
    public const int SlotCount = SlotCountR1;
    public const int TableEnd = TableEndR1;

    public static List<ArchiveEntry> ReadEntries(ReadOnlySpan<byte> data, long fileLength)
    {
        var entries = new List<ArchiveEntry>();
        if (data.Length < 24)
            return entries;

        // Check for Runaway 2 header: byte 20 is table_half_bytes (1248)
        uint r2HalfBytes = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20));
        if (r2HalfBytes > 0 && r2HalfBytes % 4 == 0 && r2HalfBytes <= 8192)
        {
            int slotCount = (int)(r2HalfBytes / 4);
            int tableEnd = 24 + (int)r2HalfBytes * 2;
            if (data.Length >= tableEnd)
            {
                for (int i = 0; i < slotCount; i++)
                {
                    uint o = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24 + i * 4));
                    uint s = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24 + (int)r2HalfBytes + i * 4));
                    if (o != 0 && s != 0 && (long)o + s <= fileLength)
                        entries.Add(new ArchiveEntry(i, o, s));
                }
                return entries;
            }
        }

        if (data.Length < TableEndR1)
            return entries;

        for (int i = 0; i < SlotCountR1; i++)
        {
            uint o = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(HeaderSizeR1 + i * 4));
            uint s = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(HeaderSizeR1 + SlotCountR1 * 4 + i * 4));
            if (o != 0 && s != 0 && (long)o + s <= fileLength)
                entries.Add(new ArchiveEntry(i, o, s));
        }
        return entries;
    }

    public static List<ArchiveEntry> ReadEntries(Stream archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        archive.Position = 0;
        var table = new byte[(int)Math.Min(archive.Length, 8192)];
        archive.ReadExactly(table);
        return ReadEntries(table, archive.Length);
    }
}

/// <summary>
/// <c>RESOURCE.004</c> -- lip-sync viseme tracks.
/// Runaway 1: <c>{ u32 offset, u16 size }[6000]</c>, 36,000 bytes.
/// Runaway 2: <c>{ u32 offset, u16 size, u8 flag }[10500]</c>, 73,500 bytes.
/// Runaway 3: <c>{ u32 offset, u32 size }[11500]</c>, 92,000 bytes.
/// </summary>
public static class VisemeArchive
{
    public const int SlotCountR1 = 6000;
    public const int TableSizeR1 = SlotCountR1 * 6;

    public const int SlotCount = SlotCountR1;
    public const int TableSize = TableSizeR1;

    public const int SlotCountR2 = 10500;
    public const int TableSizeR2 = SlotCountR2 * 7;

    public const int SlotCountR3 = 11500;
    public const int TableSizeR3 = SlotCountR3 * 8;

    public static List<ArchiveEntry> ReadEntries(ReadOnlySpan<byte> data, long fileLength)
    {
        var entries = new List<ArchiveEntry>();
        if (data.Length < TableSizeR1)
            return entries;

        // Check if Runaway 3: first offset is 92,000
        uint firstOff = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (firstOff == TableSizeR3 && data.Length >= TableSizeR3)
        {
            for (int i = 0; i < SlotCountR3; i++)
            {
                uint o = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i * 8));
                uint s = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i * 8 + 4));
                if (o != 0 && s != 0 && (long)o + s <= fileLength)
                    entries.Add(new ArchiveEntry(i, o, s));
            }
            return entries;
        }

        // Check if Runaway 2: first offset is 73,500
        if (firstOff == TableSizeR2 && data.Length >= TableSizeR2)
        {
            for (int i = 0; i < SlotCountR2; i++)
            {
                uint o = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i * 7));
                ushort s = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(i * 7 + 4));
                if (o != 0 && s != 0 && (long)o + s <= fileLength)
                    entries.Add(new ArchiveEntry(i, o, s));
            }
            return entries;
        }

        for (int i = 0; i < SlotCountR1; i++)
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
        archive.Position = 0;
        var table = new byte[(int)Math.Min(archive.Length, TableSizeR3)];
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
/// Voice lines.
/// In Runaway 1: seven independent archives <c>Dataa/DATAACA&lt;0-6&gt;.000</c>, plain offset tables of 12,000 slots.
/// In Runaway 2: single archive <c>Dataa/Dataaa.000</c>, table of 10,454 9-byte records.
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

    /// <summary>Reads voice clips from a single Runaway 2 <c>Dataaa.000</c> archive stream.</summary>
    public static SortedDictionary<int, VoiceClip> ReadSingleArchiveClips(Stream stream, string sourcePath = "")
    {
        ArgumentNullException.ThrowIfNull(stream);
        var clips = new SortedDictionary<int, VoiceClip>();
        Span<byte> head = stackalloc byte[8];
        stream.Position = 0;
        if (stream.Read(head) < 8)
            return clips;

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(head);
        uint firstOff = BinaryPrimitives.ReadUInt32LittleEndian(head.Slice(4));
        if (count == 0 || count > 100_000 || firstOff != 4 + count * 9)
            return clips;

        var table = new byte[count * 9];
        stream.Position = 4;
        stream.ReadExactly(table);

        for (int i = 0; i < count; i++)
        {
            int p = i * 9;
            uint off = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(p));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(p + 4));
            if (off > 0 && size > 0 && (long)off + size <= stream.Length)
            {
                clips[i] = new VoiceClip(i, sourcePath, off, size);
            }
        }
        return clips;
    }

    /// <summary>Reads voice clips from a single Runaway 2 <c>Dataaa.000</c> archive file.</summary>
    public static SortedDictionary<int, VoiceClip> ReadSingleArchiveClips(string dataaaPath)
    {
        ArgumentNullException.ThrowIfNull(dataaaPath);
        using FileStream f = File.OpenRead(dataaaPath);
        return ReadSingleArchiveClips(f, dataaaPath);
    }

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

