using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Formats;
using RunawayExplorer.Services;
using Xunit;

namespace RunawayExplorer.Tests;

/// <summary>The tree context menu's visibility rules, so an entry never has to be clicked to learn it doesn't apply.</summary>
public class TreeContextActionsTests
{
    private static FsNode ArchiveEntry(EntryKind kind = EntryKind.Overlay) => new()
    {
        NodeType = FsNodeType.File | FsNodeType.InArchive,
        Kind = kind,
        Name = "e05",
        ArchivePath = @"install\Resource\RESOURCE.H09",
    };

    private static FsNode LooseFile() => new()
    {
        NodeType = FsNodeType.File,
        Kind = EntryKind.Video,
        Name = "DATAVA01.001",
        ArchivePath = @"install\Datav\DATAVA01.001",
    };

    private static FsNode ArchiveFolder() => new()
    {
        NodeType = FsNodeType.Directory,
        Name = "RESOURCE.H09",
        ArchivePath = @"install\Resource\RESOURCE.H09",
    };

    private static FsNode CategoryFolder() => new() { NodeType = FsNodeType.Directory, Name = "Scenes" };

    [Fact]
    public void NoNode_YieldsNothingToShow() => Assert.True(TreeContextActions.For(null).IsEmpty);

    [Fact]
    public void ArchiveEntry_OffersExportsButNotReveal()
    {
        TreeContextActions a = TreeContextActions.For(ArchiveEntry());
        Assert.True(a.CopyPath);
        Assert.True(a.ExportItem);
        Assert.True(a.ExportRaw);
        Assert.False(a.BatchExportFolder);
        // It lives inside an archive; there is no file of its own to reveal.
        Assert.False(a.RevealInExplorer);
        Assert.False(a.ExpandAll);
        Assert.False(a.CollapseAll);
        Assert.False(a.HasExpandGroup);
    }

    [Fact]
    public void LooseFile_CanBeRevealed()
    {
        TreeContextActions a = TreeContextActions.For(LooseFile());
        Assert.True(a.RevealInExplorer);
        Assert.True(a.ExportItem);
        Assert.False(a.ExpandAll);
        Assert.False(a.CollapseAll);
        Assert.False(a.HasExpandGroup);
    }

    [Fact]
    public void ArchiveFolder_RevealsTheArchiveAndBatchExports()
    {
        TreeContextActions a = TreeContextActions.For(ArchiveFolder());
        Assert.True(a.RevealInExplorer);
        Assert.True(a.BatchExportFolder);
        Assert.False(a.ExportItem);
        Assert.False(a.ExportRaw);
        Assert.True(a.ExpandAll);
        Assert.True(a.CollapseAll);
        Assert.True(a.HasExpandGroup);
    }

    [Fact]
    public void CategoryFolder_OnlyBatchExports()
    {
        TreeContextActions a = TreeContextActions.For(CategoryFolder());
        Assert.False(a.RevealInExplorer);
        Assert.True(a.BatchExportFolder);
        Assert.True(a.HasExportGroup);
        Assert.True(a.ExpandAll);
        Assert.True(a.CollapseAll);
        Assert.True(a.HasExpandGroup);
    }
}

public class PreviewContextActionsTests
{
    private static readonly FsNode Entry = new()
    {
        NodeType = FsNodeType.File | FsNodeType.InArchive,
        Kind = EntryKind.Animation,
        Name = "e07",
        ArchivePath = @"install\Resource\RESOURCE.H09",
    };

    private static readonly FsNode Archive = new()
    {
        NodeType = FsNodeType.Directory,
        Name = "RESOURCE.H09",
        ArchivePath = @"install\Resource\RESOURCE.H09",
    };

    private static SpriteAsset AnyAsset()
    {
        // One 1-pixel frame at (0,0): record {0, x0 0, w 1, y0 0, y1 0, c 1} then segment {0,0,count 0→1, px}.
        byte[] data = [0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0xAA, 0xBB];
        return SpriteAsset.Parse(data)!;
    }

    [Fact]
    public void NothingLoaded_NoMenu() => Assert.True(PreviewContextActions.For(null, null).IsEmpty);

