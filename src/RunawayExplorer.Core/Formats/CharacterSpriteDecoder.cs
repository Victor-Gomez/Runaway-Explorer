using System.Buffers.Binary;

namespace RunawayExplorer.Core.Formats;

/// <summary>
/// One 32-byte frame record of the character sprite library.
/// <para>
/// <paramref name="DataOffset"/> is relative to the end of the entry's 3,200-byte frame table, and frame
/// 0's is always 0. <paramref name="Width"/> and <paramref name="Height"/> are the <em>extent</em> of the
/// frame's runs, not a box with a stored origin -- see <see cref="CharacterSpriteAsset"/>.
/// <paramref name="AnchorX"/> and <paramref name="AnchorY"/> are the character's feet, measured from that
/// extent's top-left corner.
/// </para>
/// </summary>
public readonly record struct CharacterFrameRecord(
    int DataOffset,
    int BodyRunCount,
    int EdgeCountA,
    int EdgeCountB,
    int AnchorX,
    int AnchorY,
    int Width,
    int Height)
{
    /// <summary>
    /// How many runs the coverage stream holds. Only the sum of the two stored counts is established: it
    /// is the run count exactly in all 2,166 frames of <em>Runaway 1</em>'s library, but it is not the
    /// split by run kind -- frame 0 of entry 0 has 908 full-coverage and 1,459 partial runs against the
    /// record's 288 and 2,079.
    /// </summary>
    public int EdgeRunCount => EdgeCountA + EdgeCountB;
}

/// <summary>
/// One entry of <em>Runaway 1</em>'s character sprite library (<c>Resource.001</c>): a short animation
/// of one character, as palette-indexed runs behind a 32-byte frame record. A different container and a
/// different codec from the scene archives' <see cref="SpriteAsset"/>, which is why it has its own class.
/// <para>
/// An entry is a fixed 3,200-byte frame table -- 100 records, the unused tail zeroed -- followed by the
/// frame data. Each frame holds two run streams back to back:
/// </para>
/// <code>
/// body     BodyRunCount  x  { u16 x, u16 y, u16 count, u8 index[count] }   palette indices
/// coverage EdgeRunCount  x  { u16 x, u16 y, u8 kind, u16 count            }
///                               kind 1 -> count bytes of partial coverage follow
///                               kind 0 -> none do, the run is fully covered
/// </code>
/// <para>
/// The coverage stream is <b>not</b> colour and not an outline. It is the sprite's whole footprint --
/// the character's silhouette <em>plus</em> the shadow it casts on the ground -- with the interior stored
/// as full-coverage runs and the anti-aliased boundary as per-pixel runs. Every body pixel of every frame
/// in the library lies inside it, the part of it outside the body is confined to the bottom tenth of the
/// frame, and the original engine drew it first as a darkening pass over the background before painting
/// the body over the top. Putting its bytes through the palette instead paints a hard white line around
/// the character, which is the quickest way to notice the mistake.
/// </para>
/// <para>
/// <b>Run coordinates are canvas coordinates, not box-relative.</b> The frame's origin is not stored: it
/// is the smallest coordinate the frame's runs use, taken over both streams together (the coverage stream
/// always starts further left and higher than the body, so it has to be included). Reading the runs as
/// box-relative draws the character 91 px right and 28 px low -- which still looks like a character
/// standing in a scene, so it does not announce itself.
/// </para>
/// </summary>
public sealed class CharacterSpriteAsset : IAnimationAsset
{
    /// <summary>Bytes per frame record.</summary>
    public const int RecordSize = 32;

    /// <summary>Every entry reserves this much for its frame table, however few frames it has.</summary>
    public const int FrameTableSize = 3200;

    /// <summary>How many records the fixed table holds.</summary>
    public const int MaxFrames = FrameTableSize / RecordSize;

    /// <summary>Bytes of header on a body run, before its pixel indices.</summary>
    private const int BodyRunHeader = 6;

