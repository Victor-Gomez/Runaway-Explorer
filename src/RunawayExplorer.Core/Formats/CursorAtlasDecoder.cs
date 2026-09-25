namespace RunawayExplorer.Core.Formats;

/// <summary>
/// Runaway 1's mouse cursors, which live side by side in one raw RGB565 raster in <c>RESOURCE.000</c>
/// (slots 112-115), over the maroon key colour <c>0x6841</c>. See <c>docs/formats/global-data.md</c>.
/// <para>
/// The atlas carries no index, so the sprites are found by scanning: rows that are entirely key colour
/// separate horizontal bands and only the first band holds cursors, columns that are entirely key colour
/// separate the sprites within it. Not every gap is a separator -- gaps inside a sprite (between the arms
/// of the crosshair) are 1 px while gaps between sprites are 12 px or more -- so runs closer together
/// than <see cref="SpriteGap"/> belong to the same sprite.
/// </para>
/// </summary>
public static class CursorAtlasDecoder
{
    /// <summary>The atlas geometry. Nothing in the archive records it; it is measured once per game.</summary>
    public const int AtlasWidth = 904;

    /// <inheritdoc cref="AtlasWidth"/>
    public const int AtlasHeight = 540;

    /// <summary>The colour the atlas uses for the parts that are not drawn: RGB 104, 8, 8.</summary>
    public const ushort KeyColor = 0x6841;

    /// <summary>
    /// Runs of key colour narrower than this are a hole inside one sprite rather than the space between
    /// two of them. Measured holes are 1 px and spaces 12 px or more, so every threshold in 3..9 splits
    /// the band the same way -- into 15 cursors.
    /// </summary>
    public const int SpriteGap = 6;

    /// <summary>One sprite's box in the atlas.</summary>
    public readonly record struct SpriteBounds(int X, int Y, int Width, int Height);

    /// <summary>Whether an entry is exactly one atlas worth of RGB565 pixels and starts on the key colour.</summary>
    public static bool IsAtlas(ReadOnlySpan<byte> data) =>
        data.Length == AtlasWidth * AtlasHeight * 2 && LooksLikeAtlas(data);

    /// <summary>
    /// Whether the start of an entry looks like the atlas: the first rows are drawn in the key colour.
    /// Takes as little as the first row, so the classifier does not have to read a megabyte per entry.
    /// </summary>
    public static bool LooksLikeAtlas(ReadOnlySpan<byte> head)
    {
        if (head.Length < AtlasWidth * 2)
            return false;

        // The top-left corner is outside every sprite, so the first row is solid key colour.
        for (int x = 0; x < AtlasWidth; x++)
        {
            if (Rgb565.Read(head, x * 2) != KeyColor)
                return false;
        }

        return true;
    }

    /// <summary>Decodes the whole atlas, with the key colour as transparent.</summary>
    public static DecodedImage DecodeAtlas(ReadOnlySpan<byte> data)
    {
        if (data.Length < AtlasWidth * AtlasHeight * 2)
            throw new ArgumentException($"A cursor atlas needs {AtlasWidth * AtlasHeight * 2} bytes.", nameof(data));

        var image = DecodedImage.Transparent(AtlasWidth, AtlasHeight);
        for (int i = 0; i < AtlasWidth * AtlasHeight; i++)
        {
            ushort p = Rgb565.Read(data, i * 2);
            if (p != KeyColor)
                Rgb565.ToBgra(p, image.Pixels, i * 4);
        }

        return image;
    }

    /// <summary>
    /// Finds the cursors of the atlas's first band, in the order they are stored: crosshair, magnifier,
    /// hand, speech balloon, four exit arrows, then a run of morph frames. Each box is as tight as its
    /// artwork. The bands below the first one hold other interface art and are not returned.
    /// </summary>
    public static IReadOnlyList<SpriteBounds> FindCursors(ReadOnlySpan<byte> data)
    {
        var sprites = new List<SpriteBounds>();
        if (data.Length < AtlasWidth * AtlasHeight * 2)
            return sprites;

        // The band is the first group of rows that are not entirely key colour.
        int top = -1, bottom = -1;
        for (int y = 0; y < AtlasHeight; y++)
        {
            bool empty = RowIsKey(data, y, 0, AtlasWidth);
            if (!empty)
            {
                if (top < 0)
                    top = y;
                bottom = y + 1;
            }
            else if (top >= 0)
            {
                break;
            }
        }

        if (top < 0)
            return sprites;

        // Walk the band left to right, collecting runs of columns that hold artwork and joining the
        // ones that sit closer together than a separator.
        int start = -1, end = -1;
        for (int x = 0; x <= AtlasWidth; x++)
        {
            bool empty = x >= AtlasWidth || ColumnIsKey(data, x, top, bottom);
            if (!empty)
            {
                if (start < 0)
                    start = x;
                end = x + 1;
            }
            else if (start >= 0 && (x >= AtlasWidth || x - end >= SpriteGap))
            {
                sprites.Add(TrimVertically(data, start, top, end - start, bottom - top));
                start = end = -1;
            }
        }

        return sprites;
    }

    private static bool RowIsKey(ReadOnlySpan<byte> data, int y, int x0, int x1)
    {
        int row = y * AtlasWidth * 2;
        for (int x = x0; x < x1; x++)
        {
            if (Rgb565.Read(data, row + x * 2) != KeyColor)
                return false;
        }
        return true;
    }

    private static bool ColumnIsKey(ReadOnlySpan<byte> data, int x, int y0, int y1)
    {
        for (int y = y0; y < y1; y++)
        {
            if (Rgb565.Read(data, (y * AtlasWidth + x) * 2) != KeyColor)
                return false;
        }
        return true;
    }

    private static SpriteBounds TrimVertically(ReadOnlySpan<byte> data, int x, int y, int width, int height)
    {
        int first = -1, last = -1;
        for (int row = y; row < y + height; row++)
        {
            if (RowIsKey(data, row, x, x + width))
                continue;

            if (first < 0)
                first = row;
            last = row + 1;
        }

        return first < 0 ? new SpriteBounds(x, y, width, height) : new SpriteBounds(x, first, width, last - first);
    }
}
