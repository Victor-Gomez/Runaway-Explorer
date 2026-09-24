using System.Buffers.Binary;
using System.Text;

namespace RunawayExplorer.Core.FileSystem;

/// <summary>
/// <c>RESOURCE.003</c> in Hollywood Monsters: the whole game script -- every spoken line, plus the
/// hotspot and inventory labels -- together with the speech cues that tie a line to its recording.
/// <para>
/// <b>Obfuscation.</b> Rows are enciphered with a per-column subtractive key, and the key is the file's
/// own first <see cref="KeySize"/> bytes:
/// <code>
/// plain[column] = (byte)(cipher[column] - key[column]);
/// </code>
/// The key is exactly as long as a large row, so column <c>c</c> of every row always uses key byte
/// <c>c</c>. There is no per-row salt.
/// </para>
/// <para>
/// <b>Layout.</b>
/// <code>
/// 0x000   byte[0x141]   key (also the first row of the table)
/// 0x141   u32[1021]     stage offsets, indexed by scene number / 10; 0 means "no such stage"
///
/// per stage, at stageOffset:
///         byte[0x186A0] speech cue descriptors -- 20,000 x 5-byte records
///         u8            small row count
///         u16           large row count
///         byte[n][0x29] small rows -- hotspot and inventory labels, enciphered
///         byte[m][0x141] large rows -- spoken lines, enciphered
/// </code>
/// Rows are NUL-terminated inside their fixed width and encoded in CP850, not Windows-1252.
/// </para>
/// <para>
/// Most of the 1,021 offset slots are zero or stale, so a stage is accepted only when its rows actually
/// decipher to text; see <see cref="ReadStages"/>. See <c>docs/formats/global-data.md</c>.
/// </para>
/// </summary>
public static class HollywoodScript
{
    /// <summary>Length of the decode key, which is also the large-row length.</summary>
    public const int KeySize = 0x141;

    /// <summary>Number of u32 slots in the stage offset table that follows the key.</summary>
    public const int StageOffsetCount = 1021;

    /// <summary>Bytes of speech-cue descriptors ahead of each stage's row counts (20,000 records).</summary>
    public const int DescriptorTableSize = 0x186A0;

    /// <summary>A hotspot or inventory label row.</summary>
    public const int SmallRowSize = 0x29;

    /// <summary>A spoken line row.</summary>
    public const int LargeRowSize = 0x141;

    /// <summary>Cue text ids at or above this index address the stage's own large rows, at <c>id - 500</c>.</summary>
    public const int LargeRowBaseId = 500;

    /// <summary>A speech cue record: text id, continuation count, voice sample id.</summary>
    public const int CueRecordSize = 5;

    /// <summary>Stage cues are addressed as a grid of this many frames per row.</summary>
    public const int StageCueStride = 100;

    private static readonly Encoding Cp850;

