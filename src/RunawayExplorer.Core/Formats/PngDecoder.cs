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

    /// <summary>
    /// Reads the size out of the IHDR chunk, which always begins at byte 16, without decoding the pixels.
    /// </summary>
    public static bool TryGetDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        if (!IsPng(data) || data.Length < 24)
            return false;

        width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data[16..]);
        height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data[20..]);
        return width > 0 && height > 0;
    }

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
