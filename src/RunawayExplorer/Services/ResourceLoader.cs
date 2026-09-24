using System.IO;
using System.Text;
using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Formats;
using RunawayExplorer.Core.Settings;

namespace RunawayExplorer.Services;

/// <summary>
/// Turns a tree node into something a viewer can show. Dispatches on <see cref="FsNode.Kind"/> -- the
/// classification made at scan time -- rather than re-probing the bytes, so what the tree says an entry
/// is and what the viewer shows can't disagree. Every decode failure becomes an <see cref="ErrorResource"/>
/// rather than an exception, so one bad entry never takes the window down.
/// </summary>
public static class ResourceLoader
{
    public static ResourceContent Load(FsNode node, VirtualFileSystem vfs, AppSettings settings, TempFileTracker tempFiles)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(vfs);

        bool indexed = vfs.GameVersion == GameVersion.HollywoodMonsters;
        int bytesPerPixel = indexed ? 1 : 2;

        try
        {
            switch (node.Kind)
            {
                case EntryKind.Background:
                {
                    byte[] data = vfs.ReadBytes(node);
                    if (indexed)
                    {
                        ImageInfo? bgInfo = node.Image;
                        if (bgInfo is null || (long)bgInfo.Width * bgInfo.Height != data.Length)
                        {
                            if (RasterDecoder.DetectIndexed(data) is not { } detected)
                                return new ErrorResource($"'{node.GetPath()}' no longer decodes as an indexed raster.");
                            bgInfo = new ImageInfo { Width = detected.Width, Height = detected.Height };
                        }
                        IndexedPalette bgPalette = vfs.ScenePaletteFor(node) ?? IndexedPalette.Grayscale;
                        return new ImageResource(
                            RasterDecoder.DecodeIndexed(data, bgInfo.Width, bgInfo.Height, bgPalette),
                            0, 0, false, "background");
                    }
                    if (PngDecoder.IsPng(data))
                    {
                        if (PngDecoder.Decode(data) is { } pngImg)
                            return new ImageResource(pngImg, 0, 0, false, "background");
                        return new ErrorResource($"'{node.GetPath()}' failed to decode as PNG.");
                    }
                    if (JpegDecoder.IsJpeg(data))
                    {
                        if (JpegDecoder.Decode(data) is { } jpgImg)
                            return new ImageResource(jpgImg, 0, 0, false, "background");
                        return new ErrorResource($"'{node.GetPath()}' failed to decode as JPEG.");
                    }
                    ImageInfo? info = node.Image;
                    if (info is null || info.Width * info.Height * 2 != data.Length)
                    {
                        // The scan said raster but the geometry is missing or stale: detect again.
                        if (RasterDecoder.TryDecode(data) is not { } re)
                            return new ErrorResource($"'{node.GetPath()}' no longer decodes as a raster.");
                        return new ImageResource(re.Image, 0, 0, false, re.Info.IsMask ? "mask layer" : "background");
                    }
                    DecodedImage image = RasterDecoder.Decode(data, info.Width, info.Height);
                    return new ImageResource(image, 0, 0, false, info.IsMask ? "mask layer" : "background");
                }

                case EntryKind.Mask:
                {
                    byte[] data = vfs.ReadBytes(node);
                    if (PngDecoder.IsPng(data))
                    {
                        if (PngDecoder.Decode(data) is { } pngImg)
                            return new ImageResource(pngImg, node.Image?.X ?? 0, node.Image?.Y ?? 0, true, "mask layer");
                        return new ErrorResource($"'{node.GetPath()}' failed to decode as PNG mask.");
                    }
                    byte[]? table1536 = vfs.SceneAttributeTableFor(node);
                    byte[]? idMap = table1536 is not null ? RleMaskDecoder.ExtractObjectMapping(table1536) : null;
                    ImageInfo? info = node.Image;

                    int? sceneW = info?.Width;
                    int? sceneH = info?.Height;
                    if (sceneW is null)
                    {
                        var bg = vfs.SceneBackgroundFor(node);
                        if (bg is not null)
                        {
                            sceneW = bg.Width;
                            sceneH = bg.Height;
                        }
                    }

                    if (SparseMaskDecoder.Detect(data, sceneW, sceneH) is { } smInfo)
                    {
                        int w = sceneW ?? smInfo.Width;
                        int h = sceneH ?? smInfo.Height;
                        DecodedImage img = SparseMaskDecoder.Decode(data, w, h, idMap, colorSeed: node.EntryIndex);
                        return new ImageResource(img, 0, 0, true, "scene mask");
                    }

                    if (SpanMaskDecoder.IsSpanMask(data))
                    {
                        DecodedImage img = SpanMaskDecoder.Decode(data);
                        return new ImageResource(img, info?.X ?? 0, info?.Y ?? 0, true, "polygon mask");
                    }

                    if (info is null)
                    {
                        if (RleMaskDecoder.TryDecode(data, idMap, sceneW, sceneH, table1536) is not { } md)
                            return new ErrorResource($"'{node.GetPath()}' no longer parses as an RLE scene mask.");
                        return new ImageResource(md.Image, 0, 0, true, "scene mask");
                    }
                    DecodedImage image = RleMaskDecoder.Decode(data, info.Width, info.Height, idMap, table1536);
                    return new ImageResource(image, 0, 0, true, "scene mask");
                }

                case EntryKind.Overlay:
                {
                    byte[] data = vfs.ReadBytes(node);
                    if (PngDecoder.IsPng(data))
                    {
                        if (PngDecoder.Decode(data) is { } pngImg)
                        {
                            bool hasBg = vfs.SceneBackgroundFor(node) is not null;
                            int x = node.Image?.X ?? 0;
                            int y = node.Image?.Y ?? 0;
                            bool positioned = hasBg || x != 0 || y != 0;
                            string kind = positioned ? "overlay" : "sprite";
                            return new ImageResource(pngImg, x, y, positioned, kind);
                        }
                        return new ErrorResource($"'{node.GetPath()}' failed to decode as PNG.");
                    }
                    if (JpegDecoder.IsJpeg(data))
                    {
                        if (JpegDecoder.Decode(data) is { } jpgImg)
                            return new ImageResource(jpgImg, node.Image?.X ?? 0, node.Image?.Y ?? 0, true, "overlay");
                        return new ErrorResource($"'{node.GetPath()}' failed to decode as JPEG.");
                    }
                    if (SpriteAsset.Parse(data, bytesPerPixel) is { } sprite1)
                    {
                        sprite1.Palette = vfs.ScenePaletteFor(node);
                        var frame = sprite1.DecodeFrame(0);
                        if (frame.Image is null)
                            return new ErrorResource($"'{node.GetPath()}' has empty overlay frame.");
                        return new ImageResource(frame.Image, frame.X, frame.Y, true, "overlay");
                    }
                    if (OverlayDecoder.TryDecode(data, bytesPerPixel, vfs.ScenePaletteFor(node)) is not { } ov)
                        return new ErrorResource($"'{node.GetPath()}' no longer parses as an overlay.");
                    return new ImageResource(ov.Image, ov.Info.X, ov.Info.Y, true, ov.Info.IsRectangular ? "overlay (rectangular)" : "overlay");
                }

                case EntryKind.Animation:
                {
                    byte[] data = vfs.ReadBytes(node);
                    SpriteAsset? asset = SpriteAsset.Parse(data, bytesPerPixel);
                    if (asset is null)
                        return new ErrorResource($"'{node.GetPath()}' no longer parses as a sprite animation.");
                    asset.Palette = vfs.ScenePaletteFor(node);
                    return new AnimationResource(asset);
                }

                case EntryKind.Music:
                case EntryKind.Ambient:
                case EntryKind.Cinematic:
                case EntryKind.Voice:
                {
                    AudioInfo pcm = node.Audio ?? VirtualFileSystem.AmbientPcm;
                    // The adjustable voice rate exists because Runaway's lines carry none. Hollywood Monsters'
                    // do not carry one either, but its rate is known (22,050 Hz 8-bit mono), so leave it alone.
                    if (node.Kind == EntryKind.Voice && pcm.Format == AudioFormat.RawPcm
                        && vfs.GameVersion != GameVersion.HollywoodMonsters)
                        pcm = new AudioInfo { Format = AudioFormat.RawPcm, SampleRate = settings.VoiceSampleRate, Channels = pcm.Channels, BitsPerSample = pcm.BitsPerSample };

                    if (pcm.Format == AudioFormat.Mp3)
                    {
                        string path = tempFiles.CreateTempFile(".mp3");
                        using (Stream src = vfs.OpenFile(node))
                        using (FileStream dst = File.Create(path))
                        {
                            src.CopyTo(dst);
                        }
                        return new SoundResource(path, pcm, pcm.DurationSeconds(node.Size));
                    }
                    else if (pcm.Format == AudioFormat.Wav)
                    {
                        string path = tempFiles.CreateTempFile(".wav");
                        using (Stream src = vfs.OpenFile(node))
                        using (FileStream dst = File.Create(path))
                        {
                            src.CopyTo(dst);
                        }
                        return new SoundResource(path, pcm, pcm.DurationSeconds(node.Size));
                    }
                    else
                    {
                        string path = tempFiles.CreateTempFile(".wav");
                        using (Stream src = vfs.OpenFile(node))
                        using (FileStream dst = File.Create(path))
                        {
                            dst.Write(WavWriter.Header((int)Math.Min(node.Size, int.MaxValue), pcm.SampleRate, pcm.Channels, pcm.BitsPerSample));
                            src.CopyTo(dst);
                        }
                        return new SoundResource(path, pcm, pcm.DurationSeconds(node.Size));
                    }
                }

                case EntryKind.Video:
                {
                    string path = tempFiles.CreateTempFile(".bik");
                    using (FileStream dst = File.Create(path))
                    {
                        if (!vfs.RestoreVideo(node, dst))
                            return new ErrorResource($"No header for '{node.Name}' in the DATAVC00 keyfile; the video cannot be restored.");
                    }
                    return new VideoResource(path);
                }

                case EntryKind.Viseme:
                {
                    byte[] data = vfs.ReadBytes(node);
                    var sb = new StringBuilder();
                    sb.Append("Lip-sync track ").Append(node.EntryIndex).Append(": ").Append(data.Length)
                      .Append(" tick(s), one mouth shape (0-5) per animation tick. Pairs with voice clip VOICE_")
                      .Append(node.EntryIndex.ToString("00000")).Append(".\n\n");
                    sb.Append(VisemeArchive.ToText(data));
                    return new TextResource(sb.ToString());
                }

                case EntryKind.Dialogue:
                {
                    var sb = new StringBuilder();
                    if (indexed)
                    {
                        // Hollywood Monsters' script stores the pairing rather than implying it from the
                        // row number, and it names a recording for only some of its rows.
                        sb.Append("Script row ").Append(node.Name);
                        if (node.Parent is { } stage)
                            sb.Append(" of ").Append(stage.Name);
                        sb.Append(node.VoiceClipIndex >= 0
                            ? $"\nPairs with voice clip: {node.VoiceClipIndex:00000}\n\n"
                            : "\nNo voice clip is cued for this row.\n\n");
                    }
                    else
                    {
                        sb.Append("Dialogue phrase ").Append(node.Name);
                        if (node.EntryIndex >= 0)
                        {
                            sb.Append(" (index ").Append(node.EntryIndex)
                              .Append(")\nPairs with voice clip: VOICE_").Append(node.EntryIndex.ToString("00000")).Append("\n\n");
                        }
                        else
                        {
                            sb.Append("\n\n");
                        }
                    }

                    sb.Append(node.Subtitle ?? "(empty)");
                    return new TextResource(sb.ToString());
                }

                case EntryKind.Data:
                case EntryKind.GlobalData:
                case EntryKind.RawFile:
                default:
                {
                    using Stream s = vfs.OpenFile(node);
                    int take = (int)Math.Min(s.Length, RawFormat.MaxDumpBytes);
                    var head = new byte[take];
                    s.ReadExactly(head);
                    byte[]? fullData = node.Kind == EntryKind.Data && node.Size == 1536 ? vfs.ReadBytes(node) : null;
                    string header = DescribeRaw(node, s.Length, fullData, indexed);
                    return new TextResource(header + RawFormat.ToHexDump(head) +
                        (s.Length > take ? $"\n... {s.Length - take:N0} more byte(s) not shown.\n" : string.Empty));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Exception($"Load '{node.GetPath()}'", ex);
            return new ErrorResource($"Failed to load '{node.GetPath()}':\n{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>What a scene archive holds, for the folder view.</summary>
    public static SceneResource LoadScene(FsNode archive, VirtualFileSystem vfs)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(vfs);

        int backgrounds = 0, overlays = 0, animations = 0, frames = 0, data = 0;
        foreach (FsNode child in archive.Children)
        {
            switch (child.Kind)
            {
                case EntryKind.Background: backgrounds++; break;
                case EntryKind.Overlay: overlays++; break;
                case EntryKind.Animation: animations++; frames += child.Image?.Frames ?? 0; break;
                default: data++; break;
            }
        }

        DecodedImage? background = null;
        try
        {
            background = vfs.SceneBackgroundFor(archive);
        }
        catch (Exception ex)
        {
            Log.Exception($"Scene background for '{archive.GetPath()}'", ex);
        }

        string summary = $"{archive.Name}: {backgrounds} background(s), {overlays} overlay(s), {animations} animation(s) with {frames} frame(s), {data} data entr{(data == 1 ? "y" : "ies")}";
        return new SceneResource(background, summary, archive);
    }

    private static string DescribeRaw(FsNode node, long length, byte[]? fullData = null, bool indexed = false)
    {
        string what = node.Kind switch
        {
            EntryKind.Data when length == 1536 => "Scene-archive data entry: 1,536-byte Scene Attribute Table (6 parallel 256-byte lookup tables indexed by Mask ID 0..255).",
            EntryKind.Data when indexed && IsPaletteSized(length) => "Scene-archive palette block: 6-bit VGA triples filling the bottom of the 256-colour table. Scenes that ship 176 colours take the top 80 from RESOURCE.000.",
            EntryKind.Data => "Scene-archive data entry. Not an image: one of the per-scene tables whose purpose is still open (see docs/formats).",
            EntryKind.GlobalData => "RESOURCE.000 entry: fonts, the UI atlas or a localised text bitmap. These use codecs the viewer does not decode yet (see docs/formats §3.4-3.6).",
            EntryKind.RawFile => "No decoder claims this file; showing its bytes.",
            _ => "Raw bytes.",
        };
        string baseDesc = $"{what}\n{length:N0} byte(s).\n\n";
        if (fullData is not null)
        {
            string breakdown = FormatAttributeTableBreakdown(fullData);
            if (!string.IsNullOrEmpty(breakdown))
                baseDesc += breakdown + "\n\nRaw hex dump:\n";
        }
        return baseDesc;
    }

    private static bool IsPaletteSized(long length) =>
        length is >= IndexedPalette.MinBlockBytes and <= IndexedPalette.FullBlockBytes &&
        length % IndexedPalette.BytesPerColor == 0;

    private static string FormatAttributeTableBreakdown(byte[] data)
    {
        int nonZero = 0;
        for (int i = 0; i < Math.Min(data.Length, 1536); i++)
            if (data[i] != 0) nonZero++;

        if (nonZero == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("Active Zones (non-zero attributes in mask lookup):");
        sb.AppendLine("Mask ID | Walkbox | Hotspot | Facing | Depth Scale | Light Tint | Footstep Material");
        sb.AppendLine("--------+---------+---------+--------+-------------+------------+------------------");

        for (int id = 0; id < 256; id++)
        {
            byte walk = data[id];
            byte hot = data.Length > 256 + id ? data[256 + id] : (byte)0;
            byte face = data.Length > 512 + id ? data[512 + id] : (byte)0;
            byte depth = data.Length > 768 + id ? data[768 + id] : (byte)0;
            byte light = data.Length > 1024 + id ? data[1024 + id] : (byte)0;
            byte mat = data.Length > 1280 + id ? data[1280 + id] : (byte)0;

            if (walk == 0 && hot == 0 && face == 0 && depth == 0 && light == 0 && mat == 0)
                continue;

            string matName = mat switch
            {
                0 => "Default",
                1 => "Concrete/Stone",
                2 => "Wood",
                3 => "Dirt/Ground",
                4 => "Metal",
                5 => "Water",
                _ => $"Material {mat}"
            };

            sb.AppendLine($"  {id,3}   |   {walk,3}   |   {hot,3}   |  {face,3}   |     {depth,3}     |    {light,3}     | {matName}");
        }

        return sb.ToString();
    }
}
