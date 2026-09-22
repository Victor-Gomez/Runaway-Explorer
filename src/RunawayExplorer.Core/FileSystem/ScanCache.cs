using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RunawayExplorer.Core.FileSystem;

/// <summary>
/// Remembers what every scene-archive entry was classified as, using a two-tier strategy:
/// 1. Fast path: keyed by archive path, file size and modification time (0 ms, zero I/O on repeat launches).
/// 2. Hash path: keyed by the archive's SHA-256 content hash, checked against both local user cache and
///    a pre-computed shipped cache of known game files (so first launches and folder moves load instantly).
/// Classifying an install means reading and probing ~900 MB of archives; with the hash cache, known files
/// load in less than a second on first launch without requiring cold classification.
/// </summary>
public sealed class ScanCache
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>Bump when the classifier or cache schema changes so old answers are discarded.</summary>
    public const int FormatVersion = 5;

    public sealed class CachedEntry
    {
        [JsonPropertyName("i")] public int Index { get; set; }
        [JsonPropertyName("o")] public long Offset { get; set; }
        [JsonPropertyName("s")] public long Size { get; set; }
        [JsonPropertyName("k")] public EntryKind Kind { get; set; }
        [JsonPropertyName("w")] public int Width { get; set; }
        [JsonPropertyName("h")] public int Height { get; set; }
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("y")] public int Y { get; set; }
        [JsonPropertyName("f")] public int Frames { get; set; }
        [JsonPropertyName("sh")] public double Sharpness { get; set; }
        [JsonPropertyName("m")] public bool IsMask { get; set; }

        public ImageInfo? ToImageInfo() => Kind is EntryKind.Data ? null : new ImageInfo
        {
            Width = Width, Height = Height, X = X, Y = Y, Frames = Frames, Sharpness = Sharpness, IsMask = IsMask,
        };

        public static CachedEntry From(ArchiveEntry entry, EntryClassification c) => new()
        {
            Index = entry.Index,
            Offset = entry.Offset,
            Size = entry.Size,
            Kind = c.Kind,
            Width = c.Image?.Width ?? 0,
            Height = c.Image?.Height ?? 0,
            X = c.Image?.X ?? 0,
            Y = c.Image?.Y ?? 0,
            Frames = c.Image?.Frames ?? 0,
            Sharpness = c.Image?.Sharpness ?? 0,
            IsMask = c.Image?.IsMask ?? false,
        };
    }

    public sealed class Document
    {
        public int Version { get; set; } = FormatVersion;
        public Dictionary<string, List<CachedEntry>> Archives { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<CachedEntry>> Hashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly Lazy<Dictionary<string, List<CachedEntry>>> ShippedCache = new(LoadShippedCache);

    /// <summary>Number of pre-computed archive classifications shipped with the application.</summary>
    public static int ShippedCount => ShippedCache.Value.Count;

    private readonly Document _doc;
    private readonly bool _includeShipped;
    private readonly object _gate = new();
    private bool _dirty;

    public string Path { get; }

    private ScanCache(string path, Document doc, bool includeShipped = true)
    {
        Path = path;
        _doc = doc;
        _doc.Archives ??= new(StringComparer.OrdinalIgnoreCase);
        _doc.Hashes ??= new(StringComparer.OrdinalIgnoreCase);
        _includeShipped = includeShipped;
    }

    /// <summary>The default location: <c>RunawayExplorer/scan-cache.json</c> under local app data.</summary>
    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RunawayExplorer", "scan-cache.json");

    public static ScanCache Load(string? path = null, bool includeShipped = true)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options);
                if (doc is not null && (doc.Version == FormatVersion || doc.Version == 4))
                {
                    doc.Version = FormatVersion;
                    return new ScanCache(path, doc, includeShipped);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Rebuilt below.
        }
        return new ScanCache(path, new Document(), includeShipped);
    }

    /// <summary>An empty cache that is never persisted (tests, or the "don't cache" setting).</summary>
    public static ScanCache Ephemeral(bool includeShipped = false) => new(string.Empty, new Document(), includeShipped);

    /// <summary>Computes a lowercase 64-character SHA-256 hash of a file's binary content.</summary>
    public static string ComputeHash(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
        return ComputeHash(stream);
    }

    /// <summary>Computes a lowercase 64-character SHA-256 hash of a stream.</summary>
    public static string ComputeHash(Stream stream)
    {
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Computes a lowercase 64-character SHA-256 hash of a byte buffer.</summary>
    public static string ComputeHash(ReadOnlySpan<byte> data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    public static string KeyFor(string archivePath)
    {
        var fi = new FileInfo(archivePath);
        return $"{fi.FullName}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
    }

    /// <summary>
    /// Attempts to get cached entries for an archive. Tries the fast path (path|size|mtime) first,
    /// then falls back to SHA-256 content hashing to check local and shipped caches.
    /// </summary>
    public List<CachedEntry>? TryGet(string archivePath)
    {
        // Tier 1: Fast-path (path + size + timestamp in local cache)
        string key = KeyFor(archivePath);
        lock (_gate)
        {
            if (_doc.Archives.TryGetValue(key, out List<CachedEntry>? localEntries))
                return localEntries;
        }

        if (!File.Exists(archivePath))
            return null;

        // Tier 2: Hash-based lookup
        string hash;
        try
        {
            hash = ComputeHash(archivePath);
        }
        catch (Exception)
        {
            return null;
        }

        return TryGetByHash(hash, archivePath);
    }

    /// <summary>
    /// Looks up entries by SHA-256 content hash. If found and <paramref name="archivePath"/> is provided,
    /// also registers the file in the Tier 1 fast-path cache so subsequent launches require no hashing.
    /// </summary>
    public List<CachedEntry>? TryGetByHash(string hash, string? archivePath = null)
    {
        List<CachedEntry>? entries = null;

        lock (_gate)
        {
            if (_doc.Hashes.TryGetValue(hash, out entries))
            {
                // Found in local hash cache
            }
        }

        if (entries is null && _includeShipped)
        {
            if (ShippedCache.Value.TryGetValue(hash, out List<CachedEntry>? shippedEntries))
                entries = shippedEntries;
        }

        if (entries is not null && archivePath is not null)
        {
            // Promote to Tier 1 fast-path for next launch on this machine
            lock (_gate)
            {
                _doc.Archives[KeyFor(archivePath)] = entries;
                _doc.Hashes[hash] = entries;
                _dirty = true;
            }
        }

        return entries;
    }

    /// <summary>Puts entries into the cache. Stores in both the fast-path table and content-hash table.</summary>
    public void Put(string archivePath, List<CachedEntry> entries, string? hash = null)
    {
        string key = KeyFor(archivePath);
        if (hash is null && File.Exists(archivePath))
        {
            try { hash = ComputeHash(archivePath); }
            catch (Exception) { }
        }

        lock (_gate)
        {
            _doc.Archives[key] = entries;
            if (hash is not null)
                _doc.Hashes[hash] = entries;
            _dirty = true;
        }
    }

    /// <summary>Writes the cache if anything changed. Failures are swallowed: a cache that can't be written costs a rescan, nothing more.</summary>
    public void Save()
    {
        if (string.IsNullOrEmpty(Path))
            return;

        string json;
        lock (_gate)
        {
            if (!_dirty)
                return;
            json = JsonSerializer.Serialize(_doc, Options);
            _dirty = false;
        }

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _doc.Archives.Clear();
            _doc.Hashes.Clear();
            _dirty = true;
        }
    }

    private static Dictionary<string, List<CachedEntry>> LoadShippedCache()
    {
        var result = new Dictionary<string, List<CachedEntry>>(StringComparer.OrdinalIgnoreCase);

        // 1. Embedded shipped cache
        try
        {
            using Stream? stream = typeof(ScanCache).Assembly.GetManifestResourceStream("RunawayExplorer.Core.Resources.shipped-scan-cache.json");
            if (stream is not null)
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, List<CachedEntry>>>(stream, Options);
                if (dict is not null)
                {
                    foreach ((string k, List<CachedEntry> v) in dict)
                        result[k] = v;
                }
            }
        }
        catch (Exception)
        {
        }

        // 2. Optional external file next to the executable
        try
        {
            string extPath = System.IO.Path.Combine(AppContext.BaseDirectory, "shipped-scan-cache.json");
            if (File.Exists(extPath))
            {
                using FileStream fs = File.OpenRead(extPath);
                var extDict = JsonSerializer.Deserialize<Dictionary<string, List<CachedEntry>>>(fs, Options);
                if (extDict is not null)
                {
                    foreach ((string k, List<CachedEntry> v) in extDict)
                        result[k] = v;
                }
            }
        }
        catch (Exception)
        {
        }

        return result;
    }
}
