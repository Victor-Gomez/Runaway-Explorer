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

        try
        {
            switch (node.Kind)
            {
                case EntryKind.Background:
                {
                    byte[] data = vfs.ReadBytes(node);
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

                    if (info is null)
                    {
                        if (RleMaskDecoder.TryDecode(data, idMap, sceneW, sceneH) is not { } md)
                            return new ErrorResource($"'{node.GetPath()}' no longer parses as an RLE scene mask.");
                        return new ImageResource(md.Image, 0, 0, true, "scene mask");
                    }
                    DecodedImage image = RleMaskDecoder.Decode(data, info.Width, info.Height, idMap);
                    return new ImageResource(image, 0, 0, true, "scene mask");
                }

                case EntryKind.Overlay:
                {
                    byte[] data = vfs.ReadBytes(node);
                    if (OverlayDecoder.TryDecode(data) is not { } ov)
                        return new ErrorResource($"'{node.GetPath()}' no longer parses as an overlay.");
                    return new ImageResource(ov.Image, ov.Info.X, ov.Info.Y, true, ov.Info.IsRectangular ? "overlay (rectangular)" : "overlay");
                }

                case EntryKind.Animation:
                {
                    byte[] data = vfs.ReadBytes(node);
                    SpriteAsset? asset = SpriteAsset.Parse(data);
                    if (asset is null)
                        return new ErrorResource($"'{node.GetPath()}' no longer parses as a sprite animation.");
                    return new AnimationResource(asset);
                }

                case EntryKind.Music:
                case EntryKind.Ambient:
                case EntryKind.Cinematic:
                case EntryKind.Voice:
                {
                    AudioInfo pcm = node.Audio ?? VirtualFileSystem.AmbientPcm;
                    if (node.Kind == EntryKind.Voice && pcm.Format == AudioFormat.RawPcm)
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

                case EntryKind.Data:
                case EntryKind.GlobalData:
                case EntryKind.RawFile:
                default:
                {
                    using Stream s = vfs.OpenFile(node);
                    int take = (int)Math.Min(s.Length, RawFormat.MaxDumpBytes);
                    var head = new byte[take];
                    s.ReadExactly(head);
                    string header = DescribeRaw(node, s.Length);
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

    private static string DescribeRaw(FsNode node, long length)
    {
        string what = node.Kind switch
        {
            EntryKind.Data => "Scene-archive data entry. Not an image: one of the per-scene tables whose purpose is still open (see docs/formats).",
            EntryKind.GlobalData => "RESOURCE.000 entry: fonts, the UI atlas or a localised text bitmap. These use codecs the viewer does not decode yet (see docs/formats §3.4-3.6).",
            EntryKind.RawFile => "No decoder claims this file; showing its bytes.",
            _ => "Raw bytes.",
        };
        return $"{what}\n{length:N0} byte(s).\n\n";
    }
}
