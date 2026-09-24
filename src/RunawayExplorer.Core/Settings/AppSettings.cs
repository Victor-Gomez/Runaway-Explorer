using System.Text.Json;
using System.Text.Json.Serialization;
using RunawayExplorer.Core.FileSystem;

namespace RunawayExplorer.Core.Settings;

/// <summary>
/// Persisted application preferences: a small JSON document stored under the user's roaming
/// application data folder.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>The installation folder for Runaway: A Road Adventure.</summary>
    public string? Runaway1Dir { get; set; }

    /// <summary>The installation folder for Runaway: The Dream of the Turtle.</summary>
    public string? Runaway2Dir { get; set; }

    /// <summary>The installation folder for Runaway: A Twist of Fate.</summary>
    public string? Runaway3Dir { get; set; }

    /// <summary>The installation folder for The Next BIG Thing.</summary>
    public string? TheNextBigThingDir { get; set; }

    /// <summary>The installation folder for Yesterday.</summary>
    public string? YesterdayDir { get; set; }

    /// <summary>The currently active game.</summary>
    public GameVersion ActiveGame { get; set; } = GameVersion.Runaway1;

    /// <summary>Gets the configured install directory for the specified game version.</summary>
    public string? GetGameDir(GameVersion game) => game switch
    {
        GameVersion.Yesterday => YesterdayDir,
        GameVersion.TheNextBigThing => TheNextBigThingDir,
        GameVersion.Runaway3 => Runaway3Dir,
        GameVersion.Runaway2 => Runaway2Dir,
        _ => Runaway1Dir,
    };

    /// <summary>Sets the install directory for the specified game version.</summary>
    public void SetGameDir(GameVersion game, string? path)
    {
        if (game == GameVersion.Yesterday)
            YesterdayDir = path;
        else if (game == GameVersion.TheNextBigThing)
            TheNextBigThingDir = path;
        else if (game == GameVersion.Runaway3)
            Runaway3Dir = path;
        else if (game == GameVersion.Runaway2)
            Runaway2Dir = path;
        else
            Runaway1Dir = path;
    }

    /// <summary>The installation directory for the currently active game.</summary>
    public string? ActiveGameDir => GetGameDir(ActiveGame);

    /// <summary>The last-used game installation folder, for backward compatibility.</summary>
    public string? BaseDir
    {
        get => ActiveGameDir;
        set => SetGameDir(ActiveGame, value);
    }

    /// <summary>Play sound entries as soon as they are selected.</summary>
    public bool AutoPlaySound { get; set; } = true;

    /// <summary>Play videos as soon as they are selected.</summary>
    public bool AutoPlayVideo { get; set; } = true;

    /// <summary>Loop sound playback on end-of-file.</summary>
    public bool LoopSoundPlayback { get; set; }

    /// <summary>Audio playback volume (0 to 100).</summary>
    public int Volume { get; set; } = 100;

    /// <summary>Whether audio playback is muted.</summary>
    public bool IsMuted { get; set; }

    /// <summary>Start animations playing as soon as they are selected.</summary>
    public bool AutoPlayAnimation { get; set; } = true;

    /// <summary>Loop animations. On by default: nearly every sprite is a cycle.</summary>
    public bool LoopAnimation { get; set; } = true;

    /// <summary>
    /// Frame rate for animation playback and for exported APNGs. The game files store no timing at all,
    /// so this is a choice, not a fact; 15 looks right for most cycles.
    /// </summary>
    public double AnimationFps { get; set; } = 15.0;

    /// <summary>
    /// How overlays and animations are rendered against the scene background:
    /// <c>"no"</c> (checkerboard), <c>"yes"</c> (color background), <c>"greyed"</c> (grayscale background).
    /// </summary>
    public string BackgroundMode { get; set; } = "yes";

    /// <summary>Draw overlays and animation frames on top of their scene's background, at their real screen position.</summary>
    public bool ShowOnBackground
    {
        get => !string.Equals(BackgroundMode, "no", StringComparison.OrdinalIgnoreCase);
        set => BackgroundMode = value ? (string.Equals(BackgroundMode, "no", StringComparison.OrdinalIgnoreCase) ? "yes" : BackgroundMode) : "no";
    }

    /// <summary>Sample rate assumed for the voice lines, which carry none of their own. 16,000 Hz sounds right.</summary>
    public int VoiceSampleRate { get; set; } = 16_000;

    /// <summary>Remember scene-archive classifications between launches (see <c>ScanCache</c>).</summary>
    public bool UseScanCache { get; set; } = true;

    /// <summary>UI theme: <c>System</c>, <c>Light</c>, or <c>Dark</c>. Maps to Avalonia's <c>ThemeVariant</c>.</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Application and metadata language: <c>en</c> (English) or <c>es</c> (Español).</summary>
    public string Language { get; set; } = "en";

    /// <summary>The folder Export / Batch Export dialogs default to on next open. <c>null</c> means "let the OS pick".</summary>
    public string? LastExportDir { get; set; }

    /// <summary>Most-recently-used install folders, in reverse-chronological order. Capped at 5 entries.</summary>
    public List<string> RecentInstalls { get; set; } = [];

    /// <summary>Last-selected path per install folder (keyed by <see cref="BaseDir"/> at save time).</summary>
    public Dictionary<string, string> LastSelectedPath { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the app checks GitHub for a newer release on startup. <c>Ask</c> (the default) means the
    /// user hasn't decided yet and gets a one-time prompt; <c>OnStartup</c> checks at most once a day;
    /// <c>Never</c> only checks when Help &gt; Check for Updates is clicked.
    /// </summary>
    public string UpdateCheckMode { get; set; } = "Ask";

    /// <summary>When the last successful update check ran. <c>null</c> means "never checked".</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>Release tag the user dismissed with "Skip this version".</summary>
    public string? SkippedUpdateVersion { get; set; }

    /// <summary>The release feed the update check reads, in GitHub's <c>/releases/latest</c> JSON shape.</summary>
    public string ReleaseFeedUrl { get; set; } = "https://api.github.com/repos/Victor-Gomez/Runaway-Explorer/releases/latest";

    /// <summary>Last non-maximized window bounds, restored on next launch. <c>null</c> means "never saved".</summary>
    public int? WindowX { get; set; }

    /// <inheritdoc cref="WindowX"/>
    public int? WindowY { get; set; }

    /// <inheritdoc cref="WindowX"/>
    public double? WindowWidth { get; set; }

    /// <inheritdoc cref="WindowX"/>
    public double? WindowHeight { get; set; }

    /// <summary>Whether the window was maximized when it was last closed.</summary>
    public bool WindowMaximized { get; set; }

    /// <summary>Registers <paramref name="install"/> as the most-recent install and trims the list to 5 entries.</summary>
    public void RegisterRecentInstall(string install)
    {
        if (string.IsNullOrWhiteSpace(install))
            return;

        RecentInstalls.RemoveAll(p => string.Equals(p, install, StringComparison.OrdinalIgnoreCase));
        RecentInstalls.Insert(0, install);
        if (RecentInstalls.Count > 5)
            RecentInstalls.RemoveRange(5, RecentInstalls.Count - 5);
    }

    /// <summary>
    /// Loads settings from <c>RunawayExplorer/settings.json</c> under the roaming app-data folder. A missing,
    /// unreadable or malformed file yields defaults rather than an exception.
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            string path = GetSettingsFilePath();
            if (!File.Exists(path))
                return new AppSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), SerializerOptions);
            if (settings is not null)
            {
                if (string.IsNullOrWhiteSpace(settings.Runaway1Dir) && string.IsNullOrWhiteSpace(settings.Runaway2Dir) && string.IsNullOrWhiteSpace(settings.Runaway3Dir) && string.IsNullOrWhiteSpace(settings.TheNextBigThingDir) && string.IsNullOrWhiteSpace(settings.YesterdayDir))
                {
                    foreach (string candidate in settings.RecentInstalls)
                    {
                        if (Directory.Exists(candidate))
                        {
                            GameVersion v = GameDetector.Detect(candidate);
                            if (v == GameVersion.Yesterday && string.IsNullOrWhiteSpace(settings.YesterdayDir))
                                settings.YesterdayDir = candidate;
                            else if (v == GameVersion.TheNextBigThing && string.IsNullOrWhiteSpace(settings.TheNextBigThingDir))
                                settings.TheNextBigThingDir = candidate;
                            else if (v == GameVersion.Runaway3 && string.IsNullOrWhiteSpace(settings.Runaway3Dir))
                                settings.Runaway3Dir = candidate;
                            else if (v == GameVersion.Runaway2 && string.IsNullOrWhiteSpace(settings.Runaway2Dir))
                                settings.Runaway2Dir = candidate;
                            else if (v == GameVersion.Runaway1 && string.IsNullOrWhiteSpace(settings.Runaway1Dir))
                                settings.Runaway1Dir = candidate;
                        }
                    }
                }
                return settings;
            }
            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        string path = GetSettingsFilePath();
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions));
    }

    private static string GetSettingsFilePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RunawayExplorer", "settings.json");
}
