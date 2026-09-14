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
public readonly record struct SpriteSegment(int X, int Y, int Count, int PixelOffset);

/// <summary>A decoded frame: the image cropped to its content, and where its top-left sits on the screen. <see cref="Image"/> is <see langword="null"/> for an empty frame.</summary>
public readonly record struct SpriteFrame(DecodedImage? Image, int X, int Y)
{
    public bool IsEmpty => Image is null;
}

/// <summary>
/// Format 3 of the scene archives: an animated sprite.
/// <code>
/// [ descriptor records, 0 or more ]  { u32 0, u16 W, i16 -(W-1), u16 H, u16 0, u16 0 }
/// record[N]                          { u32 frame_offset, u16 x0, w, y0, y1, u16 c }   14 bytes each
/// frame data                         per frame, c × { u16 x, u16 y, u8 count (0 → 1), u16 rgb565[count] }
/// </code>
/// <para>
/// <b>Every frame is a complete sprite.</b> Segments are absolute screen positions, in pixels; nothing
/// carries over between frames; <c>x</c> is a pixel column, not a byte offset. A row wider than 255
/// pixels is two consecutive segments on the same <c>y</c>. Most assets keep one box for the whole
/// animation; 69 of 442 change it per frame, so <see cref="Bounds"/> is the union of every record's box.
/// </para>
/// <para>
/// Identification: the first record's offset is 0; the table has no length field and is read until a
/// record stops making sense (offset decreases or exceeds the asset, <c>w == 0</c>, <c>y1 &lt; y0</c>).
/// <c>x0 == 0</c> is a legal position, not a terminator -- treating it as one truncated 8 assets. Then
/// frame 0 must walk exactly to frame 1's offset; that separates a real animation from a data table
/// that happens to start with a zero word.
/// </para>
/// <para>
/// Timing is not stored anywhere in the format. The engine's own frame rate is not known.
/// </para>
/// </summary>
public sealed class SpriteAsset
{
    public const int RecordSize = 14;

    private readonly byte[] _data;

    private SpriteAsset(byte[] data, IReadOnlyList<SpriteRecord> records, int dataOffset, int descriptorCount)
    {
        _data = data;
        Records = records;
        DataOffset = dataOffset;
        DescriptorCount = descriptorCount;

        int bx = int.MaxValue, by = int.MaxValue, bx1 = 0, by1 = 0;
        foreach (SpriteRecord r in records)
        {
            bx = Math.Min(bx, r.X0);
            by = Math.Min(by, r.Y0);
            bx1 = Math.Max(bx1, r.X0 + r.W);
            by1 = Math.Max(by1, r.Y1 + 1);
        }
        Bounds = (bx, by, bx1 - bx, by1 - by);
    }

    public IReadOnlyList<SpriteRecord> Records { get; }

    public int FrameCount => Records.Count;

    /// <summary>Byte offset of the frame data (end of the record table); frame offsets are relative to it.</summary>
    public int DataOffset { get; }

    /// <summary>Number of leading descriptor records skipped. Their purpose is unknown.</summary>
    public int DescriptorCount { get; }

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
        int p = dataOffset;
        for (int s = 0; s < records[0].SegmentCount; s++)
        {
            if (p + 5 > data.Length)
                return null;
            int count = data[p + 4];
            if (count == 0) count = 1;
            p += 5 + 2 * count;
        }
        int end0 = records.Count > 1 ? dataOffset + records[1].FrameOffset : data.Length;
        if (p != end0)
            return null;

        return new SpriteAsset(data, records, dataOffset, descriptors);
    }

    /// <summary>The segments of frame <paramref name="index"/>. A truncated frame yields the segments that fit.</summary>
    public List<SpriteSegment> ReadSegments(int index)
    {
        SpriteRecord rec = Records[index];
        int p = DataOffset + rec.FrameOffset;
        var segs = new List<SpriteSegment>(rec.SegmentCount);
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
            segs.Add(new SpriteSegment(x, y, count, p));
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
            Rgb565.CopyRow(_data, s.PixelOffset, image.Pixels, dst, s.Count);
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
