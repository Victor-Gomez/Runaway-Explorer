using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace RunawayExplorer.Views;

/// <summary>
/// Zoom and pan for one stage: a <see cref="ScrollViewer"/> wrapping a <see cref="LayoutTransformControl"/>
/// whose content is scaled uniformly. Owns the wheel-to-zoom, left-drag-to-pan, slider/label sync and
/// fit-to-window logic so the image and animation viewers share one implementation instead of two
/// copies that drift apart.
/// </summary>
public sealed class ZoomController
{
    public const double MinZoom = 0.1;
    public const double MaxZoom = 16.0;

    private readonly ScrollViewer _scroll;
    private readonly LayoutTransformControl _host;
    private readonly ScaleTransform _scale = new();
    private readonly Slider _slider;
    private readonly TextBlock _label;
    private readonly Func<Size> _contentSize;

    private bool _syncingSlider;
    private Point? _panStart;
    private double _panStartH, _panStartV;

    public double Zoom { get; private set; } = 1.0;

    /// <summary>True while the zoom tracks the viewport (until the user zooms manually).</summary>
    public bool FitToWindow { get; private set; }

    /// <param name="contentSize">The unscaled size of what the host shows, for fit-to-window.</param>
    public ZoomController(ScrollViewer scroll, LayoutTransformControl host, Slider slider, TextBlock label, Func<Size> contentSize)
    {
        _scroll = scroll;
        _host = host;
        _slider = slider;
        _label = label;
        _contentSize = contentSize;

        // Avalonia doesn't generate x:Name fields for objects assigned via a property (LayoutTransform),
        // so the transform is built here and wired onto the host.
        _host.LayoutTransform = _scale;

        // Tunnel routing so the wheel is intercepted before the ScrollViewer's own scroll response.
        _scroll.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        _scroll.PointerPressed += OnPointerPressed;
        _scroll.PointerMoved += OnPointerMoved;
        _scroll.PointerReleased += OnPointerReleased;
        _scroll.SizeChanged += (_, _) =>
        {
            if (FitToWindow)
                Fit();
        };
        _slider.ValueChanged += OnSliderChanged;
    }

    /// <summary>Whether wheel/drag should do anything -- false when the stage is empty.</summary>
    public Func<bool> HasContent { get; set; } = () => true;

    public void Set(double zoom)
    {
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _scale.ScaleX = Zoom;
        _scale.ScaleY = Zoom;
        _label.Text = $"{Zoom * 100:0}%";
        _syncingSlider = true;
        try { _slider.Value = Zoom * 100.0; }
        finally { _syncingSlider = false; }
    }

    public void In() { FitToWindow = false; Set(Zoom * 1.25); }

    public void Out() { FitToWindow = false; Set(Zoom / 1.25); }

    public void Zoom100() { FitToWindow = false; Set(1.0); }

    /// <summary>
    /// "Fit to window, but never more than 8x": the largest zoom that keeps the content inside the
    /// viewport on both axes, capped so tiny icons don't blow up to fill the panel. On first load the
    /// ScrollViewer hasn't laid out yet; then it defers to the next dispatcher tick.
    /// </summary>
    public void Fit()
    {
        Size content = _contentSize();
        if (content.Width <= 0 || content.Height <= 0)
        {
            Set(1.0);
            FitToWindow = true;
            return;
        }

        double vpW = _scroll.Bounds.Width;
        double vpH = _scroll.Bounds.Height;
        if (vpW <= 0 || vpH <= 0)
        {
            FitToWindow = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (FitToWindow)
                    Fit();
            }, DispatcherPriority.Loaded);
            return;
        }

        double fit = Math.Min(vpW / content.Width, vpH / content.Height);
        Set(Math.Min(fit, 8.0));
        FitToWindow = true;
        _scroll.Offset = new Vector(0, 0);
    }

    private void OnSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingSlider)
            return;
        FitToWindow = false;
        Set(e.NewValue / 100.0);
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!HasContent())
            return;
        e.Handled = true;
        FitToWindow = false;
        Set(e.Delta.Y > 0 ? Zoom * 1.25 : Zoom / 1.25);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!HasContent())
            return;
        if (e.GetCurrentPoint(_scroll).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;

        _panStart = e.GetPosition(_scroll);
        _panStartH = _scroll.Offset.X;
        _panStartV = _scroll.Offset.Y;
        e.Pointer.Capture(_scroll);
        _scroll.Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_panStart is not { } start || !e.GetCurrentPoint(_scroll).Properties.IsLeftButtonPressed)
            return;

        Point pos = e.GetPosition(_scroll);
        _scroll.Offset = new Vector(_panStartH - (pos.X - start.X), _panStartV - (pos.Y - start.Y));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _panStart = null;
        e.Pointer.Capture(null);
        _scroll.Cursor = Cursor.Default;
    }
}
