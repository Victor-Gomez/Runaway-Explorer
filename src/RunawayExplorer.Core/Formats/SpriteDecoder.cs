using System.Buffers.Binary;

namespace RunawayExplorer.Core.Formats;

/// <summary>Supported sprite animation format variants across the Runaway series.</summary>
public enum SpriteFormat
{
    /// <summary>Runaway 1: 5-byte segment headers, RGB565 only, frame offsets relative to header end.</summary>
    Runaway1 = 1,

    /// <summary>Runaway 2: 6-byte segment headers with alpha flag, paired archive entries.</summary>
    Runaway2 = 2,

    /// <summary>Runaway 3: 7-byte segment headers with 16-bit count and alpha flag, absolute table offsets.</summary>
    Runaway3 = 3,

    /// <summary>Hollywood Monsters: Runaway 1's records and 5-byte segment headers, but one palette index per pixel.</summary>
    HollywoodMonsters = 4,
}

/// <summary>
/// One 14-byte frame record. <paramref name="FrameOffset"/> is relative to the end of the record table;
/// (<paramref name="X0"/>, <paramref name="W"/>, <paramref name="Y0"/>, <paramref name="Y1"/>) is this
/// frame's bounding box on screen -- columns <c>X0 .. X0+W-1</c>, rows <c>Y0 .. Y1</c> inclusive -- and
/// every one of its <paramref name="SegmentCount"/> segments falls inside it.
/// </summary>
public readonly record struct SpriteRecord(int FrameOffset, int X0, int W, int Y0, int Y1, int SegmentCount);

/// <summary>One run of pixels: <paramref name="Count"/> values at byte <paramref name="PixelOffset"/>, painted at screen (<paramref name="X"/>, <paramref name="Y"/>).</summary>
public readonly record struct SpriteSegment(int X, int Y, int Count, int PixelOffset, byte Flag = 0)
{
    public bool HasAlpha => Flag is 1 or 7;
}

/// <summary>A decoded frame: the image cropped to its content, and where its top-left sits on the screen. <see cref="Image"/> is <see langword="null"/> for an empty frame.</summary>
public readonly record struct SpriteFrame(DecodedImage? Image, int X, int Y)
{
    public bool IsEmpty => Image is null;
}

/// <summary>Decoded segment header parameters for any sprite format.</summary>
internal readonly record struct SpriteSegmentHeader(int X, int Y, int Count, byte Flag, int HeaderBytes, int PixelBytes)
{
    public bool HasAlpha => Flag is 1 or 7;
}

/// <summary>
/// Format 3 of the scene archives: an animated sprite.
/// Supports Runaway 1 single-entry format, Runaway 2 paired header/data format, and Runaway 3 7-byte segment format.
/// </summary>
public sealed class SpriteAsset
{
    public const int RecordSize = 14;

    private readonly byte[] _data;

