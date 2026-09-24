using System.Buffers.Binary;
using System.Text;

namespace RunawayExplorer.Core.FileSystem;

/// <summary>
/// <c>Resource/RESOURCE.003</c> in Runaway 2 and Runaway 3: localized in-game dialogue, item descriptions,
/// and subtitles.
/// <para>
/// <b>Archive Layout:</b>
/// <code>
/// u32 phrase_count       (10,454 in Runaway 2; 7,228 in Runaway 3)
/// u32[phrase_count]      phrase IDs (e.g. 100, 110, 120... 90230000)
/// byte[phrase_count][401] serialized phrase records (XOR-chained)
/// </code>
/// </para>
/// <para>
/// <b>Record Codec:</b>
/// Each record is exactly 401 bytes. The text starts at byte 0 and is decoded with an index-seeded
/// XOR chain:
/// <code>
/// seed = (byte)(recordIndex &amp; 0xFF);
/// plain[0] = raw[0] ^ seed;
/// plain[i] = raw[i] ^ raw[i - 1];
/// </code>
/// The string continues until the first <c>0x00</c> NUL-terminator.
/// In Runaway 2 and Runaway 3, phrase index <c>k</c> corresponds 1:1 with voice audio clip <c>k</c>
/// in <c>Dataa/Dataaa.000</c>.
/// </para>
/// </summary>
public static class PhraseArchive
{
    public const int RecordSize = 401;

    private static readonly Encoding Windows1252;

    static PhraseArchive()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1252 = Encoding.GetEncoding(1252);
    }

    public sealed record Phrase(int Index, uint Id, string Text)
    {
        public override string ToString() => $"[{Index:D5}] (ID {Id}): {Text}";
    }

    /// <summary>Checks whether a stream matches the RESOURCE.003 phrase archive header.</summary>
    public static bool IsPhraseArchive(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (stream.Length < 8)
            return false;

        long origPos = stream.Position;
        try
        {
            stream.Position = 0;
            Span<byte> head = stackalloc byte[8];
            if (stream.Read(head) < 8)
                return false;

            uint count = BinaryPrimitives.ReadUInt32LittleEndian(head);
            if (count is < 100 or > 100_000)
                return false;

            long rem = stream.Length - (4 + (long)count * 4);
            if (rem <= 0 || rem % count != 0)
                return false;

            long recSize = rem / count;
            return recSize is >= 200 and <= 1000;
        }
        finally
        {
            stream.Position = origPos;
        }
    }

    /// <summary>Reads all phrases from the archive stream.</summary>
    public static List<Phrase> ReadPhrases(Stream stream, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        encoding ??= Windows1252;

        stream.Position = 0;
        Span<byte> head = stackalloc byte[4];
        if (stream.Read(head) < 4)
            return [];

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(head);
        if (count is 0 or > 100_000)
            return [];

        byte[] idBytes = new byte[count * 4];
        stream.ReadExactly(idBytes);

        long rem = stream.Length - (4 + (long)count * 4);
        int recSize = (rem > 0 && rem % count == 0) ? (int)(rem / count) : RecordSize;

        var phrases = new List<Phrase>((int)count);
        byte[] raw = new byte[recSize];
        byte[] plain = new byte[recSize];

        for (int i = 0; i < count; i++)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(idBytes.AsSpan(i * 4, 4));
            stream.ReadExactly(raw);

            byte seed = (byte)(i & 0xFF);
            plain[0] = (byte)(raw[0] ^ seed);
            for (int j = 1; j < recSize; j++)
            {
                plain[j] = (byte)(raw[j] ^ raw[j - 1]);
            }

            int len = 0;
            while (len < recSize && plain[len] != 0)
            {
                len++;
            }

            string text = len > 0 ? encoding.GetString(plain, 0, len) : string.Empty;
            phrases.Add(new Phrase(i, id, text));
        }

        return phrases;
    }

    /// <summary>Decodes a single phrase at <paramref name="index"/> directly from the stream.</summary>
    public static Phrase? ReadSinglePhrase(Stream stream, int index, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (index < 0)
            return null;

        encoding ??= Windows1252;
        stream.Position = 0;
        Span<byte> head = stackalloc byte[4];
        if (stream.Read(head) < 4)
            return null;

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(head);
        if (index >= count)
            return null;

        stream.Position = 4 + index * 4;
        if (stream.Read(head) < 4)
            return null;
        uint id = BinaryPrimitives.ReadUInt32LittleEndian(head);

        long dataOffset = 4 + (long)count * 4 + (long)index * RecordSize;
        stream.Position = dataOffset;

        byte[] raw = new byte[RecordSize];
        if (stream.Read(raw) < RecordSize)
            return null;

        byte[] plain = new byte[RecordSize];
        byte seed = (byte)(index & 0xFF);
        plain[0] = (byte)(raw[0] ^ seed);
        for (int j = 1; j < RecordSize; j++)
        {
            plain[j] = (byte)(raw[j] ^ raw[j - 1]);
        }

        int len = 0;
        while (len < RecordSize && plain[len] != 0)
        {
            len++;
        }

        string text = len > 0 ? encoding.GetString(plain, 0, len) : string.Empty;
        return new Phrase(index, id, text);
    }
}
