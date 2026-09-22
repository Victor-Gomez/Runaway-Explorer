using System.IO;
using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Formats;

namespace RunawayExplorer.Services;

/// <summary>Progress event fired after each file has been considered (exported, skipped, or failed).</summary>
public sealed record BatchExportProgress(string RelativePath, BatchExportResult Result, int Index, int Total);

/// <summary>Per-file outcome from a batch export walk.</summary>
public enum BatchExportResult { Exported, SkippedUnsupported, Failed }

/// <summary>Summary of a completed batch export walk.</summary>
public sealed record BatchExportSummary(int ExportedCount, int SkippedCount, int FailedCount, IReadOnlyList<string> FailedPaths);

/// <summary>What the batch walk writes for each kind of entry.</summary>
/// <param name="AnimationFps">Frame rate stamped into the APNGs. The files store none.</param>
/// <param name="VoiceSampleRate">Sample rate assumed for voice lines.</param>
/// <param name="AnimationFrames">Also write one PNG per frame, in a folder next to the APNG.</param>
public sealed record BatchExportOptions(double AnimationFps, int VoiceSampleRate, bool AnimationFrames = false);

/// <summary>
/// Walks a subtree of the virtual file system and writes every entry it knows how to convert,
/// preserving the tree's folder hierarchy under an output root:
/// <list type="bullet">
///   <item><description>backgrounds and overlays → <c>.png</c> (overlays carry <c>_at_X_Y</c>, their screen position);</description></item>
///   <item><description>animations → <c>.png</c> APNG on the union bounding box, plus optionally <c>&lt;name&gt;_frames/frame_NNNN_xX_yY.png</c>;</description></item>
///   <item><description>music, ambient, cinematic and voice → <c>.wav</c>;</description></item>
///   <item><description>videos → restored <c>.bik</c>;</description></item>
///   <item><description>lip-sync tracks → <c>.txt</c>, one value per line;</description></item>
///   <item><description>everything else is skipped.</description></item>
/// </list>
/// A <c>manifest.json</c>-style summary is not written; the file names carry the positions instead.
/// </summary>
public static class BatchExporter
{
    public static BatchExportSummary ExportSubtree(
        FsNode root,
        VirtualFileSystem vfs,
        string outputDirectory,
        BatchExportOptions options,
        Action<BatchExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(vfs);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        ArgumentNullException.ThrowIfNull(options);

        Directory.CreateDirectory(outputDirectory);

        int exported = 0, skipped = 0, failed = 0;
        var failedPaths = new List<string>();
        string rootPath = root.GetPath();

        List<FsNode> files = root.IsFile ? [root] : vfs.EnumerateFiles(root).ToList();
        int total = files.Count;
        int index = 0;

        foreach (FsNode file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            index++;
            string relativePath = RelativePath(rootPath, file.GetPath());
            string relativeDir = Path.GetDirectoryName(relativePath) ?? string.Empty;
            string destinationDir = Path.Combine(outputDirectory, SanitizeRelativePath(relativeDir));

            BatchExportResult result;
            try
            {
                Directory.CreateDirectory(destinationDir);
                result = ExportOne(file, vfs, destinationDir, options);
            }
            catch (Exception ex)
            {
                Log.Exception($"Batch export '{file.GetPath()}'", ex);
                result = BatchExportResult.Failed;
            }

            switch (result)
            {
                case BatchExportResult.Exported: exported++; break;
                case BatchExportResult.SkippedUnsupported: skipped++; break;
                case BatchExportResult.Failed: failed++; failedPaths.Add(relativePath); break;
            }

            progress?.Invoke(new BatchExportProgress(relativePath, result, index, total));
        }

        return new BatchExportSummary(exported, skipped, failed, failedPaths);
    }

    /// <summary>The file name (without directory) an entry exports as, so single-item export and batch export agree.</summary>
    public static string ExportFileName(FsNode node)
    {
        ImageInfo? img = node.Image;
        return node.Kind switch
        {
            EntryKind.Background when img is not null => $"{node.Name}_{img.Width}x{img.Height}{(img.IsMask ? "_mask" : "")}.png",
            EntryKind.Mask when img is not null => $"{node.Name}_{img.Width}x{img.Height}_mask.png",
            EntryKind.Overlay when img is not null => $"{node.Name}_{img.Width}x{img.Height}_at_{img.X}_{img.Y}.png",
            EntryKind.Animation => $"{node.Name}.png",
            EntryKind.Music or EntryKind.Ambient or EntryKind.Cinematic or EntryKind.Voice =>
                node.Audio?.Format == AudioFormat.Mp3 ? $"{node.Name}.mp3" : $"{node.Name}.wav",
            // DATAVB02.001, DATAVB02.002, ... are different videos: keep the numeric suffix in the name.
            EntryKind.Video => $"{Path.GetFileNameWithoutExtension(node.Name)}_{Path.GetExtension(node.Name).TrimStart('.')}.bik",
            EntryKind.Viseme => $"{node.Name}.txt",
            _ => $"{node.Name}.bin",
        };
    }