    /// <summary>Bytes of header on a coverage run, before its coverage bytes (if any).</summary>
    private const int CoverageRunHeader = 7;

    /// <summary>
    /// How dark the shadow-only part of the coverage mask is drawn, out of 255.
    /// <para>
    /// The engine did not draw it: it pushed the framebuffer through a darker colour table, and that table
    /// is in no container (see <c>docs/formats/executable.md</c>). A standalone image has no framebuffer to
    /// darken, so the strength is this decoder's choice, in the same way the animation frame rate and the
    /// voice sample rate are. It only affects pixels the body does not cover -- the ground shadow and the
    /// soft rim; the character's own pixels always come out at the coverage the file states.
    /// </para>
    /// </summary>
    public const byte ShadowAlpha = 128;

    private readonly byte[] _data;
    private readonly int _dataStart;
    private readonly (int X, int Y)[] _origins;

    private CharacterSpriteAsset(
        byte[] data,
        int dataStart,
        IReadOnlyList<CharacterFrameRecord> records,
        (int X, int Y)[] origins)
    {
        _data = data;
        _dataStart = dataStart;
        Records = records;
        _origins = origins;

        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = 0, y1 = 0;
        for (int i = 0; i < records.Count; i++)
        {
            x0 = Math.Min(x0, origins[i].X);
            y0 = Math.Min(y0, origins[i].Y);
            x1 = Math.Max(x1, origins[i].X + records[i].Width);
            y1 = Math.Max(y1, origins[i].Y + records[i].Height);
        }
        Bounds = records.Count == 0 ? (0, 0, 0, 0) : (x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>The frame records, in order, with the unused tail of the table dropped.</summary>
    public IReadOnlyList<CharacterFrameRecord> Records { get; }

    public int FrameCount => Records.Count;

    /// <summary>The union of every frame's extent, in canvas coordinates.</summary>
    public (int X, int Y, int Width, int Height) Bounds { get; }

    /// <summary>The colour table the body indices resolve through; a grey ramp when none is set.</summary>
    public IndexedPalette? Palette { get; set; }

    /// <summary>Where frame <paramref name="index"/>'s extent sits on the canvas.</summary>
    public (int X, int Y) OriginOf(int index) => _origins[index];

    /// <summary>
    /// Parses one library entry: the 3,200-byte frame table followed by its frame data.
    /// <para>
    /// Returns <see langword="null"/> unless every frame's two streams tile the space between its own
    /// <c>DataOffset</c> and the next frame's exactly, which is the format test -- the same rule the scene
    /// classifier uses. Parsing walks the run headers of every frame anyway, because the canvas is the
    /// union of the frames' extents and the extents are not stored.
    /// </para>
    /// </summary>
    public static CharacterSpriteAsset? Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length <= FrameTableSize)
            return null;

        var records = new List<CharacterFrameRecord>();
        for (int i = 0; i < MaxFrames; i++)
        {
            CharacterFrameRecord r = ReadRecord(data, i * RecordSize);
            if (r == default)
                break;
            records.Add(r);
        }

        if (records.Count == 0 || records[0].DataOffset != 0)
            return null;

        int dataLength = data.Length - FrameTableSize;
        var origins = new (int X, int Y)[records.Count];
        for (int i = 0; i < records.Count; i++)
        {
            CharacterFrameRecord r = records[i];
            int end = i + 1 < records.Count ? records[i + 1].DataOffset : dataLength;
            if (r.Width <= 0 || r.Height <= 0 || r.DataOffset < 0 || end <= r.DataOffset || end > dataLength)
                return null;
            if (!TryWalk(data, FrameTableSize, r, end, out origins[i]))
                return null;
        }

        return new CharacterSpriteAsset(data, FrameTableSize, records, origins);
    }

