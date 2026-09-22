using System.Buffers.Binary;
using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Formats;
using Xunit;

namespace RunawayExplorer.Core.Tests;

public class SpanMaskDecoderTests
{
    private static byte[] BuildSyntheticSpanMask((ushort X, ushort Y, ushort Width)[] spans)
    {
        byte[] buffer = new byte[2 + spans.Length * 6];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)spans.Length);
        for (int i = 0; i < spans.Length; i++)
        {
            int p = 2 + i * 6;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(p), spans[i].X);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(p + 2), spans[i].Y);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(p + 4), spans[i].Width);
        }
        return buffer;
    }

    [Fact]
    public void Detect_SyntheticSpans_CalculatesExactBoundingBox()
    {
        var spans = new (ushort X, ushort Y, ushort Width)[]
        {
            (10, 5, 20), // x: 10..30, y: 5
            (12, 6, 25), // x: 12..37, y: 6
            (8, 7, 10),  // x: 8..18, y: 7
        };

        byte[] data = BuildSyntheticSpanMask(spans);
        ImageInfo? info = SpanMaskDecoder.Detect(data);

        Assert.NotNull(info);
        Assert.Equal(8, info.X);
        Assert.Equal(5, info.Y);
        Assert.Equal(37 - 8, info.Width); // 29
        Assert.Equal(8 - 5, info.Height); // 3
        Assert.True(info.IsMask);
    }

    [Fact]
    public void Decode_SyntheticSpans_FillsSpanPixelsCorrectly()
    {
        var spans = new (ushort X, ushort Y, ushort Width)[]
        {
            (10, 5, 4), // local (0..3, row 0)
        };

        byte[] data = BuildSyntheticSpanMask(spans);
        DecodedImage image = SpanMaskDecoder.Decode(data);

        Assert.Equal(4, image.Width);
        Assert.Equal(1, image.Height);

        // All 4 pixels must be non-transparent
        for (int x = 0; x < 4; x++)
        {
            byte alpha = image.Pixels[x * 4 + 3];
            Assert.True(alpha > 0);
        }
    }

    [Fact]
    public void Runaway1_SteamF18_SpanMask_DecodesCorrectly()
    {
        string path = @"F:\Games\Steam\steamapps\common\Runaway A Road Adventure\Resource\RESOURCE.F18";
        if (!File.Exists(path))
            return;

        using var fs = File.OpenRead(path);
        List<ArchiveEntry> entries = SceneArchive.ReadEntries(fs);
        ArchiveEntry e4 = entries[4];
        byte[] data = new byte[e4.Size];
        fs.Position = e4.Offset;
        fs.ReadExactly(data);

        Assert.True(SpanMaskDecoder.IsSpanMask(data));
        ImageInfo? info = SpanMaskDecoder.Detect(data);
        Assert.NotNull(info);
        Assert.Equal(18, info.X);
        Assert.Equal(0, info.Y);
        Assert.Equal(158, info.Width);
        Assert.Equal(600, info.Height);

        DecodedImage img = SpanMaskDecoder.Decode(data);
        Assert.Equal(158, img.Width);
        Assert.Equal(600, img.Height);
    }
}
