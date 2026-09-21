namespace RunawayExplorer.Core.Formats;

/// <summary>Result of <see cref="RasterDecoder.Detect"/>: the geometry of a headerless raster, without decoding it.</summary>
public readonly record struct RasterInfo(int Width, int Height, double Sharpness, bool IsMask);

/// <summary>
/// Format 1 of the scene archives: a headerless block of exactly <c>W × H × 2</c> bytes of RGB565.
/// The width is not stored anywhere. It is recovered as the stride at which vertically adjacent pixels
/// agree best -- <c>score(W) = mean |g[i] − g[i+W]|</c> over the green channel has a razor-sharp minimum
/// at the true width -- and must then divide the pixel count exactly.
/// </summary>
public static class RasterDecoder
{
    public const int WidthMin = 100;
    public const int WidthMax = 4000;

    /// <summary>Entries smaller than this are never rasters (the smallest real one is 204×120).</summary>
    public const int MinBytes = 8_000;

    /// <summary>Pixels used for the coarse stride sweep. ~60 rows of a 1024-wide image is plenty for the minimum to stand out.</summary>
    private const int CoarseSamplePixels = 60_000;

    /// <summary>Pixels used to re-score the handful of candidate widths the coarse sweep surfaced.</summary>
    private const int FineSamplePixels = 400_000;

    /// <summary>sharpness = median(sweep) / best. Real images score 5–17; something that is not an image ~1.1.</summary>
    public const double SharpnessMin = 2.5;

    /// <summary>
    /// Mask layers (walk-behind / hotspot indices) have index values in the pixel slots, so neighbouring
    /// pixels land in unrelated colours. Artwork scores 0–10 here, masks 77+.
    /// </summary>
    public const double MaskDisagreementMin = 60.0;

