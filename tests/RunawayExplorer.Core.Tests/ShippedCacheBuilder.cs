using System.Text.Json;
using RunawayExplorer.Core.FileSystem;
using Xunit;

namespace RunawayExplorer.Core.Tests;

/// <summary>
/// Builds <c>src/RunawayExplorer.Core/Resources/shipped-scan-cache.json</c>, the pre-computed
/// classification that makes a first launch on a known install instant instead of a cold scan of
/// several hundred megabytes.
/// <para>
/// It works by scanning a real install and writing out whatever the scan cached, so it covers exactly
/// what a launch would otherwise have to classify -- today the scene archives and <c>RESOURCE.000</c> --
/// and gains anything a future <see cref="VirtualFileSystem"/> decides to cache without being touched.
/// <c>RESOURCE.IFZ</c> is deliberately not cached anywhere: identifying its entries reads 64 bytes each
/// and costs less than hashing the file would.
/// </para>
/// <para>
/// Each game's test file calls <see cref="AddInstall"/> for its own install and skips when that install
/// is absent, which is why the generated file grows on a machine that has all six games and stays valid
/// on one that has none. The entries already in the file are reused, so only archives it has never seen
/// are classified.
/// </para>
/// </summary>
internal static class ShippedCacheBuilder
{
    /// <summary>One writer at a time: every game's test merges into the same file, and xUnit runs the
    /// classes that own them in parallel.</summary>
    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>The checked-in shipped cache, found by walking up to the repository root.</summary>
    public static string JsonPath
    {
        get
        {
            string dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir)
                && !File.Exists(Path.Combine(dir, "RunawayExplorer.slnx"))
                && !Directory.Exists(Path.Combine(dir, ".git")))
            {
                dir = Path.GetDirectoryName(dir)!;
            }

            Assert.False(string.IsNullOrEmpty(dir), "Could not locate the repository root");
            return Path.Combine(dir, "src", "RunawayExplorer.Core", "Resources", "shipped-scan-cache.json");
        }
    }

    /// <summary>The shipped cache as it stands, or an empty one when it is missing or of another format version.</summary>
    public static Dictionary<string, List<ScanCache.CachedEntry>> Read()
    {
        try
        {
            if (File.Exists(JsonPath)
                && JsonSerializer.Deserialize<ScanCache.ShippedDocument>(File.ReadAllText(JsonPath), Options) is { } doc
                && doc.Version == ScanCache.FormatVersion)
            {
                return new Dictionary<string, List<ScanCache.CachedEntry>>(doc.Hashes, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (JsonException)
        {
        }

        return new Dictionary<string, List<ScanCache.CachedEntry>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether the shipped cache already answers for this archive.</summary>
    public static bool Covers(string archivePath) =>
        File.Exists(archivePath) && Read().ContainsKey(ScanCache.ComputeHash(archivePath));

    /// <summary>
    /// Scans <paramref name="installDir"/> and merges what the scan cached into the shipped file.
    /// Returns the number of archives added; 0 means the install was already covered.
    /// </summary>
    public static int AddInstall(string installDir)
    {
        lock (Gate)
        {
            Dictionary<string, List<ScanCache.CachedEntry>> shipped = Read();
            int before = shipped.Count;

            // Hand the scan what is already known, as an ordinary cache, so it only classifies archives
            // the file has never seen. Loading it from a temp file keeps this to the public API.
            string seed = Path.Combine(Path.GetTempPath(), $"shipped-seed-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(seed, JsonSerializer.Serialize(
                    new ScanCache.Document { Hashes = shipped }, Options));

                ScanCache cache = ScanCache.Load(seed, includeShipped: false);
                VirtualFileSystem.Init(installDir, cache: cache);

                foreach ((string hash, List<ScanCache.CachedEntry> entries) in cache.ContentHashes)
                {
                    if (entries.Count > 0)
                        shipped[hash] = entries;
                }
            }
            finally
            {
                if (File.Exists(seed))
                    File.Delete(seed);
            }

            if (shipped.Count != before)
            {
                File.WriteAllText(JsonPath, JsonSerializer.Serialize(
                    new ScanCache.ShippedDocument { Hashes = shipped }, Options));
            }

            return shipped.Count - before;
        }
    }
}