    private static BatchExportResult ExportOne(FsNode file, VirtualFileSystem vfs, string destinationDir, BatchExportOptions options)
    {
        string name = SanitizeSegment(ExportFileName(file));
        string path = Path.Combine(destinationDir, name);

        switch (file.Kind)
        {
            case EntryKind.Background:
            {
                byte[] data = vfs.ReadBytes(file);
                ImageInfo? info = file.Image;
                DecodedImage image = info is not null && info.Width * info.Height * 2 == data.Length
                    ? RasterDecoder.Decode(data, info.Width, info.Height)
                    : RasterDecoder.TryDecode(data)?.Image ?? throw new InvalidDataException("not a raster");
                PngWriter.Write(image, path);
                return BatchExportResult.Exported;
            }

            case EntryKind.Mask:
            {
                byte[] data = vfs.ReadBytes(file);
                ImageInfo? info = file.Image;
                DecodedImage image = info is not null
                    ? RleMaskDecoder.Decode(data, info.Width, info.Height)
                    : RleMaskDecoder.TryDecode(data)?.Image ?? throw new InvalidDataException("not an RLE mask");
                PngWriter.Write(image, path);
                return BatchExportResult.Exported;
            }

            case EntryKind.Overlay:
            {
                byte[] data = vfs.ReadBytes(file);
                (DecodedImage image, _) = OverlayDecoder.TryDecode(data) ?? throw new InvalidDataException("not an overlay");
                PngWriter.Write(image, path);
                return BatchExportResult.Exported;
            }

            case EntryKind.Animation:
            {
                SpriteAsset asset = SpriteAsset.Parse(vfs.ReadBytes(file)) ?? throw new InvalidDataException("not a sprite");
                ExportAnimation(asset, path, options.AnimationFps, options.AnimationFrames);
                return BatchExportResult.Exported;
            }

            case EntryKind.Music:
            case EntryKind.Ambient:
            case EntryKind.Cinematic:
            case EntryKind.Voice:
            {
                AudioInfo pcm = file.Audio ?? VirtualFileSystem.AmbientPcm;
                using Stream src = vfs.OpenFile(file);
                using FileStream dst = File.Create(path);
                if (pcm.Format == AudioFormat.RawPcm)
                {
                    int rate = file.Kind == EntryKind.Voice ? options.VoiceSampleRate : pcm.SampleRate;
                    dst.Write(WavWriter.Header((int)Math.Min(src.Length, int.MaxValue), rate, pcm.Channels, pcm.BitsPerSample));
                }
                src.CopyTo(dst);
                return BatchExportResult.Exported;
            }

            case EntryKind.Video:
            {
                using FileStream dst = File.Create(path);
                return vfs.RestoreVideo(file, dst) ? BatchExportResult.Exported : BatchExportResult.Failed;
            }

            case EntryKind.Viseme:
            {
                File.WriteAllText(path, VisemeArchive.ToText(vfs.ReadBytes(file)));
                return BatchExportResult.Exported;
            }

            default:
                return BatchExportResult.SkippedUnsupported;
        }
    }

    /// <summary>
    /// Writes an animation as an APNG at <paramref name="apngPath"/> and, when asked, every frame as a
    /// cropped PNG named with its screen position in <c>&lt;stem&gt;_frames/</c> next to it. Empty frames
    /// get no file; the APNG keeps a 1×1 placeholder so numbering stays 1:1.
    /// </summary>
    public static (int Frames, int FramesWritten) ExportAnimation(SpriteAsset asset, string apngPath, double fps, bool frames = false)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ApngWriter.Write(asset, apngPath, fps);

        int written = 0;
        if (frames)
        {
            string dir = Path.Combine(Path.GetDirectoryName(apngPath) ?? ".", Path.GetFileNameWithoutExtension(apngPath) + "_frames");
            written = ExportImageSequence(asset, dir);
        }
        return (asset.FrameCount, written);
    }

    /// <summary>Exports each frame of the animation as an individual PNG into <paramref name="dir"/>.</summary>
    public static int ExportImageSequence(SpriteAsset asset, string dir, string prefix = "frame")
    {
        ArgumentNullException.ThrowIfNull(asset);
        Directory.CreateDirectory(dir);
        int written = 0;
        for (int i = 0; i < asset.FrameCount; i++)
        {
            SpriteFrame frame = asset.DecodeFrame(i);
            if (frame.Image is null)
                continue;
            PngWriter.Write(frame.Image, Path.Combine(dir, $"{prefix}_{i:0000}_x{frame.X}_y{frame.Y}.png"));
            written++;
        }
        return written;
    }

    private static string RelativePath(string rootPath, string fullPath)
    {
        if (fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            string rel = fullPath[rootPath.Length..].TrimStart('\\');
            if (rel.Length > 0)
                return rel;
        }
        return Path.GetFileName(fullPath);
    }

    private static string SanitizeRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return string.Empty;
        return string.Join(Path.DirectorySeparatorChar, relativePath.Split('\\', StringSplitOptions.RemoveEmptyEntries).Select(SanitizeSegment));
    }

    public static string SanitizeSegment(string segment)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var chars = segment.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }
        string result = new string(chars).Trim();
        return result.Length == 0 ? "_" : result;
    }
}