    /// <summary>
    /// Works out whether <paramref name="data"/> is a raster and, if so, its size. Cheap enough to run
    /// at scan time; nothing is decoded.
    /// </summary>
    public static RasterInfo? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length < MinBytes || data.Length % 2 != 0)
            return null;

        int total = data.Length / 2;

        // Exactly one full screen needs no detection.
        const int screen = Rgb565.ScreenWidth * Rgb565.ScreenHeight;
        if (total == screen)
        {
            return new RasterInfo(Rgb565.ScreenWidth, Rgb565.ScreenHeight, 99.0, IsMaskLayer(data, Rgb565.ScreenWidth, Rgb565.ScreenHeight));
        }

        (int w0, double sharp)? detected = DetectStride(data);
        if (detected is { } d && d.sharp >= SharpnessMin)
        {
            // The detector can be off by a pixel or two; the true width divides the pixel count.
            foreach (int w in CandidatesAround(d.w0))
            {
                if (w >= WidthMin && total % w == 0 && total / w >= 4)
                {
                    int fundamentalW = ResolveFundamentalWidth(data, w, total);
                    int h = total / fundamentalW;
                    return new RasterInfo(fundamentalW, h, d.sharp, IsMaskLayer(data, fundamentalW, h));
                }
            }
        }

        // Fallback for one or more stacked full screens that are too flat to defeat stride detection (e.g. solid title cards).
        if (total % screen == 0)
        {
            int h = total / Rgb565.ScreenWidth;
            return new RasterInfo(Rgb565.ScreenWidth, h, 99.0, IsMaskLayer(data, Rgb565.ScreenWidth, h));
        }

        return null;
    }

    /// <summary>Decodes a raster whose geometry <see cref="Detect"/> already established.</summary>
    public static DecodedImage Decode(ReadOnlySpan<byte> data, int width, int height)
    {
        if (data.Length != width * height * 2)
            throw new ArgumentException($"Expected {width * height * 2} bytes for {width}x{height}, got {data.Length}.");

        var pixels = new byte[width * height * 4];
        Rgb565.CopyRow(data, 0, pixels, 0, width * height);
        return new DecodedImage(width, height, pixels);
    }

    /// <summary>Detect + decode in one step. <see langword="null"/> when the bytes are not a raster.</summary>
    public static (DecodedImage Image, RasterInfo Info)? TryDecode(ReadOnlySpan<byte> data)
    {
        if (Detect(data) is not { } info)
            return null;
        return (Decode(data, info.Width, info.Height), info);
    }

    private static IEnumerable<int> CandidatesAround(int w0)
    {
        yield return w0;
        for (int delta = 1; delta <= 3; delta++)
        {
            yield return w0 - delta;
            yield return w0 + delta;
        }
    }

    /// <summary>
    /// The lag at which vertically adjacent pixels agree best, and how sharply. A coarse sweep over
    /// every lag with a small sample finds the neighbourhood; the near-minimum candidates are then
    /// re-scored with a large sample so the pick is as reliable as a full sweep would be, at a fraction
    /// of the cost (the full sweep is 760 M subtractions per image, which is too slow for a scan).
    /// </summary>
    public static (int Width, double Sharpness)? DetectStride(ReadOnlySpan<byte> data)
    {
        int totalPixels = data.Length / 2;
        int start = FindActiveRegionStart(data, totalPixels, FineSamplePixels);
        int nFine = Math.Min(totalPixels - start, FineSamplePixels);
        if (nFine < 2000)
            return null;

        var g = new short[nFine];
        for (int i = 0; i < nFine; i++)
            g[i] = (short)Rgb565.Green(data, (start + i) * 2);

        int wmax = Math.Min(WidthMax, Math.Max(WidthMin + 1, totalPixels / 40));
        if (wmax <= WidthMin)
            return null;

        // The coarse sample is three chunks spread over the fine range rather than its first rows:
        // the top of a scene is often flat sky, which would make every lag score low and the
        // sharpness ratio meaningless.
        int lagCount = wmax - WidthMin + 1;
        int chunkLen = Math.Min(nFine, CoarseSamplePixels / CoarseChunks);
        var chunkStarts = new int[CoarseChunks];
        for (int c = 0; c < CoarseChunks; c++)
            chunkStarts[c] = CoarseChunks == 1 ? 0 : (int)((long)(nFine - chunkLen) * c / (CoarseChunks - 1));

        var coarse = new double[lagCount];
        for (int li = 0; li < lagCount; li++)
        {
            long sum = 0, count = 0;
            foreach (int cs in chunkStarts)
                AccumulateAbsLagDiff(g, cs, chunkLen, WidthMin + li, ref sum, ref count);
            coarse[li] = count == 0 ? double.MaxValue : sum / (double)count;
        }

        double best = double.MaxValue;
        foreach (double s in coarse)
            best = Math.Min(best, s);
        if (best <= 0 || best == double.MaxValue)
            return null;

        var sorted = (double[])coarse.Clone();
        Array.Sort(sorted);
        double median = sorted[sorted.Length / 2];

        // Re-score everything within 2% of the coarse minimum with the big sample, then pick the
        // smallest score (ties: smallest width), as the reference implementation does.
        int pick = -1;
        double pickScore = double.MaxValue;
        for (int li = 0; li < lagCount; li++)
        {
            if (coarse[li] > best * 1.02)
                continue;
            long sum = 0, count = 0;
            AccumulateAbsLagDiff(g, 0, nFine, WidthMin + li, ref sum, ref count);
            double fine = count == 0 ? double.MaxValue : sum / (double)count;
            if (fine < pickScore)
            {
                pickScore = fine;
                pick = WidthMin + li;
            }
        }

        if (pick < 0 || pickScore == double.MaxValue)
            return null;

        return (pick, median / best);
    }

    private const int CoarseChunks = 3;

    private static int FindActiveRegionStart(ReadOnlySpan<byte> data, int totalPixels, int sampleLength)
    {
        // If the entry begins with extensive uniform rows (e.g. solid black padding before end credits),
        // skip past them so the stride detector samples regions with actual artwork signal.
        ushort first = Rgb565.Read(data, 0);
        int flatCount = 0;
        for (int i = 0; i < totalPixels; i++)
        {
            if (Rgb565.Read(data, i * 2) != first)
                break;
            flatCount++;
        }

        if (flatCount >= 2000)
        {
            return Math.Max(0, Math.Min(flatCount, totalPixels - sampleLength));
        }

        return 0;
    }

    internal static int ResolveFundamentalWidth(ReadOnlySpan<byte> data, int detectedW, int totalPixels)
    {
        // Harmonic / octave check: if detectedW is an exact multiple (e.g. 2x) of a smaller divisor of totalPixels,
        // test whether the smaller divisor also scores very low diff (fundamental period vs octave doubling).
        int start = FindActiveRegionStart(data, totalPixels, FineSamplePixels);
        int nSample = Math.Min(totalPixels - start, 100_000);

        foreach (int factor in new[] { 2, 3 })
        {
            if (detectedW % factor != 0)
                continue;

            int subW = detectedW / factor;
            if (subW < Rgb565.ScreenWidth || totalPixels % subW != 0)
                continue;

            double scoreSub = ScoreStride(data, start, nSample, subW);
            double scoreDetected = ScoreStride(data, start, nSample, detectedW);

            if (scoreSub <= scoreDetected * 1.35)
                return subW;
        }

        return detectedW;
    }

    private static double ScoreStride(ReadOnlySpan<byte> data, int start, int length, int stride)
    {
        int n = length - stride;
        if (n <= 0)
            return double.MaxValue;

        long sum = 0;
        for (int i = 0; i < n; i++)
        {
            int g1 = Rgb565.Green(data, (start + i) * 2);
            int g2 = Rgb565.Green(data, (start + i + stride) * 2);
            sum += Math.Abs(g1 - g2);
        }

        return (double)sum / n;
    }

    private static void AccumulateAbsLagDiff(short[] g, int start, int length, int lag, ref long sum, ref long count)
    {
        int n = length - lag;
        if (n <= 0)
            return;

        ReadOnlySpan<short> a = g.AsSpan(start, n);
        ReadOnlySpan<short> b = g.AsSpan(start + lag, n);
        long s = 0;
        for (int i = 0; i < n; i++)
            s += Math.Abs(a[i] - b[i]);
        sum += s;
        count += n;
    }

    /// <summary>
    /// Mean over horizontally adjacent pixel pairs of (max − min) of the three per-channel absolute
    /// differences. Artwork changes all channels together; an index layer does not.
    /// </summary>
    internal static double ChannelDisagreement(ReadOnlySpan<byte> data, int width, int height)
    {
        long sum = 0;
        long pairs = 0;
        for (int y = 0; y < height; y++)
        {
            int row = y * width * 2;
            for (int x = 0; x + 1 < width; x++)
            {
                ushort p = Rgb565.Read(data, row + x * 2);
                ushort q = Rgb565.Read(data, row + (x + 1) * 2);
                int dr = Math.Abs(((p >> 11) << 3) - ((q >> 11) << 3));
                int dg = Math.Abs((((p >> 5) & 0x3F) << 2) - (((q >> 5) & 0x3F) << 2));
                int db = Math.Abs(((p & 0x1F) << 3) - ((q & 0x1F) << 3));
                sum += Math.Max(dr, Math.Max(dg, db)) - Math.Min(dr, Math.Min(dg, db));
                pairs++;
            }
        }
        return pairs == 0 ? 0 : sum / (double)pairs;
    }

    private static bool IsMaskLayer(ReadOnlySpan<byte> data, int width, int height) =>
        ChannelDisagreement(data, width, height) >= MaskDisagreementMin;
}