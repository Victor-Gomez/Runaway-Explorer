using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using RunawayExplorer.Views;
using Xunit;

namespace RunawayExplorer.Tests;

public class AnimationExportOverlayTests : UiTestBase
{
    [AvaloniaFact]
    public void Construct_LoadsXaml_DefaultsToApng()
    {
        var overlay = new AnimationExportOverlay();
        var apngRadio = overlay.FindControl<RadioButton>("ApngRadio");
        var seqRadio = overlay.FindControl<RadioButton>("SequenceRadio");

        Assert.NotNull(apngRadio);
        Assert.NotNull(seqRadio);
        Assert.True(apngRadio.IsChecked);
        Assert.False(seqRadio.IsChecked);
    }

    [AvaloniaFact]
    public async Task ExportClick_WithApngSelected_CompletesWithApng()
    {
        var overlay = new AnimationExportOverlay();
        var exportBtn = overlay.FindControl<Button>("ExportBtn")!;

        Task<AnimationExportFormat> task = overlay.ShowAsync();
        Assert.True(overlay.IsVisible);

        exportBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        AnimationExportFormat result = await task;
        Assert.Equal(AnimationExportFormat.Apng, result);
        Assert.False(overlay.IsVisible);
    }

    [AvaloniaFact]
    public async Task ExportClick_WithSequenceSelected_CompletesWithSequence()
    {
        var overlay = new AnimationExportOverlay();
        var seqRadio = overlay.FindControl<RadioButton>("SequenceRadio")!;
        var exportBtn = overlay.FindControl<Button>("ExportBtn")!;

        Task<AnimationExportFormat> task = overlay.ShowAsync();
        seqRadio.IsChecked = true;
        exportBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        AnimationExportFormat result = await task;
        Assert.Equal(AnimationExportFormat.ImageSequence, result);
        Assert.False(overlay.IsVisible);
    }

    [AvaloniaFact]
    public async Task CancelClick_CompletesWithCancel()
    {
        var overlay = new AnimationExportOverlay();
        var cancelBtn = overlay.FindControl<Button>("CancelBtn")!;

        Task<AnimationExportFormat> task = overlay.ShowAsync();
        cancelBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        AnimationExportFormat result = await task;
        Assert.Equal(AnimationExportFormat.Cancel, result);
        Assert.False(overlay.IsVisible);
    }
}