using RunawayExplorer.Core.Formats;
using Xunit;
using static RunawayExplorer.Core.Tests.SyntheticAssets;

namespace RunawayExplorer.Core.Tests;

public class FontDecoderTests
{
    private static readonly (byte Top, byte Height, byte Width)[] Glyphs =
    [
        (5, 12, 9),
        (3, 14, 11),
        (5, 12, 6),
    ];

    [Fact]
    public void ReadsTheRecordsOfAGlyphTable()
    {
        FontDecoder.Glyph[] glyphs = FontDecoder.ReadGlyphTable(GlyphTable(Glyphs));

        Assert.Equal(3, glyphs.Length);
        Assert.Equal(new FontDecoder.Glyph(0, 5, 12, 9), glyphs[0]);
        Assert.Equal(new FontDecoder.Glyph(9 * 12, 3, 14, 11), glyphs[1]);
        Assert.Equal(new FontDecoder.Glyph(9 * 12 + 11 * 14, 5, 12, 6), glyphs[2]);
    }

    [Fact]
    public void AcceptsATableWhoseRecordsTileTheBitmapExactly()
    {
        byte[] table = GlyphTable(Glyphs);
        byte[] bitmap = FontBitmap(Glyphs);

        Assert.True(FontDecoder.IsGlyphTable(table, bitmap.Length));

        // One byte either way and the records no longer end on the bitmap's last byte.
        Assert.False(FontDecoder.IsGlyphTable(table, bitmap.Length - 1));
        Assert.False(FontDecoder.IsGlyphTable(table, bitmap.Length + 1));
    }

    [Fact]
    public void RejectsWhatIsNotATable()
    {
        Assert.False(FontDecoder.IsGlyphTable([], 0));
        // A length that is not a whole number of records.
        Assert.False(FontDecoder.IsGlyphTable(new byte[7], 100));
        // Records that all start at zero: they do not chain.
        Assert.False(FontDecoder.IsGlyphTable(new byte[10], 100));
    }

    [Fact]
    public void LineHeightIsTheTallestTopPlusHeight()
    {
        Assert.Equal(17, FontDecoder.LineHeight(FontDecoder.ReadGlyphTable(GlyphTable(Glyphs))));
    }

    [Fact]
    public void DrawsOutlineAndFillAsSeparateColours()
    {
        byte[] table = GlyphTable(Glyphs);
        byte[] bitmap = FontBitmap(Glyphs);

        DecodedImage image = FontDecoder.Decode(bitmap, FontDecoder.ReadGlyphTable(table));

        Assert.Equal(17, image.Height);
        // The first glyph is 9 wide at top 5: its transparent border, opaque outline ring and fill.
        Assert.Equal((0, 0, 0, 0), Pixel(image, 0, 5));
        Assert.Equal((0, 0, 0, 255), Pixel(image, 1, 6));
        Assert.Equal((255, 255, 255, 255), Pixel(image, 4, 11));
    }

    [Fact]
    public void WrapsTheSheetAtTheGivenWidth()
    {
        byte[] table = GlyphTable(Glyphs);
        byte[] bitmap = FontBitmap(Glyphs);

        DecodedImage narrow = FontDecoder.Decode(bitmap, FontDecoder.ReadGlyphTable(table), maxWidth: 12);

        Assert.True(narrow.Width <= 12);
        // Three glyphs, none of which fits beside another, so three lines of 17 plus the gaps.
        Assert.Equal(17 * 3 + 2, narrow.Height);
    }
}
