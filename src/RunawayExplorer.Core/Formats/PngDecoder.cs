using StbImageSharp;

namespace RunawayExplorer.Core.Formats;

/// <summary>
/// PNG image decoder supporting all PNG formats (RGB, RGBA, grayscale, grayscale+alpha, and indexed)
/// for scene backgrounds, overlays, and UI assets across The Next BIG Thing and Yesterday.
/// </summary>
public static class PngDecoder
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsPng(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data[..8].SequenceEqual(PngSignature);

    public static DecodedImage? Decode(byte[] pngData)
    {
        ArgumentNullException.ThrowIfNull(pngData);
        if (!IsPng(pngData))
            return null;

        try
        {
            var result = ImageResult.FromMemory(pngData, ColorComponents.RedGreenBlueAlpha);
            if (result is null || result.Width <= 0 || result.Height <= 0)
                return null;

            byte[] pixels = result.Data;
            // Convert RGBA to BGRA
            for (int i = 0; i < pixels.Length; i += 4)
            {
                (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            }

            return new DecodedImage(result.Width, result.Height, pixels);
        }
        catch
        {
            return null;
        }
    }
}
