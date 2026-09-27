using RunawayExplorer.Core.Formats;
using Xunit;
using static RunawayExplorer.Core.Tests.SyntheticAssets;

namespace RunawayExplorer.Core.Tests;

public class BmpDecoderTests
{
    [Fact]
    public void ReadsTheHeaderWithoutDecodingThePixels()
    {
        Assert.True(BmpDecoder.TryReadHeader(Bmp(68, 68), out int w, out int h));
        Assert.Equal((68, 68), (w, h));

        Assert.False(BmpDecoder.IsBmp([]));
        Assert.False(BmpDecoder.IsBmp(new byte[64]));
        // A truncated file: the header promises more pixels than the entry holds.
        Assert.False(BmpDecoder.IsBmp(Bmp(68, 68).AsSpan(0, 1000)));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(32)]
    public void DecodesBottomUpRowsIntoTopDownPixels(int bpp)
    {
        DecodedImage image = BmpDecoder.Decode(Bmp(5, 3, bpp))!;

        Assert.Equal((5, 3), (image.Width, image.Height));
        // Pixel (x, y) was written as (B, G, R) = (x, y, 0x40), so the row order proves the flip.
        Assert.Equal((0x40, 0, 0, 255), Pixel(image, 0, 0));
        Assert.Equal((0x40, 2, 4, 255), Pixel(image, 4, 2));
    }

    [Fact]
    public void KeepsTheAlphaChannelOfA32BitBitmap()
    {
        DecodedImage image = BmpDecoder.Decode(Bmp(4, 2, 32, alpha: 0x80))!;
        Assert.Equal((byte)0x80, Pixel(image, 1, 1).A);
    }

    [Fact]
    public void TreatsAnAllZeroFourthByteAsPaddingRatherThanTransparency()
    {
        // A 32-bit BMP has no alpha channel by definition; honouring the zeroes would decode the whole
        // image as invisible.
        DecodedImage image = BmpDecoder.Decode(Bmp(4, 2, 32, alpha: null))!;
        Assert.All(Enumerable.Range(0, 4), x => Assert.Equal((byte)255, Pixel(image, x, 0).A));
    }

    [Fact]
    public void RejectsWhatItCannotRead()
    {
        // Compressed and paletted bitmaps do not occur in the games and are not guessed at.
        byte[] rle = Bmp(8, 8);
        rle[30] = 1; // BI_RLE8
        Assert.False(BmpDecoder.IsBmp(rle));

        byte[] paletted = Bmp(8, 8);
        paletted[28] = 8; // 8 bpp
        Assert.False(BmpDecoder.IsBmp(paletted));
    }
}
