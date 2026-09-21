using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace RunawayExplorer.ViewModels;

/// <summary>
/// View-model for one overlay entry in the Scene Overlays sidebar checklist. Each item controls the
/// visibility of an <see cref="Avalonia.Controls.Image"/> composited on the scene canvas.
/// </summary>
public sealed class SceneOverlayItem : INotifyPropertyChanged
{
    private bool _isChecked = true;

    /// <summary>Display label shown next to the checkbox (e.g. "e03  overlay").</summary>
    public required string DisplayName { get; init; }

    /// <summary>Screen X where the overlay is placed.</summary>
    public int X { get; init; }

    /// <summary>Screen Y where the overlay is placed.</summary>
    public int Y { get; init; }

    /// <summary>The decoded overlay as an Avalonia bitmap.</summary>
    public required Bitmap OverlayBitmap { get; init; }

    /// <summary>The <see cref="Image"/> control on the scene canvas. Set after the Image is created.</summary>
    public Image? CanvasImage { get; set; }

    /// <summary>Bound to the CheckBox in the sidebar. Toggling hides/shows the canvas image.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged();
            if (CanvasImage is not null)
                CanvasImage.IsVisible = value;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
