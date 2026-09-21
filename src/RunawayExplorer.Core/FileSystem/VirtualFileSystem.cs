using RunawayExplorer.Core.Formats;
using RunawayExplorer.Core.Metadata;

namespace RunawayExplorer.Core.FileSystem;

/// <summary>
/// The whole install as one tree. Runaway keeps its assets in nameless offset-table archives spread
/// over three folders, so the tree is organised by what the entries are rather than by where they sit:
/// <code>
/// &lt;install&gt;
///   Scenes            one folder per RESOURCE.&lt;L&gt;&lt;nn&gt;, entries classified as background / overlay / animation / data
///   Music             RESOURCE.M*
///   Ambient &amp; SFX     RESOURCE.S*
///   Cinematic Audio   RESOURCE.002
///   Voice             the DATAACA shards merged, grouped in blocks of 500 clips
///   Lip-sync          RESOURCE.004
///   Video             Datav/*, headers restored from DATAVC00
///   Global Data       RESOURCE.000 entries, plus the archives no decoder claims
/// </code>
/// Building it means classifying every scene-archive entry, which reads ~900 MB; see <see cref="ScanCache"/>.
/// </summary>
public sealed class VirtualFileSystem
{
    public const string ScenesFolder = "Scenes";
    public const string MusicFolder = "Music";
    public const string AmbientFolder = "Ambient & SFX";
    public const string CinematicFolder = "Cinematic Audio";
    public const string VoiceFolder = "Voice";
    public const string LipSyncFolder = "Lip-sync";
    public const string VideoFolder = "Video";
    public const string GlobalFolder = "Global Data";

    public const int VoiceGroupSize = 500;

    public static readonly AudioInfo MusicPcm = new() { SampleRate = 16_000, Channels = 2, BitsPerSample = 16 };
    public static readonly AudioInfo AmbientPcm = new() { SampleRate = 22_050, Channels = 2, BitsPerSample = 16 };
    public static readonly AudioInfo CinematicPcm = new() { SampleRate = 22_050, Channels = 2, BitsPerSample = 16 };

    /// <summary>The files carry no rate; 16,000 Hz is what the lines sound right at (22,050 makes them rushed and high).</summary>
    public static readonly AudioInfo VoicePcm = new() { SampleRate = 16_000, Channels = 1, BitsPerSample = 8 };

    private readonly Dictionary<string, DecodedImage?> _backgroundCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _backgroundGate = new();
    private readonly Dictionary<string, byte[]?> _attributeTableCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _attributeTableGate = new();

    private VirtualFileSystem(string baseDir, FsNode root, VideoKeyfile? keyfile)
    {
        BaseDir = baseDir;
        Root = root;
        Keyfile = keyfile;
    }

    public string BaseDir { get; }

    public FsNode Root { get; }

    /// <summary>Active metadata and display language (e.g. "en", "es").</summary>
    public string Language { get; private set; } = SceneCatalog.DefaultLanguage;

    /// <summary>The parsed <c>DATAVC00</c> keyfile, or <see langword="null"/> when the install has none.</summary>
    public VideoKeyfile? Keyfile { get; }

    /// <summary>Counts gathered while scanning, for the status line.</summary>
    public ScanSummary Summary { get; private set; } = new();

    public sealed class ScanSummary
    {
        public int SceneArchives { get; set; }
        public int Backgrounds { get; set; }
        public int Masks { get; set; }
        public int Overlays { get; set; }
        public int Animations { get; set; }
        public int DataEntries { get; set; }
        public int AudioClips { get; set; }
        public int VoiceClips { get; set; }
        public int Videos { get; set; }
        public int Visemes { get; set; }
        public bool FromCache { get; set; }

        public override string ToString() =>
            $"{SceneArchives} scenes: {Backgrounds} backgrounds, {Masks} masks, {Overlays} overlays, {Animations} animations; " +
            $"{AudioClips} audio clips, {VoiceClips} voice lines, {Videos} videos, {Visemes} lip-sync tracks";
    }

    // ---------------------------------------------------------------------------------------------
    // Building
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Scans <paramref name="baseDir"/> (the folder containing <c>Resource\</c>) and builds the tree.
    /// Throws <see cref="DirectoryNotFoundException"/> when no <c>Resource</c> folder is found.
    /// </summary>
    public static VirtualFileSystem Init(
        string baseDir,
        Action<string>? progress = null,
        ScanCache? cache = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDir);
        baseDir = Path.GetFullPath(baseDir);

