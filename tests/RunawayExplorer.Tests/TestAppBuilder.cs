using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(RunawayExplorer.Tests.TestAppBuilder))]

namespace RunawayExplorer.Tests;

/// <summary>
/// Entry point for <c>[AvaloniaFact]</c>/<c>[AvaloniaTheory]</c> tests. A dedicated minimal
/// <see cref="Application"/> rather than the real <c>RunawayExplorer.App</c>: the real app's
/// <c>OnFrameworkInitializationCompleted</c> creates a full <c>MainWindow</c> (VFS, LibVLC, settings
/// I/O), which is far more than a unit test needs. This merges the same FluentTheme + VectorIcons
/// resources the real app does, so tests that resolve icon resources (see
/// <c>ResourceKeyToImageConverterTests</c>) see the real thing.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Resources.MergedDictionaries.Add(new ResourceInclude((Uri?)null)
        {
            Source = new Uri("avares://RunawayExplorer/Assets/Icons/VectorIcons.axaml"),
        });
        Resources.MergedDictionaries.Add(new ResourceInclude((Uri?)null)
        {
            Source = new Uri("avares://RunawayExplorer/Assets/Localization/Strings.en.axaml"),
        });

        // MainWindow.axaml binds the tree's icons through this converter as a StaticResource, which the
        // real App.axaml supplies. StaticResource throws when unresolved (unlike DynamicResource), so
        // without it no test can Show() a MainWindow.
        Resources["ResourceKeyToImageConverter"] = new RunawayExplorer.Services.ResourceKeyToImageConverter();

        // MainWindow's tree template reads this brush; the real value lives in App.axaml's theme
        // dictionaries and only needs to resolve to something here.
        Resources["TextFillColorPrimaryBrush"] = new SolidColorBrush(Colors.White);
    }
}
