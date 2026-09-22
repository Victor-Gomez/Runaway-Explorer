using System.Text;

namespace RunawayExplorer.Core.FileSystem;

/// <summary>
/// Classifies a <see cref="FsNode"/>. Flags-based so a node can be both a <see cref="File"/> and
/// <see cref="InArchive"/> at once.
/// </summary>
[Flags]
public enum FsNodeType
{
    None = 0,
    Root = 1,
    Directory = 2,
    File = 4,
    InArchive = 8,
}

/// <summary>
/// What a node holds, decided once at scan time. Runaway's archive entries carry no names and no
/// extensions, so this -- not a file extension -- is what the viewers, the tree icons, the type filter
/// and the exporters all key off.
/// </summary>
public enum EntryKind
{
    /// <summary>A folder in the virtual tree (the root, a category, or one archive).</summary>
    Folder,

    /// <summary>A raw RGB565 raster from a scene archive: the scene's background or an alternate state.</summary>
    Background,

    /// <summary>An RLE-encoded scene mask layer: walk-behind regions, interaction hotspots, and depth zones.</summary>
    Mask,

    /// <summary>A positioned row-record image: a prop, foreground layer, title card or UI element.</summary>
    Overlay,

    /// <summary>A multi-frame sprite animation.</summary>
    Animation,

    /// <summary>A scene-archive entry that is not an image (the per-scene tables).</summary>
    Data,

    /// <summary>16 kHz stereo PCM from <c>RESOURCE.M*</c>.</summary>
    Music,

    /// <summary>22 kHz stereo PCM from <c>RESOURCE.S*</c>.</summary>
    Ambient,

    /// <summary>22 kHz stereo PCM from <c>RESOURCE.002</c>.</summary>
    Cinematic,

    /// <summary>8-bit mono PCM voice line from the <c>DATAACA</c> shards.</summary>
    Voice,

    /// <summary>A Bink video from <c>Datav/</c> whose header must be restored from the keyfile.</summary>
    Video,

    /// <summary>A lip-sync viseme track from <c>RESOURCE.004</c>.</summary>
    Viseme,

    /// <summary>An entry of <c>RESOURCE.000</c> (fonts, UI atlas, localised bitmaps): shown as a hex dump.</summary>
    GlobalData,

    /// <summary>A loose file on disk that no decoder claims: shown as a hex dump.</summary>
    RawFile,
}

/// <summary>Image-shaped facts gathered at scan time so the tree can label entries without re-decoding.</summary>
public sealed class ImageInfo
{
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Screen position of the top-left pixel (overlays and animations).</summary>
    public int X { get; init; }
    public int Y { get; init; }

    /// <summary>Frame count (animations only).</summary>
    public int Frames { get; init; }

    /// <summary>Width-detection confidence (rasters only); 99 for stacked full screens.</summary>
    public double Sharpness { get; init; }

    /// <summary>Raster whose pixels are index values rather than colours.</summary>
    public bool IsMask { get; init; }
}

public enum AudioFormat
{
    RawPcm,
    Wav,
    Mp3,
}

/// <summary>Format and playback attributes of an audio entry.</summary>
public sealed class AudioInfo
{
    public AudioFormat Format { get; init; } = AudioFormat.RawPcm;
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public int BitsPerSample { get; init; }

    public double DurationSeconds(long byteCount)
    {
        if (Format == AudioFormat.Mp3)
        {
            // Typical MP3 voice lines and music: approximate based on 128 kbps (16,000 B/s)
            return byteCount > 0 ? byteCount / 16_000.0 : 0;
        }

        long audioBytes = Format == AudioFormat.Wav ? Math.Max(0, byteCount - 44) : byteCount;
        return SampleRate <= 0 || Channels <= 0 || BitsPerSample <= 0
            ? 0
            : audioBytes / (double)(SampleRate * Channels * (BitsPerSample / 8));
    }
}


/// <summary>
/// A single node (directory or file) in the in-memory virtual file system tree built by
/// <see cref="VirtualFileSystem"/>.
/// </summary>
public sealed class FsNode
{
    public FsNodeType NodeType { get; set; }

    public EntryKind Kind { get; set; } = EntryKind.Folder;

    /// <summary>Path segment: the folder name on disk, the archive file name, or a synthetic entry name such as <c>e07</c>.</summary>
    public string Name { get; set; } = "";

    /// <summary>Display name if known (typically with decoded dimensions), else falls back to <see cref="Name"/>.</summary>
    public string? FriendlyName { get; set; }

    /// <summary>Byte offset within the owning archive. Only meaningful when <see cref="FsNodeType.InArchive"/> is set.</summary>
    public long Offset { get; set; }

    /// <summary>Size in bytes (file nodes).</summary>
    public long Size { get; set; }

    /// <summary>Slot index inside the owning archive's table, for archive entries.</summary>
    public int EntryIndex { get; set; } = -1;

    /// <summary>
    /// The absolute path used to open this file's bytes: the owning archive's path for
    /// <see cref="FsNodeType.InArchive"/> nodes, or the physical file path on disk for loose files.
    /// For an archive folder node, the archive's own path.
    /// </summary>
    public string? ArchivePath { get; set; }

    public ImageInfo? Image { get; set; }

    public AudioInfo? Audio { get; set; }

    public FsNode? Parent { get; set; }

    public List<FsNode> Children { get; } = [];

    public string DisplayName => FriendlyName ?? Name;

    public bool IsFile => (NodeType & FsNodeType.File) != 0;

    public bool IsDirectory => (NodeType & FsNodeType.Directory) != 0 || (NodeType & FsNodeType.Root) != 0;

    /// <summary>Builds the full <c>\</c>-delimited path from the root down to this node.</summary>
    public string GetPath()
    {
        var segments = new List<string>();
        for (FsNode? node = this; node is not null && node.Parent is not null; node = node.Parent)
            segments.Add(node.Name);

        segments.Reverse();

        var sb = new StringBuilder("\\");
        sb.Append(string.Join('\\', segments));
        return sb.ToString();
    }
}
