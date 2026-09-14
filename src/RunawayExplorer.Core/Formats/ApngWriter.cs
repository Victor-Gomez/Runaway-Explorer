using System.Buffers.Binary;

namespace RunawayExplorer.Core.Formats;

/// <summary>
/// Writes a <see cref="SpriteAsset"/> as one animated PNG, chunk by chunk.
/// <para>
/// The canvas is <see cref="SpriteAsset.Bounds"/> -- the union of the frames' boxes. Each frame is
/// stored cropped, at its own size, with APNG's native per-frame x/y offset taken straight from the
/// frame's screen coordinates, so frames of different sizes line up exactly with nothing estimated.
/// <c>dispose_op = 1</c> clears each frame before the next; <c>blend_op = 0</c> copies. An empty frame
/// becomes a 1×1 transparent frame, so frame <i>N</i> of the file is always frame <i>N</i> of the asset
/// and timing survives.
/// </para>
/// <para>
/// Written by hand rather than through a library writer because the usual ones merge consecutive
/// identical frames, after which file frame <i>N</i> is no longer asset frame <i>N</i>. The format
/// stores no timing; <c>fps</c> is the caller's choice.
/// </para>
/// </summary>
public static class ApngWriter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static void Write(SpriteAsset asset, string path, double fps)
    {
        ArgumentNullException.ThrowIfNull(asset);
        using FileStream stream = File.Create(path);
        Write(asset, stream, fps);
    }

    public static void Write(SpriteAsset asset, Stream output, double fps)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(output);
        if (fps <= 0 || double.IsNaN(fps) || double.IsInfinity(fps))
            throw new ArgumentOutOfRangeException(nameof(fps));

        (int bx, int by, int bw, int bh) = asset.Bounds;
        ushort delayNum = (ushort)Math.Max(1, (int)Math.Round(1000.0 / fps));
        const ushort delayDen = 1000;

        output.Write(Signature);
        PngWriter.WriteChunk(output, "IHDR", PngWriter.Ihdr(bw, bh));

        var actl = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(actl, (uint)asset.FrameCount);
        BinaryPrimitives.WriteUInt32BigEndian(actl.AsSpan(4), 0); // loop forever
        PngWriter.WriteChunk(output, "acTL", actl);

        uint seq = 0;
        for (int fi = 0; fi < asset.FrameCount; fi++)
        {
            SpriteFrame frame = asset.DecodeFrame(fi);
            DecodedImage rgba;
            int ox, oy;
            if (frame.Image is not null)
            {
                rgba = frame.Image;
                ox = frame.X - bx;
                oy = frame.Y - by;
            }
            else
            {
                rgba = DecodedImage.Transparent(1, 1);
                ox = 0;
                oy = 0;
            }

            // Every segment is inside its record's box and the canvas is the union of those boxes; clip
            // anyway so a damaged asset cannot produce a file viewers reject.
            if (ox < 0 || oy < 0 || ox + rgba.Width > bw || oy + rgba.Height > bh)
            {
                int cx0 = Math.Max(0, -ox), cy0 = Math.Max(0, -oy);
                int cx1 = Math.Min(rgba.Width, bw - ox), cy1 = Math.Min(rgba.Height, bh - oy);
                if (cx1 <= cx0 || cy1 <= cy0)
                {
                    rgba = DecodedImage.Transparent(1, 1);
                    ox = 0;
                    oy = 0;
                }
                else
                {
                    rgba = rgba.Crop(cx0, cy0, cx1 - cx0, cy1 - cy0);
                    ox += cx0;
                    oy += cy0;
                }
            }

            if (fi == 0)
            {
                // The first frame doubles as the default image and must be full-canvas, so it is placed
                // on the canvas rather than offset.
                var full = DecodedImage.Transparent(bw, bh);
                full.Blit(rgba, ox, oy);
                PngWriter.WriteChunk(output, "fcTL", Fctl(seq++, bw, bh, 0, 0, delayNum, delayDen));
                PngWriter.WriteChunk(output, "IDAT", PngWriter.CompressPixels(full));
            }
            else
            {
                PngWriter.WriteChunk(output, "fcTL", Fctl(seq++, rgba.Width, rgba.Height, ox, oy, delayNum, delayDen));
                byte[] data = PngWriter.CompressPixels(rgba);
                var fdat = new byte[4 + data.Length];
                BinaryPrimitives.WriteUInt32BigEndian(fdat, seq++);
                data.CopyTo(fdat, 4);
                PngWriter.WriteChunk(output, "fdAT", fdat);
            }
        }

        PngWriter.WriteChunk(output, "IEND", []);
    }

    private static byte[] Fctl(uint seq, int w, int h, int x, int y, ushort delayNum, ushort delayDen)
    {
        var b = new byte[26];
        BinaryPrimitives.WriteUInt32BigEndian(b, seq);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4), (uint)w);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8), (uint)h);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(12), (uint)x);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(16), (uint)y);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(20), delayNum);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(22), delayDen);
        b[24] = 1; // dispose_op: clear to transparent before the next frame
        b[25] = 0; // blend_op: copy
        return b;
    }
}
