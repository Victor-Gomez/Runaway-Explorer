using System.Reflection;

namespace RunawayExplorer.Services;

/// <summary>
/// Facts about the running build and its environment, gathered in one place so Help &gt; About and the
/// update check can't disagree with each other. Everything here is cheap and side-effect free except
/// <see cref="DescribeLibVlc"/>, which deliberately touches the native engine.
/// </summary>
public static class AppInfo
{
    private static readonly Assembly Self = typeof(AppInfo).Assembly;

    /// <summary>Numeric build version, e.g. <c>1.1.0</c>. Stamped from the release tag by CI.</summary>
    public static Version Version => Self.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Short display version -- the three numeric components without the trailing assembly revision.</summary>
    public static string DisplayVersion => $"{Version.Major}.{Version.Minor}.{Version.Build}";

    /// <summary>Full informational version: <c>1.1.0+&lt;sha&gt;</c> on CI builds, whatever the csproj says locally.</summary>
    public static string InformationalVersion =>
        Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? DisplayVersion;

    /// <summary>The commit this build came from, or <see langword="null"/> for a local build. Short 7-character form.</summary>
    public static string? CommitSha
    {
        get
        {
            int plus = InformationalVersion.IndexOf('+');
            if (plus < 0 || plus == InformationalVersion.Length - 1)
                return null;

            string sha = InformationalVersion[(plus + 1)..];
            return sha.Length > 7 ? sha[..7] : sha;
        }
    }

    /// <summary>Version of the Avalonia assembly actually loaded, for bug reports.</summary>
    public static string AvaloniaVersion =>
        typeof(Avalonia.Application).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>
    /// One-line status of LibVLC. Initialising the native engine is the only reliable probe, so this
    /// forces the shared instance and reports whatever it throws -- on Linux a missing distro package
    /// surfaces here rather than as a silent dead play button.
    /// </summary>
    public static string DescribeLibVlc()
    {
        try
        {
            return $"Loaded: {LibVlcRuntime.Shared.Version}";
        }
        catch (Exception ex)
        {
            return $"Unavailable: {ex.Message}";
        }
    }
}
