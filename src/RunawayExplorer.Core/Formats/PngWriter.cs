using System.Buffers.Binary;
using System.IO.Compression;

namespace RunawayExplorer.Core.Formats;

/// <summary>
/// A small self-contained PNG encoder (8-bit RGBA, non-interlaced, filter type 0 on every row) so the
/// Core library can write images -- and the APNG writer can build frames -- without taking a dependency
/// on a UI framework's imaging stack. Files are larger than a filtered encoder would produce; they are
/// still valid PNGs that every viewer opens.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static void Write(DecodedImage image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        using FileStream stream = File.Create(path);
        Write(image, stream);
    }

    public static void Write(DecodedImage image, Stream output)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(output);

        output.Write(Signature);
        WriteChunk(output, "IHDR", Ihdr(image.Width, image.Height));
        WriteChunk(output, "IDAT", CompressPixels(image));
        WriteChunk(output, "IEND", []);
    }

    public static byte[] ToBytes(DecodedImage image)
    {
        using var ms = new MemoryStream();
        Write(image, ms);
        return ms.ToArray();
    }

    internal static byte[] Ihdr(int width, int height)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // colour type: RGBA
        ihdr[10] = 0;  // compression
        ihdr[11] = 0;  // filter method
        ihdr[12] = 0;  // not interlaced
        return ihdr;
    }

    /// <summary>The zlib-compressed scanline data (filter byte 0 + RGBA per row) for <paramref name="image"/>.</summary>
    internal static byte[] CompressPixels(DecodedImage image)
    {
        int rowBytes = image.Width * 4;
        var raw = new byte[(rowBytes + 1) * image.Height];
        for (int y = 0; y < image.Height; y++)
        {
            int dst = y * (rowBytes + 1);
            raw[dst] = 0;
            int src = y * rowBytes;
            for (int x = 0; x < image.Width; x++)
            {
                // BGRA in memory, RGBA on disk.
                raw[dst + 1 + x * 4] = image.Pixels[src + x * 4 + 2];
                raw[dst + 2 + x * 4] = image.Pixels[src + x * 4 + 1];
                raw[dst + 3 + x * 4] = image.Pixels[src + x * 4];
                raw[dst + 4 + x * 4] = image.Pixels[src + x * 4 + 3];
            }
        }

        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw);
        return ms.ToArray();
    }

    internal static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        output.Write(len);

        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        uint crc = Crc32.Compute(typeBytes);
        crc = Crc32.Update(crc, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }
}

/// <summary>The CRC-32 PNG chunks carry (the ISO 3309 / zlib polynomial).</summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data) => Update(0, data);

    /// <summary>Continues a running CRC; pass the previous result back in to hash several buffers as one.</summary>
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        uint c = crc ^ 0xFFFFFFFFu;
        foreach (byte b in data)
            c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