    [Fact]
    public void Images_ZoomAndExport()
    {
        var a = PreviewContextActions.For(new ImageResource(DecodedImage.Transparent(1, 1), 0, 0, false, "background"), Entry);
        Assert.True(a.Zoom);
        Assert.True(a.Export);
        Assert.True(a.ExportRaw);
        Assert.True(a.CopyImage);
        Assert.False(a.CopyText);
        Assert.False(a.OpenExternally);
    }

    [Fact]
    public void Animations_Zoom()
    {
        var a = PreviewContextActions.For(new AnimationResource(AnyAsset()), Entry);
        Assert.True(a.Zoom);
        Assert.True(a.CopyImage);
    }

    [Fact]
    public void Text_Copies()
    {
        var a = PreviewContextActions.For(new TextResource("x"), Entry);
        Assert.True(a.CopyText);
        Assert.False(a.CopyImage);
    }

    [Fact]
    public void Video_OpensExternally()
    {
        var a = PreviewContextActions.For(new VideoResource("x.bik"), Entry);
        Assert.True(a.OpenExternally);
        Assert.False(a.CopyImage);
    }

    [Fact]
    public void Scene_ExportsTheBackgroundButHasNoRawBytes()
    {
        var a = PreviewContextActions.For(new SceneResource(DecodedImage.Transparent(1, 1), "s", Archive), Archive);
        Assert.True(a.Zoom);
        Assert.True(a.Export);
        Assert.False(a.ExportRaw);
        Assert.True(a.RevealInExplorer);
        Assert.True(a.CopyImage);
    }

    [Fact]
    public void Errors_ExportNothing()
    {
        var a = PreviewContextActions.For(new ErrorResource("boom"), Entry);
        Assert.False(a.Export);
        Assert.False(a.ExportRaw);
        Assert.True(a.CopyPath);
        Assert.False(a.CopyImage);
    }
}

public class ResourceTypeFilterTests
{
    [Fact]
    public void AllMatchesEverything()
    {
        Assert.True(ResourceTypeFilter.All.Matches(new FsNode { Kind = EntryKind.Data }));
        Assert.Same(ResourceTypeFilter.All, ResourceTypeFilter.Categories[0]);
    }

    [Fact]
    public void EveryKindHasACategory()
    {
        foreach (EntryKind kind in Enum.GetValues<EntryKind>())
        {
            if (kind == EntryKind.Folder)
                continue;
            Assert.Contains(ResourceTypeFilter.Categories.Skip(1), f => f.Matches(new FsNode { Kind = kind }));
        }
    }

    [Fact]
    public void CategoriesMatchOnlyTheirKinds()
    {
        ResourceTypeFilter animations = ResourceTypeFilter.Categories.First(c => c.Label == "Animations");
        Assert.True(animations.Matches(new FsNode { Kind = EntryKind.Animation }));
        Assert.False(animations.Matches(new FsNode { Kind = EntryKind.Overlay }));
    }
}

public class KeyboardShortcutsTests
{
    [Fact]
    public void EveryShortcutBelongsToAListedGroup()
    {
        foreach (KeyboardShortcut s in KeyboardShortcuts.All)
            Assert.Contains(s.Group, KeyboardShortcuts.Groups);
    }

    [Fact]
    public void GesturesAreUniqueWithinAGroup()
    {
        foreach (string group in KeyboardShortcuts.Groups)
        {
            var gestures = KeyboardShortcuts.InGroup(group).Select(s => s.Gesture).ToList();
            Assert.Equal(gestures.Distinct().Count(), gestures.Count);
        }
    }

    [Fact]
    public void TheCheatSheetCoversTheHandledKeys()
    {
        string[] gestures = KeyboardShortcuts.All.Select(s => s.Gesture).ToArray();
        Assert.Contains("Ctrl+O", gestures);
        Assert.Contains("Ctrl+P", gestures);
        Assert.Contains("Ctrl+E", gestures);
        Assert.Contains("Ctrl+C", gestures);
        Assert.Contains("Space", gestures);
        Assert.Contains("B", gestures);
        Assert.Contains("M", gestures);
        Assert.Contains("F1", gestures);
    }
}
