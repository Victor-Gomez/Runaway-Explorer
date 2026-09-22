using System.Buffers.Binary;

namespace RunawayExplorer.Core.Formats;

/// <summary>
/// One 14-byte frame record. <paramref name="FrameOffset"/> is relative to the end of the record table;
/// (<paramref name="X0"/>, <paramref name="W"/>, <paramref name="Y0"/>, <paramref name="Y1"/>) is this
/// frame's bounding box on screen -- columns <c>X0 .. X0+W-1</c>, rows <c>Y0 .. Y1</c> inclusive -- and
/// every one of its <paramref name="SegmentCount"/> segments falls inside it.
/// </summary>
public readonly record struct SpriteRecord(int FrameOffset, int X0, int W, int Y0, int Y1, int SegmentCount);

/// <summary>One run of pixels: <paramref name="Count"/> RGB565 values at byte <paramref name="PixelOffset"/>, painted at screen (<paramref name="X"/>, <paramref name="Y"/>).</summary>
public readonly record struct SpriteSegment(int X, int Y, int Count, int PixelOffset, bool HasAlpha = false);

/// <summary>A decoded frame: the image cropped to its content, and where its top-left sits on the screen. <see cref="Image"/> is <see langword="null"/> for an empty frame.</summary>
public readonly record struct SpriteFrame(DecodedImage? Image, int X, int Y)
{
    public bool IsEmpty => Image is null;
}

/// <summary>
/// Format 3 of the scene archives: an animated sprite.
/// Supports both Runaway 1 single-entry format and Runaway 2 paired header/data format.
/// </summary>
public sealed class SpriteAsset
{
    public const int RecordSize = 14;

    private readonly byte[] _data;

    private SpriteAsset(byte[] data, IReadOnlyList<SpriteRecord> records, int dataOffset, int descriptorCount, bool isRunaway2 = false)
    {
        _data = data;
        Records = records;
        DataOffset = dataOffset;
        DescriptorCount = descriptorCount;
        IsRunaway2 = isRunaway2;

        int bx = int.MaxValue, by = int.MaxValue, bx1 = 0, by1 = 0;
        foreach (SpriteRecord r in records)
        {
            bx = Math.Min(bx, r.X0);
            by = Math.Min(by, r.Y0);
            bx1 = Math.Max(bx1, r.X0 + r.W);
            by1 = Math.Max(by1, r.Y1 + 1);
        }
        Bounds = (bx == int.MaxValue ? 0 : bx, by == int.MaxValue ? 0 : by, Math.Max(0, bx1 - bx), Math.Max(0, by1 - by));
    }

    public IReadOnlyList<SpriteRecord> Records { get; }

    public int FrameCount => Records.Count;

    /// <summary>Byte offset of the frame data (end of the record table); frame offsets are relative to it.</summary>
    public int DataOffset { get; }

    /// <summary>Number of leading descriptor records skipped. Their purpose is unknown.</summary>
    public int DescriptorCount { get; }

    /// <summary>True if this sprite uses the Runaway 2 6-byte segment headers and optional alpha channel.</summary>
    public bool IsRunaway2 { get; }

    /// <summary>The union of every frame's box: the region of the screen the animation ever touches. The natural canvas for reassembly.</summary>
    public (int X, int Y, int Width, int Height) Bounds { get; }

    /// <summary>The raw asset bytes.</summary>
    public ReadOnlySpan<byte> Data => _data;

