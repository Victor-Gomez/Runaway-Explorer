using System.Text.Json;
using System.Text.Json.Serialization;

namespace RunawayExplorer.Core.FileSystem;

/// <summary>
/// Remembers what every scene-archive entry was classified as, keyed by the archive's path, size and
/// modification time. Classifying an install means reading and probing ~900 MB of archives -- a minute
/// or so cold -- and the answer never changes for an unchanged file, so the second launch reads this
/// instead. A stale or corrupt cache is simply ignored and rebuilt.
/// </summary>
public sealed class ScanCache
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>Bump when the classifier changes so old answers are discarded.</summary>
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
    }

    private readonly Document _doc;
    private readonly object _gate = new();
    private bool _dirty;

    public string Path { get; }

    private ScanCache(string path, Document doc)
    {
        Path = path;
        _doc = doc;
    }

    /// <summary>The default location: <c>RunawayExplorer/scan-cache.json</c> under local app data.</summary>
    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RunawayExplorer", "scan-cache.json");

    public static ScanCache Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options);
                if (doc is not null && doc.Version == FormatVersion)
                    return new ScanCache(path, doc);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Rebuilt below.
        }
        return new ScanCache(path, new Document());
    }

    /// <summary>An empty cache that is never persisted (tests, or the "don't cache" setting).</summary>
    public static ScanCache Ephemeral() => new(string.Empty, new Document());

    public static string KeyFor(string archivePath)
    {
        var fi = new FileInfo(archivePath);
        return $"{fi.FullName}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
    }

    public List<CachedEntry>? TryGet(string archivePath)
    {
        string key = KeyFor(archivePath);
        lock (_gate)
            return _doc.Archives.TryGetValue(key, out List<CachedEntry>? entries) ? entries : null;
    }

    public void Put(string archivePath, List<CachedEntry> entries)
    {
        string key = KeyFor(archivePath);
        lock (_gate)
        {
            _doc.Archives[key] = entries;
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
            _dirty = true;
        }
    }
}
