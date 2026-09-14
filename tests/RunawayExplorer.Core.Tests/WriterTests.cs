using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using RunawayExplorer.Core.Formats;
using Xunit;
using static RunawayExplorer.Core.Tests.SyntheticAssets;

namespace RunawayExplorer.Core.Tests;

/// <summary>A minimal PNG/APNG chunk reader, independent of the writer, so the tests check bytes on disk rather than the writer against itself.</summary>
internal static class PngReader
{
    public sealed record Chunk(string Type, byte[] Data);

    public static List<Chunk> Chunks(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        var chunks = new List<Chunk>();
        int pos = 8;
        while (pos < png.Length)
        {
            int len = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos));
            string type = Encoding.ASCII.GetString(png, pos + 4, 4);
            byte[] data = png[(pos + 8)..(pos + 8 + len)];
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + len));
            Assert.Equal(Crc32.Update(Crc32.Compute(Encoding.ASCII.GetBytes(type)), data), crc);
            chunks.Add(new Chunk(type, data));
            pos += 12 + len;
        }
        return chunks;
    }

    /// <summary>Inflates concatenated IDAT/fdAT payloads and strips the per-row filter byte (always 0 here).</summary>
    public static byte[] Rgba(IEnumerable<byte[]> zlibParts, int width, int height)
    {
        using var joined = new MemoryStream();
        foreach (byte[] p in zlibParts) joined.Write(p);
        joined.Position = 0;
        using var z = new ZLibStream(joined, CompressionMode.Decompress);
        var raw = new byte[(width * 4 + 1) * height];
        z.ReadExactly(raw);
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            Assert.Equal(0, raw[y * (width * 4 + 1)]);
            Buffer.BlockCopy(raw, y * (width * 4 + 1) + 1, rgba, y * width * 4, width * 4);
        }
        return rgba;
    }
}

public class PngWriterTests
{
    [Fact]
    public void WritesAValidRgbaPng_WithRedAndBlueInTheRightOrder()
    {
        var image = DecodedImage.Transparent(2, 1);
        image.Pixels[0] = 10; image.Pixels[1] = 20; image.Pixels[2] = 30; image.Pixels[3] = 255; // B G R A
        image.Pixels[4] = 0; image.Pixels[5] = 0; image.Pixels[6] = 0; image.Pixels[7] = 0;

        byte[] png = PngWriter.ToBytes(image);
        List<PngReader.Chunk> chunks = PngReader.Chunks(png);

        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Select(c => c.Type));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(chunks[0].Data));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(chunks[0].Data.AsSpan(4)));
        Assert.Equal((8, 6), (chunks[0].Data[8], chunks[0].Data[9]));

        byte[] rgba = PngReader.Rgba([chunks[1].Data], 2, 1);
        Assert.Equal(new byte[] { 30, 20, 10, 255, 0, 0, 0, 0 }, rgba);
    }

    [Fact]
    public void Crc32_MatchesTheKnownVector()
    {
        // The classic check value: CRC-32 of "123456789" is 0xCBF43926.
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));
    }
}

public class ApngWriterTests
{
    private static SpriteAsset Asset() => SpriteAsset.Parse(Sprite(
    [
        new Frame([(100, 50, [Rgb565(255, 0, 0), Rgb565(255, 0, 0)])]),
        new Frame([]),
        new Frame([(103, 52, [Rgb565(0, 0, 255)])]),
    ], sharedBox: false))!;

