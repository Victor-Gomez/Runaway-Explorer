using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace RunawayExplorer.Views;

public enum AnimationExportFormat
{
    Cancel,
    Apng,
    ImageSequence
}

public partial class AnimationExportOverlay : UserControl
{
    private TaskCompletionSource<AnimationExportFormat>? _tcs;

    public AnimationExportOverlay()
    {
        InitializeComponent();
    }

    public Task<AnimationExportFormat> ShowAsync()
    {
        _tcs?.TrySetResult(AnimationExportFormat.Cancel);
        _tcs = new TaskCompletionSource<AnimationExportFormat>();

        ApngRadio.IsChecked = true;
        IsVisible = true;
        Focus();
        ExportBtn.Focus();

        return _tcs.Task;
    }

    public void Confirm()
    {
        AnimationExportFormat format = SequenceRadio.IsChecked == true
            ? AnimationExportFormat.ImageSequence
            : AnimationExportFormat.Apng;
        Dismiss(format);
    }

    public void Dismiss(AnimationExportFormat format = AnimationExportFormat.Cancel)
    {
        IsVisible = false;
        _tcs?.TrySetResult(format);
    }

    private void Scrim_PointerPressed(object? sender, PointerPressedEventArgs e) => Dismiss(AnimationExportFormat.Cancel);

    private void Card_PointerPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private void Close_Click(object? sender, RoutedEventArgs e) => Dismiss(AnimationExportFormat.Cancel);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Dismiss(AnimationExportFormat.Cancel);

    private void Export_Click(object? sender, RoutedEventArgs e) => Confirm();

    private void ApngCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ApngRadio.IsChecked = true;
        if (e.ClickCount == 2)
        {
            Dismiss(AnimationExportFormat.Apng);
        }
    }

    private void SequenceCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SequenceRadio.IsChecked = true;
        if (e.ClickCount == 2)
        {
            Dismiss(AnimationExportFormat.ImageSequence);
        }
    }
}