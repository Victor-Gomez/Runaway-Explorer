using System.Buffers.Binary;
using System.Text;

namespace RunawayExplorer.Core.FileSystem;

/// <summary>
/// <c>Datav/DATAVC00.&lt;nnn&gt;</c>. Every <c>DATAV*.&lt;nnn&gt;</c> is a Bink file whose first 1024 bytes
/// have been replaced with junk; the real headers live here.
/// <code>
/// u32 data_offset      = 8 + N*15; its low byte is the XOR seed (0x21)
/// u32 N                number of videos (87)
/// byte[N][15]          filename records: 13-byte NUL-padded name + 2 bytes
/// byte[N][2048]        header blocks; first 1024 bytes are the real header
/// </code>
/// Records and header blocks are XOR-chained: <c>plain[0] = raw[0] ^ seed</c>,
/// <c>plain[i] = raw[i] ^ raw[i-1]</c>. The exe matches videos by filename. Restoration is
/// <c>decoded_header + original[1024:]</c>. (Reverse-engineered from the routine at VA 0x401000.)
/// </summary>
public sealed class VideoKeyfile
{
    public const int HeaderBytes = 1024;
    private const int RecordSize = 15;
    private const int BlockSize = 2048;

    private readonly Dictionary<string, byte[]> _headers;

    private VideoKeyfile(Dictionary<string, byte[]> headers, byte seed)
    {
        _headers = headers;
        Seed = seed;
    }

    public byte Seed { get; }

    public int Count => _headers.Count;

    /// <summary>Video file names (upper-case) the keyfile carries headers for.</summary>
    public IEnumerable<string> Names => _headers.Keys;

    /// <summary>True for <c>DATAVC00.*</c>, the keyfile's own name.</summary>
    public static bool IsKeyfileName(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName).Equals("DATAVC00", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses a keyfile. <see langword="null"/> if the bytes don't have the expected shape.</summary>
    public static VideoKeyfile? Parse(ReadOnlySpan<byte> keydata)
    {
        if (keydata.Length < 16)
            return null;

        uint dataOff = BinaryPrimitives.ReadUInt32LittleEndian(keydata);
        uint n = BinaryPrimitives.ReadUInt32LittleEndian(keydata.Slice(4));
        if (n == 0 || n > 100_000 || dataOff != 8 + n * RecordSize)
            return null;
        if (keydata.Length != dataOff + n * BlockSize)
            return null;

        byte seed = (byte)(dataOff & 0xFF);
        var headers = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < n; i++)
        {
            byte[] rec = XorChainDecode(keydata.Slice(8 + i * RecordSize, RecordSize), seed);
            int nul = Array.IndexOf(rec, (byte)0, 0, 13);
            string name = Encoding.ASCII.GetString(rec, 0, nul < 0 ? 13 : nul).ToUpperInvariant();
            if (name.Length == 0)
                continue;
            headers[name] = XorChainDecode(keydata.Slice((int)dataOff + i * BlockSize, HeaderBytes), seed);
        }

        return new VideoKeyfile(headers, seed);
    }

    public static VideoKeyfile? Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>The restored 1024-byte header for <paramref name="videoFileName"/>, or <see langword="null"/> if the keyfile has none.</summary>
    public byte[]? HeaderFor(string videoFileName) =>
        _headers.TryGetValue(Path.GetFileName(videoFileName), out byte[]? h) ? h : null;

    /// <summary>Writes the restored Bink file: the decoded header followed by everything after the junk.</summary>
    public bool Restore(string videoPath, Stream output)
    {
        byte[]? header = HeaderFor(videoPath);
        if (header is null)
            return false;

        using FileStream src = File.OpenRead(videoPath);
        if (src.Length < HeaderBytes)
            return false;
        output.Write(header);
        src.Position = HeaderBytes;
        src.CopyTo(output);
        return true;
    }

    internal static byte[] XorChainDecode(ReadOnlySpan<byte> raw, byte seed)
    {
        var plain = new byte[raw.Length];
        if (raw.Length == 0)
            return plain;
        plain[0] = (byte)(raw[0] ^ seed);
        for (int i = 1; i < raw.Length; i++)
            plain[i] = (byte)(raw[i] ^ raw[i - 1]);
        return plain;
    }
}