        string? resourceDir = FindDir(baseDir, "Resource");
        if (resourceDir is null)
            throw new DirectoryNotFoundException($"No 'Resource' folder under {baseDir}. Pick the game's install folder (the one containing Runaway.exe).");

        cache ??= ScanCache.Ephemeral();
        string activeLanguage = language ?? SceneCatalog.DefaultLanguage;

        var root = new FsNode { NodeType = FsNodeType.Root | FsNodeType.Directory, Name = Path.GetFileName(baseDir.TrimEnd(Path.DirectorySeparatorChar)) };
        var summary = new ScanSummary();

        string? dataaDir = FindDir(baseDir, "Dataa");
        string? datavDir = FindDir(baseDir, "Datav");

        VideoKeyfile? keyfile = null;
        if (datavDir is not null)
        {
            string? keyPath = Directory.EnumerateFiles(datavDir).FirstOrDefault(f => VideoKeyfile.IsKeyfileName(Path.GetFileName(f)));
            if (keyPath is not null)
            {
                try { keyfile = VideoKeyfile.Load(keyPath); }
                catch (IOException) { keyfile = null; }
            }
        }

        var vfs = new VirtualFileSystem(baseDir, root, keyfile)
        {
            Language = activeLanguage
        };

        string[] resourceFiles = Directory.GetFiles(resourceDir);
        Array.Sort(resourceFiles, StringComparer.OrdinalIgnoreCase);

        FsNode scenes = Folder(root, ScenesFolder);
        scenes.FriendlyName = SceneCatalog.GetCategoryTitle(ScenesFolder, activeLanguage);
        FsNode music = Folder(root, MusicFolder);
        music.FriendlyName = SceneCatalog.GetCategoryTitle(MusicFolder, activeLanguage);
        FsNode ambient = Folder(root, AmbientFolder);
        ambient.FriendlyName = SceneCatalog.GetCategoryTitle(AmbientFolder, activeLanguage);
        FsNode cinematic = Folder(root, CinematicFolder);
        cinematic.FriendlyName = SceneCatalog.GetCategoryTitle(CinematicFolder, activeLanguage);
        FsNode voice = Folder(root, VoiceFolder);
        voice.FriendlyName = SceneCatalog.GetCategoryTitle(VoiceFolder, activeLanguage);
        FsNode lipSync = Folder(root, LipSyncFolder);
        lipSync.FriendlyName = SceneCatalog.GetCategoryTitle(LipSyncFolder, activeLanguage);
        FsNode video = Folder(root, VideoFolder);
        video.FriendlyName = SceneCatalog.GetCategoryTitle(VideoFolder, activeLanguage);
        FsNode global = Folder(root, GlobalFolder);
        global.FriendlyName = SceneCatalog.GetCategoryTitle(GlobalFolder, activeLanguage);

