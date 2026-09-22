using System.IO;

namespace RunawayExplorer.Core.FileSystem;

/// <summary>Supported game release.</summary>
public enum GameVersion
{
    /// <summary>Runaway: A Road Adventure (2001).</summary>
    Runaway1,

    /// <summary>Runaway 2: The Dream of the Turtle (2006).</summary>
    Runaway2,
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

        // Check for Runaway 2 signatures: RunawayTDOTT.exe, or Dataa/Dataaa.000
        string exeR2 = Path.Combine(baseDir, "RunawayTDOTT.exe");
        if (File.Exists(exeR2))
            return GameVersion.Runaway2;

        string dataa000 = Path.Combine(baseDir, "Dataa", "Dataaa.000");
        if (File.Exists(dataa000))
            return GameVersion.Runaway2;

        string dataa000Alt = Path.Combine(baseDir, "DATAA", "DATAAA.000");
        if (File.Exists(dataa000Alt))
            return GameVersion.Runaway2;


        // R2 has unique scene archives like RESOURCE.SP1 or RESOURCE.B04A
        if (File.Exists(Path.Combine(baseDir, "RESOURCE.SP1")) ||
            File.Exists(Path.Combine(baseDir, "Resource", "RESOURCE.SP1")) ||
            File.Exists(Path.Combine(baseDir, "RESOURCE.B04A")) ||
            File.Exists(Path.Combine(baseDir, "Resource", "RESOURCE.B04A")))
            return GameVersion.Runaway2;

        return GameVersion.Runaway1;
    }
}
