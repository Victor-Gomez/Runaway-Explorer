using RunawayExplorer.Core.FileSystem;

namespace RunawayExplorer.Services;

/// <summary>
/// Which tree context-menu entries apply to a given node. A plain value computed from the node alone so
/// the menu's visibility rules are unit-testable rather than scattered across click handlers.
/// </summary>
public readonly record struct TreeContextActions(
    bool CopyPath,
    bool RevealInExplorer,
    bool ExportItem,
    bool ExportRaw,
    bool BatchExportFolder)
{
    /// <summary>Nothing applies -- the menu has no reason to open.</summary>
    public bool IsEmpty => !CopyPath && !RevealInExplorer && !ExportItem && !ExportRaw && !BatchExportFolder;

    /// <summary>True when at least one export entry applies, i.e. the group's separator earns its place.</summary>
    public bool HasExportGroup => ExportItem || ExportRaw || BatchExportFolder;

    public static TreeContextActions For(FsNode? node)
    {
        if (node is null)
            return default;

        bool isFile = node.IsFile;
        bool isDirectory = node.IsDirectory;
        bool inArchive = (node.NodeType & FsNodeType.InArchive) != 0;
        // Archive folders and loose files have a real path on disk; archive entries do not.
        bool onDisk = !inArchive && !string.IsNullOrEmpty(node.ArchivePath);

        return new TreeContextActions(
            CopyPath: true,
            RevealInExplorer: onDisk,
            // Every file kind has a decoded export shape except the ones we only hex-dump, and even those
            // fall back to their raw bytes, so the item export is always on offer for files.
            ExportItem: isFile,
            ExportRaw: isFile,
            BatchExportFolder: isDirectory);
    }
}

/// <summary>
/// Which preview-area context-menu entries apply to whatever is currently on the canvas. Keyed off the
/// loaded <see cref="ResourceContent"/>: the canvas shows one of several very different viewers and
/// offering "Zoom In" over a waveform is noise.
/// </summary>
public readonly record struct PreviewContextActions(
    bool Zoom,
    bool CopyText,
    bool OpenExternally,
    bool Export,
    bool ExportRaw,
    bool CopyPath,
    bool RevealInExplorer)
{
    public bool IsEmpty => !Zoom && !CopyText && !OpenExternally && !Export && !ExportRaw && !CopyPath && !RevealInExplorer;

    public bool HasViewGroup => Zoom || CopyText || OpenExternally;

    public bool HasExportGroup => Export || ExportRaw;

    public static PreviewContextActions For(ResourceContent? content, FsNode? node)
    {
        if (content is null)
            return default;

        bool isFile = node is not null && node.IsFile;
        bool onDisk = node is not null && (node.NodeType & FsNodeType.InArchive) == 0 && !string.IsNullOrEmpty(node.ArchivePath);

        return new PreviewContextActions(
            Zoom: content is ImageResource or AnimationResource or SceneResource,
            CopyText: content is TextResource,
            OpenExternally: content is VideoResource,
            // A scene view is the archive's background; exporting it means the background PNG.
            Export: content is not ErrorResource && (isFile || content is SceneResource),
            ExportRaw: isFile && content is not ErrorResource,
            CopyPath: node is not null,
            RevealInExplorer: onDisk);
    }
}
