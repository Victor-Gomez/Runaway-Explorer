using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RunawayExplorer.Core.FileSystem;

namespace RunawayExplorer.ViewModels;

/// <summary>
/// Lazily-populated TreeView wrapper around an <see cref="FsNode"/>. The underlying tree is already
/// fully built in memory by <see cref="VirtualFileSystem.Init"/>, but an install has ~18,000 nodes
/// (11,000 of them voice lines) -- materializing a view-model + TreeViewItem for every one up front
/// would be slow. Each node gets a single placeholder child until it is actually expanded.
/// </summary>
public sealed class FsNodeViewModel : INotifyPropertyChanged
{
    private static readonly Dictionary<EntryKind, string> KindIcons = new()
    {
        [EntryKind.Background] = "ImageTypeIcon",
        [EntryKind.Mask] = "ImageTypeIcon",
        [EntryKind.Overlay] = "ImageTypeIcon",
        [EntryKind.Animation] = "AnimationTypeIcon",
        [EntryKind.Data] = "DataTypeIcon",
        [EntryKind.Music] = "SoundTypeIcon",
        [EntryKind.Ambient] = "SoundTypeIcon",
        [EntryKind.Cinematic] = "SoundTypeIcon",
        [EntryKind.Voice] = "SoundTypeIcon",
        [EntryKind.Video] = "VideoTypeIcon",
        [EntryKind.Viseme] = "DataTypeIcon",
        [EntryKind.Dialogue] = "DataTypeIcon",
        [EntryKind.GlobalData] = "DataTypeIcon",
        [EntryKind.Font] = "ImageTypeIcon",
        [EntryKind.RawFile] = "RawFileTypeIcon",
    };

    private const string RootVectorIcon = "HouseIcon";
    private const string ArchiveVectorIcon = "ArchiveTypeIcon";
    private const string FolderClosedVectorIcon = "FolderClosedTypeIcon";
    private const string FolderOpenVectorIcon = "FolderOpenTypeIcon";

    // Icons are requested once per visible row; cache the resolved IImage per key instead of
    // re-looking-up on every binding pull.
    private static readonly Dictionary<string, IImage> IconCache = [];

    private readonly VirtualFileSystem? _vfs;
    private bool _childrenLoaded;
    private bool _isExpanded;

    public FsNode Node { get; }

    /// <summary>True for the single synthetic "loading" placeholder inserted so an unexpanded node still shows an expander arrow.</summary>
    public bool IsPlaceholder { get; }

    public ObservableCollection<FsNodeViewModel> Children { get; } = [];

    public FsNodeViewModel(FsNode node, VirtualFileSystem vfs)
        : this(node, vfs, isPlaceholder: false, eagerChildren: null)
    {
    }

    private FsNodeViewModel(FsNode node, VirtualFileSystem? vfs, bool isPlaceholder, List<FsNodeViewModel>? eagerChildren)
    {
        Node = node;
        _vfs = vfs;
        IsPlaceholder = isPlaceholder;

        if (eagerChildren is not null)
        {
            _childrenLoaded = true;
            foreach (FsNodeViewModel child in eagerChildren)
                Children.Add(child);
            // Deliberately NOT auto-expanded: with a broad filter almost every folder contains a match,
            // so forcing every matching directory open would expand the whole tree.
        }
        else if (!isPlaceholder && node.Children.Count > 0)
        {
            Children.Add(new FsNodeViewModel(node, vfs: null, isPlaceholder: true, eagerChildren: null));
        }
    }

