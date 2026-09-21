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
    public static EntryClassification Classify(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (SpriteAsset.Parse(data) is { } sprite)
        {
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

        if (RasterDecoder.Detect(data) is { } raster)
        {
            return new EntryClassification(EntryKind.Background, new ImageInfo
            {
                Width = raster.Width,
                Height = raster.Height,
                Sharpness = raster.Sharpness,
                IsMask = raster.IsMask,
            });
        }

        if (RleMaskDecoder.Detect(data) is { } mask)
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