    private SpriteAsset(byte[] data, IReadOnlyList<SpriteRecord> records, int dataOffset, int descriptorCount, SpriteFormat format)
    {
        _data = data;
        Records = records;
        DataOffset = dataOffset;
        DescriptorCount = descriptorCount;
        Format = format;

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

    /// <summary>The sprite format variant (Runaway 1, 2, or 3).</summary>
    public SpriteFormat Format { get; }

    /// <summary>True if this sprite uses the Runaway 2 6-byte segment headers and optional alpha channel.</summary>
    public bool IsRunaway2 => Format == SpriteFormat.Runaway2;

    /// <summary>True if this sprite uses the Runaway 3 7-byte segment headers (16-bit count) and absolute table offsets.</summary>
    public bool IsRunaway3 => Format == SpriteFormat.Runaway3;

    /// <summary>True if this sprite's pixels are palette indices rather than colours.</summary>
    public bool IsIndexed => Format == SpriteFormat.HollywoodMonsters;

    /// <summary>The union of every frame's box: the region of the screen the animation ever touches. The natural canvas for reassembly.</summary>
    public (int X, int Y, int Width, int Height) Bounds { get; }

    /// <summary>The raw asset bytes.</summary>
    public ReadOnlySpan<byte> Data => _data;

    /// <summary>
    /// Reads and validates a segment header at byte position <paramref name="p"/> according to <paramref name="format"/>.
    /// </summary>
    internal static bool TryReadSegmentHeader(
        ReadOnlySpan<byte> data,
        int p,
        SpriteFormat format,
        out SpriteSegmentHeader header)
    {
        header = default;
        switch (format)
        {
            case SpriteFormat.Runaway3:
            {
                if (p + 7 > data.Length) return false;
                int x = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p));
                int y = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p + 2));
                byte flag = data[p + 4];
                int count = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p + 5));
                int bpp = flag switch
                {
                    0 => 2,
                    1 => 3,
                    6 => 3,
                    7 => 4,
                    _ => 0
                };
                if (bpp == 0 || p + 7 + count * bpp > data.Length) return false;
                header = new SpriteSegmentHeader(x, y, count, flag, 7, count * bpp);
                return true;
            }
            case SpriteFormat.Runaway2:
            {
                if (p + 6 > data.Length) return false;
                int x = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p));
                int y = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p + 2));
                byte flag = data[p + 4];
                byte count = data[p + 5];
                int bpp = flag == 0 ? 2 : (flag == 1 ? 3 : 0);
                if (bpp == 0 || p + 6 + count * bpp > data.Length) return false;
                header = new SpriteSegmentHeader(x, y, count, flag, 6, count * bpp);
                return true;
            }
            case SpriteFormat.Runaway1:
            case SpriteFormat.HollywoodMonsters:
            {
                if (p + 5 > data.Length) return false;
                int x = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p));
                int y = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p + 2));
                int count = data[p + 4];
                if (count == 0) count = 1;
                int bytesPerPixel = format == SpriteFormat.HollywoodMonsters ? 1 : 2;
                if (p + 5 + bytesPerPixel * count > data.Length) return false;
                header = new SpriteSegmentHeader(x, y, count, 0, 5, bytesPerPixel * count);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Walks all segments for a frame starting at byte position <paramref name="startP"/>, returning the ending position.
    /// </summary>
    internal static bool TryWalkFrame(
        ReadOnlySpan<byte> data,
        int startP,
        int segmentCount,
        SpriteFormat format,
        out int endP)
    {
        endP = startP;
        for (int s = 0; s < segmentCount; s++)
        {
            if (!TryReadSegmentHeader(data, endP, format, out SpriteSegmentHeader h))
                return false;
            endP += h.HeaderBytes + h.PixelBytes;
        }
        return true;
    }

    /// <summary>
    /// Parses <paramref name="data"/> as a sprite asset. <see langword="null"/> when it is not one.
    /// Cheap: only the record table and frame 0 are walked.
    /// </summary>
    public static SpriteAsset? Parse(byte[] data) => Parse(data, bytesPerPixel: 2);

    /// <summary>
    /// Parses <paramref name="data"/> as a sprite asset. <see langword="null"/> when it is not one.
    /// Cheap: only the record table and frame 0 are walked.
    /// <para>
    /// <paramref name="bytesPerPixel"/> selects the pixel width the segment walk expects: 2 for the
    /// RGB565 sprites of the Runaway games, 1 for Hollywood Monsters' palette-indexed ones. It has to be
    /// told rather than guessed, because the walk is what proves the format and both widths are structurally
    /// plausible on short frames.
    /// </para>
    /// </summary>
    public static SpriteAsset? Parse(byte[] data, int bytesPerPixel)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (bytesPerPixel == 1)
            return ParseIndexed(data);

        // 1. Try parsing as Runaway 3 paired sprite (fo0 == headerSize, 7-byte segments with u16 count)
        if (data.Length >= 16)
        {
            ushort fc = BinaryPrimitives.ReadUInt16LittleEndian(data);
            uint fo0 = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(2));
            int headerSize = 2 + fc * RecordSize;
            if (fc > 0 && fc <= 2000 && fo0 == headerSize && data.Length >= headerSize)
            {
                var r3Records = new List<SpriteRecord>(fc);
                bool validRecords = true;
                long prevFo = -1;
                for (int k = 0; k < fc; k++)
                {
                    int rp = 2 + k * RecordSize;
                    long fo = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rp));
                    int x0 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rp + 4));
                    int x1 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rp + 6));
                    int y0 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rp + 8));
                    int y1 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rp + 10));
                    int c = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rp + 12));
                    int w = x1 >= x0 ? x1 - x0 : 0;
                    if (fo < prevFo || fo > data.Length || (x1 < x0 && c > 0) || y1 < y0 || y1 >= 8192 || x1 > 8192)
                    {
                        validRecords = false;
                        break;
                    }
                    r3Records.Add(new SpriteRecord((int)(fo - headerSize), x0, w, y0, y1, c));
                    prevFo = fo;
                }

                if (validRecords)
                {
                    // Find first frame with segments to verify walk
                    int walkFrame = -1;
                    for (int k = 0; k < r3Records.Count; k++)
                    {
                        if (r3Records[k].SegmentCount > 0)
                        {
                            walkFrame = k;
                            break;
                        }
                    }

                    if (walkFrame >= 0)
                    {
                        int startP = headerSize + r3Records[walkFrame].FrameOffset;
                        int expectedEnd = walkFrame + 1 < r3Records.Count ? headerSize + r3Records[walkFrame + 1].FrameOffset : data.Length;
                        if (TryWalkFrame(data, startP, r3Records[walkFrame].SegmentCount, SpriteFormat.Runaway3, out int endP) && endP == expectedEnd)
                        {
                            int r3Descriptors = walkFrame > 0 && r3Records[0].SegmentCount == 0 ? 1 : 0;
                            return new SpriteAsset(data, r3Records, headerSize, r3Descriptors, SpriteFormat.Runaway3);
                        }
                    }
                    else if (r3Records.Count > 0 && r3Records[0].SegmentCount == 0)
                    {
                        return new SpriteAsset(data, r3Records, headerSize, descriptorCount: 1, SpriteFormat.Runaway3);
                    }
                }
            }
        }

        // 2. Try parsing as Runaway 2 paired sprite
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
                        int startP = headerSize;
                        int expectedEnd = r2Records.Count > 1 ? headerSize + r2Records[1].FrameOffset : data.Length;
                        if (TryWalkFrame(data, startP, r2Records[0].SegmentCount, SpriteFormat.Runaway2, out int endP) && endP == expectedEnd)
                        {
                            return new SpriteAsset(data, r2Records, headerSize, descriptorCount: 0, SpriteFormat.Runaway2);
                        }
                    }
                }
            }
        }

        // 3. Try parsing as Runaway 1 single-entry sprite
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
        int startPR1 = dataOffset;
        int expectedEndR1 = records.Count > 1 ? dataOffset + records[1].FrameOffset : data.Length;
        if (!TryWalkFrame(data, startPR1, records[0].SegmentCount, SpriteFormat.Runaway1, out int endPR1) || endPR1 != expectedEndR1)
            return null;

        return new SpriteAsset(data, records, dataOffset, descriptors, SpriteFormat.Runaway1);
    }

    /// <summary>
    /// The colour table for a <see cref="SpriteFormat.HollywoodMonsters"/> asset, whose pixels are palette
    /// indices. Unset, <see cref="IndexedPalette.Grayscale"/> stands in so the frames are still readable.
    /// </summary>
    public IndexedPalette? Palette { get; set; }

    /// <summary>
    /// Hollywood Monsters' sprites: Runaway 1's 14-byte records and 5-byte segment headers with one
    /// palette index per pixel. The leading descriptor records of the Runaway 1 format do not occur here.
    /// </summary>
    private static SpriteAsset? ParseIndexed(byte[] data)
    {
        if (data.Length < RecordSize || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0)
            return null;

        var records = new List<SpriteRecord>();
        long prev = -1;
        int i = 0;
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
        int expectedEnd = records.Count > 1 ? dataOffset + records[1].FrameOffset : data.Length;
        if (!TryWalkFrame(data, dataOffset, records[0].SegmentCount, SpriteFormat.HollywoodMonsters, out int endP) || endP != expectedEnd)
            return null;

        return new SpriteAsset(data, records, dataOffset, descriptorCount: 0, SpriteFormat.HollywoodMonsters);
    }

    /// <summary>The segments of frame <paramref name="index"/>. A truncated frame yields the segments that fit.</summary>
    public List<SpriteSegment> ReadSegments(int index)
    {
        SpriteRecord rec = Records[index];
        int p = DataOffset + rec.FrameOffset;
        var segs = new List<SpriteSegment>(rec.SegmentCount);

        for (int s = 0; s < rec.SegmentCount; s++)
        {
            if (!TryReadSegmentHeader(_data, p, Format, out SpriteSegmentHeader h))
                break;
            p += h.HeaderBytes;
            segs.Add(new SpriteSegment(h.X, h.Y, h.Count, p, h.Flag));
            p += h.PixelBytes;
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
            if (s.Flag == 6)
            {
                int src = s.PixelOffset;
                for (int k = 0; k < s.Count; k++)
                {
                    int outIdx = dst + k * 4;
                    image.Pixels[outIdx] = _data[src];
                    image.Pixels[outIdx + 1] = _data[src + 1];
                    image.Pixels[outIdx + 2] = _data[src + 2];
                    image.Pixels[outIdx + 3] = 255;
                    src += 3;
                }
            }
            else if (s.Flag == 7)
            {
                int src = s.PixelOffset;
                for (int k = 0; k < s.Count; k++)
                {
                    byte b = _data[src];
                    byte g = _data[src + 1];
                    byte r = _data[src + 2];
                    byte a = _data[src + 3];
                    src += 4;

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
                            image.Pixels[outIdx + 1] = (byte)((g * srcA + image.Pixels[outIdx] * dstA * (1f - srcA)) / outA);
                            image.Pixels[outIdx + 2] = (byte)((r * srcA + image.Pixels[outIdx] * dstA * (1f - srcA)) / outA);
                            image.Pixels[outIdx + 3] = (byte)(outA * 255f);
                        }
                    }
                }
            }
            else if (s.HasAlpha)
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
                            image.Pixels[outIdx + 1] = (byte)((g * srcA + image.Pixels[outIdx] * dstA * (1f - srcA)) / outA);
                            image.Pixels[outIdx + 2] = (byte)((r * srcA + image.Pixels[outIdx] * dstA * (1f - srcA)) / outA);
                            image.Pixels[outIdx + 3] = (byte)(outA * 255f);
                        }
                    }
                }
            }
            else if (Format == SpriteFormat.HollywoodMonsters)
            {
                (Palette ?? IndexedPalette.Grayscale).CopyRow(_data, s.PixelOffset, image.Pixels, dst, s.Count);
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
                if (!TryReadSegmentHeader(_data, p, Format, out SpriteSegmentHeader h))
                    return $"frame {f}: segment {s} invalid or past end of asset";

                if (h.X < rec.X0 || h.X + h.Count > rec.X0 + rec.W || h.Y < rec.Y0 || h.Y > rec.Y1)
                    return $"frame {f}: segment {s} at ({h.X},{h.Y})+{h.Count} outside the frame's box";

                p += h.HeaderBytes + h.PixelBytes;
            }

            int expectedEnd = f + 1 < Records.Count ? DataOffset + Records[f + 1].FrameOffset : _data.Length;
            if (p != expectedEnd)
                return $"frame {f}: walked to {p}, expected {expectedEnd}";
        }
        return null;
    }
}
