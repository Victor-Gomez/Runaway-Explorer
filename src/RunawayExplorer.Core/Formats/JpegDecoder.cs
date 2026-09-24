using System.Buffers.Binary;
using StbImageSharp;

namespace RunawayExplorer.Core.Formats;

/// <summary>
/// Fast JPEG geometry detection via SOF marker parsing, and decoding into tightly packed BGRA32 <see cref="DecodedImage"/>.
/// </summary>
public static class JpegDecoder
{
    /// <summary>Returns true if <paramref name="data"/> begins with the standard JPEG SOI marker (0xFF, 0xD8, 0xFF).</summary>
    public static bool IsJpeg(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    /// <summary>
    /// Rapidly scans JPEG markers to extract image width and height without decompressing the image.
    /// </summary>
    public static bool TryGetDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!IsJpeg(data))
            return false;

        int p = 2;
        while (p + 4 < data.Length)
        {
            if (data[p] != 0xFF)
                break;
            while (p < data.Length && data[p] == 0xFF)
                p++;
            if (p >= data.Length)
                break;

            byte marker = data[p++];
            if (marker is 0xD9 or 0xDA) // EOI or SOS (start of scan: pixel stream begins)
                break;

            if (p + 2 > data.Length)
                break;

            int len = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(p));
            // Start of Frame markers SOF0..SOF15 (excluding DHT 0xC4, JPG 0xC8, DAC 0xCC)
            if ((marker >= 0xC0 && marker <= 0xC3) ||
                (marker >= 0xC5 && marker <= 0xC7) ||
                (marker >= 0xC9 && marker <= 0xCB) ||
                (marker >= 0xCD && marker <= 0xCF))
            {
                if (p + 7 <= data.Length)
                {
                    height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(p + 3));
                    width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(p + 5));
                    return width > 0 && height > 0;
                }
                break;
            }

            p += len;
        }

        return false;
    }

    /// <summary>
    /// Decodes JPEG bytes into a BGRA32 <see cref="DecodedImage"/>.
    /// </summary>
    public static DecodedImage? Decode(byte[] data)
    {
        try
        {
            var result = ImageResult.FromMemory(data, ColorComponents.RedGreenBlueAlpha);
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