    [Fact]
    public void FramesAreOneToOne_OffsetByTheirScreenPosition_OnTheUnionCanvas()
    {
        SpriteAsset asset = Asset();
        using var ms = new MemoryStream();
        ApngWriter.Write(asset, ms, fps: 12);
        List<PngReader.Chunk> chunks = PngReader.Chunks(ms.ToArray());

        Assert.Equal(["IHDR", "acTL", "fcTL", "IDAT", "fcTL", "fdAT", "fcTL", "fdAT", "IEND"], chunks.Select(c => c.Type));

        // Canvas = union of the boxes: x 100..103, y 50..52 -> 4x3.
        Assert.Equal((4u, 3u), (BinaryPrimitives.ReadUInt32BigEndian(chunks[0].Data), BinaryPrimitives.ReadUInt32BigEndian(chunks[0].Data.AsSpan(4))));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(chunks[1].Data)); // num_frames
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(chunks[1].Data.AsSpan(4))); // loop forever

        // Sequence numbers run 0..4 across fcTL and fdAT.
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(chunks[2].Data));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(chunks[4].Data));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(chunks[5].Data));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(chunks[6].Data));
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32BigEndian(chunks[7].Data));

        // Frame 0 is full-canvas at 0,0 (it doubles as the default image).
        Fctl f0 = Fctl.Parse(chunks[2].Data);
        Assert.Equal((4u, 3u, 0u, 0u), (f0.W, f0.H, f0.X, f0.Y));
        Assert.Equal((83, 1000), (f0.DelayNum, f0.DelayDen));
        Assert.Equal((1, 0), (f0.Dispose, f0.Blend));

        // Frame 1 is empty: a 1x1 placeholder keeps the numbering.
        Fctl f1 = Fctl.Parse(chunks[4].Data);
        Assert.Equal((1u, 1u, 0u, 0u), (f1.W, f1.H, f1.X, f1.Y));

        // Frame 2 sits at (103-100, 52-50).
        Fctl f2 = Fctl.Parse(chunks[6].Data);
        Assert.Equal((1u, 1u, 3u, 2u), (f2.W, f2.H, f2.X, f2.Y));
        byte[] rgba2 = PngReader.Rgba([chunks[7].Data[4..]], 1, 1);
        Assert.Equal(new byte[] { 0, 0, 0xF8, 255 }, rgba2);

        // And placing frame 0's canvas pixels: red at (0,0) and (1,0), transparent elsewhere.
        byte[] rgba0 = PngReader.Rgba([chunks[3].Data], 4, 3);
        Assert.Equal(new byte[] { 0xF8, 0, 0, 255 }, rgba0[..4]);
        Assert.Equal(0, rgba0[(2 * 4 + 3) * 4 + 3]);
    }

    [Theory]
    [InlineData(12, 83)]
    [InlineData(15, 67)]
    [InlineData(60, 17)]
    public void DelayComesFromTheRequestedFps(double fps, int expectedNum)
    {
        using var ms = new MemoryStream();
        ApngWriter.Write(Asset(), ms, fps);
        List<PngReader.Chunk> chunks = PngReader.Chunks(ms.ToArray());
        Assert.Equal(expectedNum, Fctl.Parse(chunks[2].Data).DelayNum);
    }

    [Fact]
    public void RejectsNonsenseFps()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ApngWriter.Write(Asset(), new MemoryStream(), 0));
    }

    private readonly record struct Fctl(uint Seq, uint W, uint H, uint X, uint Y, int DelayNum, int DelayDen, int Dispose, int Blend)
    {
        public static Fctl Parse(byte[] d) => new(
            BinaryPrimitives.ReadUInt32BigEndian(d),
            BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(4)),
            BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(8)),
            BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(12)),
            BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(16)),
            BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(20)),
            BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(22)),
            d[24], d[25]);
    }
}

public class WavWriterTests
{
    [Fact]
    public void HeaderIsCanonical44BytePcm()
    {
        byte[] h = WavWriter.Header(1000, 22050, 2, 16);
        Assert.Equal(44, h.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(h, 0, 4));
        Assert.Equal(1036, BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(4)));
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(h, 8, 8));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(h.AsSpan(20)));
        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(h.AsSpan(22)));
        Assert.Equal(22050, BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(24)));
        Assert.Equal(22050 * 4, BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(28)));
        Assert.Equal(4, BinaryPrimitives.ReadInt16LittleEndian(h.AsSpan(32)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(h.AsSpan(34)));
        Assert.Equal("data", Encoding.ASCII.GetString(h, 36, 4));
        Assert.Equal(1000, BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(40)));
    }

    [Fact]
    public void EightBitMono_ForVoice()
    {
        byte[] h = WavWriter.Header(10, 22050, 1, 8);
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(h.AsSpan(32)));
        Assert.Equal(8, BinaryPrimitives.ReadInt16LittleEndian(h.AsSpan(34)));
    }
}

public class RawFormatTests
{
    [Fact]
    public void HexDumpLineShape()
    {
        string dump = RawFormat.ToHexDump("ABC"u8);
        // Three bytes, then 13 empty three-character slots, then the character column.
        Assert.Equal("0000:0000 | 41 42 43 " + new string(' ', 13 * 3) + "| ABC\n", dump);
    }

    [Fact]
    public void TruncatesAt128K()
    {
        string dump = RawFormat.ToHexDump(new byte[RawFormat.MaxDumpBytes + 16]);
        Assert.Contains("16 more byte(s) not shown", dump);
    }
}

public class DecodedImageTests
{
    [Fact]
    public void Blit_CopiesOpaquePixelsOnly_AndClips()
    {
        var canvas = DecodedImage.Transparent(2, 2);
        var src = DecodedImage.Transparent(2, 1);
        src.Pixels[3] = 255; src.Pixels[2] = 9; // opaque red-ish at (0,0); (1,0) transparent

        canvas.Blit(src, 1, 1);
        Assert.Equal(255, Pixel(canvas, 1, 1).A);
        Assert.Equal(9, Pixel(canvas, 1, 1).R);
        Assert.Equal(0, Pixel(canvas, 0, 0).A);

        canvas.Blit(src, -1, 0); // (0,0) of src lands off-canvas; (1,0) is transparent anyway
        Assert.Equal(0, Pixel(canvas, 0, 0).A);
    }

    [Fact]
    public void Crop_ClampsToTheImage()
    {
        var img = DecodedImage.Transparent(4, 4);
        DecodedImage c = img.Crop(2, 2, 10, 10);
        Assert.Equal((2, 2), (c.Width, c.Height));
    }

    [Fact]
    public void RejectsMismatchedBuffers()
    {
        Assert.Throws<ArgumentException>(() => new DecodedImage(2, 2, new byte[3]));
    }
}
