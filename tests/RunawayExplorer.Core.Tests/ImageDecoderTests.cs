using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Formats;
using Xunit;
using static RunawayExplorer.Core.Tests.SyntheticAssets;

namespace RunawayExplorer.Core.Tests;

public class Rgb565Tests
{
    [Fact]
    public void ExpandsByShifting_LikeTheGame()
    {
        var dst = new byte[4];
        Core.Formats.Rgb565.ToBgra(0xFFFF, dst, 0);
        Assert.Equal(new byte[] { 0xF8, 0xFC, 0xF8, 0xFF }, dst);

        Core.Formats.Rgb565.ToBgra(Rgb565(255, 0, 0), dst, 0);
        Assert.Equal((0xF8, 0, 0, 255), (dst[2], dst[1], dst[0], dst[3]));
    }
}

public class RasterDecoderTests
{
    [Theory]
    [InlineData(204, 120)]
    [InlineData(282, 188)]
    [InlineData(1372, 600)]
    public void DetectsTheWidthOfAHeaderlessRaster(int width, int height)
    {
        byte[] data = Raster(width, height);

        RasterInfo? info = RasterDecoder.Detect(data);

        Assert.NotNull(info);
        Assert.Equal(width, info.Value.Width);
        Assert.Equal(height, info.Value.Height);
        Assert.True(info.Value.Sharpness >= RasterDecoder.SharpnessMin);
        Assert.False(info.Value.IsMask);
    }

    [Fact]
    public void FullScreenMultiples_NeedNoDetection()
    {
        // A flat title card defeats the stride detector; its size is unambiguous anyway.
        byte[] data = Raster(1024, 1200, (_, _) => 0x1234);

        RasterInfo? info = RasterDecoder.Detect(data);

        Assert.NotNull(info);
        Assert.Equal(1024, info.Value.Width);
        Assert.Equal(1200, info.Value.Height);
        Assert.Equal(99.0, info.Value.Sharpness);
    }

    [Fact]
    public void Noise_IsNotARaster()
    {
        var rng = new Random(1);
        var data = new byte[300 * 200 * 2];
        rng.NextBytes(data);

        Assert.Null(RasterDecoder.Detect(data));
    }

    [Fact]
    public void OddLengthOrTiny_IsNotARaster()
    {
        Assert.Null(RasterDecoder.Detect(new byte[9001]));
        Assert.Null(RasterDecoder.Detect(new byte[100]));
    }

    [Fact]
    public void DecodedPixels_MatchTheSource()
    {
        byte[] data = Raster(204, 120);
        DecodedImage image = RasterDecoder.Decode(data, 204, 120);

        Assert.Equal(204, image.Width);
        Assert.Equal(120, image.Height);
        (byte r, byte g, byte b, byte a) = Pixel(image, 10, 5);
        ushort expected = Rgb565((10 * 7) & 0xFF, (10 * 13 + 5) & 0xFF, (5 * 3) & 0xFF);
        Assert.Equal(((expected >> 11) << 3, ((expected >> 5) & 0x3F) << 2, (expected & 0x1F) << 3, 255), (r, g, b, a));
    }

    [Fact]
    public void IndexLayers_AreFlaggedAsMasks()
    {
        // Neighbouring pixels in unrelated colours: an index map, not artwork.
        var rng = new Random(7);
        byte[] data = Raster(1024, 600, (_, _) => (ushort)rng.Next(65536));

        RasterInfo? info = RasterDecoder.Detect(data);

        Assert.NotNull(info);
        Assert.True(info.Value.IsMask);
    }
}

public class OverlayDecoderTests
{
    [Fact]
    public void ParsesByteExactRecords_AndCropsToContent()
    {
        ushort red = Rgb565(255, 0, 0), blue = Rgb565(0, 0, 255);
        byte[] data = Overlay(
        [
            (100, 50, [red, red, red]),
            (101, 51, [blue]),
        ]);

        (DecodedImage image, OverlayInfo info) = OverlayDecoder.TryDecode(data)!.Value;

        Assert.Equal(100, info.X);
        Assert.Equal(50, info.Y);
        Assert.Equal(3, info.Width);
        Assert.Equal(2, info.Height);
        Assert.False(info.IsRectangular);
        Assert.Equal((0xF8, 0, 0, 255), Pixel(image, 0, 0));
        Assert.Equal((0, 0, 0xF8, 255), Pixel(image, 1, 1));
        Assert.Equal(0, Pixel(image, 0, 1).A); // not covered by any record: transparent
    }

    [Fact]
    public void RectangularOverlays_AreRecognised()
    {
        byte[] data = Overlay([(10, 10, [1, 2]), (10, 11, [3, 4]), (10, 12, [5, 6])]);
        Assert.True(OverlayDecoder.Parse(data)!.IsRectangular);
    }

    [Fact]
    public void TrailingOrMissingBytes_RejectTheEntry()
    {
        byte[] data = Overlay([(1, 1, [7])]);
        Assert.NotNull(OverlayDecoder.Parse(data));
        Assert.Null(OverlayDecoder.Parse(data.Concat(new byte[] { 0 }).ToArray()));
        Assert.Null(OverlayDecoder.Parse(data[..^1]));
    }

    [Fact]
    public void ZeroCountRecord_IsNotAnOverlay()
    {
        byte[] data = [1, 0, 0, 0, 0, 0, 0, 0];
        Assert.Null(OverlayDecoder.Parse(data));
    }
}

public class SpriteDecoderTests
{
    private static readonly Frame Walk0 = new([(348, 63, [1, 2, 3]), (349, 64, [4])]);
    private static readonly Frame Walk1 = new([(350, 63, [5, 6]), (350, 65, [7, 8, 9])]);
    private static readonly Frame Empty = new([]);

