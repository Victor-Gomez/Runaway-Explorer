using System.IO;

namespace RunawayExplorer.Core.FileSystem;

/// <summary>Supported game release.</summary>
public enum GameVersion
{
    /// <summary>Runaway: A Road Adventure (2001).</summary>
    Runaway1 = 0,

    /// <summary>Runaway 2: The Dream of the Turtle (2006).</summary>
    Runaway2 = 1,

    /// <summary>Runaway: A Twist of Fate (2009).</summary>
    Runaway3 = 2,
}

/// <summary>Metadata and detection definition for a supported game.</summary>
public sealed class GameDefinition
{
    public GameVersion Version { get; }
    public string Title { get; }
    public IReadOnlyList<string> Signatures { get; }

    public GameDefinition(GameVersion version, string title, IReadOnlyList<string> signatures)
    {
        Version = version;
        Title = title;
        Signatures = signatures;
    }

    public static readonly GameDefinition Runaway3 = new(
        GameVersion.Runaway3,
        "Runaway: A Twist of Fate",
        [
            "RATOF.exe",
            "ratof.config",
            "RATOF-Config.exe",
            "Resource/RESOURCE.D01",
            "RESOURCE.D01"
        ]);

    public static readonly GameDefinition Runaway2 = new(
        GameVersion.Runaway2,
        "Runaway: The Dream of the Turtle",
        [
            "RunawayTDOTT.exe",
            "Resource/RESOURCE.B04A",
            "RESOURCE.B04A",
            "Resource/RESOURCE.SP1",
            "RESOURCE.SP1",
            "Dataa/Dataaa.000",
            "DATAA/DATAAA.000"
        ]);

    public static readonly GameDefinition Runaway1 = new(
        GameVersion.Runaway1,
        "Runaway: A Road Adventure",
        [
            "Runaway.exe",
            "Resource/RESOURCE.001",
            "RESOURCE.001"
        ]);

    public static readonly IReadOnlyList<GameDefinition> All = [Runaway3, Runaway2, Runaway1];

    public static GameDefinition Get(GameVersion version) => version switch
    {
        GameVersion.Runaway3 => Runaway3,
        GameVersion.Runaway2 => Runaway2,
        _ => Runaway1,
    };
}

public static class GameVersionExtensions
{
    public static string GetTitle(this GameVersion version) => GameDefinition.Get(version).Title;
}

/// <summary>Detects which game is installed in a given folder.</summary>
public static class GameDetector
{
    /// <summary>
    /// Detects the game version from the files present in <paramref name="baseDir"/>.
    /// Defaults to <see cref="GameVersion.Runaway1"/> if unknown.
    /// </summary>
    public static GameVersion Detect(string baseDir)
    {
        if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir))
            return GameVersion.Runaway1;

        foreach (GameDefinition game in GameDefinition.All)
        {
            foreach (string sig in game.Signatures)
            {
                string fullPath = Path.Combine(baseDir, sig.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath))
                    return game.Version;
            }
        }

        return GameVersion.Runaway1;
    }
}