    /// <summary>
    /// A cheap gate for the classifier: could <paramref name="head"/> be the start of a library entry?
    /// Frame 0's record starts at zero and names a non-empty frame that fits inside the entry.
    /// </summary>
    public static bool CouldBeEntry(ReadOnlySpan<byte> head, long totalSize)
    {
        if (head.Length < RecordSize || totalSize <= FrameTableSize)
            return false;

        CharacterFrameRecord r = ReadRecord(head, 0);
        return r.DataOffset == 0
            && r.BodyRunCount > 0
            && r.EdgeRunCount > 0
            && r.Width is > 0 and <= 4096
            && r.Height is > 0 and <= 4096
            && (long)r.BodyRunCount * BodyRunHeader + (long)r.EdgeRunCount * CoverageRunHeader <= totalSize - FrameTableSize;
    }

    /// <summary>
    /// Decodes one frame to a BGRA image cropped to its own extent, positioned at its canvas origin.
    /// <para>
    /// The body's indices give the colour and the coverage stream gives the alpha, so the character comes
    /// out anti-aliased exactly as the file describes. Where the coverage mask reaches beyond the body --
    /// the ground shadow and the soft rim -- the pixel is black at <see cref="ShadowAlpha"/> scaled by its
    /// coverage, because what the engine did there was darken the scene behind it.
    /// </para>
    /// </summary>
    public SpriteFrame DecodeFrame(int index)
    {
        if (index < 0 || index >= Records.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        CharacterFrameRecord r = Records[index];
        (int ox, int oy) = _origins[index];
        int w = r.Width, h = r.Height;

        IndexedPalette palette = Palette ?? IndexedPalette.Grayscale;
        var coverage = new byte[w * h];
        var pixels = new byte[w * h * 4];
        var painted = new bool[w * h];

        int bodyEnd = ReadBody(_data, _dataStart + r.DataOffset, r.BodyRunCount, (x, y, src, count) =>
        {
            int row = y - oy;
            if (row < 0 || row >= h)
                return;
            for (int i = 0; i < count; i++)
            {
                int col = x - ox + i;
                if (col < 0 || col >= w)
                    continue;
                int p = row * w + col;
                palette.ToBgra(_data[src + i], pixels, p * 4);
                painted[p] = true;
            }
        });

        int frameEnd = index + 1 < Records.Count
            ? _dataStart + Records[index + 1].DataOffset
            : _data.Length;
        ReadCoverage(_data, bodyEnd, frameEnd, (x, y, kind, src, count) =>
        {
            int row = y - oy;
            if (row < 0 || row >= h)
                return;
            for (int i = 0; i < count; i++)
            {
                int col = x - ox + i;
                if (col < 0 || col >= w)
                    continue;
                byte v = kind == 0 ? (byte)255 : _data[src + i];
                int p = row * w + col;
                if (v > coverage[p])
                    coverage[p] = v;
            }
        });

        for (int p = 0; p < coverage.Length; p++)
        {
            byte cov = coverage[p];
            if (painted[p])
                pixels[p * 4 + 3] = cov;
            else if (cov != 0)
                pixels[p * 4 + 3] = (byte)(cov * ShadowAlpha / 255);
        }

        return new SpriteFrame(new DecodedImage(w, h, pixels), ox, oy);
    }

    /// <summary>
    /// Re-checks everything the format asserts and reports the first thing that fails: that each frame's
    /// streams tile it exactly, that the coverage runs number <see cref="CharacterFrameRecord.EdgeRunCount"/>,
    /// that the runs' extent is the declared width and height, and that the character's feet land on one
    /// fixed canvas point throughout the animation. <see langword="null"/> when the entry walks clean.
    /// </summary>
    public string? Verify()
    {
        (int X, int Y)? feet = null;
        for (int i = 0; i < Records.Count; i++)
        {
            CharacterFrameRecord r = Records[i];
            (int ox, int oy) = _origins[i];
            int frameEnd = i + 1 < Records.Count ? _dataStart + Records[i + 1].DataOffset : _data.Length;

            int maxX = 0, maxY = 0, runs = 0;
            int bodyEnd;
            try
            {
                bodyEnd = ReadBody(_data, _dataStart + r.DataOffset, r.BodyRunCount, (x, y, _, count) =>
                {
                    maxX = Math.Max(maxX, x + count);
                    maxY = Math.Max(maxY, y);
                });
                int end = ReadCoverage(_data, bodyEnd, frameEnd, (x, y, _, _, count) =>
                {
                    maxX = Math.Max(maxX, x + count);
                    maxY = Math.Max(maxY, y);
                    runs++;
                });
                if (end != frameEnd)
                    return $"frame {i}: coverage stream ends at {end}, frame ends at {frameEnd}";
            }
            catch (ArgumentOutOfRangeException)
            {
                return $"frame {i}: a run reads past the end of the entry";
            }

            if (runs != r.EdgeRunCount)
                return $"frame {i}: {runs} coverage runs, record says {r.EdgeRunCount}";
            if (maxX - ox != r.Width || maxY - oy + 1 != r.Height)
                return $"frame {i}: runs span {maxX - ox}x{maxY - oy + 1}, record says {r.Width}x{r.Height}";

            (int X, int Y) here = (ox + r.AnchorX, oy + r.AnchorY);
            feet ??= here;
            if (feet != here)
                return $"frame {i}: feet at canvas {here}, earlier frames put them at {feet}";
        }
        return null;
    }

    private static CharacterFrameRecord ReadRecord(ReadOnlySpan<byte> data, int p) => new(
        (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p)),
        (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p + 4)),
        (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p + 8)),
        (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p + 12)),
        (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p + 16)),
        (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p + 20)),
        (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p + 24)),
        (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p + 28)));

    /// <summary>Walks <paramref name="count"/> body runs from <paramref name="p"/>; returns where they end.</summary>
    private static int ReadBody(byte[] data, int p, int count, Action<int, int, int, int> run)
    {
        for (int i = 0; i < count; i++)
        {
            if (p + BodyRunHeader > data.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            int x = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p));
            int y = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 2));
            int n = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 4));
            p += BodyRunHeader;
            if (p + n > data.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            run(x, y, p, n);
            p += n;
        }
        return p;
    }

    /// <summary>Walks coverage runs from <paramref name="p"/> up to <paramref name="end"/>; returns where they end.</summary>
    private static int ReadCoverage(byte[] data, int p, int end, Action<int, int, byte, int, int> run)
    {
        while (p < end)
        {
            if (p + CoverageRunHeader > data.Length)
                throw new ArgumentOutOfRangeException(nameof(end));
            int x = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p));
            int y = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 2));
            byte kind = data[p + 4];
            int n = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 5));
            p += CoverageRunHeader;
            int payload = kind == 0 ? 0 : n;
            if (p + payload > data.Length)
                throw new ArgumentOutOfRangeException(nameof(end));
            run(x, y, kind, p, n);
            p += payload;
        }
        return p;
    }

    /// <summary>
    /// Walks both streams of one frame without decoding pixels, to find its canvas origin and confirm
    /// they tile the frame exactly.
    /// </summary>
    private static bool TryWalk(byte[] data, int dataStart, CharacterFrameRecord r, int end, out (int X, int Y) origin)
    {
        origin = default;
        int minX = int.MaxValue, minY = int.MaxValue;
        void Seen(int x, int y)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
        }

        try
        {
            int bodyEnd = ReadBody(data, dataStart + r.DataOffset, r.BodyRunCount, (x, y, _, _) => Seen(x, y));
            int frameEnd = dataStart + end;
            if (bodyEnd > frameEnd)
                return false;
            int runs = 0;
            int coverageEnd = ReadCoverage(data, bodyEnd, frameEnd, (x, y, _, _, _) =>
            {
                Seen(x, y);
                runs++;
            });
            if (coverageEnd != frameEnd || runs != r.EdgeRunCount || minX == int.MaxValue)
                return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        origin = (minX, minY);
        return true;
    }
}