    [Fact]
    public void ParsesRecords_AndWalksEveryFrameByteExact()
    {
        byte[] data = Sprite([Walk0, Walk1, Empty, Walk0]);

        SpriteAsset? asset = SpriteAsset.Parse(data);

        Assert.NotNull(asset);
        Assert.Equal(4, asset.FrameCount);
        Assert.Equal(0, asset.DescriptorCount);
        Assert.Null(asset.Verify());
        Assert.Equal((348, 63, 5, 3), asset.Bounds);
    }

    [Fact]
    public void EveryFrameIsComplete_NothingCarriesOver()
    {
        SpriteAsset asset = SpriteAsset.Parse(Sprite([Walk0, Walk1]))!;

        SpriteFrame f1 = asset.DecodeFrame(1);
        Assert.NotNull(f1.Image);
        Assert.Equal((350, 63), (f1.X, f1.Y));
        Assert.Equal((3, 3), (f1.Image.Width, f1.Image.Height));
        // Row 64 belongs to frame 0 only; in frame 1 it is transparent.
        Assert.Equal(0, Pixel(f1.Image, 0, 1).A);
        Assert.Equal(255, Pixel(f1.Image, 0, 0).A);
    }

    [Fact]
    public void XIsAPixelColumn_NotAByteOffset()
    {
        SpriteAsset asset = SpriteAsset.Parse(Sprite([Walk0]))!;
        SpriteFrame f0 = asset.DecodeFrame(0);
        Assert.Equal(348, f0.X);
    }

    [Fact]
    public void EmptyFrames_DecodeToNothing_ButKeepTheirSlot()
    {
        SpriteAsset asset = SpriteAsset.Parse(Sprite([Walk0, Empty, Walk1]))!;
        Assert.True(asset.DecodeFrame(1).IsEmpty);
        Assert.Equal(3, asset.FrameCount);
    }

    [Fact]
    public void FramesAtColumnZero_AreNotATerminator()
    {
        var atZero = new Frame([(0, 10, [1, 2])]);
        SpriteAsset asset = SpriteAsset.Parse(Sprite([Walk0, atZero, Walk1], sharedBox: false))!;
        Assert.Equal(3, asset.FrameCount);
        Assert.Equal(0, asset.Records[1].X0);
    }

    [Fact]
    public void PerFrameBoxes_UnionIntoTheBounds()
    {
        var far = new Frame([(900, 500, [1])]);
        SpriteAsset asset = SpriteAsset.Parse(Sprite([Walk0, far], sharedBox: false))!;
        Assert.Equal((348, 63, 900 + 1 - 348, 500 + 1 - 63), asset.Bounds);
    }

    [Fact]
    public void DescriptorRecords_AreSkipped()
    {
        SpriteAsset asset = SpriteAsset.Parse(Sprite([Walk0, Walk1], descriptorWidth: 1372))!;
        Assert.Equal(1, asset.DescriptorCount);
        Assert.Equal(2, asset.FrameCount);
        Assert.Null(asset.Verify());
    }

    [Fact]
    public void ADataTableStartingWithZero_IsNotASprite()
    {
        // Two plausible records -- the first at offset 0 -- but frame 0's single 2-pixel segment walks
        // 9 bytes, not the 40 the second record claims, so this is a table that starts with a zero.
        byte[] data = Sprite([new Frame([(5, 1, [1, 2])]), new Frame([(5, 2, [3])])]);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(SpriteAsset.RecordSize), 40);
        Assert.Null(SpriteAsset.Parse(data));
    }

    [Fact]
    public void CanvasFrames_AllShareTheBoundsSize()
    {
        SpriteAsset asset = SpriteAsset.Parse(Sprite([Walk0, Walk1, Empty]))!;
        for (int i = 0; i < asset.FrameCount; i++)
        {
            DecodedImage c = asset.DecodeFrameOnCanvas(i);
            Assert.Equal((asset.Bounds.Width, asset.Bounds.Height), (c.Width, c.Height));
        }
        Assert.Equal(255, Pixel(asset.DecodeFrameOnCanvas(0), 0, 0).A);
        Assert.Equal(0, Pixel(asset.DecodeFrameOnCanvas(1), 0, 0).A);
    }
}

public class EntryClassifierTests
{
    [Fact]
    public void SpriteWinsOverOverlayWinsOverRaster()
    {
        Assert.Equal(EntryKind.Animation, EntryClassifier.Classify(Sprite([new Frame([(1, 1, [1])])])).Kind);
        Assert.Equal(EntryKind.Overlay, EntryClassifier.Classify(Overlay([(1, 1, [1])])).Kind);
        Assert.Equal(EntryKind.Background, EntryClassifier.Classify(Raster(204, 120)).Kind);
        Assert.Equal(EntryKind.Data, EntryClassifier.Classify(new byte[1536]).Kind);
    }

    [Fact]
    public void CarriesTheGeometryTheTreeShows()
    {
        EntryClassification c = EntryClassifier.Classify(Overlay([(320, 80, [1, 2, 3])]));
        Assert.Equal((320, 80, 3, 1), (c.Image!.X, c.Image.Y, c.Image.Width, c.Image.Height));

        c = EntryClassifier.Classify(Sprite([new Frame([(5, 6, [1])]), new Frame([(7, 8, [1])])], sharedBox: false));
        Assert.Equal(2, c.Image!.Frames);
        Assert.Equal((5, 6, 3, 3), (c.Image.X, c.Image.Y, c.Image.Width, c.Image.Height));
    }
}