    /// <summary>
    /// Eagerly builds a pruned view of the tree rooted at <paramref name="node"/> containing only file
    /// nodes matching <paramref name="matchesFile"/> and the directories needed to reach them. Returns
    /// <see langword="null"/> if nothing under <paramref name="node"/> matches.
    /// </summary>
    public static FsNodeViewModel? BuildFiltered(FsNode node, VirtualFileSystem vfs, Func<FsNode, bool> matchesFile)
    {
        if (node.IsFile)
            return matchesFile(node) ? new FsNodeViewModel(node, vfs, isPlaceholder: false, eagerChildren: []) : null;

        var matchingChildren = new List<FsNodeViewModel>();

        foreach (FsNode dir in vfs.GetDirectories(node))
        {
            FsNodeViewModel? childVm = BuildFiltered(dir, vfs, matchesFile);
            if (childVm is not null)
                matchingChildren.Add(childVm);
        }

        foreach (FsNode file in vfs.GetFiles(node))
        {
            FsNodeViewModel? childVm = BuildFiltered(file, vfs, matchesFile);
            if (childVm is not null)
                matchingChildren.Add(childVm);
        }

        if (matchingChildren.Count == 0)
            return null;

        return new FsNodeViewModel(node, vfs, isPlaceholder: false, eagerChildren: matchingChildren);
    }

    public bool IsDirectory => Node.IsDirectory;

    public bool IsFile => Node.IsFile;

    public string DisplayName => IsPlaceholder ? "Loading..." : Node.DisplayName;

    /// <summary>A vector icon from <c>Assets/Icons/VectorIcons.axaml</c> for this node's kind.</summary>
    public IImage? IconSource
    {
        get
        {
            if (IsPlaceholder)
                return null;

            if (IsDirectory)
            {
                if ((Node.NodeType & FsNodeType.Root) != 0)
                    return GetVectorIcon(RootVectorIcon);
                if (Node.ArchivePath is not null)
                    return GetVectorIcon(ArchiveVectorIcon);
                return GetVectorIcon(IsExpanded ? FolderOpenVectorIcon : FolderClosedVectorIcon);
            }

            return GetVectorIcon(KindIcons.TryGetValue(Node.Kind, out string? key) ? key : "RawFileTypeIcon");
        }
    }

    private static IImage GetVectorIcon(string resourceKey)
    {
        string cacheKey = "vector:" + resourceKey;
        if (IconCache.TryGetValue(cacheKey, out IImage? cached))
            return cached;

        // TryFindResource walks merged dictionaries; the plain Resources[key] indexer does not, and these
        // icons live in a merged ResourceInclude.
        if (!Application.Current!.TryFindResource(resourceKey, out object? resource) || resource is not IImage image)
            throw new InvalidOperationException($"Vector icon resource '{resourceKey}' was not found.");

        IconCache[cacheKey] = image;
        return image;
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;

            _isExpanded = value;

            if (value && !_childrenLoaded)
                LoadChildren();

            OnPropertyChanged();
            if (IsDirectory)
                OnPropertyChanged(nameof(IconSource));
        }
    }

    private void LoadChildren()
    {
        _childrenLoaded = true;
        Children.Clear();

        if (_vfs is null)
            return;

        foreach (FsNode dir in _vfs.GetDirectories(Node))
            Children.Add(new FsNodeViewModel(dir, _vfs));

        foreach (FsNode file in _vfs.GetFiles(Node))
            Children.Add(new FsNodeViewModel(file, _vfs));
    }

    /// <summary>Recursively expands this directory and all descendant directories.</summary>
    public void ExpandAll()
    {
        if (!IsDirectory)
            return;

        IsExpanded = true;
        foreach (FsNodeViewModel child in Children)
        {
            if (child.IsDirectory)
                child.ExpandAll();
        }
    }

    /// <summary>Recursively collapses this directory and all descendant directories.</summary>
    public void CollapseAll()
    {
        if (!IsDirectory)
            return;

        foreach (FsNodeViewModel child in Children)
        {
            if (child.IsDirectory)
                child.CollapseAll();
        }
        IsExpanded = false;
    }

    /// <summary>Notifies that DisplayName has changed, recursively updating children.</summary>
    public void NotifyDisplayNameChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
        foreach (FsNodeViewModel child in Children)
        {
            if (!child.IsPlaceholder)
                child.NotifyDisplayNameChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
