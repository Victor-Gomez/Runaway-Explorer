using System.Buffers.Binary;
using RunawayExplorer.Core.Formats;

namespace RunawayExplorer.Core.FileSystem;

/// <summary>What one scene-archive entry turned out to be.</summary>
public sealed record EntryClassification(EntryKind Kind, ImageInfo? Image)
{
    public static readonly EntryClassification DataEntry = new(EntryKind.Data, null);
}

/// <summary>
/// Decides which of the three image formats -- or none -- a scene-archive entry is. The order matters
/// and each test is a structural check on the bytes, not a guess:
/// <list type="number">
///   <item><description>sprite: the first record's frame offset is 0 and frame 0 walks exactly to frame 1;</description></item>
///   <item><description>overlay: the row records consume the entry byte-exact;</description></item>
///   <item><description>raster: the entry is exactly <c>W × H × 2</c> bytes for a sharply detected width.</description></item>
/// </list>
/// Anything else is data.
/// </summary>
public static class EntryClassifier
{
    public static EntryClassification Classify(byte[] data, int? sceneWidth = null, int? sceneHeight = null, GameVersion gameVersion = GameVersion.Runaway1)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (gameVersion == GameVersion.HollywoodMonsters)
            return ClassifyIndexed(data, sceneWidth, sceneHeight);

        // PNG image check (used in TNBT and Yesterday)
        if (data.Length >= 24 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
            data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
        {
            int width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16));
            int height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20));
            if (width > 0 && height > 0)
            {
                byte colorType = data.Length >= 26 ? data[25] : (byte)2;
                if (colorType is 0 or 4) // Grayscale or Grayscale + Alpha
                {
                    return new EntryClassification(EntryKind.Mask, new ImageInfo
                    {
                        Width = width,
                        Height = height,
                        IsMask = true,
                    });
                }

                bool isBg;
                if (gameVersion is GameVersion.TheNextBigThing or GameVersion.Yesterday)
                {
                    // In Yesterday and TNBT, scene backgrounds are opaque full-scene rasters (JPEG or 24-bit RGB PNG).
                    // RGBA PNGs (colorType 6) have transparency and are object sprites / overlays, never backgrounds.
                    isBg = colorType == 2 && (sceneWidth is null ? (width >= 1920 && height >= 1080) : (width == sceneWidth && height == sceneHeight));
                }
                else
                {
                    isBg = sceneWidth is null || (width == sceneWidth && height == sceneHeight);
                }

                return new EntryClassification(isBg ? EntryKind.Background : EntryKind.Overlay, new ImageInfo
                {
                    Width = width,
                    Height = height,
                });
            }
        }

        // JPEG image check (used in TNBT and Yesterday)
        if (JpegDecoder.TryGetDimensions(data, out int jw, out int jh))
        {
            bool isBg = sceneWidth is null || (jw == sceneWidth && jh == sceneHeight);
            return new EntryClassification(isBg ? EntryKind.Background : EntryKind.Overlay, new ImageInfo
            {
                Width = jw,
                Height = jh,
            });
        }

        if (SpriteAsset.Parse(data) is { } sprite)
        {
            if (sprite.FrameCount == 1)
            {
                return new EntryClassification(EntryKind.Overlay, new ImageInfo
                {
                    X = sprite.Bounds.X,
                    Y = sprite.Bounds.Y,
                    Width = sprite.Bounds.Width,
                    Height = sprite.Bounds.Height,
                    Frames = 1,
                });
            }

            return new EntryClassification(EntryKind.Animation, new ImageInfo
            {
                X = sprite.Bounds.X,
                Y = sprite.Bounds.Y,
                Width = sprite.Bounds.Width,
                Height = sprite.Bounds.Height,
                Frames = sprite.FrameCount,
            });
        }

        if (OverlayDecoder.Parse(data) is { } overlay)
        {
            return new EntryClassification(EntryKind.Overlay, new ImageInfo
            {
                X = overlay.X,
                Y = overlay.Y,
                Width = overlay.Width,
                Height = overlay.Height,
            });
        }

        if (gameVersion is not (GameVersion.TheNextBigThing or GameVersion.Yesterday) && RasterDecoder.Detect(data) is { } raster)
        {
            return new EntryClassification(EntryKind.Background, new ImageInfo
            {
                Width = raster.Width,
                Height = raster.Height,
                Sharpness = raster.Sharpness,
                IsMask = raster.IsMask,
            });
        }

        if (RleMaskDecoder.Detect(data, sceneWidth, sceneHeight) is { } mask)
        {
            return new EntryClassification(EntryKind.Mask, new ImageInfo
            {
                Width = mask.Width,
                Height = mask.Height,
                IsMask = true,
            });
        }

        if (SparseMaskDecoder.Detect(data, sceneWidth, sceneHeight) is { } sparseMask)
        {
            return new EntryClassification(EntryKind.Mask, new ImageInfo
            {
                Width = sparseMask.Width,
                Height = sparseMask.Height,
                IsMask = true,
            });
        }

        if (SpanMaskDecoder.Detect(data) is { } spanMask)
        {
            return new EntryClassification(EntryKind.Mask, spanMask);
        }

        return EntryClassification.DataEntry;
    }

    /// <summary>
    /// Hollywood Monsters' scene entries. Same three image formats as Runaway 1, but with one palette
    /// index per pixel instead of an RGB565 pair, plus a fourth kind the Runaway games do not have: the
    /// scene's own palette block. Palettes are tested first because a block of 6-bit VGA triples is also
    /// a whole number of 3-byte RLE mask runs, and would otherwise be claimed by the mask decoder.
    /// </summary>
    private static EntryClassification ClassifyIndexed(byte[] data, int? sceneWidth, int? sceneHeight)
    {
        if (IndexedPalette.IsPaletteBlock(data))
            return EntryClassification.DataEntry;

        if (RasterDecoder.DetectIndexed(data) is { } raster)
        {
            return new EntryClassification(EntryKind.Background, new ImageInfo
            {
                Width = raster.Width,
                Height = raster.Height,
                Sharpness = raster.Sharpness,
            });
        }

        if (SpriteAsset.Parse(data, bytesPerPixel: 1) is { } sprite)
        {
            var info = new ImageInfo
            {
                X = sprite.Bounds.X,
                Y = sprite.Bounds.Y,
                Width = sprite.Bounds.Width,
                Height = sprite.Bounds.Height,
                Frames = sprite.FrameCount,
            };
            return new EntryClassification(sprite.FrameCount == 1 ? EntryKind.Overlay : EntryKind.Animation, info);
        }

        if (OverlayDecoder.Parse(data, bytesPerPixel: 1) is { } overlay)
        {
            return new EntryClassification(EntryKind.Overlay, new ImageInfo
            {
                X = overlay.X,
                Y = overlay.Y,
                Width = overlay.Width,
                Height = overlay.Height,
            });
        }

        if (RleMaskDecoder.Detect(data, sceneWidth, sceneHeight) is { } mask)
        {
            return new EntryClassification(EntryKind.Mask, new ImageInfo
            {
                Width = mask.Width,
                Height = mask.Height,
                IsMask = true,
            });
        }

        return EntryClassification.DataEntry;
    }
}
