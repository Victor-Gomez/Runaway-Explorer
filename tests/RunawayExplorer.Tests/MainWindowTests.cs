using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RunawayExplorer.Views;
using Xunit;

namespace RunawayExplorer.Tests;

/// <summary>
/// Constructs the real main window against the real theme. XAML faults -- an unresolvable
/// StaticResource, a handler name that doesn't exist, a mistyped x:Name -- only surface when the
/// window is actually built, so a compiling build proves nothing on its own.
/// </summary>
public class MainWindowTests : UiTestBase
{
    [AvaloniaFact]
    public void Construct_LoadsTheXaml_WithEveryPanelHidden()
    {
        MainWindow window = Show(new MainWindow());
        window.Measure(new Size(1180, 720));
        window.Arrange(new Rect(0, 0, 1180, 720));

        Assert.NotNull(window.FindControl<TreeView>("Tree"));
        foreach (string panel in new[] { "ImagePanel", "AnimationPanel", "SoundPanel", "VideoPanel" })
        {
            Control? control = window.FindControl<Control>(panel);
            Assert.NotNull(control);
            Assert.False(control.IsVisible, $"{panel} should start hidden");
        }
        Assert.False(window.FindControl<TextBox>("TextPanel")!.IsVisible);
        Assert.False(window.FindControl<SettingsPanel>("SettingsOverlay")!.IsVisible);

        // Quality of Life controls
        Assert.NotNull(window.FindControl<Button>("SearchClearButton"));
        Assert.NotNull(window.FindControl<Slider>("SoundVolumeSlider"));
        Assert.NotNull(window.FindControl<Button>("SoundMuteButton"));
        Assert.NotNull(window.FindControl<Slider>("VideoVolumeSlider"));
        Assert.NotNull(window.FindControl<Button>("VideoMuteButton"));
        Assert.NotNull(window.FindControl<Image>("WaveformImage"));
        Assert.NotNull(window.FindControl<TextBlock>("SelectedPathText"));
    }

    [AvaloniaFact]
    public void TypeFilter_ListsEveryCategory()
    {
        MainWindow window = Track(new MainWindow());
        var combo = window.FindControl<ComboBox>("TypeFilterCombo")!;
        Assert.Equal(Services.ResourceTypeFilter.Categories.Count, combo.ItemCount);
        Assert.Equal(0, combo.SelectedIndex);
    }

    [AvaloniaFact]
    public void SettingsOverlay_OpensAndCloses()
    {
        MainWindow window = Show(new MainWindow());
        var overlay = window.FindControl<SettingsPanel>("SettingsOverlay")!;

        overlay.Show(window, new Core.Settings.AppSettings());
        Assert.True(overlay.IsVisible);

        overlay.Hide();
        Assert.False(overlay.IsVisible);
    }

    [AvaloniaFact]
    public void FsNodeViewModel_ExpandAllAndCollapseAll_OperatesRecursively()
    {
        var rootNode = new Core.FileSystem.FsNode
        {
            NodeType = Core.FileSystem.FsNodeType.Directory | Core.FileSystem.FsNodeType.Root,
            Name = "Root",
        };
        var childDir = new Core.FileSystem.FsNode
        {
            NodeType = Core.FileSystem.FsNodeType.Directory,
            Name = "Folder",
        };
        var grandChildDir = new Core.FileSystem.FsNode
        {
            NodeType = Core.FileSystem.FsNodeType.Directory,
            Name = "SubFolder",
        };
        var fileNode = new Core.FileSystem.FsNode
        {
            NodeType = Core.FileSystem.FsNodeType.File,
            Name = "file.txt",
        };

        childDir.Children.Add(grandChildDir);
        grandChildDir.Children.Add(fileNode);
        rootNode.Children.Add(childDir);

        var vfs = new Core.FileSystem.VirtualFileSystem(string.Empty, rootNode);
        var rootVm = new ViewModels.FsNodeViewModel(rootNode, vfs);

        Assert.False(rootVm.IsExpanded);

        rootVm.ExpandAll();
        Assert.True(rootVm.IsExpanded);
        var childVm = rootVm.Children.First(c => c.DisplayName == "Folder");
        Assert.True(childVm.IsExpanded);
        var grandChildVm = childVm.Children.First(c => c.DisplayName == "SubFolder");
        Assert.True(grandChildVm.IsExpanded);

        rootVm.CollapseAll();
        Assert.False(rootVm.IsExpanded);
        Assert.False(childVm.IsExpanded);
        Assert.False(grandChildVm.IsExpanded);
    }
}