        // Scene archives are the expensive part; classify them in parallel, then attach in name order.
        List<string> sceneArchives = resourceFiles.Where(f => SceneArchive.IsSceneArchiveName(Path.GetFileName(f))).ToList();
        var sceneNodes = new FsNode?[sceneArchives.Count];
        int done = 0;
        bool anyFromCache = false;

        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6),
        };
        Parallel.For(0, sceneArchives.Count, options, i =>
        {
            string path = sceneArchives[i];
            progress?.Invoke($"Scanning {Path.GetFileName(path)}  ({Interlocked.Increment(ref done)}/{sceneArchives.Count})");
            (FsNode node, bool fromCache) = BuildSceneArchive(path, cache, activeLanguage, cancellationToken);
            sceneNodes[i] = node;
            if (fromCache)
                anyFromCache = true;
        });

        foreach (FsNode? node in sceneNodes)
        {
            if (node is null)
                continue;
            Attach(scenes, node);
            summary.SceneArchives++;
            foreach (FsNode child in node.Children)
            {
                switch (child.Kind)
                {
                    case EntryKind.Background: summary.Backgrounds++; break;
                    case EntryKind.Mask: summary.Masks++; break;
                    case EntryKind.Overlay: summary.Overlays++; break;
                    case EntryKind.Animation: summary.Animations++; break;
                    default: summary.DataEntries++; break;
                }
            }
        }
        cache.Save();
        summary.FromCache = anyFromCache;

        foreach (string path in resourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(path);
            string upper = name.ToUpperInvariant();

            if (SceneArchive.IsSceneArchiveName(name))
                continue;

            if (upper.StartsWith("RESOURCE.M", StringComparison.Ordinal))
            {
                progress?.Invoke($"Reading {name}");
                summary.AudioClips += Attach(music, BuildAudioArchive(path, EntryKind.Music, MusicPcm, dedupe: false, activeLanguage)).Children.Count;
            }
            else if (upper.StartsWith("RESOURCE.S", StringComparison.Ordinal))
            {
                progress?.Invoke($"Reading {name}");
                summary.AudioClips += Attach(ambient, BuildAudioArchive(path, EntryKind.Ambient, AmbientPcm, dedupe: false, activeLanguage)).Children.Count;
            }
            else if (upper == "RESOURCE.002")
            {
                progress?.Invoke($"Reading {name}");
                summary.AudioClips += Attach(cinematic, BuildAudioArchive(path, EntryKind.Cinematic, CinematicPcm, dedupe: true, activeLanguage)).Children.Count;
            }
            else if (upper == "RESOURCE.004")
            {
                progress?.Invoke($"Reading {name}");
                summary.Visemes += Attach(lipSync, BuildVisemeArchive(path)).Children.Count;
            }
            else if (upper == "RESOURCE.000")
            {
                progress?.Invoke($"Reading {name}");
                Attach(global, BuildGlobalArchive(path));
            }
            else
            {
                Attach(global, LooseFile(path));
            }
        }

        if (dataaDir is not null)
        {
            progress?.Invoke("Reading voice shards");
            summary.VoiceClips = BuildVoice(voice, dataaDir);
        }

        if (datavDir is not null)
        {
            progress?.Invoke("Reading videos");
            summary.Videos = BuildVideos(video, datavDir, keyfile);
        }

        // Empty categories only add noise.
        root.Children.RemoveAll(c => c.Children.Count == 0);

        vfs.Summary = summary;
        return vfs;
    }

    /// <summary>Updates display names of all nodes in memory to match the specified language.</summary>
    public void ApplyLanguage(string language)
    {
        Language = language;
        UpdateLanguageRecursive(Root, language);
    }

    private void UpdateLanguageRecursive(FsNode node, string language)
    {
        if (node == Root)
        {
            foreach (FsNode child in node.Children)
                UpdateLanguageRecursive(child, language);
            return;
        }

        if (node.Parent == Root && node.IsDirectory)
        {
            node.FriendlyName = SceneCatalog.GetCategoryTitle(node.Name, language);
        }
        else if (node.IsDirectory && node.Parent?.Parent == Root)
        {
            if (node.Parent.Name == ScenesFolder)
                node.FriendlyName = SceneCatalog.GetSceneTitle(node.Name, language);
            else if (node.Parent.Name is MusicFolder or AmbientFolder or CinematicFolder)
                node.FriendlyName = SceneCatalog.GetAudioTitle(node.Name, language);
        }
        else if (node.IsFile && node.Parent?.Parent?.Name == ScenesFolder)
        {
            node.FriendlyName = SceneCatalog.FormatSceneEntryLabel(node, language);
        }

        foreach (FsNode child in node.Children)
            UpdateLanguageRecursive(child, language);
    }

    private static (FsNode Node, bool FromCache) BuildSceneArchive(string path, ScanCache cache, string language, CancellationToken cancellationToken)
    {
        var node = new FsNode
        {
            NodeType = FsNodeType.Directory,
            Kind = EntryKind.Folder,
            Name = Path.GetFileName(path),
            FriendlyName = SceneCatalog.GetSceneTitle(Path.GetFileName(path), language),
            ArchivePath = path,
            Size = new FileInfo(path).Length,
        };

        List<ScanCache.CachedEntry>? cached = cache.TryGet(path);
        bool fromCache = cached is not null;
        if (cached is null)
        {
            cached = [];
            using FileStream f = File.OpenRead(path);
            foreach (ArchiveEntry entry in SceneArchive.ReadEntries(f))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var buffer = new byte[entry.Size];
                f.Position = entry.Offset;
                f.ReadExactly(buffer);
                EntryClassification c;
                try
                {
                    c = EntryClassifier.Classify(buffer);
                }
                catch (Exception)
                {
                    // A malformed entry must not take the whole archive down; it is simply "data".
                    c = EntryClassification.DataEntry;
                }
                cached.Add(ScanCache.CachedEntry.From(entry, c));
            }
            cache.Put(path, cached);
        }

        foreach (ScanCache.CachedEntry e in cached)
        {
            var child = new FsNode
            {
                NodeType = FsNodeType.File | FsNodeType.InArchive,
                Kind = e.Kind,
                Name = $"e{e.Index:00}",
                Offset = e.Offset,
                Size = e.Size,
                EntryIndex = e.Index,
                ArchivePath = path,
                Image = e.ToImageInfo(),
            };
            child.FriendlyName = SceneCatalog.FormatSceneEntryLabel(child, language);
            Attach(node, child);
        }

        return (node, fromCache);
    }

    /// <summary>The tree label for a scene entry: index plus the decoded facts that identify it at a glance.</summary>
    public static string SceneEntryLabel(FsNode entry) =>
        SceneCatalog.FormatSceneEntryLabel(entry, null);

    private static FsNode BuildAudioArchive(string path, EntryKind kind, AudioInfo pcm, bool dedupe, string language)
    {
        var node = new FsNode
        {
            NodeType = FsNodeType.Directory,
            Name = Path.GetFileName(path),
            FriendlyName = SceneCatalog.GetAudioTitle(Path.GetFileName(path), language),
            ArchivePath = path,
            Size = new FileInfo(path).Length,
        };

        List<ArchiveEntry> entries;
        using (FileStream f = File.OpenRead(path))
            entries = AudioArchive.ReadEntries(f);

        var seen = new HashSet<long>();
        foreach (ArchiveEntry e in entries)
        {
            if (dedupe && !seen.Add(e.Offset))
                continue;
            var child = new FsNode
            {
                NodeType = FsNodeType.File | FsNodeType.InArchive,
                Kind = kind,
                Name = $"t{e.Index:000}",
                Offset = e.Offset,
                Size = e.Size,
                EntryIndex = e.Index,
                ArchivePath = path,
                Audio = pcm,
            };
            child.FriendlyName = $"{child.Name}  {FormatDuration(pcm.DurationSeconds(e.Size))}";
            Attach(node, child);
        }
        return node;
    }

    private static FsNode BuildVisemeArchive(string path)
    {
        var node = new FsNode
        {
            NodeType = FsNodeType.Directory,
            Name = Path.GetFileName(path),
            ArchivePath = path,
            Size = new FileInfo(path).Length,
        };

        List<ArchiveEntry> entries;
        using (FileStream f = File.OpenRead(path))
            entries = VisemeArchive.ReadEntries(f);

        foreach (ArchiveEntry e in entries)
        {
            Attach(node, new FsNode
            {
                NodeType = FsNodeType.File | FsNodeType.InArchive,
                Kind = EntryKind.Viseme,
                Name = $"v{e.Index:00000}",
                FriendlyName = $"v{e.Index:00000}  {e.Size} tick{(e.Size == 1 ? "" : "s")}",
                Offset = e.Offset,
                Size = e.Size,
                EntryIndex = e.Index,
                ArchivePath = path,
            });
        }
        return node;
    }

    private static FsNode BuildGlobalArchive(string path)
    {
        var node = new FsNode
        {
            NodeType = FsNodeType.Directory,
            Name = Path.GetFileName(path),
            ArchivePath = path,
            Size = new FileInfo(path).Length,
        };

        List<ArchiveEntry> entries;
        using (FileStream f = File.OpenRead(path))
            entries = GlobalArchive.ReadEntries(f);

        foreach (ArchiveEntry e in entries)
        {
            Attach(node, new FsNode
            {
                NodeType = FsNodeType.File | FsNodeType.InArchive,
                Kind = EntryKind.GlobalData,
                Name = $"e{e.Index:000}",
                FriendlyName = $"e{e.Index:000}  {FormatSize(e.Size)}",
                Offset = e.Offset,
                Size = e.Size,
                EntryIndex = e.Index,
                ArchivePath = path,
            });
        }
        return node;
    }

    private static FsNode LooseFile(string path)
    {
        var fi = new FileInfo(path);
        return new FsNode
        {
            NodeType = FsNodeType.File,
            Kind = EntryKind.RawFile,
            Name = fi.Name,
            FriendlyName = $"{fi.Name}  {FormatSize(fi.Length)}",
            Size = fi.Length,
            ArchivePath = path,
        };
    }

    private static int BuildVoice(FsNode voice, string dataaDir)
    {
        var shards = new List<string>();
        foreach (string wanted in VoiceArchive.ShardNames())
        {
            string? found = Directory.EnumerateFiles(dataaDir)
                .FirstOrDefault(f => Path.GetFileName(f).Equals(wanted, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
                shards.Add(found);
        }
        if (shards.Count == 0)
            return 0;

        SortedDictionary<int, VoiceArchive.VoiceClip> clips = VoiceArchive.ReadClips(shards);
        var groups = new Dictionary<int, FsNode>();
        foreach (VoiceArchive.VoiceClip clip in clips.Values)
        {
            int g = clip.Index / VoiceGroupSize;
            if (!groups.TryGetValue(g, out FsNode? group))
            {
                int lo = g * VoiceGroupSize;
                group = new FsNode
                {
                    NodeType = FsNodeType.Directory,
                    Name = $"{lo:00000}-{lo + VoiceGroupSize - 1:00000}",
                };
                groups[g] = group;
                Attach(voice, group);
            }

            var node = new FsNode
            {
                NodeType = FsNodeType.File | FsNodeType.InArchive,
                Kind = EntryKind.Voice,
                Name = $"VOICE_{clip.Index:00000}",
                Offset = clip.Offset,
                Size = clip.Size,
                EntryIndex = clip.Index,
                ArchivePath = clip.ShardPath,
                Audio = VoicePcm,
            };
            node.FriendlyName = $"{node.Name}  {FormatDuration(VoicePcm.DurationSeconds(clip.Size))}";
            Attach(group, node);
        }
        return clips.Count;
    }

    private static int BuildVideos(FsNode video, string datavDir, VideoKeyfile? keyfile)
    {
        int count = 0;
        string[] files = Directory.GetFiles(datavDir);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (string path in files)
        {
            string name = Path.GetFileName(path);
            if (VideoKeyfile.IsKeyfileName(name))
                continue;

            var fi = new FileInfo(path);
            bool restorable = keyfile?.HeaderFor(name) is not null;
            Attach(video, new FsNode
            {
                NodeType = FsNodeType.File,
                Kind = restorable ? EntryKind.Video : EntryKind.RawFile,
                Name = name,
                FriendlyName = $"{name}  {FormatSize(fi.Length)}{(restorable ? "" : " (no header in keyfile)")}",
                Size = fi.Length,
                ArchivePath = path,
            });
            if (restorable)
                count++;
        }
        return count;
    }

    private static FsNode Folder(FsNode parent, string name) =>
        Attach(parent, new FsNode { NodeType = FsNodeType.Directory, Name = name });

    private static FsNode Attach(FsNode parent, FsNode child)
    {
        child.Parent = parent;
        parent.Children.Add(child);
        return child;
    }

    private static string? FindDir(string root, string name)
    {
        if (!Directory.Exists(root))
            return null;
        return Directory.EnumerateDirectories(root)
            .FirstOrDefault(d => Path.GetFileName(d).Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------------------------------------
    // Navigation
    // ---------------------------------------------------------------------------------------------

    public IEnumerable<FsNode> GetDirectories(FsNode node) => node.Children.Where(c => c.IsDirectory);

    public IEnumerable<FsNode> GetFiles(FsNode node) =>
        node.Children
            .Where(c => c.IsFile)
            .OrderBy(c => GetTypeOrder(c.Kind))
            .ThenBy(c => c.EntryIndex >= 0 ? c.EntryIndex : int.MaxValue)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase);

    internal static int GetTypeOrder(EntryKind kind) => kind switch
    {
        EntryKind.Background => 1,
        EntryKind.Mask => 2,
        EntryKind.Overlay => 3,
        EntryKind.Animation => 4,
        EntryKind.Music => 5,
        EntryKind.Ambient => 6,
        EntryKind.Cinematic => 7,
        EntryKind.Voice => 8,
        EntryKind.Video => 9,
        EntryKind.Viseme => 10,
        EntryKind.Data => 99,
        EntryKind.GlobalData => 100,
        EntryKind.RawFile => 101,
        _ => 50,
    };

    /// <summary>Every file node under <paramref name="node"/>, depth-first in tree order.</summary>
    public IEnumerable<FsNode> EnumerateFiles(FsNode node)
    {
        foreach (FsNode child in node.Children)
        {
            if (child.IsFile)
                yield return child;
            if (child.IsDirectory)
            {
                foreach (FsNode desc in EnumerateFiles(child))
                    yield return desc;
            }
        }
    }

    /// <summary>Resolves a <c>\</c>-delimited path produced by <see cref="FsNode.GetPath"/>. Case-insensitive.</summary>
    public FsNode? FindNode(FsNode root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (string.IsNullOrEmpty(path))
            return null;

        FsNode current = root;
        foreach (string segment in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            FsNode? next = current.Children.FirstOrDefault(c => c.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (next is null)
                return null;
            current = next;
        }
        return current;
    }

    // ---------------------------------------------------------------------------------------------
    // Bytes
    // ---------------------------------------------------------------------------------------------

    /// <summary>The node's bytes exactly as stored: the archive window for an entry, the whole file for a loose file.</summary>
    public Stream OpenFile(FsNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!node.IsFile || string.IsNullOrEmpty(node.ArchivePath))
            throw new InvalidOperationException($"'{node.GetPath()}' has no bytes to open.");

        var stream = new FileStream(node.ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        if ((node.NodeType & FsNodeType.InArchive) == 0)
            return stream;
        return new ArchiveWindowStream(stream, node.Offset, node.Size);
    }

    public byte[] ReadBytes(FsNode node)
    {
        using Stream s = OpenFile(node);
        var buffer = new byte[s.Length];
        s.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>
    /// The scene background (entry 0 of the owning archive) for any node inside a scene archive, decoded
    /// once and cached. <see langword="null"/> when the node is not in a scene archive or entry 0 is not a
    /// raster.
    /// </summary>
    public DecodedImage? SceneBackgroundFor(FsNode node)
    {
        FsNode? archive = node.IsDirectory && node.Parent?.Name == ScenesFolder ? node : node.Parent;
        if (archive is null || archive.Parent?.Name != ScenesFolder || archive.ArchivePath is null)
            return null;

        lock (_backgroundGate)
        {
            if (_backgroundCache.TryGetValue(archive.ArchivePath, out DecodedImage? cached))
                return cached;
        }

        FsNode? entry0 = archive.Children.FirstOrDefault(c => c.Kind == EntryKind.Background && c.EntryIndex == 0)
                         ?? archive.Children.FirstOrDefault(c => c.Kind == EntryKind.Background);
        DecodedImage? image = null;
        if (entry0?.Image is { } info)
        {
            byte[] bytes = ReadBytes(entry0);
            image = RasterDecoder.Decode(bytes, info.Width, info.Height);
        }

        lock (_backgroundGate)
            _backgroundCache[archive.ArchivePath] = image;
        return image;
    }

    /// <summary>
    /// The scene archive's 1536-byte attribute table (Entry 2) for any node inside a scene archive, read once
    /// and cached. <see langword="null"/> when not in a scene archive or no 1536-byte entry exists.
    /// </summary>
    public byte[]? SceneAttributeTableFor(FsNode node)
    {
        FsNode? archive = node.IsDirectory && node.Parent?.Name == ScenesFolder ? node : node.Parent;
        if (archive is null || archive.Parent?.Name != ScenesFolder || archive.ArchivePath is null)
            return null;

        lock (_attributeTableGate)
        {
            if (_attributeTableCache.TryGetValue(archive.ArchivePath, out byte[]? cached))
                return cached;
        }

        FsNode? tableEntry = archive.Children.FirstOrDefault(c => c.Size == 1536);
        byte[]? tableBytes = null;
        if (tableEntry is not null)
        {
            try
            {
                tableBytes = ReadBytes(tableEntry);
            }
            catch
            {
                // Ignore missing/corrupt table and return null
            }
        }

        lock (_attributeTableGate)
            _attributeTableCache[archive.ArchivePath] = tableBytes;
        return tableBytes;
    }

    /// <summary>Writes the restored Bink stream for a <see cref="EntryKind.Video"/> node.</summary>
    public bool RestoreVideo(FsNode node, Stream output)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Kind != EntryKind.Video || Keyfile is null || node.ArchivePath is null)
            return false;
        return Keyfile.Restore(node.ArchivePath, output);
    }

    // ---------------------------------------------------------------------------------------------
    // Formatting helpers shared by the labels
    // ---------------------------------------------------------------------------------------------

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024)
            return $"{kb:0.#} KB";
        double mb = kb / 1024.0;
        if (mb < 1024)
            return $"{mb:0.##} MB";
        return $"{mb / 1024.0:0.##} GB";
    }

    public static string FormatDuration(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds))
            seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        return $"{t.Minutes}:{t.Seconds:00}";
    }
}
