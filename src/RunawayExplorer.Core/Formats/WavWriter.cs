using System.Buffers.Binary;

namespace RunawayExplorer.Core.Formats;

/// <summary>
/// Wraps headerless PCM in a 44-byte RIFF/WAVE header. Every audio payload in Runaway is raw PCM:
/// 16-bit signed stereo in the <c>RESOURCE.M/S/002</c> archives, 8-bit unsigned mono for voice.
/// </summary>
public static class WavWriter
{
    public const int HeaderSize = 44;

    public static byte[] Header(int pcmByteCount, int sampleRate, int channels, int bitsPerSample)
    {
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);

        var h = new byte[HeaderSize];
        "RIFF"u8.CopyTo(h);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(4), 36 + pcmByteCount);
        "WAVE"u8.CopyTo(h.AsSpan(8));
        "fmt "u8.CopyTo(h.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(20), 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(22), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(28), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(32), blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(34), (short)bitsPerSample);
        "data"u8.CopyTo(h.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(40), pcmByteCount);
        return h;
    }

    public static void Write(Stream output, ReadOnlySpan<byte> pcm, int sampleRate, int channels, int bitsPerSample)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Write(Header(pcm.Length, sampleRate, channels, bitsPerSample));
        output.Write(pcm);
    }

    public static void Write(string path, ReadOnlySpan<byte> pcm, int sampleRate, int channels, int bitsPerSample)
    {
        using FileStream stream = File.Create(path);
        Write(stream, pcm, sampleRate, channels, bitsPerSample);
    }
}
