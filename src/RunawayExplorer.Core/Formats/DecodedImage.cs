namespace RunawayExplorer.Core.Formats;

/// <summary>
/// An engine-agnostic decoded pixel buffer shared by every decoder in <see cref="RunawayExplorer.Core.Formats"/>.
/// Pixel data is tightly packed BGRA32 (4 bytes per pixel, order Blue/Green/Red/Alpha), stored top-down
/// and row-major with a stride of <c>Width * 4</c> bytes. Alpha is straight (unpremultiplied). This layout
/// maps directly onto Avalonia's <c>PixelFormat.Bgra8888</c>.
/// </summary>
public sealed class DecodedImage
{
    public int Width { get; }

    public int Height { get; }

    /// <summary>Tightly packed BGRA32 pixel data, top-down, row-major. Length is always <c>Width * Height * 4</c>.</summary>
    public byte[] Pixels { get; }

    public DecodedImage(int width, int height, byte[] pixels)
    {
        if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(pixels);

        int expectedLength = checked(width * height * 4);
        if (pixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Pixel buffer length {pixels.Length} does not match Width*Height*4 ({expectedLength}).",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>A fully transparent image of the given size.</summary>
    public static DecodedImage Transparent(int width, int height) =>
        new(width, height, new byte[checked(width * height * 4)]);

    /// <summary>
    /// Copies <paramref name="source"/> onto this image at (<paramref name="x"/>, <paramref name="y"/>),
    /// replacing pixels where the source alpha is non-zero. No blending: the game's own images are
    /// 1-bit transparent, so a pixel is either there or it isn't. Pixels that fall outside this image
    /// are dropped.
    /// </summary>
    public void Blit(DecodedImage source, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(source);

        for (int sy = 0; sy < source.Height; sy++)
        {
            int dy = y + sy;
            if (dy < 0 || dy >= Height)
                continue;

            for (int sx = 0; sx < source.Width; sx++)
            {
                int dx = x + sx;
                if (dx < 0 || dx >= Width)
                    continue;

                int si = (sy * source.Width + sx) * 4;
                if (source.Pixels[si + 3] == 0)
                    continue;

                int di = (dy * Width + dx) * 4;
                Pixels[di] = source.Pixels[si];
                Pixels[di + 1] = source.Pixels[si + 1];
                Pixels[di + 2] = source.Pixels[si + 2];
                Pixels[di + 3] = source.Pixels[si + 3];
            }
        }
    }

    /// <summary>A copy of the rectangle (<paramref name="x"/>, <paramref name="y"/>, <paramref name="width"/>, <paramref name="height"/>), clamped to the image.</summary>
    public DecodedImage Crop(int x, int y, int width, int height)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(Width, x + width), y1 = Math.Min(Height, y + height);
        int w = Math.Max(0, x1 - x0), h = Math.Max(0, y1 - y0);
        var pixels = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(Pixels, ((y0 + row) * Width + x0) * 4, pixels, row * w * 4, w * 4);
        return new DecodedImage(w, h, pixels);
    }
}