    static HollywoodScript()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp850 = Encoding.GetEncoding(850);
    }

    /// <summary>One hotspot or inventory label. <paramref name="Id"/> is 1-based, as the cue tables address them.</summary>
    public sealed record Label(int Id, string Text);

    /// <summary>
    /// One spoken line. <paramref name="Id"/> is the cue text id (<see cref="LargeRowBaseId"/> upwards).
    /// <paramref name="VoiceClipId"/> is the <c>RESOURCE.004</c> slot the cues pair it with, or -1 if no cue
    /// references it.
    /// </summary>
    public sealed record Line(int Id, string Text, int VoiceClipId);

    /// <summary>One scene's text. <paramref name="Index"/> is the scene number divided by 10.</summary>
    public sealed record Stage(int Index, long Offset, IReadOnlyList<Label> Labels, IReadOnlyList<Line> Lines)
    {
        /// <summary>The scene number this stage belongs to (<c>Index * 10</c>).</summary>
        public int SceneNumber => Index * 10;
    }

    /// <summary>
    /// Reads every stage that deciphers to text. Slots that are zero, out of range, or decipher to
    /// something other than text are skipped: the offset table is mostly stale, so a structural check
    /// alone would admit hundreds of false stages.
    /// <para>
    /// On the Spanish first edition this yields 80 stages, 589 labels and 3,430 lines. Cross-checked
    /// against the scene list in the ScummVM <c>hollywood</c> engine, the 74 playable ones are every
    /// playable scene it knows of except 2060, 3110 and 5130, whose offset slots are zero; the other 6
    /// are cutscene stages that are also scenes in its registry. Nothing decodes that is not a scene.
    /// </para>
    /// </summary>
    public static List<Stage> ReadStages(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var stages = new List<Stage>();
        long length = stream.Length;
        if (length < KeySize + StageOffsetCount * 4)
            return stages;

        stream.Seek(0, SeekOrigin.Begin);
        var key = new byte[KeySize];
        stream.ReadExactly(key);

        var table = new byte[StageOffsetCount * 4];
        stream.ReadExactly(table);

        for (int i = 0; i < StageOffsetCount; i++)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(i * 4));
            if (offset == 0 || offset + DescriptorTableSize + 3 > length)
                continue;

            if (ReadStage(stream, key, i, offset, length) is { } stage)
                stages.Add(stage);
        }

        return stages;
    }

    private static Stage? ReadStage(Stream stream, byte[] key, int index, uint offset, long length)
    {
        stream.Seek(offset + DescriptorTableSize, SeekOrigin.Begin);
        Span<byte> counts = stackalloc byte[3];
        stream.ReadExactly(counts);

        int smallCount = counts[0];
        int largeCount = BinaryPrimitives.ReadUInt16LittleEndian(counts[1..]);
        if (largeCount == 0)
            return null;

        long rowsStart = offset + DescriptorTableSize + 3;
        long smallBytes = (long)smallCount * SmallRowSize;
        long largeBytes = (long)largeCount * LargeRowSize;
        if (rowsStart + smallBytes + largeBytes > length)
            return null;

        var smallRows = new byte[smallBytes];
        stream.ReadExactly(smallRows);
        var largeRows = new byte[largeBytes];
        stream.ReadExactly(largeRows);

        // The offset table is padded with stale values, so require the rows to actually be text. Only the
        // large rows are judged: a stage may carry no labels at all (scene 7020) or lead with an empty
        // one (scene 1070), and gating on those rejected both even though their dialogue decodes cleanly.
        // A blank line row is likewise allowed, as long as the probe is not blank throughout.
        int probe = Math.Min(largeCount, 5);
        bool anyText = false;
        for (int r = 0; r < probe; r++)
        {
            ReadOnlySpan<byte> row = Decode(largeRows, r * LargeRowSize, LargeRowSize, key);
            if (row.Length == 0)
                continue;
            if (!IsText(row))
                return null;
            anyText = true;
        }

        if (!anyText)
            return null;

        Dictionary<int, int> voiceByTextId = ReadCueVoiceMap(stream, offset, length);

        var labels = new List<Label>(smallCount);
        for (int r = 0; r < smallCount; r++)
            labels.Add(new Label(r + 1, Text(Decode(smallRows, r * SmallRowSize, SmallRowSize, key))));

        var lines = new List<Line>(largeCount);
        for (int r = 0; r < largeCount; r++)
        {
            int id = LargeRowBaseId + r;
            lines.Add(new Line(id, Text(Decode(largeRows, r * LargeRowSize, LargeRowSize, key)),
                voiceByTextId.TryGetValue(id, out int voice) ? voice : -1));
        }

        return new Stage(index, offset, labels, lines);
    }

    /// <summary>
    /// Builds text id -&gt; voice clip id from the stage's cue descriptors.
    /// <para>
    /// Only frame 0 of each cue row is read. A row is one utterance and its later frames are
    /// continuations, so the grid is densely populated with records that name a line without being the
    /// cue that plays it: scanning all 20,000 records raises apparent coverage from 42% to 92% but makes
    /// two thirds of the lines ambiguous (197,660 records disagree with an earlier record about the same
    /// line, and no majority emerges -- every candidate is attested exactly once). At frame 0 the mapping
    /// is near-unique instead: of 2,369 distinct line ids cued there across the shipped game, only 196
    /// carry more than one voice id, and the ids advance in lockstep with the recordings
    /// (line 500 -&gt; clip 2227, 501 -&gt; 2228, 502 -&gt; 2229 in stage 101).
    /// </para>
    /// <para>
    /// The result is that a little under half the lines get a clip and the rest get none. That is the
    /// intended trade: an unlinked line is obvious, a line linked to the wrong recording is not.
    /// </para>
    /// </summary>
    private static Dictionary<int, int> ReadCueVoiceMap(Stream stream, uint offset, long length)
    {
        var map = new Dictionary<int, int>();
        if (offset + DescriptorTableSize > length)
            return map;

        stream.Seek(offset, SeekOrigin.Begin);
        var cues = new byte[DescriptorTableSize];
        stream.ReadExactly(cues);

        int records = DescriptorTableSize / CueRecordSize;
        for (int record = 0; record < records; record += StageCueStride)
        {
            int p = record * CueRecordSize;
            int textId = BinaryPrimitives.ReadUInt16LittleEndian(cues.AsSpan(p));
            if (textId < LargeRowBaseId)
                continue;

            int voiceId = BinaryPrimitives.ReadUInt16LittleEndian(cues.AsSpan(p + 3));
            if (voiceId != 0)
                map.TryAdd(textId, voiceId);
        }

        return map;
    }

    /// <summary>Deciphers one row and trims it at the first NUL.</summary>
    private static ReadOnlySpan<byte> Decode(byte[] rows, int start, int size, byte[] key)
    {
        var plain = new byte[size];
        for (int c = 0; c < size; c++)
            plain[c] = (byte)(rows[start + c] - key[c]);

        int end = Array.IndexOf(plain, (byte)0);
        return end < 0 ? plain : plain.AsSpan(0, end);
    }

    private static string Text(ReadOnlySpan<byte> row) => Cp850.GetString(row);

    /// <summary>
    /// True for a deciphered row that reads as text: no control bytes, and mostly printable ASCII. The
    /// remainder is the CP850 accented range, which Spanish and Italian lines are full of.
    /// </summary>
    private static bool IsText(ReadOnlySpan<byte> row)
    {
        if (row.Length < 2)
            return false;

        int ascii = 0;
        foreach (byte b in row)
        {
            if (b < 0x20)
                return false;
            if (b < 0x7F)
                ascii++;
        }

        return ascii * 10 >= row.Length * 7;
    }
}
