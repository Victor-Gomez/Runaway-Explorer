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
    public static Bitmap? ToBitmap(DecodedImage? image, bool grayscale = false)
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
        if (!grayscale)
        {
            for (int y = 0; y < image.Height; y++)
                Marshal.Copy(image.Pixels, y * srcStride, fb.Address + y * fb.RowBytes, srcStride);
        }
        else
        {
            unsafe
            {
                byte* dstBase = (byte*)fb.Address;
                fixed (byte* srcBase = image.Pixels)
                {
                    for (int y = 0; y < image.Height; y++)
                    {
                        byte* srcRow = srcBase + (y * srcStride);
                        byte* dstRow = dstBase + (y * fb.RowBytes);
                        for (int x = 0; x < image.Width; x++)
                        {
                            int px = x * 4;
                            byte b = srcRow[px + 0];
                            byte g = srcRow[px + 1];
                            byte r = srcRow[px + 2];
                            byte a = srcRow[px + 3];

                            // Standard ITU-R BT.601 luma formula:
                            byte gray = (byte)((r * 77 + g * 150 + b * 29) >> 8);

                            dstRow[px + 0] = gray;
                            dstRow[px + 1] = gray;
                            dstRow[px + 2] = gray;
                            dstRow[px + 3] = a;
                        }
                    }
                }
            }
        }

        return wb;
    }

    /// <summary>Decodes a PNG byte array into a <see cref="DecodedImage"/>.</summary>
    public static DecodedImage? FromPng(byte[] pngBytes)
    {
        try
        {
            using var ms = new System.IO.MemoryStream(pngBytes);
            using var bmp = new Bitmap(ms);
            int w = bmp.PixelSize.Width;
            int h = bmp.PixelSize.Height;
            var pixels = new byte[w * h * 4];
            unsafe
            {
                fixed (byte* ptr = pixels)
                {
                    bmp.CopyPixels(new PixelRect(0, 0, w, h), (IntPtr)ptr, pixels.Length, w * 4);
                }
            }
            return new DecodedImage(w, h, pixels);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
