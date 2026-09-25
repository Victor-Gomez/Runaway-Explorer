using RunawayExplorer.Core.Formats;
using Xunit;
using static RunawayExplorer.Core.Tests.SyntheticAssets;

namespace RunawayExplorer.Core.Tests;

public class CursorAtlasDecoderTests
{
    [Fact]
    public void RecognisesAnAtlasBySizeAndKeyColour()
    {
        byte[] atlas = CursorAtlas();

        Assert.True(CursorAtlasDecoder.IsAtlas(atlas));
        Assert.True(CursorAtlasDecoder.LooksLikeAtlas(atlas.AsSpan(0, CursorAtlasDecoder.AtlasWidth * 20 * 2)));

        // A raster of the right size whose first row is not key colour is something else.
        var other = new byte[atlas.Length];
        Assert.False(CursorAtlasDecoder.IsAtlas(other));
        // And the right pixels at the wrong size are not an atlas either.
        Assert.False(CursorAtlasDecoder.IsAtlas(atlas.AsSpan(0, atlas.Length - 2)));
    }

    [Fact]
    public void DecodesTheKeyColourAsTransparent()
    {
        DecodedImage image = CursorAtlasDecoder.DecodeAtlas(CursorAtlas());

        Assert.Equal(CursorAtlasDecoder.AtlasWidth, image.Width);
        Assert.Equal(CursorAtlasDecoder.AtlasHeight, image.Height);
        Assert.Equal((0, 0, 0, 0), Pixel(image, 0, 0));
        Assert.Equal((248, 252, 248, 255), Pixel(image, 25, 20));
    }

    [Fact]
    public void FindsTheSpritesOfTheFirstBand()
    {
        IReadOnlyList<CursorAtlasDecoder.SpriteBounds> cursors = CursorAtlasDecoder.FindCursors(CursorAtlas());

        Assert.Equal(new CursorAtlasDecoder.SpriteBounds(20, 10, 30, 30), Assert.Single(cursors));
    }
}
