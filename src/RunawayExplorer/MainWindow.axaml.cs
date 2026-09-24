using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Formats;
using RunawayExplorer.Core.Settings;
using RunawayExplorer.Services;
using RunawayExplorer.ViewModels;
using RunawayExplorer.Views;

namespace RunawayExplorer;

/// <summary>
/// The browser + previewer shell. Owns the loaded <see cref="VirtualFileSystem"/>, the currently
/// selected entry, and the sound/video players. Deliberately a thin code-behind rather than MVVM.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly TempFileTracker _tempFiles;

    // Lazily created on first use: constructing a LibVlcMediaPlayer spins up the native libvlc engine,
    // which takes a couple hundred ms -- eating that eagerly here would delay showing the window.
    private LibVlcMediaPlayer? _mediaPlayerBacking;
    private LibVlcMediaPlayer _mediaPlayer => _mediaPlayerBacking ??= CreateSoundPlayer();

    private LibVlcMediaPlayer? _videoPlayerBacking;
    private LibVlcMediaPlayer _videoPlayer => _videoPlayerBacking ??= CreateVideoPlayer();

    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _videoPositionTimer;
    private readonly DispatcherTimer _animTimer;
    private readonly DispatcherTimer _searchDebounceTimer;

    private readonly ZoomController _imageZoom;
    private readonly ZoomController _animZoom;
    private static readonly IBrush Checkerboard = CreateCheckerboard();

    private CancellationTokenSource? _batchExportCts;
    private VirtualFileSystem? _vfs;
    private FsNode? _selectedNode;
    private bool _initializingGameSelector;
    private ResourceContent? _currentContent;

    // Incremented on every new selection; async loads compare their captured value against this and
    // drop stale results (the user clicked away while the load was in flight).
    private int _resourceLoadGeneration;

    private ShortcutsWindow? _shortcutsWindow;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        // Falls back to a private tracker when the host application isn't our App -- only the case
        // under the headless test harness. The real app always supplies the shared one.
        _tempFiles = (Application.Current as App)?.TempFiles ?? new TempFileTracker();

        _imageZoom = new ZoomController(ImageScrollViewer, ImageTransformHost, ImageZoomSlider, ImageZoomLabel,
            () => new Size(ImageStage.Width, ImageStage.Height)) { HasContent = () => ImagePanel.IsVisible };
        _animZoom = new ZoomController(AnimScrollViewer, AnimTransformHost, AnimZoomSlider, AnimZoomLabel,
            () => new Size(AnimStage.Width, AnimStage.Height)) { HasContent = () => AnimationPanel.IsVisible };

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _positionTimer.Tick += PositionTimer_Tick;

        _videoPositionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _videoPositionTimer.Tick += VideoPositionTimer_Tick;

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 15) };
        _animTimer.Tick += AnimTimer_Tick;

        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            _ = ApplyTreeFilterAsync();
        };

        TypeFilterCombo.ItemsSource = ResourceTypeFilter.GetCategories(_settings.Language);
        TypeFilterCombo.SelectedIndex = 0;

        InitializeOptionsMenu();

        SoundVolumeSlider.Value = _settings.Volume;
        VideoVolumeSlider.Value = _settings.Volume;
        SoundVolumeText.Text = $"{_settings.Volume}%";
        VideoVolumeText.Text = $"{_settings.Volume}%";
        UpdateMuteVisuals();

        // Tunnel routing so shortcuts fire even when a child control has keyboard focus.
        AddHandler(KeyDownEvent, MainWindow_PreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DropEvent, Window_Drop);
        VideoScrollViewer.AddHandler(PointerWheelChangedEvent, VideoScrollViewer_PreviewPointerWheelChanged, RoutingStrategies.Tunnel);

        Loaded += MainWindow_Loaded;
        RestoreWindowGeometry();
        Closing += (_, _) => SaveWindowGeometry();
        Closed += (_, _) =>
        {
            _animTimer.Stop();
            // Guard on the backing fields: if no sound/video was ever played, don't spin the native
            // player up just to tear it down.
            _mediaPlayerBacking?.Dispose();
            if (_videoPlayerBacking is not null)
            {
                StopVideo();
                _videoPlayerBacking.Dispose();
            }
        };
    }

    private LibVlcMediaPlayer CreateSoundPlayer()
    {
        var player = new LibVlcMediaPlayer();
        player.Volume = _settings.Volume;
        player.IsMuted = _settings.IsMuted;
        player.MediaOpened += MediaPlayer_MediaOpened;
        player.MediaEnded += MediaPlayer_MediaEnded;
        return player;
    }

    private LibVlcMediaPlayer CreateVideoPlayer()
    {
        var player = new LibVlcMediaPlayer();
        player.Volume = _settings.Volume;
        player.IsMuted = _settings.IsMuted;
        player.MediaEnded += VideoPlayer_MediaEnded;
        player.MediaOpened += VideoPlayer_MediaOpened;

        // TimeChanged fires on LibVLC's callback thread; marshal to the UI thread and use it to preempt
        // the natural end of playback, so the video output never goes black at the end (see
        // HandleVideoTimeChanged).
        player.NativePlayer.TimeChanged += (_, args) =>
        {
            long timeMs = args.Time;
            Dispatcher.UIThread.Post(() => HandleVideoTimeChanged(timeMs));
        };

        // Bind to the VideoView only once its NativeControlHost is attached to the visual tree; before
        // that MediaPlayer.Hwnd stays zero and Play() spawns a separate VLC window instead.
        if (VideoPlayer.GetVisualRoot() is not null)
            VideoPlayer.MediaPlayer = player.NativePlayer;
        else
            VideoPlayer.AttachedToVisualTree += AttachOnce;

        void AttachOnce(object? sender, VisualTreeAttachmentEventArgs e)
        {
            VideoPlayer.AttachedToVisualTree -= AttachOnce;
            VideoPlayer.MediaPlayer = player.NativePlayer;
        }

        return player;
    }

    private static IBrush CreateCheckerboard()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing
        {
            Brush = new SolidColorBrush(Color.Parse("#FF3C3C3C")),
            Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)),
        });
        var lights = new GeometryGroup();
        lights.Children.Add(new RectangleGeometry(new Rect(0, 0, 8, 8)));
        lights.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8)));
        group.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(Color.Parse("#FF505050")), Geometry = lights });
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute),
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Keyboard
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// App-wide keyboard shortcuts. Tunnel-routed so they fire even when the tree has focus, but they
    /// bail out when the user is typing in the search box -- "B" would otherwise steal the letter.
    /// </summary>
    private void MainWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
        bool typing = SearchBox.IsFocused || TextPanel.IsFocused || AnimFpsBox.IsKeyboardFocusWithin;

        if (e.Key == Key.Escape && SettingsOverlay.IsVisible)
        {
            SettingsOverlay.Hide();
            e.Handled = true;
            return;
        }

        if (AnimationExportOverlay.IsVisible)
        {
            if (e.Key == Key.Escape)
            {
                AnimationExportOverlay.Dismiss(AnimationExportFormat.Cancel);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter)
            {
                AnimationExportOverlay.Confirm();
                e.Handled = true;
                return;
            }
            return;
        }

        if (ctrl && e.Key == Key.O)
        {
            OpenSettings_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.E)
        {
            Export_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.P)
        {
            OpenCommandPalette();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.OemComma)
        {
            OpenSettings_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrl && e.Key is Key.OemPlus or Key.Add)
        {
            PreviewZoomIn_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrl && e.Key is Key.OemMinus or Key.Subtract)
        {
            PreviewZoomOut_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrl && e.Key is Key.D0 or Key.NumPad0)
        {
            PreviewResetZoom_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrl && e.Key is Key.D1 or Key.NumPad1)
        {
            PreviewZoom100_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F1)
        {
            ShowShortcutsCheatSheet();
            e.Handled = true;
        }
        else if (!typing && e.Key == Key.Space)
        {
            if (AnimationPanel.IsVisible)
                AnimPlayPause_Click(this, new RoutedEventArgs());
            else if (SoundPanel.IsVisible)
                PlayPauseButton_Click(this, new RoutedEventArgs());
            else if (VideoPanel.IsVisible)
                VideoPlayPauseButton_Click(this, new RoutedEventArgs());
            else
                return;
            e.Handled = true;
        }
        else if (!typing && e.Key == Key.B && (ImageBackgroundGroup.IsVisible && ImagePanel.IsVisible || AnimationPanel.IsVisible))
        {
            CycleBackgroundMode();
            e.Handled = true;
        }
        else if (!typing && AnimationPanel.IsVisible && e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            switch (e.Key)
            {
                case Key.Left: StepAnimation(-1); break;
                case Key.Right: StepAnimation(+1); break;
                case Key.Home: ShowAnimationFrame(0); break;
                case Key.End: ShowAnimationFrame(_animAsset?.FrameCount - 1 ?? 0); break;
            }
            e.Handled = true;
        }
        else if (!typing && SoundPanel.IsVisible && e.Key is Key.Left or Key.Right)
        {
            if (_mediaPlayer.HasDurationTimeSpan)
            {
                TimeSpan target = _mediaPlayer.Position + TimeSpan.FromSeconds(e.Key == Key.Right ? 5 : -5);
                if (target < TimeSpan.Zero) target = TimeSpan.Zero;
                if (_mediaPlayer.Duration is { } dur && target > dur) target = dur;
                _mediaPlayer.Position = target;
                SoundSlider.Value = target.TotalSeconds;
                UpdateSoundTimeReadout();
                e.Handled = true;
            }
        }
        else if (ctrl && e.Key == Key.C && (ImagePanel.IsVisible || AnimationPanel.IsVisible) && !typing)
        {
            PreviewCopyImage_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (!typing && e.Key == Key.M && (SoundPanel.IsVisible || VideoPanel.IsVisible))
        {
            MuteButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Drag and drop of an install folder
    // ---------------------------------------------------------------------------------------------

    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DroppedFolder(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void Window_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DroppedFolder(e) is not { } folder)
            return;
        await OpenInstallAsync(folder);
    }

    private static string? DroppedFolder(DragEventArgs e)
    {
        var items = e.DataTransfer?.TryGetFiles()?.ToList();
        if (items is not { Count: 1 })
            return null;

        string? path = items[0].TryGetLocalPath();
        return !string.IsNullOrEmpty(path) && Directory.Exists(path) ? path : null;
    }

    // ---------------------------------------------------------------------------------------------
    // Window geometry
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Last known bounds while in the Normal state. Tracked continuously because a maximized window
    /// reports its maximized frame, so sampling at close would persist that as the restore bounds.
    /// </summary>
    private PixelRect? _normalBounds;

    private void RestoreWindowGeometry()
    {
        PixelRect? saved = WindowGeometry.FromSettings(_settings.WindowX, _settings.WindowY, _settings.WindowWidth, _settings.WindowHeight);

        if (saved is { } bounds && WindowGeometry.IsRestorable(bounds, ScreenBounds()))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = bounds.Position;
            Width = bounds.Width;
            Height = bounds.Height;
            _normalBounds = bounds;
        }

        if (_settings.WindowMaximized)
            WindowState = WindowState.Maximized;

        PositionChanged += (_, _) => CaptureNormalBounds();
        SizeChanged += (_, _) => CaptureNormalBounds();
    }

    private void CaptureNormalBounds()
    {
        if (WindowState != WindowState.Normal)
            return;
        _normalBounds = new PixelRect(Position.X, Position.Y, (int)Width, (int)Height);
    }

    private void SaveWindowGeometry()
    {
        if (_normalBounds is { } bounds)
        {
            _settings.WindowX = bounds.X;
            _settings.WindowY = bounds.Y;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
        }
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        _settings.Save();
    }

    private IReadOnlyList<PixelRect> ScreenBounds() => Screens?.All?.Select(s => s.Bounds).ToList() ?? [];

    // ---------------------------------------------------------------------------------------------
    // Startup
    // ---------------------------------------------------------------------------------------------

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        // Application-startup behaviour rather than window construction: skip it under the headless
        // test harness, where LibVLC crashes on teardown and the install scan and network call would
        // bleed across tests.
        if (Application.Current is not App)
            return;

        WarmUpMediaEngine();

        _initializingGameSelector = true;
        GameSelectorCombo.SelectedIndex = (int)_settings.ActiveGame;
        _initializingGameSelector = false;

        RefreshRecentInstallsMenu();
        await ReloadActiveGameAsync();

        // Last, so a slow network call can never delay the tree appearing.
        await RunStartupUpdateCheckAsync();
    }

    /// <summary>
    /// Constructs the LibVLC engine in the background so the first sound or video doesn't pay for it.
    /// Not run from the constructor: doing it there would delay showing the window.
    /// </summary>
    private void WarmUpMediaEngine()
    {
        _ = Task.Run(() =>
        {
            try
            {
                _ = LibVlcRuntime.Shared;
                Dispatcher.UIThread.Post(() =>
                {
                    _ = _mediaPlayer;
                    _ = _videoPlayer;
                });
            }
            catch (Exception ex)
            {
                Log.Exception("LibVLC warm-up", ex);
            }
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Install loading
    // ---------------------------------------------------------------------------------------------

    public async Task ReloadActiveGameAsync()
    {
        string? dir = _settings.ActiveGameDir;
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            await InitVfsAsync(dir);
        }
        else
        {
            UnloadCurrentGame();
        }
    }

    private void UnloadCurrentGame()
    {
        _vfs = null;
        Tree.ItemsSource = null;
        ClearContentPanels();
        string gameName = _settings.ActiveGame.GetTitle();
        SetStatus($"No installation folder configured for {gameName}. Open Settings (Options -> Settings) to set the installation folder.");
    }

    private async void GameSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializingGameSelector) return;
        var newGame = (GameVersion)Math.Clamp(GameSelectorCombo.SelectedIndex, 0, 5);
        if (_settings.ActiveGame == newGame && _vfs is not null) return;
        _settings.ActiveGame = newGame;
        _settings.Save();
        await ReloadActiveGameAsync();
    }

    private async Task OpenInstallAsync(string folder)
    {
        GameVersion detected = GameDetector.Detect(folder);
        _settings.SetGameDir(detected, folder);
        _settings.ActiveGame = detected;
        _settings.RegisterRecentInstall(folder);
        _settings.Save();

        _initializingGameSelector = true;
        GameSelectorCombo.SelectedIndex = (int)detected;
        _initializingGameSelector = false;

        await InitVfsAsync(folder);
        RefreshRecentInstallsMenu();
    }

    private async Task InitVfsAsync(string baseDir)
    {
        ScanProgressBar.IsVisible = true;
        ScanProgressBar.IsIndeterminate = true;
        SetStatus($"Scanning \"{baseDir}\"...");

        TypeFilterCombo.SelectedIndex = 0;
        SearchBox.Text = string.Empty;

        try
        {
            ScanCache cache = _settings.UseScanCache ? ScanCache.Load() : ScanCache.Ephemeral();
            VirtualFileSystem vfs = await Task.Run(() => VirtualFileSystem.Init(
                baseDir,
                text => Dispatcher.UIThread.Post(() => SetStatus(text)),
                cache,
                language: _settings.Language));

            _vfs = vfs;
            var rootVm = new FsNodeViewModel(vfs.Root, vfs) { IsExpanded = true };
            Tree.ItemsSource = new[] { rootVm };

            SetStatus($"Loaded \"{baseDir}\"  -  {vfs.Summary}");
            Log.Info($"Loaded {baseDir}: {vfs.Summary} (cache hit: {vfs.Summary.FromCache})");

            // Restore the last selection for this install, deferred so the tree has rendered first.
            if (_settings.LastSelectedPath.TryGetValue(baseDir, out string? savedPath) && !string.IsNullOrEmpty(savedPath))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    FsNode? target = vfs.FindNode(vfs.Root, savedPath);
                    if (target is not null)
                        TryRestoreTreeSelection(rootVm, target);
                }, DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            Log.Exception($"InitVfs {baseDir}", ex);
            await Dialogs.ShowMessageBox(this,
                $"Could not load a Runaway install from:\n{baseDir}\n\n{ex.Message}",
                "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("No Runaway install loaded.");
        }
        finally
        {
            ScanProgressBar.IsVisible = false;
            ScanProgressBar.IsIndeterminate = false;
        }
    }

    private void SetStatus(string text) => SelectedPathText.Text = text;

    private async void SelectedPathText_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_selectedNode is null)
            return;

        var prop = e.GetCurrentPoint(this).Properties;
        if (prop.IsRightButtonPressed)
        {
            await RevealOnDiskAsync(_selectedNode);
            e.Handled = true;
        }
        else if (prop.IsLeftButtonPressed)
        {
            string path = _selectedNode.GetPath();
            await Dialogs.SetClipboardTextAsync(this, path);
            string previousText = SelectedPathText.Text ?? "";
            SelectedPathText.Text = "Copied path to clipboard!";
            _ = Task.Delay(1200).ContinueWith(_ =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (SelectedPathText.Text == "Copied path to clipboard!")
                        SelectedPathText.Text = previousText;
                });
            });
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Tree filter & search
    // ---------------------------------------------------------------------------------------------

    private void TreeFilter_Changed(object? sender, SelectionChangedEventArgs e) => _ = ApplyTreeFilterAsync();

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        SearchClearButton.IsVisible = !string.IsNullOrEmpty(SearchBox.Text);
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void SearchClearButton_Click(object? sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        Tree.Focus();
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Text = string.Empty;
            Tree.Focus();
            e.Handled = true;
        }
    }

    private async Task ApplyTreeFilterAsync()
    {
        if (_vfs is null)
            return;

        VirtualFileSystem vfs = _vfs;
        var typeFilter = TypeFilterCombo.SelectedItem as ResourceTypeFilter ?? ResourceTypeFilter.All;
        string searchText = (SearchBox.Text ?? string.Empty).Trim();
        FsNode? previouslySelected = _selectedNode;

        if (typeFilter == ResourceTypeFilter.All && searchText.Length == 0)
        {
            var rootVm = new FsNodeViewModel(vfs.Root, vfs) { IsExpanded = true };
            Tree.ItemsSource = new[] { rootVm };
            SetStatus($"Loaded \"{vfs.BaseDir}\".");
            TryRestoreTreeSelection(rootVm, previouslySelected);
            return;
        }

        SetStatus("Filtering...");

        bool Matches(FsNode node) =>
            typeFilter.Matches(node) &&
            (searchText.Length == 0 || MatchesSearch(node, searchText));

        // A search hit on a folder name (the archive, "RESOURCE.H09") should show that folder's entries,
        // so the name of every ancestor counts too.
        static bool MatchesSearch(FsNode node, string text)
        {
            for (FsNode? n = node; n is not null && n.Parent is not null; n = n.Parent)
            {
                if (n.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        FsNodeViewModel? filteredRoot = await Task.Run(() => FsNodeViewModel.BuildFiltered(vfs.Root, vfs, Matches));

        if (filteredRoot is not null)
            filteredRoot.IsExpanded = true;

        Tree.ItemsSource = filteredRoot is null ? [] : new[] { filteredRoot };
        SetStatus(filteredRoot is null
            ? "No matching entries."
            : $"Filtered: {typeFilter.Label}" + (searchText.Length > 0 ? $", \"{searchText}\"" : string.Empty));

        if (filteredRoot is not null)
            TryRestoreTreeSelection(filteredRoot, previouslySelected);
    }

    /// <summary>
    /// Walks the (possibly-lazy) tree rooted at <paramref name="rootVm"/> to <paramref name="target"/>,
    /// expanding directory VMs as it descends and selecting the final match. No-ops when the target has
    /// been filtered out.
    /// </summary>
    private void TryRestoreTreeSelection(FsNodeViewModel rootVm, FsNode? target)
    {
        if (target is null)
            return;

        var path = new List<FsNode>();
        for (FsNode? cursor = target; cursor is not null && cursor.Parent is not null; cursor = cursor.Parent)
            path.Add(cursor);
        path.Reverse();

        FsNodeViewModel cursorVm = rootVm;
        foreach (FsNode segment in path)
        {
            if (!cursorVm.IsExpanded)
                cursorVm.IsExpanded = true;

            FsNodeViewModel? next = cursorVm.Children.FirstOrDefault(c => ReferenceEquals(c.Node, segment));
            if (next is null)
                return;
            cursorVm = next;
        }

        FsNodeViewModel targetVm = cursorVm;
        Dispatcher.UIThread.Post(() =>
        {
            targetVm.IsExpanded = targetVm.IsDirectory && targetVm.IsExpanded;
            BringVmIntoViewAndSelect(targetVm);
        }, DispatcherPriority.Background);
    }

    private void BringVmIntoViewAndSelect(FsNodeViewModel vm)
    {
        Tree.SelectedItem = vm;

        if (Tree.FindDescendantOfType<ScrollViewer>() is { } sv)
            sv.Offset = default;

        if (Tree.TreeContainerFromItem(vm) is { } container)
            container.BringIntoView();

        // BringIntoView also scrolls horizontally to reveal deep items; snap X back so the hierarchy
        // stays readable.
        Dispatcher.UIThread.Post(() =>
        {
            if (Tree.FindDescendantOfType<ScrollViewer>() is { } sv2)
                sv2.Offset = sv2.Offset.WithX(0);
        }, DispatcherPriority.Background);
    }

    // ---------------------------------------------------------------------------------------------
    // Menu: File
    // ---------------------------------------------------------------------------------------------

    private void SelectFolder_Click(object? sender, RoutedEventArgs e)
    {
        OpenSettings_Click(sender, e);
    }

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedNode is null || _currentContent is null)
        {
            await Dialogs.ShowMessageBox(this, "Select an entry to export first.", "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await ExportContentAsync(_selectedNode, _currentContent);
    }

    /// <summary>Exports whatever the viewer holds for <paramref name="node"/>, in its natural format.</summary>
    private async Task ExportContentAsync(FsNode node, ResourceContent content)
    {
        string stem = Path.GetFileNameWithoutExtension(BatchExporter.ExportFileName(node));
        if (node.IsDirectory)
            stem = BatchExporter.SanitizeSegment(node.Name) + "_background";

        switch (content)
        {
            case ImageResource image:
                await ExportImageAsync(image.Image, stem + ".png");
                break;

            case SceneResource { Background: { } bg }:
                await ExportImageAsync(bg, stem + ".png");
                break;

            case AnimationResource anim:
                await ExportAnimationAsync(anim.Asset, stem + ".png");
                break;

            case TextResource text:
                await ExportTextAsync(text.Text, node.Kind == EntryKind.Viseme ? stem + ".txt" : stem + ".txt");
                break;

            case SoundResource sound:
            {
                string ext = Path.GetExtension(sound.TempFilePath);
                string filterDesc = ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ? "MP3 audio" : "WAV audio";
                await ExportExtractedFileAsync(sound.TempFilePath, stem + ext, filterDesc);
                break;
            }

            case VideoResource video:
                await ExportExtractedFileAsync(video.TempFilePath, stem + ".bik", "Bink video");
                break;

            case ErrorResource error:
                await Dialogs.ShowMessageBox(this, error.Message, "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;

            default:
                await Dialogs.ShowMessageBox(this, "Nothing to export for this selection.", "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
        }
    }

    private async Task ExportImageAsync(DecodedImage image, string defaultName)
    {
        string? path = await Dialogs.ShowSaveFileDialog(this, "Export Image", defaultName,
            [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }], _settings.LastExportDir);
        if (path is null)
            return;

        try
        {
            await Task.Run(() => PngWriter.Write(image, path));
            RememberExportFolder(path);
            SetStatus($"Exported {path}");
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageBox(this, $"Export failed:\n{ex.Message}", "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ExportAnimationAsync(SpriteAsset asset, string defaultName)
    {
        AnimationExportFormat format = await AnimationExportOverlay.ShowAsync();
        if (format == AnimationExportFormat.Cancel)
            return;

        if (format == AnimationExportFormat.Apng)
        {
            string title = LocalizationManager.Instance.GetString("ExportAnim_SaveApng_Title", "Export Animation (APNG)");
            string? path = await Dialogs.ShowSaveFileDialog(this, title, defaultName,
                [new FilePickerFileType("Animated PNG") { Patterns = ["*.png"] }], _settings.LastExportDir);
            if (path is null)
                return;

            double fps = _settings.AnimationFps;
            try
            {
                (int total, _) = await Task.Run(() => BatchExporter.ExportAnimation(asset, path, fps));
                RememberExportFolder(path);
                SetStatus($"Exported {path} ({total} frames at {fps:0.#} fps).");
            }
            catch (Exception ex)
            {
                await Dialogs.ShowMessageBox(this, $"Export failed:\n{ex.Message}", "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else if (format == AnimationExportFormat.ImageSequence)
        {
            string stem = Path.GetFileNameWithoutExtension(defaultName);
            string title = LocalizationManager.Instance.GetString("ExportAnim_SaveSequence_Title", "Select Destination Folder for Image Sequence");
            string? folder = await Dialogs.ShowOpenFolderDialog(this, title, _settings.LastExportDir);
            if (folder is null)
                return;

            try
            {
                string targetDir = Path.GetFileName(folder).Equals(stem, StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(folder).Equals(stem + "_frames", StringComparison.OrdinalIgnoreCase)
                    ? folder
                    : Path.Combine(folder, stem + "_frames");

                int written = await Task.Run(() => BatchExporter.ExportImageSequence(asset, targetDir));
                RememberExportFolder(folder);
                SetStatus($"Exported {written} frame(s) to {targetDir}");
            }
            catch (Exception ex)
            {
                await Dialogs.ShowMessageBox(this, $"Export failed:\n{ex.Message}", "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async Task ExportTextAsync(string text, string defaultName)
    {
        string? path = await Dialogs.ShowSaveFileDialog(this, "Export Text", defaultName,
            [new FilePickerFileType("Text file") { Patterns = ["*.txt"] }], _settings.LastExportDir);
        if (path is null)
            return;

        try
        {
            File.WriteAllText(path, text);
            RememberExportFolder(path);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageBox(this, $"Export failed:\n{ex.Message}", "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ExportExtractedFileAsync(string tempFilePath, string defaultName, string description)
    {
        string extension = Path.GetExtension(defaultName).TrimStart('.');
        string? path = await Dialogs.ShowSaveFileDialog(this, "Export", defaultName,
            [new FilePickerFileType(description) { Patterns = [$"*.{extension}"] }], _settings.LastExportDir);
        if (path is null)
            return;

        try
        {
            File.Copy(tempFilePath, path, overwrite: true);
            RememberExportFolder(path);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageBox(this, $"Export failed:\n{ex.Message}", "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportRaw_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedNode is null || !_selectedNode.IsFile || _vfs is null)
        {
            await Dialogs.ShowMessageBox(this, "Select an entry to export first.", "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await ExportRawAsync(_selectedNode);
    }

    /// <summary>Writes <paramref name="node"/>'s bytes verbatim. Takes the node explicitly so the tree's context menu can export a row without first loading it.</summary>
    private async Task ExportRawAsync(FsNode node)
    {
        if (_vfs is null)
            return;

        bool loose = (node.NodeType & FsNodeType.InArchive) == 0;
        string defaultName = loose ? node.Name : $"{BatchExporter.SanitizeSegment(node.Parent?.Name ?? "entry")}_{node.Name}.bin";
        string ext = Path.GetExtension(defaultName).TrimStart('.');
        if (ext.Length == 0) ext = "bin";

        string? path = await Dialogs.ShowSaveFileDialog(this, "Export Raw", defaultName,
            [new FilePickerFileType($"{ext.ToUpperInvariant()} file") { Patterns = [$"*.{ext}"] }], _settings.LastExportDir);
        if (path is null)
            return;

        try
        {
            using Stream source = _vfs.OpenFile(node);
            using FileStream dest = File.Create(path);
            await source.CopyToAsync(dest);
            RememberExportFolder(path);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageBox(this, $"Raw export failed:\n{ex.Message}", "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BatchExport_Click(object? sender, RoutedEventArgs e)
    {
        if (_vfs is null)
        {
            await Dialogs.ShowMessageBox(this, "Load a Runaway install first.", "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        FsNode sourceRoot = _selectedNode is { IsDirectory: true } dir ? dir : _vfs.Root;
        await BatchExportAsync(sourceRoot);
    }

    private async Task BatchExportAsync(FsNode sourceRoot)
    {
        if (_vfs is null)
            return;

        string? outputDir = await Dialogs.ShowOpenFolderDialog(this,
            $"Choose output folder for batch export of \"{sourceRoot.GetPath()}\"", _settings.LastExportDir);
        if (outputDir is null)
            return;

        RememberExportFolder(outputDir);
        ScanProgressBar.IsVisible = true;
        ScanProgressBar.IsIndeterminate = false;
        ScanProgressBar.Minimum = 0;
        ScanProgressBar.Maximum = 1;
        ScanProgressBar.Value = 0;
        CancelBatchButton.IsVisible = true;
        CancelBatchButton.IsEnabled = true;
        SetStatus($"Exporting {sourceRoot.GetPath()} to {outputDir}...");

        VirtualFileSystem vfs = _vfs;
        var options = new BatchExportOptions(_settings.AnimationFps, _settings.VoiceSampleRate);

        _batchExportCts?.Cancel();
        _batchExportCts = new CancellationTokenSource();
        CancellationToken token = _batchExportCts.Token;

        // Throttle progress: a Dispatcher post per file floods the UI thread on thousands of entries.
        DateTime lastUiUpdate = DateTime.MinValue;

        try
        {
            BatchExportSummary summary = await Task.Run(() => BatchExporter.ExportSubtree(
                sourceRoot, vfs, outputDir, options,
                progress: p =>
                {
                    DateTime now = DateTime.UtcNow;
                    if ((now - lastUiUpdate).TotalMilliseconds < 40 && p.Index != p.Total)
                        return;
                    lastUiUpdate = now;
                    Dispatcher.UIThread.Post(() =>
                    {
                        ScanProgressBar.Maximum = Math.Max(1, p.Total);
                        ScanProgressBar.Value = p.Index;
                        SetStatus($"{p.Index} / {p.Total}: {p.RelativePath}");
                    });
                },
                cancellationToken: token));

            if (token.IsCancellationRequested)
            {
                SetStatus($"Batch export cancelled after {summary.ExportedCount} entr{(summary.ExportedCount == 1 ? "y" : "ies")}.");
            }
            else
            {
                await Dialogs.ShowMessageBox(this,
                    $"Batch export complete.\n\n" +
                    $"Exported: {summary.ExportedCount}\n" +
                    $"Skipped (not decodable): {summary.SkippedCount}\n" +
                    $"Failed: {summary.FailedCount}\n\n" +
                    $"Output folder: {outputDir}",
                    "Runaway Explorer", MessageBoxButton.OK,
                    summary.FailedCount == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                SetStatus($"Batch export finished ({summary.ExportedCount} exported, {summary.FailedCount} failed).");
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Batch export cancelled.");
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageBox(this, $"Batch export failed:\n{ex.Message}", "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Batch export failed.");
        }
        finally
        {
            ScanProgressBar.IsVisible = false;
            CancelBatchButton.IsVisible = false;
            _batchExportCts?.Dispose();
            _batchExportCts = null;
        }
    }

    private void CancelBatchButton_Click(object? sender, RoutedEventArgs e)
    {
        _batchExportCts?.Cancel();
        CancelBatchButton.IsEnabled = false;
    }

    private void OpenCommandPalette()
    {
        if (_vfs is null)
            return;
        _ = OpenCommandPaletteAsync();
    }

    private async Task OpenCommandPaletteAsync()
    {
        var palette = new CommandPaletteWindow(_vfs!);
        FsNode? selected = await palette.ShowDialog<FsNode?>(this);
        if (selected is null)
            return;

        FsNodeViewModel? rootVm = (Tree.ItemsSource as IEnumerable<FsNodeViewModel>)?.FirstOrDefault();
        if (rootVm is not null)
            TryRestoreTreeSelection(rootVm, selected);
        else
            LoadSelectedResource(selected);
    }

    private void RememberExportFolder(string chosenPath)
    {
        string? dir = Directory.Exists(chosenPath) ? chosenPath : Path.GetDirectoryName(chosenPath);
        if (string.IsNullOrEmpty(dir) || dir == _settings.LastExportDir)
            return;
        _settings.LastExportDir = dir;
        _settings.Save();
    }

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------------------------------------------
    // Tree context menu
    // ---------------------------------------------------------------------------------------------

    /// <summary>The node the menu was opened on. Right-clicking a row doesn't select it, so the selection is not a reliable stand-in.</summary>
    private FsNode? _contextTargetNode;
    private FsNodeViewModel? _contextTargetVm;

    private FsNode? ResolveContextTarget() => _contextTargetNode ?? (Tree.SelectedItem as FsNodeViewModel)?.Node;

    private void TreeContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _contextTargetVm = (sender as ContextMenu)?.PlacementTarget switch
        {
            Control control => control.DataContext as FsNodeViewModel
                               ?? control.FindAncestorOfType<TreeViewItem>()?.DataContext as FsNodeViewModel,
            _ => null,
        } ?? (Tree.SelectedItem as FsNodeViewModel);
        _contextTargetNode = _contextTargetVm?.Node;

        TreeContextActions actions = TreeContextActions.For(_contextTargetNode);
        if (actions.IsEmpty)
        {
            e.Cancel = true;
            return;
        }

        TreeMenuCopyPath.IsVisible = actions.CopyPath;
        TreeMenuReveal.IsVisible = actions.RevealInExplorer;
        TreeMenuExpandSeparator.IsVisible = actions.HasExpandGroup;
        TreeMenuExpandAll.IsVisible = actions.ExpandAll;
        TreeMenuCollapseAll.IsVisible = actions.CollapseAll;
        TreeMenuExportSeparator.IsVisible = actions.HasExportGroup;
        TreeMenuExportItem.IsVisible = actions.ExportItem;
        TreeMenuExportRaw.IsVisible = actions.ExportRaw;
        TreeMenuBatchExport.IsVisible = actions.BatchExportFolder;
    }

    private void TreeContextExpandAll_Click(object? sender, RoutedEventArgs e)
    {
        (_contextTargetVm ?? Tree.SelectedItem as FsNodeViewModel)?.ExpandAll();
    }

    private void TreeContextCollapseAll_Click(object? sender, RoutedEventArgs e)
    {
        (_contextTargetVm ?? Tree.SelectedItem as FsNodeViewModel)?.CollapseAll();
    }

    private async void TreeContextCopyPath_Click(object? sender, RoutedEventArgs e)
    {
        FsNode? node = ResolveContextTarget();
        if (node is null)
            return;
        await Dialogs.SetClipboardTextAsync(this, node.GetPath());
        SetStatus($"Copied path: {node.GetPath()}");
    }

    private async void TreeContextRevealInExplorer_Click(object? sender, RoutedEventArgs e)
    {
        FsNode? node = ResolveContextTarget();
        if (node is null)
            return;
        await RevealOnDiskAsync(node);
    }

    private async Task RevealOnDiskAsync(FsNode node)
    {
        // Archive entries live inside a file; reveal the archive itself in that case.
        string? path = node.ArchivePath;
        if (string.IsNullOrEmpty(path))
        {
            await Dialogs.ShowMessageBox(this, "This item has no file on disk.", "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsLinux())
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{Path.GetDirectoryName(path)}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("open", $"-R \"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageBox(this, $"Could not open file manager:\n{ex.Message}", "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TreeContextExportItem_Click(object? sender, RoutedEventArgs e)
    {
        FsNode? node = ResolveContextTarget();
        if (node is null || !node.IsFile || _vfs is null)
            return;

        VirtualFileSystem vfs = _vfs;
        AppSettings settings = _settings;
        TempFileTracker temp = _tempFiles;
        ResourceContent content = await Task.Run(() => ResourceLoader.Load(node, vfs, settings, temp));
        await ExportContentAsync(node, content);
    }

    private async void TreeContextExportRaw_Click(object? sender, RoutedEventArgs e)
    {
        FsNode? node = ResolveContextTarget();
        if (node is null || !node.IsFile)
            return;
        await ExportRawAsync(node);
    }

    private async void TreeContextBatchExport_Click(object? sender, RoutedEventArgs e)
    {
        FsNode? node = ResolveContextTarget();
        if (node is null || !node.IsDirectory)
            return;
        await BatchExportAsync(node);
    }

    // ---------------------------------------------------------------------------------------------
    // Preview context menu
    // ---------------------------------------------------------------------------------------------

    private void PreviewContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        PreviewContextActions actions = PreviewContextActions.For(_currentContent, _selectedNode);
        if (actions.IsEmpty)
        {
            e.Cancel = true;
            return;
        }

        PreviewMenuZoomIn.IsVisible = actions.Zoom;
        PreviewMenuZoomOut.IsVisible = actions.Zoom;
        PreviewMenuFit.IsVisible = actions.Zoom;
        PreviewMenuZoom100.IsVisible = actions.Zoom;
        PreviewMenuCopyImage.IsVisible = actions.CopyImage;
        PreviewMenuCopySelection.IsVisible = actions.CopyText;
        PreviewMenuCopyText.IsVisible = actions.CopyText;
        PreviewMenuOpenExternal.IsVisible = actions.OpenExternally;
        PreviewMenuViewSeparator.IsVisible = actions.HasViewGroup;
        PreviewMenuExport.IsVisible = actions.Export;
        PreviewMenuExportRaw.IsVisible = actions.ExportRaw;
        PreviewMenuPathSeparator.IsVisible = actions.CopyPath || actions.RevealInExplorer;
        PreviewMenuCopyPath.IsVisible = actions.CopyPath;
        PreviewMenuReveal.IsVisible = actions.RevealInExplorer;
    }

    private ZoomController? ActiveZoom => AnimationPanel.IsVisible ? _animZoom : ImagePanel.IsVisible ? _imageZoom : null;

    private void PreviewZoomIn_Click(object? sender, RoutedEventArgs e) => ActiveZoom?.In();

    private void PreviewZoomOut_Click(object? sender, RoutedEventArgs e) => ActiveZoom?.Out();

    private void PreviewResetZoom_Click(object? sender, RoutedEventArgs e) => ActiveZoom?.Fit();

    private void PreviewZoom100_Click(object? sender, RoutedEventArgs e) => ActiveZoom?.Zoom100();

    private async void PreviewCopyImage_Click(object? sender, RoutedEventArgs e)
    {
        await CopyCurrentImageToClipboardAsync();
    }

    private async Task CopyCurrentImageToClipboardAsync()
    {
        DecodedImage? image = null;
        if (_currentContent is ImageResource img)
        {
            image = img.Image;
        }
        else if (_currentContent is AnimationResource)
        {
            if (_animAsset is not null && _animFrames is not null && _animFrameIndex >= 0 && _animFrameIndex < _animFrames.Length)
            {
                SpriteFrame frame = _animFrames[_animFrameIndex] ??= _animAsset.DecodeFrame(_animFrameIndex);
                image = frame.Image;
            }
        }
        else if (_currentContent is SceneResource scene)
        {
            image = scene.Background;
        }

        if (image is null)
            return;

        await Dialogs.SetClipboardImageAsync(this, image);
        SetStatus("Copied image to clipboard.");
    }

    private async void PreviewCopySelection_Click(object? sender, RoutedEventArgs e)
    {
        string selection = TextPanel.SelectedText;
        await Dialogs.SetClipboardTextAsync(this, string.IsNullOrEmpty(selection) ? TextPanel.Text ?? "" : selection);
        SetStatus(string.IsNullOrEmpty(selection) ? "Copied all text." : "Copied selection.");
    }

    private async void PreviewCopyAllText_Click(object? sender, RoutedEventArgs e)
    {
        await Dialogs.SetClipboardTextAsync(this, TextPanel.Text ?? "");
        SetStatus("Copied all text.");
    }

    private async void PreviewCopyPath_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedNode is null)
            return;
        await Dialogs.SetClipboardTextAsync(this, _selectedNode.GetPath());
        SetStatus($"Copied path: {_selectedNode.GetPath()}");
    }

    private async void PreviewReveal_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedNode is not null)
            await RevealOnDiskAsync(_selectedNode);
    }

    // ---------------------------------------------------------------------------------------------
    // Menu: Options / Help
    // ---------------------------------------------------------------------------------------------

    private void InitializeOptionsMenu()
    {
        AutoPlaySoundMenuItem.IsChecked = _settings.AutoPlaySound;
        AutoPlayVideoMenuItem.IsChecked = _settings.AutoPlayVideo;
        LoopSoundMenuItem.IsChecked = _settings.LoopSoundPlayback;
        UpdateMenuBackgroundChecks();
        AnimLoopCheck.IsChecked = _settings.LoopAnimation;
        AnimFpsBox.Value = (decimal)_settings.AnimationFps;
        _animTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _settings.AnimationFps));
        RefreshRecentInstallsMenu();
        ApplyTheme(_settings.Theme);
        ApplyLanguage(_settings.Language);
    }

    internal void ApplyTheme(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme switch
        {
            "Light" => Avalonia.Styling.ThemeVariant.Light,
            "System" => Avalonia.Styling.ThemeVariant.Default,
            _ => Avalonia.Styling.ThemeVariant.Dark,
        };
    }

    internal void ApplyLanguage(string language)
    {
        _settings.Language = language;
        _settings.Save();
        LocalizationManager.Instance.SetLanguage(language);

        if (_vfs is not null)
        {
            _vfs.ApplyLanguage(language);
            if (Tree.ItemsSource is IEnumerable<FsNodeViewModel> roots)
            {
                foreach (FsNodeViewModel root in roots)
                    root.NotifyDisplayNameChanged();
            }
        }

        int prevFilterIdx = TypeFilterCombo.SelectedIndex;
        TypeFilterCombo.ItemsSource = ResourceTypeFilter.GetCategories(language);
        TypeFilterCombo.SelectedIndex = Math.Max(0, prevFilterIdx);
    }

    private void OpenSettings_Click(object? sender, RoutedEventArgs e) => SettingsOverlay.Show(this, _settings);

    private void AutoPlaySound_Click(object? sender, RoutedEventArgs e)
    {
        _settings.AutoPlaySound = AutoPlaySoundMenuItem.IsChecked;
        _settings.Save();
    }

    private void AutoPlayVideo_Click(object? sender, RoutedEventArgs e)
    {
        _settings.AutoPlayVideo = AutoPlayVideoMenuItem.IsChecked;
        _settings.Save();
    }

    private void LoopSound_Click(object? sender, RoutedEventArgs e)
    {
        _settings.LoopSoundPlayback = LoopSoundMenuItem.IsChecked;
        _settings.Save();
    }

    private void BackgroundNo_Click(object? sender, RoutedEventArgs e) => SetBackgroundMode("no");
    private void BackgroundYes_Click(object? sender, RoutedEventArgs e) => SetBackgroundMode("yes");
    private void BackgroundGreyed_Click(object? sender, RoutedEventArgs e) => SetBackgroundMode("greyed");

    private void SetBackgroundMode(string mode)
    {
        if (string.Equals(_settings.BackgroundMode, mode, StringComparison.OrdinalIgnoreCase))
            return;
        _settings.BackgroundMode = mode;
        _settings.Save();
        OnShowOnBackgroundChanged();
    }

    private void CycleBackgroundMode()
    {
        int currentIndex = BackgroundModeToIndex(_settings.BackgroundMode);
        int nextIndex = (currentIndex + 1) % 3;
        SetBackgroundMode(IndexToBackgroundMode(nextIndex));
    }

    private static int BackgroundModeToIndex(string mode) => mode.ToLowerInvariant() switch
    {
        "no" => 0,
        "greyed" => 2,
        _ => 1,
    };

    private static string IndexToBackgroundMode(int index) => index switch
    {
        0 => "no",
        2 => "greyed",
        _ => "yes",
    };

    private void UpdateMenuBackgroundChecks()
    {
        string mode = _settings.BackgroundMode.ToLowerInvariant();
        BackgroundNoMenuItem.IsChecked = mode == "no";
        BackgroundYesMenuItem.IsChecked = mode == "yes";
        BackgroundGreyedMenuItem.IsChecked = mode == "greyed";
    }

    /// <summary>Called after the setting changed anywhere: re-sync every control that mirrors it and redraw the stage.</summary>
    internal void OnShowOnBackgroundChanged()
    {
        UpdateMenuBackgroundChecks();
        string mode = _settings.BackgroundMode.ToLowerInvariant();
        _syncingBackgroundToggles = true;
        try
        {
            ImageBgNoRadio.IsChecked = mode == "no";
            ImageBgYesRadio.IsChecked = mode == "yes";
            ImageBgGreyedRadio.IsChecked = mode == "greyed";

            AnimBgNoRadio.IsChecked = mode == "no";
            AnimBgYesRadio.IsChecked = mode == "yes";
            AnimBgGreyedRadio.IsChecked = mode == "greyed";
        }
        finally
        {
            _syncingBackgroundToggles = false;
        }

        if (_currentContent is ImageResource image)
            LayoutImageStage(image);
        else if (_currentContent is AnimationResource)
            LayoutAnimationStage();
    }

    internal void OnAnimationFpsChanged()
    {
        _animTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _settings.AnimationFps));
        _syncingFps = true;
        try { AnimFpsBox.Value = (decimal)_settings.AnimationFps; }
        finally { _syncingFps = false; }
    }

    internal void OnLoopAnimationChanged() => AnimLoopCheck.IsChecked = _settings.LoopAnimation;

    internal async Task ClearScanCacheAndReloadAsync()
    {
        ScanCache.Load().Clear();
        try
        {
            if (File.Exists(ScanCache.DefaultPath))
                File.Delete(ScanCache.DefaultPath);
        }
        catch (IOException ex)
        {
            Log.Exception("Delete scan cache", ex);
        }

        if (_vfs is not null)
            await InitVfsAsync(_vfs.BaseDir);
    }

    private void RefreshRecentInstallsMenu()
    {
        RecentInstallsMenu.Items.Clear();
        RecentInstallsMenu.IsEnabled = _settings.RecentInstalls.Count > 0;
        foreach (string path in _settings.RecentInstalls)
        {
            var item = new MenuItem { Header = path, Tag = path };
            item.Click += RecentInstallItem_Click;
            RecentInstallsMenu.Items.Add(item);
        }
    }

    private async void RecentInstallItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string path })
            return;
        if (!Directory.Exists(path))
        {
            _settings.RecentInstalls.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _settings.Save();
            RefreshRecentInstallsMenu();
            await Dialogs.ShowMessageBox(this, $"Install folder no longer exists:\n{path}", "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await OpenInstallAsync(path);
    }

    private async void About_Click(object? sender, RoutedEventArgs e) => await new AboutWindow(_settings).ShowDialog(this);

    private void Shortcuts_Click(object? sender, RoutedEventArgs e) => ShowShortcutsCheatSheet();

    private void ShowShortcutsCheatSheet()
    {
        if (_shortcutsWindow is not null)
        {
            _shortcutsWindow.Activate();
            return;
        }

        _shortcutsWindow = new ShortcutsWindow();
        _shortcutsWindow.Closed += (_, _) => _shortcutsWindow = null;
        _shortcutsWindow.Show(this);
    }

    private async void CheckForUpdates_Click(object? sender, RoutedEventArgs e) => await UpdateUi.CheckInteractiveAsync(this, _settings);

    private async void OpenLogFile_Click(object? sender, RoutedEventArgs e)
    {
        if (!File.Exists(Log.FilePath))
        {
            await Dialogs.ShowMessageBox(this, $"No log file has been written yet.\n\nIt will appear at:\n{Log.FilePath}",
                "Open Log File", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Exception("Failed to open log file", ex);
            await Dialogs.ShowMessageBox(this, $"Could not open the log file.\n\n{ex.Message}", "Open Log File", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- Startup update check ----------------------------------------------------------------

    /// <summary>
    /// The quiet half of the update flow. Unlike the Help menu's explicit check this reports nothing
    /// unless there is genuinely something newer, and on first launch it asks for consent instead of
    /// checking.
    /// </summary>
    private async Task RunStartupUpdateCheckAsync()
    {
        if (_settings.UpdateCheckMode == "Ask")
        {
            await PromptForUpdateConsentAsync();
            return;
        }

        if (_settings.UpdateCheckMode != "OnStartup")
            return;

        if (_settings.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < UpdateChecker.StartupCheckInterval)
            return;

        UpdateCheckResult result = await UpdateChecker.CheckAsync(_settings.ReleaseFeedUrl, AppInfo.Version);
        if (result.Error is not null)
            return;

        _settings.LastUpdateCheckUtc = DateTime.UtcNow;
        _settings.Save();

        if (!result.UpdateAvailable)
            return;

        if (UpdateChecker.TryParseTag(_settings.SkippedUpdateVersion) is { } skipped && !UpdateChecker.IsNewer(skipped, result.LatestVersion))
            return;

        ShowUpdateBanner(result);
    }

    private async Task PromptForUpdateConsentAsync()
    {
        MessageBoxResult choice = await Dialogs.ShowMessageBox(this,
            "Check GitHub for new versions of Runaway Explorer automatically?\n\n" +
            "This contacts github.com at most once a day and only reads the latest release number. " +
            "Nothing is downloaded or installed without asking you.\n\n" +
            "You can change this later in Options > Settings.",
            "Automatic Update Checks", MessageBoxButton.YesNo, MessageBoxImage.Question);

        _settings.UpdateCheckMode = choice == MessageBoxResult.Yes ? "OnStartup" : "Never";
        _settings.Save();

        if (choice == MessageBoxResult.Yes)
            await RunStartupUpdateCheckAsync();
    }

    private UpdateCheckResult _pendingUpdate;

    private void ShowUpdateBanner(UpdateCheckResult result)
    {
        _pendingUpdate = result;
        UpdateBannerText.Text = $"Runaway Explorer {result.LatestVersion} is available -- you have {AppInfo.DisplayVersion}.";
        UpdateBanner.IsVisible = true;
    }

    private void UpdateBannerDownload_Click(object? sender, RoutedEventArgs e)
    {
        UpdateUi.OpenReleasePage(_pendingUpdate.ReleaseUrl, _settings);
        UpdateBanner.IsVisible = false;
    }

    private void UpdateBannerSkip_Click(object? sender, RoutedEventArgs e)
    {
        _settings.SkippedUpdateVersion = _pendingUpdate.LatestVersion;
        _settings.Save();
        UpdateBanner.IsVisible = false;
    }

    private void UpdateBannerDismiss_Click(object? sender, RoutedEventArgs e) => UpdateBanner.IsVisible = false;

    // ---------------------------------------------------------------------------------------------
    // Tree selection
    // ---------------------------------------------------------------------------------------------

    private void Tree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Tree.SelectedItem is not FsNodeViewModel { IsPlaceholder: false } vm)
            return;

        SetStatus(vm.Node.GetPath());
        PersistLastSelectedPath(vm.Node);

        if (vm.IsFile)
            LoadSelectedResource(vm.Node);
        else
            LoadSelectedFolder(vm.Node);
    }

    private void PersistLastSelectedPath(FsNode node)
    {
        if (_vfs is null)
            return;
        string path = node.GetPath();
        if (_settings.LastSelectedPath.TryGetValue(_vfs.BaseDir, out string? existing) && existing == path)
            return;
        _settings.LastSelectedPath[_vfs.BaseDir] = path;
        _settings.Save();
    }

    /// <summary>A folder selection: a scene archive shows its background and a summary; other folders clear the viewer.</summary>
    private async void LoadSelectedFolder(FsNode folder)
    {
        StopSound();
        _selectedNode = folder;
        _currentContent = null;
        int generation = ++_resourceLoadGeneration;
        ClearContentPanels();

        if (_vfs is null)
            return;

        bool isSceneArchive = folder.Parent?.Name == VirtualFileSystem.ScenesFolder && folder.ArchivePath is not null;
        if (!isSceneArchive)
        {
            SetStatus($"{folder.GetPath()}  -  {CountFiles(folder)} entr{(CountFiles(folder) == 1 ? "y" : "ies")}");
            return;
        }

        VirtualFileSystem vfs = _vfs;
        SceneResource scene = await Task.Run(() => ResourceLoader.LoadScene(folder, vfs));
        if (generation != _resourceLoadGeneration)
            return;

        _currentContent = scene;
        ShowContent(scene);
        SetStatus($"{folder.GetPath()}  -  {scene.Summary}");
    }

    private static int CountFiles(FsNode folder)
    {
        int n = 0;
        foreach (FsNode c in folder.Children)
            n += c.IsFile ? 1 : CountFiles(c);
        return n;
    }

    private async void LoadSelectedResource(FsNode node)
    {
        if (_vfs is null)
            return;

        StopSound();
        _selectedNode = node;

        int generation = ++_resourceLoadGeneration;
        VirtualFileSystem vfs = _vfs;
        AppSettings settings = _settings;
        TempFileTracker tempFiles = _tempFiles;
        SetStatus($"{node.GetPath()}  -  loading...");

        ResourceContent content;
        try
        {
            content = await Task.Run(() => ResourceLoader.Load(node, vfs, settings, tempFiles));
        }
        catch (Exception ex)
        {
            content = new ErrorResource($"Failed to load \"{node.GetPath()}\":\n{ex.Message}");
        }

        if (generation != _resourceLoadGeneration)
            return;

        _currentContent = content;
        ShowContent(content);
        SetStatus($"{node.GetPath()}  -  {FormatContentMetadata(node, content)}");
    }

    private static string FormatContentMetadata(FsNode node, ResourceContent content)
    {
        string sizeText = VirtualFileSystem.FormatSize(node.Size);
        string details = content switch
        {
            ImageResource { Positioned: true } i => $"{i.Kind}, {i.Image.Width}×{i.Image.Height} at screen {i.X},{i.Y}",
            ImageResource i => $"{i.Kind}, {i.Image.Width}×{i.Image.Height}",
            AnimationResource a => $"animation, {a.Asset.FrameCount} frames, bounding box {a.Asset.Bounds.Width}×{a.Asset.Bounds.Height} at screen {a.Asset.Bounds.X},{a.Asset.Bounds.Y}" +
                                   (a.Asset.DescriptorCount > 0 ? $", {a.Asset.DescriptorCount} descriptor record(s) skipped" : ""),
            SoundResource s => s.Pcm.Format switch
            {
                AudioFormat.Mp3 => $"MP3 audio, {VirtualFileSystem.FormatDuration(s.DurationSeconds)}",
                AudioFormat.Wav => $"{s.Pcm.SampleRate:N0} Hz, {s.Pcm.Channels}ch, {s.Pcm.BitsPerSample}-bit WAV, {VirtualFileSystem.FormatDuration(s.DurationSeconds)}",
                _ => $"{s.Pcm.SampleRate:N0} Hz, {s.Pcm.Channels}ch, {s.Pcm.BitsPerSample}-bit PCM, {VirtualFileSystem.FormatDuration(s.DurationSeconds)}",
            },
            VideoResource => "Bink video, header restored",
            TextResource t => $"{t.Text.Length:N0} chars",
            ErrorResource => "load failed",
            _ => "",
        };
        return string.IsNullOrEmpty(details) ? sizeText : $"{sizeText}  |  {details}";
    }

    // ---------------------------------------------------------------------------------------------
    // Content panel switching
    // ---------------------------------------------------------------------------------------------

    private void ClearContentPanels()
    {
        ImagePanel.IsVisible = false;
        AnimationPanel.IsVisible = false;
        TextPanel.IsVisible = false;
        SoundPanel.IsVisible = false;
        VideoPanel.IsVisible = false;

        StopVideo();
        StopAnimation();
        _animAsset = null;
        _animFrames = null;
        _animCanvas = null;
        PreviewImage.Source = null;
        PreviewImage.IsVisible = true;
        PreviewImage.Opacity = 1.0;
        ImageBackground.Source = null;
        AnimFrameImage.Source = null;
        AnimBackground.Source = null;

        SceneOverlayLayer.Children.Clear();
        SceneOverlayLayer.Opacity = 1.0;
        if (SceneOverlayOpacitySlider is not null)
            SceneOverlayOpacitySlider.Value = 100;
        SceneOverlaysPanel.IsVisible = false;
        SceneOverlaysItemsControl.ItemsSource = null;

        SceneMaskImage.Source = null;
        SceneMaskImage.IsVisible = false;
        ImageMaskTypeGroup.IsVisible = false;
    }

    private void ShowContent(ResourceContent content)
    {
        ClearContentPanels();

        switch (content)
        {
            case ImageResource image:
                ShowImage(image);
                break;

            case SceneResource scene:
                ShowScene(scene);
                break;

            case AnimationResource anim:
                ShowAnimation(anim);
                break;

            case TextResource text:
                TextPanel.Text = text.Text;
                TextPanel.IsVisible = true;
                break;

            case SoundResource sound:
                ShowSound(sound);
                break;

            case VideoResource video:
                ShowVideo(video);
                break;

            case ErrorResource error:
                TextPanel.Text = error.Message;
                TextPanel.IsVisible = true;
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Image viewer (backgrounds, overlays, scene folders)
    // ---------------------------------------------------------------------------------------------

    private bool _syncingBackgroundToggles;
    private Bitmap? _sceneBackgroundBitmap;
    private Bitmap? _sceneBackgroundGrayscaleBitmap;
    private string? _sceneBackgroundArchive;

    /// <summary>The current scene's background as a bitmap, converted once per archive.</summary>
    private Bitmap? SceneBackgroundBitmapFor(FsNode? node, bool grayscale = false)
    {
        if (node is null || _vfs is null)
            return null;
        string? archive = node.IsDirectory ? node.ArchivePath : node.Parent?.ArchivePath;
        if (archive is null)
            return null;
        if (_sceneBackgroundArchive != archive)
        {
            _sceneBackgroundArchive = archive;
            DecodedImage? raw = _vfs.SceneBackgroundFor(node);
            _sceneBackgroundBitmap = BitmapConverter.ToBitmap(raw);
            _sceneBackgroundGrayscaleBitmap = BitmapConverter.ToBitmap(raw, grayscale: true);
        }

        return grayscale ? _sceneBackgroundGrayscaleBitmap : _sceneBackgroundBitmap;
    }

    private MaskLayers _activeMaskLayers = MaskLayers.All;
    private bool _syncingMaskToggles;

    private void SyncMaskLayerButtons()
    {
        _syncingMaskToggles = true;
        try
        {
            MaskTypeWalkToggle.IsChecked = MaskTypeWalkToggle.IsVisible && _activeMaskLayers.HasFlag(MaskLayers.Walk);
            MaskTypeHotspotToggle.IsChecked = MaskTypeHotspotToggle.IsVisible && _activeMaskLayers.HasFlag(MaskLayers.Hotspot);
            MaskTypeDepthToggle.IsChecked = MaskTypeDepthToggle.IsVisible && _activeMaskLayers.HasFlag(MaskLayers.Depth);
            MaskTypeMaterialToggle.IsChecked = MaskTypeMaterialToggle.IsVisible && _activeMaskLayers.HasFlag(MaskLayers.Material);
            MaskTypeOccluderToggle.IsChecked = MaskTypeOccluderToggle.IsVisible && _activeMaskLayers.HasFlag(MaskLayers.Occluder);
        }
        finally
        {
            _syncingMaskToggles = false;
        }
    }

    private void ReadMaskLayersFromButtons()
    {
        MaskLayers layers = MaskLayers.None;
        if (MaskTypeWalkToggle.IsVisible && MaskTypeWalkToggle.IsChecked == true) layers |= MaskLayers.Walk;
        if (MaskTypeHotspotToggle.IsVisible && MaskTypeHotspotToggle.IsChecked == true) layers |= MaskLayers.Hotspot;
        if (MaskTypeDepthToggle.IsVisible && MaskTypeDepthToggle.IsChecked == true) layers |= MaskLayers.Depth;
        if (MaskTypeMaterialToggle.IsVisible && MaskTypeMaterialToggle.IsChecked == true) layers |= MaskLayers.Material;
        if (MaskTypeOccluderToggle.IsVisible && MaskTypeOccluderToggle.IsChecked == true) layers |= MaskLayers.Occluder;
        _activeMaskLayers = layers;
    }

    private void MaskTypeToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (_syncingMaskToggles) return;
        ReadMaskLayersFromButtons();
        ApplyMaskLayers();
    }

    private void MaskTypeToggle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is ToggleButton clicked && e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            _syncingMaskToggles = true;
            try
            {
                MaskTypeWalkToggle.IsChecked = clicked == MaskTypeWalkToggle;
                MaskTypeHotspotToggle.IsChecked = clicked == MaskTypeHotspotToggle;
                MaskTypeDepthToggle.IsChecked = clicked == MaskTypeDepthToggle;
                MaskTypeMaterialToggle.IsChecked = clicked == MaskTypeMaterialToggle;
                MaskTypeOccluderToggle.IsChecked = clicked == MaskTypeOccluderToggle;
            }
            finally { _syncingMaskToggles = false; }
            ReadMaskLayersFromButtons();
            ApplyMaskLayers();
        }
    }

    private void UpdateMaskLayerButtonsForNode(FsNode node)
    {
        if (_vfs is null) return;
        byte[]? table1536 = _vfs.SceneAttributeTableFor(node);
        if (table1536 is not null && table1536.Length >= 1536)
        {
            var presence = RleMaskDecoder.DetectAttributePresence(table1536);
            MaskTypeWalkToggle.IsVisible = presence.HasWalk;
            MaskTypeHotspotToggle.IsVisible = presence.HasHotspot;
            MaskTypeDepthToggle.IsVisible = presence.HasDepth;
            MaskTypeMaterialToggle.IsVisible = presence.HasMaterial;
        }
        else
        {
            MaskTypeWalkToggle.IsVisible = true;
            MaskTypeHotspotToggle.IsVisible = false;
            MaskTypeDepthToggle.IsVisible = false;
            MaskTypeMaterialToggle.IsVisible = false;
        }

        FsNode archive = node.IsDirectory ? node : (node.Parent ?? node);
        bool hasOccluders = archive.Children.Any(c => c.Kind == EntryKind.Mask && IsOccluderNode(c));
        MaskTypeOccluderToggle.IsVisible = hasOccluders || IsOccluderNode(node);

        SyncMaskLayerButtons();
        ReadMaskLayersFromButtons();
    }

    private void ApplyMaskLayers()
    {
        if (_vfs is null) return;

        if (_currentContent is ImageResource && _selectedNode is not null && _selectedNode.Kind == EntryKind.Mask)
        {
            byte[] data = _vfs.ReadBytes(_selectedNode);
            byte[]? table1536 = _vfs.SceneAttributeTableFor(_selectedNode);
            byte[]? idMap = table1536 is not null ? RleMaskDecoder.ExtractObjectMapping(table1536) : null;
            ImageInfo? info = _selectedNode.Image;

            int? sceneW = info?.Width;
            int? sceneH = info?.Height;
            if (sceneW is null)
            {
                var bg = _vfs.SceneBackgroundFor(_selectedNode);
                if (bg is not null)
                {
                    sceneW = bg.Width;
                    sceneH = bg.Height;
                }
            }

            if (SparseMaskDecoder.Detect(data, sceneW, sceneH) is { } smInfo)
            {
                if (_activeMaskLayers.HasFlag(MaskLayers.Occluder))
                {
                    int w = sceneW ?? smInfo.Width;
                    int h = sceneH ?? smInfo.Height;
                    DecodedImage img = SparseMaskDecoder.Decode(data, w, h, idMap, colorSeed: _selectedNode.EntryIndex);
                    PreviewImage.Source = BitmapConverter.ToBitmap(img);
                    PreviewImage.IsVisible = true;
                }
                else
                {
                    PreviewImage.Source = null;
                    PreviewImage.IsVisible = false;
                }
                return;
            }

            if (SpanMaskDecoder.IsSpanMask(data))
            {
                if (_activeMaskLayers.HasFlag(MaskLayers.Occluder) || _activeMaskLayers.HasFlag(MaskLayers.Walk))
                {
                    DecodedImage img = SpanMaskDecoder.Decode(data);
                    PreviewImage.Source = BitmapConverter.ToBitmap(img);
                    PreviewImage.IsVisible = true;
                }
                else
                {
                    PreviewImage.Source = null;
                    PreviewImage.IsVisible = false;
                }
                return;
            }

            if (PngDecoder.IsPng(data))
            {
                if (PngDecoder.Decode(data) is { } img)
                {
                    PreviewImage.Source = BitmapConverter.ToBitmap(img);
                    PreviewImage.IsVisible = true;
                }
                return;
            }

            int maskW = info?.Width ?? sceneW ?? 1024;
            int maskH = info?.Height ?? sceneH ?? 600;
            DecodedImage decoded = RleMaskDecoder.Decode(data, maskW, maskH, idMap, table1536, _activeMaskLayers);
            PreviewImage.Source = BitmapConverter.ToBitmap(decoded);
            PreviewImage.IsVisible = true;

            FsNode archive = _selectedNode.Parent ?? _selectedNode;
            UpdateSceneMaskOverlays(archive, _activeMaskLayers, excludeNode: _selectedNode);
        }
        else if (_currentContent is SceneResource scene)
        {
            UpdateSceneMaskOverlays(scene.Archive, _activeMaskLayers);
        }
    }

    private void UpdateSceneMaskOverlays(FsNode archive, MaskLayers layers, FsNode? excludeNode = null)
    {
        if (_vfs is null)
        {
            SceneMaskImage.IsVisible = false;
            return;
        }

        FsNode? rleMaskNode = archive.Children.FirstOrDefault(c => c.Kind == EntryKind.Mask && !IsOccluderNode(c));
        List<FsNode> occluderNodes = archive.Children.Where(c => c.Kind == EntryKind.Mask && IsOccluderNode(c) && c != excludeNode).ToList();

        bool showRle = (excludeNode is null) && layers != MaskLayers.None &&
            (layers.HasFlag(MaskLayers.Walk) || layers.HasFlag(MaskLayers.Hotspot) || layers.HasFlag(MaskLayers.Depth) || layers.HasFlag(MaskLayers.Material));
        bool showOcc = layers.HasFlag(MaskLayers.Occluder) && occluderNodes.Count > 0;

        if (!showRle && !showOcc)
        {
            SceneMaskImage.Source = null;
            SceneMaskImage.IsVisible = false;
            return;
        }

        var bg = _vfs.SceneBackgroundFor(archive);
        int sceneW = bg?.Width ?? 1024;
        int sceneH = bg?.Height ?? 600;

        byte[]? table1536 = _vfs.SceneAttributeTableFor(archive);
        byte[]? idMap = table1536 is not null ? RleMaskDecoder.ExtractObjectMapping(table1536) : null;

        var compositePixels = new byte[sceneW * sceneH * 4];

        if (showRle && rleMaskNode is not null)
        {
            byte[] rleData = _vfs.ReadBytes(rleMaskNode);
            var rleImg = RleMaskDecoder.Decode(rleData, sceneW, sceneH, idMap, table1536, layers, transparentBackground: true);
            Array.Copy(rleImg.Pixels, compositePixels, Math.Min(rleImg.Pixels.Length, compositePixels.Length));
        }

        if (showOcc)
        {
            foreach (var occNode in occluderNodes)
            {
                byte[] occData = _vfs.ReadBytes(occNode);
                if (SparseMaskDecoder.Detect(occData, sceneW, sceneH) is { } sm)
                {
                    var occImg = SparseMaskDecoder.Decode(occData, sceneW, sceneH, idMap, colorSeed: occNode.EntryIndex);
                    BlendOver(compositePixels, occImg.Pixels);
                }
                else if (SpanMaskDecoder.IsSpanMask(occData))
                {
                    var occImg = SpanMaskDecoder.Decode(occData);
                    BlendAt(compositePixels, sceneW, sceneH, occImg.Pixels, occImg.Width, occImg.Height, occNode.Image?.X ?? 0, occNode.Image?.Y ?? 0);
                }
            }
        }

        SceneMaskImage.Source = BitmapConverter.ToBitmap(new DecodedImage(sceneW, sceneH, compositePixels));
        SceneMaskImage.Width = sceneW;
        SceneMaskImage.Height = sceneH;
        SceneMaskImage.IsVisible = true;
    }

    private bool IsOccluderNode(FsNode node)
    {
        if (node.Kind != EntryKind.Mask || _vfs is null) return false;
        try
        {
            using var s = _vfs.OpenFile(node);
            Span<byte> head = stackalloc byte[16];
            int read = s.Read(head);
            if (read >= 6 && SpanMaskDecoder.IsSpanMask(head[..read])) return true;
            if (read >= 9 && head[6] is 2 or 4) return true;
        }
        catch { }
        return false;
    }

    private static void BlendOver(byte[] dst, byte[] src)
    {
        int len = Math.Min(dst.Length, src.Length);
        for (int p = 0; p < len; p += 4)
        {
            byte a = src[p + 3];
            if (a > 0)
            {
                dst[p + 0] = src[p + 0];
                dst[p + 1] = src[p + 1];
                dst[p + 2] = src[p + 2];
                dst[p + 3] = a;
            }
        }
    }

    private static void BlendAt(byte[] dst, int dstW, int dstH, byte[] src, int srcW, int srcH, int posX, int posY)
    {
        for (int y = 0; y < srcH; y++)
        {
            int dy = posY + y;
            if (dy < 0 || dy >= dstH) continue;
            for (int x = 0; x < srcW; x++)
            {
                int dx = posX + x;
                if (dx < 0 || dx >= dstW) continue;
                int srcIdx = (y * srcW + x) * 4;
                byte a = src[srcIdx + 3];
                if (a > 0)
                {
                    int dstIdx = (dy * dstW + dx) * 4;
                    dst[dstIdx + 0] = src[srcIdx + 0];
                    dst[dstIdx + 1] = src[srcIdx + 1];
                    dst[dstIdx + 2] = src[srcIdx + 2];
                    dst[dstIdx + 3] = a;
                }
            }
        }
    }

    private void ShowImage(ImageResource image)
    {
        PreviewImage.Source = BitmapConverter.ToBitmap(image.Image);
        PreviewImage.IsVisible = true;
        bool hasSceneBg = _selectedNode is not null && SceneBackgroundBitmapFor(_selectedNode) is not null;
        ImageBackgroundGroup.IsVisible = image.Positioned && hasSceneBg;
        string mode = _settings.BackgroundMode.ToLowerInvariant();
        _syncingBackgroundToggles = true;
        try
        {
            ImageBgNoRadio.IsChecked = mode == "no";
            ImageBgYesRadio.IsChecked = mode == "yes";
            ImageBgGreyedRadio.IsChecked = mode == "greyed";
        }
        finally { _syncingBackgroundToggles = false; }

        bool isMask = image.Kind.Contains("mask", StringComparison.OrdinalIgnoreCase);
        bool isRleMask = string.Equals(image.Kind, "scene mask", StringComparison.OrdinalIgnoreCase);
        if (isRleMask && _selectedNode is not null)
        {
            UpdateMaskLayerButtonsForNode(_selectedNode);
            ImageMaskTypeGroup.IsVisible = true;
            ApplyMaskLayers();
        }
        else
        {
            ImageMaskTypeGroup.IsVisible = false;
            SceneMaskImage.IsVisible = false;
        }

        LayoutImageStage(image);
        ImagePanel.IsVisible = true;
        _imageZoom.Fit();
    }

    private void LayoutImageStage(ImageResource image)
    {
        bool isMask = image.Kind.Contains("mask", StringComparison.OrdinalIgnoreCase);
        bool showBg = image.Positioned && !string.Equals(_settings.BackgroundMode, "no", StringComparison.OrdinalIgnoreCase) && _selectedNode is not null;
        bool isGreyed = string.Equals(_settings.BackgroundMode, "greyed", StringComparison.OrdinalIgnoreCase);

        Bitmap? background = showBg
            ? SceneBackgroundBitmapFor(_selectedNode, isGreyed)
            : null;

        if (background is not null)
        {
            ImageBackground.Source = background;
            ImageBackground.IsVisible = true;
            ImageStage.Background = Brushes.Black;
            ImageStage.Width = background.PixelSize.Width;
            ImageStage.Height = background.PixelSize.Height;
            Canvas.SetLeft(PreviewImage, image.X);
            Canvas.SetTop(PreviewImage, image.Y);
            PreviewImage.Opacity = isMask ? 0.55 : 1.0;
            string bgDesc = isGreyed ? "greyed scene background" : "scene background";
            ImageInfoText.Text = isMask
                ? $"{image.Kind} {image.Image.Width}×{image.Image.Height}, overlay on the {bgDesc}"
                : $"{image.Kind} {image.Image.Width}×{image.Image.Height} at screen {image.X},{image.Y}, on the {bgDesc}";
        }
        else
        {
            ImageBackground.Source = null;
            ImageBackground.IsVisible = false;
            ImageStage.Background = Checkerboard;
            ImageStage.Width = image.Image.Width;
            ImageStage.Height = image.Image.Height;
            Canvas.SetLeft(PreviewImage, 0);
            Canvas.SetTop(PreviewImage, 0);
            PreviewImage.Opacity = 1.0;
            ImageInfoText.Text = image.Positioned
                ? (isMask
                    ? $"{image.Kind} {image.Image.Width}×{image.Image.Height}"
                    : $"{image.Kind} {image.Image.Width}×{image.Image.Height} at screen {image.X},{image.Y}")
                : $"{image.Kind} {image.Image.Width}×{image.Image.Height}";
        }

        if (_imageZoom.FitToWindow)
            _imageZoom.Fit();
    }

    private void ShowScene(SceneResource scene)
    {
        ImageBackgroundGroup.IsVisible = false;
        ImageBackground.Source = null;
        ImageBackground.IsVisible = false;
        PreviewImage.Opacity = 1.0;
        if (scene.Background is { } bg)
        {
            PreviewImage.Source = BitmapConverter.ToBitmap(bg);
            PreviewImage.IsVisible = true;
            ImageStage.Background = Brushes.Black;
            ImageStage.Width = bg.Width;
            ImageStage.Height = bg.Height;
            SceneOverlayLayer.Width = bg.Width;
            SceneOverlayLayer.Height = bg.Height;
            ImageInfoText.Text = $"{scene.Summary}. Showing the scene background (entry 0).";
        }
        else
        {
            PreviewImage.Source = null;
            ImageStage.Background = Checkerboard;
            ImageStage.Width = 320;
            ImageStage.Height = 200;
            ImageInfoText.Text = $"{scene.Summary}. No background raster in this archive.";
        }
        Canvas.SetLeft(PreviewImage, 0);
        Canvas.SetTop(PreviewImage, 0);

        bool hasMasks = scene.Archive.Children.Any(c => c.Kind == EntryKind.Mask);
        if (hasMasks)
        {
            UpdateMaskLayerButtonsForNode(scene.Archive);
            ImageMaskTypeGroup.IsVisible = true;
            UpdateSceneMaskOverlays(scene.Archive, _activeMaskLayers);
        }
        else
        {
            ImageMaskTypeGroup.IsVisible = false;
            SceneMaskImage.IsVisible = false;
        }

        // Composite overlay entries onto the scene canvas and populate the sidebar checklist.
        PopulateSceneOverlays(scene);

        ImagePanel.IsVisible = true;
        _imageZoom.Fit();
    }

    /// <summary>
    /// Finds every <see cref="EntryKind.Overlay"/> child in the scene archive, decodes it, places it on
    /// <c>SceneOverlayLayer</c>, and fills the sidebar checklist so the user can toggle each one.
    /// </summary>
    private void PopulateSceneOverlays(SceneResource scene)
    {
        SceneOverlayLayer.Children.Clear();
        if (_vfs is null || scene.Background is null)
        {
            SceneOverlaysPanel.IsVisible = false;
            return;
        }

        var items = new List<SceneOverlayItem>();
        VirtualFileSystem vfs = _vfs;
        foreach (FsNode child in scene.Archive.Children)
        {
            if (child.Kind != EntryKind.Overlay)
                continue;

            try
            {
                byte[] data = vfs.ReadBytes(child);
                DecodedImage? overlayImg = null;
                int posX = 0, posY = 0;
                string kind = "overlay";

                if (SpriteAsset.Parse(data) is { } sprite1)
                {
                    var frame = sprite1.DecodeFrame(0);
                    overlayImg = frame.Image;
                    posX = frame.X;
                    posY = frame.Y;
                    kind = "overlay (sprite)";
                }
                else if (OverlayDecoder.TryDecode(data) is { } ov)
                {
                    overlayImg = ov.Image;
                    posX = ov.Info.X;
                    posY = ov.Info.Y;
                    kind = ov.Info.IsRectangular ? "overlay (rect)" : "overlay";
                }
                else if (PngDecoder.IsPng(data))
                {
                    overlayImg = PngDecoder.Decode(data);
                    posX = child.Image?.X ?? 0;
                    posY = child.Image?.Y ?? 0;
                    kind = "overlay (png)";
                }
                else if (JpegDecoder.IsJpeg(data))
                {
                    overlayImg = JpegDecoder.Decode(data);
                    posX = child.Image?.X ?? 0;
                    posY = child.Image?.Y ?? 0;
                    kind = "overlay (jpeg)";
                }

                if (overlayImg is null) continue;

                Bitmap? bmp = BitmapConverter.ToBitmap(overlayImg);
                if (bmp is null)
                    continue;

                var img = new Image
                {
                    Source = bmp,
                    Stretch = Stretch.None,
                };
                RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.None);
                Canvas.SetLeft(img, posX);
                Canvas.SetTop(img, posY);
                SceneOverlayLayer.Children.Add(img);

                var item = new SceneOverlayItem
                {
                    DisplayName = $"{child.Name}  {kind}",
                    X = posX,
                    Y = posY,
                    OverlayBitmap = bmp,
                    CanvasImage = img,
                };
                items.Add(item);
            }
            catch (Exception ex)
            {
                Log.Exception($"Scene overlay '{child.GetPath()}'", ex);
            }
        }

        if (items.Count > 0)
        {
            SceneOverlaysItemsControl.ItemsSource = items;
            SceneOverlaysPanel.IsVisible = true;
        }
        else
        {
            SceneOverlaysPanel.IsVisible = false;
        }
    }

    private void SceneOverlaysSelectAll_Click(object? sender, RoutedEventArgs e)
    {
        if (SceneOverlaysItemsControl.ItemsSource is IEnumerable<SceneOverlayItem> items)
            foreach (var item in items)
                item.IsChecked = true;
    }

    private void SceneOverlaysSelectNone_Click(object? sender, RoutedEventArgs e)
    {
        if (SceneOverlaysItemsControl.ItemsSource is IEnumerable<SceneOverlayItem> items)
            foreach (var item in items)
                item.IsChecked = false;
    }

    private void SceneOverlayOpacity_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (SceneOverlayLayer is not null)
            SceneOverlayLayer.Opacity = e.NewValue / 100.0;
        if (SceneOverlayOpacityText is not null)
            SceneOverlayOpacityText.Text = $"{(int)e.NewValue}%";
    }

    private void SceneOverlaySolo_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SceneOverlayItem clickedItem } &&
            SceneOverlaysItemsControl.ItemsSource is IEnumerable<SceneOverlayItem> items)
        {
            var itemList = items.ToList();
            bool isAlreadySolo = clickedItem.IsChecked && itemList.All(i => i == clickedItem ? i.IsChecked : !i.IsChecked);
            if (isAlreadySolo)
            {
                foreach (var item in itemList)
                    item.IsChecked = true;
            }
            else
            {
                foreach (var item in itemList)
                    item.IsChecked = (item == clickedItem);
            }
        }
    }

    private void ImageBgRadio_Click(object? sender, RoutedEventArgs e)
    {
        if (_syncingBackgroundToggles)
            return;
        if (sender is RadioButton { Tag: string mode })
            SetBackgroundMode(mode);
    }

    private void ImageZoomIn_Click(object? sender, RoutedEventArgs e) => _imageZoom.In();
    private void ImageZoomOut_Click(object? sender, RoutedEventArgs e) => _imageZoom.Out();
    private void ImageResetZoom_Click(object? sender, RoutedEventArgs e) => _imageZoom.Fit();
    private void ImageZoom100_Click(object? sender, RoutedEventArgs e) => _imageZoom.Zoom100();
    private void ImageZoomLabel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _imageZoom.Zoom100();
        e.Handled = true;
    }

    // ---------------------------------------------------------------------------------------------
    // Animation viewer
    // ---------------------------------------------------------------------------------------------

    private SpriteAsset? _animAsset;
    // Decoded frames, cropped to their content, cached on first use.
    private SpriteFrame?[]? _animFrames;
    // One surface the size of the animation's bounding box that every frame is composited onto at
    // its own offset. The Image control never moves between frames, so there is no per-frame
    // rounding of a fractional-zoom position -- which is what made animations wobble by a pixel when
    // each frame was its own bitmap placed at its own (x, y).
    private WriteableBitmap? _animCanvas;
    private int _animFrameIndex;
    private bool _animPlaying;
    private bool _animScrubDragging;
    private bool _syncingScrub;
    private bool _syncingFps;

    private void ShowAnimation(AnimationResource anim)
    {
        _animAsset = anim.Asset;
        _animFrames = new SpriteFrame?[anim.Asset.FrameCount];
        _animCanvas = new WriteableBitmap(
            new PixelSize(Math.Max(1, anim.Asset.Bounds.Width), Math.Max(1, anim.Asset.Bounds.Height)),
            new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
        AnimFrameImage.Source = _animCanvas;
        AnimFrameImage.IsVisible = true;
        _animFrameIndex = 0;

        string mode = _settings.BackgroundMode.ToLowerInvariant();
        _syncingBackgroundToggles = true;
        try
        {
            AnimBgNoRadio.IsChecked = mode == "no";
            AnimBgYesRadio.IsChecked = mode == "yes";
            AnimBgGreyedRadio.IsChecked = mode == "greyed";
        }
        finally { _syncingBackgroundToggles = false; }

        _syncingScrub = true;
        try
        {
            AnimScrubSlider.Maximum = Math.Max(0, anim.Asset.FrameCount - 1);
            AnimScrubSlider.Value = 0;
        }
        finally { _syncingScrub = false; }

        LayoutAnimationStage();
        ShowAnimationFrame(0);
        AnimationPanel.IsVisible = true;
        _animZoom.Fit();

        if (_settings.AutoPlayAnimation && anim.Asset.FrameCount > 1)
            StartAnimation();
        else
            SetAnimPlayIcon(false);
    }

    /// <summary>
    /// Sizes the stage and places the background. With the background on, the stage is the screen and
    /// frames sit at their absolute coordinates; off, the stage is the animation's bounding box and
    /// frames are offset by its origin. Either way frames of different sizes line up, because every
    /// segment carries its own screen position.
    /// </summary>
    private void LayoutAnimationStage()
    {
        if (_animAsset is null)
            return;

        (int bx, int by, int bw, int bh) = _animAsset.Bounds;
        bool showBg = !string.Equals(_settings.BackgroundMode, "no", StringComparison.OrdinalIgnoreCase) && _selectedNode is not null;
        bool isGreyed = string.Equals(_settings.BackgroundMode, "greyed", StringComparison.OrdinalIgnoreCase);

        Bitmap? background = showBg ? SceneBackgroundBitmapFor(_selectedNode, isGreyed) : null;

        if (background is not null)
        {
            AnimBackground.Source = background;
            AnimBackground.IsVisible = true;
            AnimStage.Background = Brushes.Black;
            AnimStage.Width = background.PixelSize.Width;
            AnimStage.Height = background.PixelSize.Height;
            Canvas.SetLeft(AnimBoundsRect, bx);
            Canvas.SetTop(AnimBoundsRect, by);
            string bgDesc = isGreyed ? "greyed scene background" : "scene background";
            AnimInfoText.Text = $"{_animAsset.FrameCount} frames, bounding box {bw}×{bh} at screen {bx},{by}, on the {bgDesc}";
        }
        else
        {
            AnimBackground.Source = null;
            AnimBackground.IsVisible = false;
            AnimStage.Background = Checkerboard;
            AnimStage.Width = bw;
            AnimStage.Height = bh;
            Canvas.SetLeft(AnimBoundsRect, 0);
            Canvas.SetTop(AnimBoundsRect, 0);
            AnimInfoText.Text = $"{_animAsset.FrameCount} frames, bounding box {bw}×{bh} at screen {bx},{by}";
        }
        AnimBoundsRect.Width = bw;
        AnimBoundsRect.Height = bh;

        PlaceAnimationFrame();
        if (_animZoom.FitToWindow)
            _animZoom.Fit();
    }

    private void ShowAnimationFrame(int index)
    {
        if (_animAsset is null || _animFrames is null || _animCanvas is null || _animAsset.FrameCount == 0)
            return;

        index = Math.Clamp(index, 0, _animAsset.FrameCount - 1);
        _animFrameIndex = index;

        SpriteFrame frame = _animFrames[index] ??= _animAsset.DecodeFrame(index);
        PaintAnimationFrame(frame);

        AnimFrameText.Text = frame.IsEmpty
            ? $"frame {index + 1} / {_animAsset.FrameCount}  (empty)"
            : $"frame {index + 1} / {_animAsset.FrameCount}  at {frame.X},{frame.Y}";

        if (!_animScrubDragging)
        {
            _syncingScrub = true;
            try { AnimScrubSlider.Value = index; }
            finally { _syncingScrub = false; }
        }
    }

    /// <summary>Puts the surface where the bounding box sits: at its screen position over the background, at 0,0 otherwise.</summary>
    private void PlaceAnimationFrame()
    {
        if (_animAsset is null)
            return;

        bool onBackground = AnimBackground.IsVisible;
        Canvas.SetLeft(AnimFrameImage, onBackground ? _animAsset.Bounds.X : 0);
        Canvas.SetTop(AnimFrameImage, onBackground ? _animAsset.Bounds.Y : 0);
    }

    /// <summary>Clears the surface and blits one frame at its offset inside the bounding box.</summary>
    private void PaintAnimationFrame(SpriteFrame frame)
    {
        if (_animAsset is null || _animCanvas is null)
            return;

        (int bx, int by, int bw, int bh) = _animAsset.Bounds;
        using (Avalonia.Platform.ILockedFramebuffer fb = _animCanvas.Lock())
        {
            unsafe
            {
                var span = new Span<byte>((void*)fb.Address, fb.RowBytes * fb.Size.Height);
                span.Clear();
                if (frame.Image is { } img)
                {
                    int ox = frame.X - bx, oy = frame.Y - by;
                    for (int y = 0; y < img.Height; y++)
                    {
                        int dy = oy + y;
                        if (dy < 0 || dy >= bh) continue;
                        int x0 = Math.Max(0, -ox), x1 = Math.Min(img.Width, bw - ox);
                        if (x1 <= x0) continue;
                        img.Pixels.AsSpan((y * img.Width + x0) * 4, (x1 - x0) * 4)
                            .CopyTo(span.Slice(dy * fb.RowBytes + (ox + x0) * 4));
                    }
                }
            }
        }
        // The bitmap object is unchanged; tell the Image its pixels are not.
        AnimFrameImage.InvalidateVisual();
    }

    private void StartAnimation()
    {
        if (_animAsset is null || _animAsset.FrameCount < 2)
            return;
        _animPlaying = true;
        _animTimer.Start();
        SetAnimPlayIcon(true);
    }

    private void PauseAnimation()
    {
        _animPlaying = false;
        _animTimer.Stop();
        SetAnimPlayIcon(false);
    }

    private void StopAnimation()
    {
        PauseAnimation();
        _animFrameIndex = 0;
    }

    private void AnimTimer_Tick(object? sender, EventArgs e)
    {
        if (_animAsset is null || !_animPlaying)
            return;

        int next = _animFrameIndex + 1;
        if (next >= _animAsset.FrameCount)
        {
            if (_settings.LoopAnimation)
                next = 0;
            else
            {
                PauseAnimation();
                return;
            }
        }
        ShowAnimationFrame(next);
    }

    private void StepAnimation(int delta)
    {
        if (_animAsset is null)
            return;
        PauseAnimation();
        int n = _animAsset.FrameCount;
        ShowAnimationFrame(((_animFrameIndex + delta) % n + n) % n);
    }

    private void SetAnimPlayIcon(bool playing) =>
        AnimPlayPauseIcon.Source = (IImage)Application.Current!.FindResource(playing ? "PauseIcon" : "PlayIcon")!;

    private void AnimPlayPause_Click(object? sender, RoutedEventArgs e)
    {
        if (_animPlaying)
            PauseAnimation();
        else
            StartAnimation();
    }

    private void AnimStop_Click(object? sender, RoutedEventArgs e)
    {
        StopAnimation();
        ShowAnimationFrame(0);
    }

    private void AnimStepBack_Click(object? sender, RoutedEventArgs e) => StepAnimation(-1);

    private void AnimStepForward_Click(object? sender, RoutedEventArgs e) => StepAnimation(+1);

    private void AnimScrubSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingScrub || _animAsset is null)
            return;
        ShowAnimationFrame((int)Math.Round(e.NewValue));
    }

    private void AnimScrubSlider_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _animScrubDragging = true;
        PauseAnimation();
    }

    private void AnimScrubSlider_PointerReleased(object? sender, PointerReleasedEventArgs e) => _animScrubDragging = false;

    private void AnimLoop_Changed(object? sender, RoutedEventArgs e)
    {
        if (_settings.LoopAnimation == (AnimLoopCheck.IsChecked == true))
            return;
        _settings.LoopAnimation = AnimLoopCheck.IsChecked == true;
        _settings.Save();
    }

    private void AnimFps_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_syncingFps || AnimFpsBox.Value is not { } value)
            return;
        double fps = Math.Clamp((double)value, 1, 60);
        if (Math.Abs(fps - _settings.AnimationFps) < 0.01)
            return;
        _settings.AnimationFps = fps;
        _settings.Save();
        _animTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / fps);
    }

    private void AnimBgRadio_Click(object? sender, RoutedEventArgs e)
    {
        if (_syncingBackgroundToggles)
            return;
        if (sender is RadioButton { Tag: string mode })
            SetBackgroundMode(mode);
    }

    private void AnimZoomIn_Click(object? sender, RoutedEventArgs e) => _animZoom.In();
    private void AnimZoomOut_Click(object? sender, RoutedEventArgs e) => _animZoom.Out();
    private void AnimResetZoom_Click(object? sender, RoutedEventArgs e) => _animZoom.Fit();
    private void AnimZoom100_Click(object? sender, RoutedEventArgs e) => _animZoom.Zoom100();
    private void AnimZoomLabel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _animZoom.Zoom100();
        e.Handled = true;
    }

    // ---------------------------------------------------------------------------------------------
    // Sound playback
    // ---------------------------------------------------------------------------------------------

    private float[]? _cachedWaveformPeaks;
    private bool _sliderDragging;
    private const double WaveformPixelsPerBar = 4.0;
    private static readonly Color WaveformColor = Color.FromRgb(0xE8, 0xEC, 0xF4);

    private void ShowSound(SoundResource sound)
    {
        string formatDesc = sound.Pcm.Format switch
        {
            AudioFormat.Mp3 => "MP3 audio",
            AudioFormat.Wav => $"{sound.Pcm.SampleRate:N0} Hz, {(sound.Pcm.Channels == 1 ? "mono" : "stereo")}, {sound.Pcm.BitsPerSample}-bit WAV",
            _ => $"{sound.Pcm.SampleRate:N0} Hz, {(sound.Pcm.Channels == 1 ? "mono" : "stereo")}, {sound.Pcm.BitsPerSample}-bit PCM",
        };
        string subtitleLine = !string.IsNullOrWhiteSpace(_selectedNode?.Subtitle)
            ? $"\n\n\"{_selectedNode.Subtitle}\""
            : "";
        SoundInfoText.Text = $"{_selectedNode?.DisplayName}\n{formatDesc}" +
                             (_selectedNode?.Kind == EntryKind.Voice && sound.Pcm.Format == AudioFormat.RawPcm ? $"  (rate assumed; change it in Settings > Playback)" : "") +
                             subtitleLine;
        SoundSlider.Value = 0;
        SoundCurrentTimeText.Text = "0:00";
        SoundTotalTimeText.Text = VirtualFileSystem.FormatDuration(sound.DurationSeconds);
        SetPlayPauseIcon(playing: false);
        _mediaPlayer.Open(sound.TempFilePath);

        _cachedWaveformPeaks = null;
        WaveformImage.Source = null;

        string wavPath = sound.TempFilePath;
        _ = Task.Run(() => WaveformRenderer.SamplePeaks(wavPath))
            .ContinueWith(t =>
            {
                if (!ReferenceEquals(_currentContent, sound))
                    return;
                if (t.IsCompletedSuccessfully && t.Result.Length > 0)
                {
                    _cachedWaveformPeaks = t.Result;
                    RenderWaveformAtCurrentWidth();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());

        SoundPanel.IsVisible = true;

        if (_settings.AutoPlaySound)
            PlaySound();
    }

    private void RenderWaveformAtCurrentWidth()
    {
        if (_cachedWaveformPeaks is null || _cachedWaveformPeaks.Length == 0)
            return;

        double width = WaveformImage.Bounds.Width;
        if (width < 1)
            width = SoundPanel.Bounds.Width;
        if (width < 1)
            return;

        int barCount = Math.Max(20, (int)Math.Round(width / WaveformPixelsPerBar));
        WaveformImage.Source = WaveformRenderer.Render(_cachedWaveformPeaks, WaveformColor, canvasWidth: width, canvasHeight: WaveformImage.Height, barCount: barCount);
    }

    private void WaveformImage_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width != e.PreviousSize.Width)
            RenderWaveformAtCurrentWidth();
    }

    private void PlaySound()
    {
        _mediaPlayer.Play();
        _positionTimer.Start();
        SetPlayPauseIcon(playing: true);
    }

    private void PauseSound()
    {
        _mediaPlayer.Pause();
        _positionTimer.Stop();
        SetPlayPauseIcon(playing: false);
    }

    private void SetPlayPauseIcon(bool playing) =>
        PlayPauseIcon.Source = (IImage)Application.Current!.FindResource(playing ? "PauseIcon" : "PlayIcon")!;

    private void StopSound()
    {
        _positionTimer.Stop();
        if (_mediaPlayerBacking is not null)
            _mediaPlayerBacking.CloseAsync();
        WaveformImage.Source = null;
    }

    private void MediaPlayer_MediaEnded(object? sender, EventArgs e)
    {
        // Once LibVLC reaches Ended, a plain Play() is a no-op; Stop() first resets its state machine.
        _mediaPlayer.Stop();
        _mediaPlayer.Position = TimeSpan.Zero;

        if (_settings.LoopSoundPlayback)
        {
            _mediaPlayer.Play();
        }
        else
        {
            _positionTimer.Stop();
            SoundSlider.Value = 0;
            SetPlayPauseIcon(playing: false);
            UpdateSoundTimeReadout();
        }
    }

    private void MediaPlayer_MediaOpened(object? sender, EventArgs e)
    {
        if (_mediaPlayer.HasDurationTimeSpan)
            SoundSlider.Maximum = Math.Max(0.01, _mediaPlayer.Duration!.Value.TotalSeconds);
        UpdateSoundTimeReadout();
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_sliderDragging)
            SoundSlider.Value = _mediaPlayer.Position.TotalSeconds;
        UpdateSoundTimeReadout();
    }

    private void UpdateSoundTimeReadout()
    {
        SoundCurrentTimeText.Text = VirtualFileSystem.FormatDuration(_mediaPlayer.Position.TotalSeconds);
        SoundTotalTimeText.Text = VirtualFileSystem.FormatDuration((_mediaPlayer.Duration ?? TimeSpan.Zero).TotalSeconds);
    }

    private void PlayPauseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_positionTimer.IsEnabled)
            PauseSound();
        else
            PlaySound();
    }

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        _mediaPlayer.Stop();
        _positionTimer.Stop();
        SoundSlider.Value = 0;
        SetPlayPauseIcon(playing: false);
        UpdateSoundTimeReadout();
    }

    private void SoundSlider_PointerPressed(object? sender, PointerPressedEventArgs e) => _sliderDragging = true;

    private void SoundSlider_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _sliderDragging = false;
        _mediaPlayer.Position = TimeSpan.FromSeconds(SoundSlider.Value);
        UpdateSoundTimeReadout();
    }

    private bool _isScrubbingWaveform;

    private void WaveformImage_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_mediaPlayer.HasDurationTimeSpan)
            return;

        _isScrubbingWaveform = true;
        e.Pointer.Capture(WaveformImage);
        SeekWaveformAt(e.GetPosition(WaveformImage).X);
        e.Handled = true;
    }

    private void WaveformImage_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isScrubbingWaveform && _mediaPlayer.HasDurationTimeSpan)
        {
            SeekWaveformAt(e.GetPosition(WaveformImage).X);
            e.Handled = true;
        }
    }

    private void WaveformImage_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isScrubbingWaveform)
        {
            _isScrubbingWaveform = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void SeekWaveformAt(double x)
    {
        if (_mediaPlayer.Duration is not { } duration || WaveformImage.Bounds.Width <= 0)
            return;

        double fraction = Math.Clamp(x / WaveformImage.Bounds.Width, 0.0, 1.0);
        TimeSpan target = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * fraction);
        _mediaPlayer.Position = target;
        SoundSlider.Value = target.TotalSeconds;
        UpdateSoundTimeReadout();
    }

    private bool _syncingVolume;

    private void VolumeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingVolume)
            return;

        _syncingVolume = true;
        try
        {
            int vol = Math.Clamp((int)e.NewValue, 0, 100);
            _settings.Volume = vol;
            _settings.Save();

            if (_mediaPlayerBacking is not null)
                _mediaPlayerBacking.Volume = vol;
            if (_videoPlayerBacking is not null)
                _videoPlayerBacking.Volume = vol;

            SoundVolumeSlider.Value = vol;
            VideoVolumeSlider.Value = vol;
            SoundVolumeText.Text = $"{vol}%";
            VideoVolumeText.Text = $"{vol}%";
        }
        finally
        {
            _syncingVolume = false;
        }
    }

    private void MuteButton_Click(object? sender, RoutedEventArgs e)
    {
        _settings.IsMuted = !_settings.IsMuted;
        _settings.Save();

        if (_mediaPlayerBacking is not null)
            _mediaPlayerBacking.IsMuted = _settings.IsMuted;
        if (_videoPlayerBacking is not null)
            _videoPlayerBacking.IsMuted = _settings.IsMuted;

        UpdateMuteVisuals();
    }

    private void UpdateMuteVisuals()
    {
        bool muted = _settings.IsMuted;
        double opacity = muted ? 0.35 : 1.0;
        SoundMuteButton.Opacity = opacity;
        VideoMuteButton.Opacity = opacity;
        string tip = muted ? "Unmute (M)" : "Mute (M)";
        ToolTip.SetTip(SoundMuteButton, tip);
        ToolTip.SetTip(VideoMuteButton, tip);
    }

    // ---------------------------------------------------------------------------------------------
    // Video: the restored .bik played natively by LibVLC
    // ---------------------------------------------------------------------------------------------

    private int _videoLoadGeneration;
    private bool _videoIsPlaying;
    private bool _videoSliderDragging;
    private bool _videoDurationKnown;
    private VideoResource? _currentVideo;

    private async void ShowVideo(VideoResource video)
    {
        _currentVideo = video;
        int generation = ++_videoLoadGeneration;
        _videoBaseWidth = 640;
        _videoBaseHeight = 375;
        VideoZoomSlider.Value = 100;
        ApplyVideoZoom();
        VideoPanel.IsVisible = true;
        VideoLabel.Text = $"{_selectedNode?.Name}  -  Bink video, header restored from the keyfile";
        VideoPlayPauseButton.IsEnabled = false;

        // The VideoView is a NativeControlHost whose child window only exists once the panel has been
        // laid out. Binding the player before that leaves MediaPlayer.Hwnd at zero and Play() opens a
        // separate "VLC (Direct3D11 output)" window instead of rendering here -- so yield until layout
        // has run, then bind.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (generation != _videoLoadGeneration || !ReferenceEquals(_currentVideo, video))
            return;

        VideoPlayer.MediaPlayer = _videoPlayer.NativePlayer;

        _videoDurationKnown = false;
        _videoSliderDragging = false;
        VideoScrubSlider.IsEnabled = false;
        VideoScrubSlider.Maximum = 1;
        VideoScrubSlider.Value = 0;
        VideoCurrentTimeText.Text = "0:00";
        VideoTotalTimeText.Text = "--:--";

        _videoPlayer.Open(video.TempFilePath);
        if (_settings.AutoPlayVideo)
        {
            _videoPlayer.Play();
            _videoIsPlaying = true;
            SetVideoPlayPauseIcon(playing: true);
            _videoPositionTimer.Start();
        }
        else
        {
            _videoIsPlaying = false;
            SetVideoPlayPauseIcon(playing: false);
        }
        VideoPlayPauseButton.IsEnabled = true;
    }

    private void SetVideoPlayPauseIcon(bool playing) =>
        VideoPlayPauseIcon.Source = (IImage)Application.Current!.FindResource(playing ? "PauseIcon" : "PlayIcon")!;

    private void VideoPlayer_MediaOpened(object? sender, EventArgs e)
    {
        if (!_videoPlayer.HasDurationTimeSpan)
            return;

        // Size the native surface to the video's own dimensions. A surface with a different aspect
        // letterboxes in black, and because it is a native child window those black bars paint over
        // the Avalonia controls beneath them (the scrub slider, most visibly).
        uint w = 0, h = 0;
        if (_videoPlayer.NativePlayer.Size(0, ref w, ref h) && w > 0 && h > 0)
        {
            _videoBaseWidth = w;
            _videoBaseHeight = h;
            // Start fitted to the viewport (the cutscenes are 1024×600, wider than most windows). The
            // surface is native, so anything past the viewport would paint over the transport below.
            double vpW = VideoScrollViewer.Viewport.Width, vpH = VideoScrollViewer.Viewport.Height;
            double fit = vpW > 0 && vpH > 0 ? Math.Min(1.0, Math.Min(vpW / w, vpH / h)) : 1.0;
            VideoZoomSlider.Value = Math.Clamp(Math.Floor(fit * 100), VideoZoomSlider.Minimum, VideoZoomSlider.Maximum);
            ApplyVideoZoom();
        }

        _videoDurationKnown = true;
        VideoScrubSlider.Maximum = Math.Max(0.01, _videoPlayer.Duration!.Value.TotalSeconds);
        VideoScrubSlider.IsEnabled = true;
        UpdateVideoTimeReadout();
    }

    private void VideoPositionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_videoSliderDragging && _videoDurationKnown)
            VideoScrubSlider.Value = _videoPlayer.Position.TotalSeconds;
        UpdateVideoTimeReadout();
    }

    private void UpdateVideoTimeReadout()
    {
        VideoCurrentTimeText.Text = VirtualFileSystem.FormatDuration(_videoPlayer.Position.TotalSeconds);
        VideoTotalTimeText.Text = _videoDurationKnown
            ? VirtualFileSystem.FormatDuration((_videoPlayer.Duration ?? TimeSpan.Zero).TotalSeconds)
            : "--:--";
    }

    private void VideoScrubSlider_PointerPressed(object? sender, PointerPressedEventArgs e) => _videoSliderDragging = true;

    private void VideoScrubSlider_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _videoSliderDragging = false;
        if (_videoDurationKnown)
            _videoPlayer.Position = TimeSpan.FromSeconds(VideoScrubSlider.Value);
    }

    private void VideoScrubSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_videoSliderDragging)
            VideoCurrentTimeText.Text = VirtualFileSystem.FormatDuration(VideoScrubSlider.Value);
    }

    private void VideoStopButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentVideo is null)
            return;
        _videoPlayer.Stop();
        _videoPlayer.Position = TimeSpan.Zero;
        _videoPositionTimer.Stop();
        _videoIsPlaying = false;
        VideoScrubSlider.Value = 0;
        SetVideoPlayPauseIcon(playing: false);
        UpdateVideoTimeReadout();
    }

    // Base "100% zoom" video surface size: the video's own dimensions once LibVLC reports them, a
    // plausible default before that. Zoom scales VideoPlayer.Width/Height directly rather than
    // applying a ScaleTransform: VideoView is a NativeControlHost, outside Avalonia's compositor.
    private double _videoBaseWidth = 640;
    private double _videoBaseHeight = 375;

    private void VideoZoomSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e) => ApplyVideoZoom();

    private void ApplyVideoZoom()
    {
        // ValueChanged fires during XAML init before VideoPlayer/VideoZoomLabel are constructed.
        if (VideoPlayer is null || VideoZoomLabel is null)
            return;

        double zoom = VideoZoomSlider.Value / 100.0;
        VideoPlayer.Width = _videoBaseWidth * zoom;
        VideoPlayer.Height = _videoBaseHeight * zoom;
        VideoZoomLabel.Text = $"{VideoZoomSlider.Value:0}%";
    }

    private void VideoScrollViewer_PreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        double factor = e.Delta.Y > 0 ? 1.25 : 1.0 / 1.25;
        VideoZoomSlider.Value = Math.Clamp(VideoZoomSlider.Value * factor, VideoZoomSlider.Minimum, VideoZoomSlider.Maximum);
        e.Handled = true;
    }

    private void VideoZoom100_Click(object? sender, RoutedEventArgs e) => VideoZoomSlider.Value = 100;

    private void VideoZoomLabel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        VideoZoomSlider.Value = 100;
        e.Handled = true;
    }

    private void StopVideo()
    {
        _videoLoadGeneration++;
        _videoIsPlaying = false;
        _videoPositionTimer.Stop();
        _currentVideo = null;
        _videoPlayerBacking?.CloseAsync();
    }

    private void VideoPlayPauseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentVideo is null)
            return;

        if (_videoIsPlaying)
        {
            _videoPlayer.Pause();
            _videoIsPlaying = false;
            _videoPositionTimer.Stop();
            SetVideoPlayPauseIcon(playing: false);
        }
        else
        {
            _videoPlayer.Play();
            _videoIsPlaying = true;
            _videoPositionTimer.Start();
            SetVideoPlayPauseIcon(playing: true);
        }
    }

    private void VideoPlayer_MediaEnded(object? sender, EventArgs e)
    {
        // Fallback: TimeChanged didn't fire close enough to the end to preempt, so LibVLC reached Ended
        // and released the video output. Stop+Play+Pause brings it back to a state where frame 0 renders.
        _videoIsPlaying = false;
        _videoPositionTimer.Stop();
        _videoPlayer.Stop();
        _videoPlayer.Play();
        _videoPlayer.Pause();
        _videoPlayer.Position = TimeSpan.Zero;
        VideoScrubSlider.Value = 0;
        SetVideoPlayPauseIcon(playing: false);
        UpdateVideoTimeReadout();
    }

    /// <summary>
    /// Marshaled from LibVLC's TimeChanged callback. When playback approaches the end, rewinds and pauses
    /// so LibVLC never reaches Ended -- the video output stays alive showing frame 0 instead of going black.
    /// </summary>
    private void HandleVideoTimeChanged(long timeMs)
    {
        if (!_videoIsPlaying || !_videoDurationKnown)
            return;

        long totalMs = (long)_videoPlayer.Duration!.Value.TotalMilliseconds;
        if (timeMs < totalMs - 200)
            return;

        _videoPlayer.Position = TimeSpan.Zero;
        _videoPlayer.Pause();
        _videoIsPlaying = false;
        _videoPositionTimer.Stop();
        VideoScrubSlider.Value = 0;
        SetVideoPlayPauseIcon(playing: false);
        UpdateVideoTimeReadout();
    }

    private void OpenExternalButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentContent is not VideoResource video)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(video.TempFilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _ = Dialogs.ShowMessageBox(this, $"Could not open external player:\n{ex.Message}", "Runaway Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