    /// <summary>
    /// Parses <paramref name="data"/> as a sprite asset. <see langword="null"/> when it is not one.
    /// Cheap: only the record table and frame 0 are walked.
    /// </summary>
    public static SpriteAsset? Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // 1. Try parsing as Runaway 2 paired sprite
        if (data.Length >= 16)
        {
            ushort fc = BinaryPrimitives.ReadUInt16LittleEndian(data);
            uint fo0 = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(2));
            if (fc > 0 && fc <= 2000 && fo0 == 0)
            {
                int headerSize = 2 + fc * RecordSize;
                if (data.Length >= headerSize)
                {
                    var r2Records = new List<SpriteRecord>(fc);
                    bool validRecords = true;
                    long prevFo = -1;
                    for (int k = 0; k < fc; k++)
                    {
                        int rp = 2 + k * RecordSize;
                        long fo = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rp));
                        int x0 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rp + 4));
                        int w = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rp + 6));
                        int y0 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rp + 8));
                        int y1 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rp + 10));
                        int c = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rp + 12));
                        if (fo < prevFo || fo > data.Length - headerSize || w == 0 || y1 < y0 || y1 >= 4096 || x0 + w > 4096)
                        {
                            validRecords = false;
                            break;
                        }
                        r2Records.Add(new SpriteRecord((int)fo, x0, w, y0, y1, c));
                        prevFo = fo;
                    }

                    if (validRecords)
                    {
                        // Verify frame 0 walk
                        int p = headerSize;
                        bool walkOk = true;
                        for (int s = 0; s < r2Records[0].SegmentCount; s++)
                        {
                            if (p + 6 > data.Length) { walkOk = false; break; }
                            byte flag = data[p + 4];
                            byte cnt = data[p + 5];
                            int bpp = flag == 0 ? 2 : (flag == 1 ? 3 : 0);
                            if (bpp == 0 || p + 6 + cnt * bpp > data.Length) { walkOk = false; break; }
                            p += 6 + cnt * bpp;
                        }
                        int end0 = r2Records.Count > 1 ? headerSize + r2Records[1].FrameOffset : data.Length;
                        if (walkOk && p == end0)
                        {
                            return new SpriteAsset(data, r2Records, headerSize, descriptorCount: 0, isRunaway2: true);
                        }
                    }
                }
            }
        }

        // 2. Try parsing as Runaway 1 single-entry sprite
        if (data.Length < RecordSize || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0)
            return null;

        int i = 0;
        int descriptors = 0;
        // Descriptor records open 13 assets belonging to wide scenes: zero segments, W = scene width.
        while (i + RecordSize <= data.Length)
        {
            uint off = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i));
            int x0 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i + 4));
            short neg = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i + 6));
            if (off != 0 || x0 == 0 || neg != -(x0 - 1))
                break;
            i += RecordSize;
            descriptors++;
        }

        var records = new List<SpriteRecord>();
        long prev = -1;
        while (i + RecordSize <= data.Length)
        {
            long off = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i));
            int x0 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i + 4));
            int w = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i + 6));
            int y0 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i + 8));
            int y1 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i + 10));
            int c = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i + 12));
            if (off < prev || off >= data.Length || w == 0 || y1 < y0 || y1 >= 4096 || x0 + w > 4096)
                break;
            records.Add(new SpriteRecord((int)off, x0, w, y0, y1, c));
            prev = off;
            i += RecordSize;
        }

        if (records.Count == 0 || records[0].FrameOffset != 0)
            return null;

        int dataOffset = i;

        // Frame 0 must walk cleanly to frame 1 (or to the end of the asset).
        int pR1 = dataOffset;
        for (int s = 0; s < records[0].SegmentCount; s++)
        {
            if (pR1 + 5 > data.Length)
                return null;
            int count = data[pR1 + 4];
            if (count == 0) count = 1;
            pR1 += 5 + 2 * count;
        }
        int end0R1 = records.Count > 1 ? dataOffset + records[1].FrameOffset : data.Length;
        if (pR1 != end0R1)
            return null;

        return new SpriteAsset(data, records, dataOffset, descriptors, isRunaway2: false);
    }

    /// <summary>The segments of frame <paramref name="index"/>. A truncated frame yields the segments that fit.</summary>
    public List<SpriteSegment> ReadSegments(int index)
    {
        SpriteRecord rec = Records[index];
        int p = DataOffset + rec.FrameOffset;
        var segs = new List<SpriteSegment>(rec.SegmentCount);

        if (IsRunaway2)
        {
            for (int s = 0; s < rec.SegmentCount; s++)
            {
                if (p + 6 > _data.Length)
                    break;
                int x = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(p));
                int y = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(p + 2));
                byte flag = _data[p + 4];
                byte count = _data[p + 5];
                bool hasAlpha = flag == 1;
                int bpp = flag == 0 ? 2 : (flag == 1 ? 3 : 2);
                p += 6;
                if (p + count * bpp > _data.Length)
                    break;
                segs.Add(new SpriteSegment(x, y, count, p, hasAlpha));
                p += count * bpp;
            }
            return segs;
        }

        for (int s = 0; s < rec.SegmentCount; s++)
        {
            if (p + 5 > _data.Length)
                break;
            int x = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(p));
            int y = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(p + 2));
            int count = _data[p + 4];
            if (count == 0) count = 1;
            p += 5;
            if (p + 2 * count > _data.Length)
                break;
            segs.Add(new SpriteSegment(x, y, count, p, HasAlpha: false));
            p += 2 * count;
        }
        return segs;
    }

    /// <summary>Paints frame <paramref name="index"/>, cropped to its content.</summary>
    public SpriteFrame DecodeFrame(int index)
    {
        List<SpriteSegment> segs = ReadSegments(index);
        if (segs.Count == 0)
            return new SpriteFrame(null, 0, 0);

        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = 0, y1 = 0;
        foreach (SpriteSegment s in segs)
        {
            x0 = Math.Min(x0, s.X);
            y0 = Math.Min(y0, s.Y);
            x1 = Math.Max(x1, s.X + s.Count);
            y1 = Math.Max(y1, s.Y + 1);
        }

        var image = DecodedImage.Transparent(x1 - x0, y1 - y0);
        foreach (SpriteSegment s in segs)
        {
            int dst = ((s.Y - y0) * image.Width + (s.X - x0)) * 4;
            if (s.HasAlpha)
            {
                int src = s.PixelOffset;
                for (int k = 0; k < s.Count; k++)
                {
                    ushort p16 = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(src));
                    byte a = _data[src + 2];
                    src += 3;

                    byte r = (byte)((p16 >> 11) << 3);
                    byte g = (byte)(((p16 >> 5) & 63) << 2);
                    byte b = (byte)((p16 & 31) << 3);

                    int outIdx = dst + k * 4;
                    if (a == 255 || image.Pixels[outIdx + 3] == 0)
                    {
                        image.Pixels[outIdx] = b;
                        image.Pixels[outIdx + 1] = g;
                        image.Pixels[outIdx + 2] = r;
                        image.Pixels[outIdx + 3] = a;
                    }
                    else
                    {
                        float srcA = a / 255f;
                        float dstA = image.Pixels[outIdx + 3] / 255f;
                        float outA = srcA + dstA * (1f - srcA);
                        if (outA > 0)
                        {
                            image.Pixels[outIdx] = (byte)((b * srcA + image.Pixels[outIdx] * dstA * (1f - srcA)) / outA);
                            image.Pixels[outIdx + 1] = (byte)((g * srcA + image.Pixels[outIdx + 1] * dstA * (1f - srcA)) / outA);
                            image.Pixels[outIdx + 2] = (byte)((r * srcA + image.Pixels[outIdx + 2] * dstA * (1f - srcA)) / outA);
                            image.Pixels[outIdx + 3] = (byte)(outA * 255f);
                        }
                    }
                }
            }
            else
            {
                Rgb565.CopyRow(_data, s.PixelOffset, image.Pixels, dst, s.Count);
            }
        }
        return new SpriteFrame(image, x0, y0);
    }


    /// <summary>
    /// Frame <paramref name="index"/> placed on a canvas the size of <see cref="Bounds"/>, so every frame
    /// of the animation has the same size and frames line up without any per-frame offset.
    /// </summary>
    public DecodedImage DecodeFrameOnCanvas(int index)
    {
        var canvas = DecodedImage.Transparent(Bounds.Width, Bounds.Height);
        SpriteFrame frame = DecodeFrame(index);
        if (frame.Image is not null)
            canvas.Blit(frame.Image, frame.X - Bounds.X, frame.Y - Bounds.Y);
        return canvas;
    }

    /// <summary>
    /// Internal-consistency check over every frame: walking each frame's segments must land exactly
    /// on the next frame's stored offset, and every segment must lie inside its own frame's box.
    /// Returns a description of the first violation, or <see langword="null"/> when the asset is clean.
    /// </summary>
    public string? Verify()
    {
        for (int f = 0; f < Records.Count; f++)
        {
            SpriteRecord rec = Records[f];
            int p = DataOffset + rec.FrameOffset;
            for (int s = 0; s < rec.SegmentCount; s++)
            {
                if (p + 5 > _data.Length)
                    return $"frame {f}: segment {s} header past end of asset";
                int x = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(p));
                int y = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(p + 2));
                int count = _data[p + 4];
                if (count == 0) count = 1;
                if (x < rec.X0 || x + count > rec.X0 + rec.W || y < rec.Y0 || y > rec.Y1)
                    return $"frame {f}: segment {s} at ({x},{y})+{count} outside the frame's box";
                p += 5 + 2 * count;
            }
            int expectedEnd = f + 1 < Records.Count ? DataOffset + Records[f + 1].FrameOffset : _data.Length;
            if (p != expectedEnd)
                return $"frame {f}: walked to {p}, expected {expectedEnd}";
        }
        return null;
    }
}
