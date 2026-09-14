using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RunawayExplorer.Core.Formats;

namespace RunawayExplorer.Services;

/// <summary>
/// Turns a <see cref="DecodedImage"/> into an Avalonia <see cref="Bitmap"/> the viewers can display.
/// Copies into a <see cref="WriteableBitmap"/>'s own buffer rather than handing Skia a pinned pointer:
/// the backing isn't always copied synchronously, and a managed array that goes out of scope on a
/// worker thread can move under it.
/// </summary>
public static class BitmapConverter
{
    public static Bitmap? ToBitmap(DecodedImage? image)
    {
        if (image is null || image.Width <= 0 || image.Height <= 0)
            return null;

        var wb = new WriteableBitmap(
            new PixelSize(image.Width, image.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        int srcStride = image.Width * 4;
        using ILockedFramebuffer fb = wb.Lock();
        for (int y = 0; y < image.Height; y++)
            Marshal.Copy(image.Pixels, y * srcStride, fb.Address + y * fb.RowBytes, srcStride);

        return wb;
    }
}
