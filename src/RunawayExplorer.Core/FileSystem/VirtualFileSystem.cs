using System.Buffers.Binary;
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
    public const string DialogueFolder = "Dialogue";
    public const string GlobalFolder = "Global Data";

    public const int VoiceGroupSize = 500;
    public const int DialogueGroupSize = 500;

    public static readonly AudioInfo MusicPcm = new() { SampleRate = 16_000, Channels = 2, BitsPerSample = 16 };
    public static readonly AudioInfo AmbientPcm = new() { SampleRate = 22_050, Channels = 2, BitsPerSample = 16 };
    public static readonly AudioInfo CinematicPcm = new() { SampleRate = 22_050, Channels = 2, BitsPerSample = 16 };

    /// <summary>The files carry no rate; 16,000 Hz is what the lines sound right at (22,050 makes them rushed and high).</summary>
    public static readonly AudioInfo VoicePcm = new() { SampleRate = 16_000, Channels = 1, BitsPerSample = 8 };

    /// <summary>
    /// Hollywood Monsters' music and cinematic tracks (<c>RESOURCE.M*</c>, <c>RESOURCE.002</c>): 16-bit signed
    /// mono at 11,025 Hz. The autocorrelation of the samples decays smoothly from lag 1 -- which it would not
    /// do if the stream were interleaved stereo. No rate is stored, so the rate was settled by listening:
    /// 22,050 plays the tracks at double tempo.
    /// </summary>
    public static readonly AudioInfo HollywoodMonstersMusicPcm = new() { SampleRate = 11_025, Channels = 1, BitsPerSample = 16 };

    /// <summary>
    /// Hollywood Monsters' sound effects (<c>RESOURCE.S*</c>, <c>RESOURCE.001</c>): 8-bit unsigned mono at
    /// 11,025 Hz. One slot of <c>RESOURCE.S01</c> was shipped as a whole RIFF/WAVE file instead of a bare
    /// stream, and its <c>fmt </c> chunk states exactly this format.
    /// </summary>
    public static readonly AudioInfo HollywoodMonstersSoundPcm = new() { SampleRate = 11_025, Channels = 1, BitsPerSample = 8 };

    /// <summary>First <c>RESOURCE.000</c> slot holding a resident sound effect in <em>Hollywood Monsters</em>.</summary>
    public const int ResidentSoundFirstEntry = 0x55;

    /// <summary>Last such slot; these 14 run to the end of the archive.</summary>
    public const int ResidentSoundLastEntry = 0x62;

    /// <summary>
    /// Hollywood Monsters' voice bank (<c>RESOURCE.004</c>): 8-bit unsigned mono at 22,050 Hz -- twice the
    /// sound-effect rate, so it needs its own entry. Settled by listening; this is the Spanish release.
    /// </summary>
    public static readonly AudioInfo HollywoodMonstersVoicePcm = new() { SampleRate = 22_050, Channels = 1, BitsPerSample = 8 };

    private readonly Dictionary<string, DecodedImage?> _backgroundCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _backgroundGate = new();
    private readonly Dictionary<string, byte[]?> _attributeTableCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _attributeTableGate = new();
    private readonly Dictionary<string, List<(int Index, IndexedPalette Palette)>> _archivePalettes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _paletteGate = new();
    private Dictionary<int, IndexedPalette>? _sharedPalettes;

    public VirtualFileSystem(string baseDir, FsNode root, VideoKeyfile? keyfile = null, GameVersion gameVersion = GameVersion.Runaway1)
    {
        BaseDir = baseDir;
        Root = root;
        Keyfile = keyfile;
        GameVersion = gameVersion;
    }

    public string BaseDir { get; }

    public FsNode Root { get; }

    /// <summary>The detected or configured game release.</summary>
    public GameVersion GameVersion { get; }

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
        public int Phrases { get; set; }
        public bool FromCache { get; set; }

        public override string ToString() =>
            $"{SceneArchives} scenes: {Backgrounds} backgrounds, {Masks} masks, {Overlays} overlays, {Animations} animations; " +
            $"{AudioClips} audio clips, {VoiceClips} voice lines, {Videos} videos, {Visemes} lip-sync tracks, {Phrases} dialogue phrases";
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

        GameVersion gameVersion = GameDetector.Detect(baseDir);

        string? resourceDir = FindDir(baseDir, "Resource");
        if (resourceDir is null && gameVersion == GameVersion.HollywoodMonsters)
        {
            // Hollywood Monsters has no Resource folder: the archives sit next to Monsters.exe, either in a
            // Monsters subfolder of the CD/install root or directly in the folder the user picked.
            resourceDir = FindDir(baseDir, "Monsters") ?? baseDir;
        }
        if (resourceDir is null)
            throw new DirectoryNotFoundException($"No 'Resource' folder under {baseDir}. Pick the game's install folder (the one containing Runaway.exe or RunawayTDOTT.exe).");
        cache ??= ScanCache.Ephemeral();
        string activeLanguage = language ?? SceneCatalog.DefaultLanguage;

        var root = new FsNode { NodeType = FsNodeType.Root | FsNodeType.Directory, Name = Path.GetFileName(baseDir.TrimEnd(Path.DirectorySeparatorChar)) };
        var summary = new ScanSummary();

        string? dataaDir = FindDir(baseDir, "Dataa");
        string? datavDir = FindDir(baseDir, "Datav");

        VideoKeyfile? keyfile = null;
        if (datavDir is not null && gameVersion is GameVersion.Runaway1 or GameVersion.Runaway2)
        {
            string? keyPath = Directory.EnumerateFiles(datavDir).FirstOrDefault(f => VideoKeyfile.IsKeyfileName(Path.GetFileName(f)));
            if (keyPath is not null)
            {
                try { keyfile = VideoKeyfile.Load(keyPath); }
                catch (IOException) { keyfile = null; }
            }
        }

        var vfs = new VirtualFileSystem(baseDir, root, keyfile, gameVersion)
        {
            Language = activeLanguage
        };

        string[] resourceFiles = gameVersion == GameVersion.HollywoodMonsters
            ? Directory.GetFiles(resourceDir, "RESOURCE.*", SearchOption.TopDirectoryOnly)
            : Directory.GetFiles(resourceDir, "*", SearchOption.AllDirectories);
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
        FsNode dialogue = Folder(root, DialogueFolder);
        dialogue.FriendlyName = SceneCatalog.GetCategoryTitle(DialogueFolder, activeLanguage);
        FsNode global = Folder(root, GlobalFolder);
        global.FriendlyName = SceneCatalog.GetCategoryTitle(GlobalFolder, activeLanguage);

        // Scene archives are the expensive part; classify them in parallel, then attach in name order.
        List<string> sceneArchives = resourceFiles.Where(f => SceneArchive.IsSceneArchiveName(Path.GetFileName(f), gameVersion)).ToList();
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
            (FsNode node, bool fromCache) = BuildSceneArchive(path, cache, activeLanguage, gameVersion, cancellationToken);
            sceneNodes[i] = node;
            if (fromCache)
                anyFromCache = true;
        });

        foreach (FsNode? node in sceneNodes)
        {
            if (node is null)
                continue;
            if (node.Children.Count == 0 && gameVersion == GameVersion.HollywoodMonsters)
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

        Dictionary<int, string>? transcripts = null;
        foreach (string path in resourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(path);
            string upper = name.ToUpperInvariant();

            if (SceneArchive.IsSceneArchiveName(name, gameVersion))
                continue;

            bool hollywood = gameVersion == GameVersion.HollywoodMonsters;

            if (hollywood && upper == "RESOURCE.004")
            {
                // Not lip-sync here: RESOURCE.004 is the 265 MB voice bank, one clip per slot.
                progress?.Invoke($"Reading {name}");
                summary.VoiceClips += Attach(voice, BuildAudioArchive(path, EntryKind.Voice, HollywoodMonstersVoicePcm, dedupe: false, activeLanguage, gameVersion)).Children.Count;
            }
            else if (hollywood && upper is "RESOURCE.001" or "RESOURCE.002" or "RESOURCE.003")
            {
                if (upper == "RESOURCE.001")
                {
                    progress?.Invoke($"Reading {name}");
                    summary.AudioClips += Attach(ambient, BuildAudioArchive(path, EntryKind.Ambient, HollywoodMonstersSoundPcm, dedupe: false, activeLanguage, gameVersion)).Children.Count;
                }
                else if (upper == "RESOURCE.002")
                {
                    progress?.Invoke($"Reading {name}");
                    summary.AudioClips += Attach(cinematic, BuildAudioArchive(path, EntryKind.Cinematic, HollywoodMonstersMusicPcm, dedupe: true, activeLanguage, gameVersion)).Children.Count;
                }
                else
                {
                    // RESOURCE.003 is the script: obfuscated rows indexed by scene, not the Runaway 2
                    // phrase table (see docs/formats/global-data.md).
                    progress?.Invoke($"Reading {name}");
                    FsNode script = Attach(dialogue, BuildHollywoodScriptArchive(path));
                    summary.Phrases = script.Children.Sum(stage => stage.Children.Count);
                }
            }
            else if (upper.StartsWith("RESOURCE.M", StringComparison.Ordinal))
            {
                progress?.Invoke($"Reading {name}");
                AudioInfo musicPcm = hollywood ? HollywoodMonstersMusicPcm : MusicPcm;
                summary.AudioClips += Attach(music, BuildAudioArchive(path, EntryKind.Music, musicPcm, dedupe: false, activeLanguage, gameVersion)).Children.Count;
            }
            else if (upper.StartsWith("RESOURCE.S", StringComparison.Ordinal))
            {
                progress?.Invoke($"Reading {name}");
                AudioInfo ambientPcm = hollywood ? HollywoodMonstersSoundPcm : AmbientPcm;
                summary.AudioClips += Attach(ambient, BuildAudioArchive(path, EntryKind.Ambient, ambientPcm, dedupe: false, activeLanguage, gameVersion)).Children.Count;
            }
            else if (upper == "RESOURCE.002" && gameVersion is GameVersion.Runaway1 or GameVersion.Runaway2)
            {
                progress?.Invoke($"Reading {name}");
                summary.AudioClips += Attach(cinematic, BuildAudioArchive(path, EntryKind.Cinematic, CinematicPcm, dedupe: true, activeLanguage, gameVersion)).Children.Count;
            }
            else if (upper == "RESOURCE.004")
            {
                progress?.Invoke($"Reading {name}");
                summary.Visemes += Attach(lipSync, BuildVisemeArchive(path)).Children.Count;
            }
            else if (upper == "RESOURCE.003" && gameVersion != GameVersion.Runaway1)
            {
                progress?.Invoke($"Reading {name}");
                var (dNode, dTranscripts) = BuildPhraseArchive(path, gameVersion);
                Attach(dialogue, dNode);
                summary.Phrases = dTranscripts.Count;
                transcripts = dTranscripts;
            }
            else if (upper == "RESOURCE.000")
            {
                progress?.Invoke($"Reading {name}");
                Attach(global, BuildGlobalArchive(path, gameVersion));
            }
            else
            {
                Attach(global, LooseFile(path));
            }
        }

        if (dataaDir is not null)
        {
            progress?.Invoke("Reading voice shards");
            summary.VoiceClips = BuildVoice(voice, dataaDir, gameVersion, transcripts);
        }

        if (datavDir is not null)
        {
            progress?.Invoke("Reading videos");
            summary.Videos = BuildVideos(video, datavDir, keyfile, gameVersion);
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
                node.FriendlyName = SceneCatalog.GetSceneTitle(node.Name, language, GameVersion);
            else if (node.Parent.Name is MusicFolder or AmbientFolder or CinematicFolder)
                node.FriendlyName = SceneCatalog.GetAudioTitle(node.Name, language, GameVersion);
        }
        else if (node.IsFile && node.Parent?.Parent?.Name == ScenesFolder)
        {
            node.FriendlyName = SceneCatalog.FormatSceneEntryLabel(node, language);
        }

        foreach (FsNode child in node.Children)
            UpdateLanguageRecursive(child, language);
    }

    internal static (FsNode Node, bool FromCache) BuildSceneArchive(string path, ScanCache cache, string language, GameVersion gameVersion, CancellationToken cancellationToken = default)
    {
        var node = new FsNode
        {
            NodeType = FsNodeType.Directory,
            Kind = EntryKind.Folder,
            Name = Path.GetFileName(path),
            FriendlyName = SceneCatalog.GetSceneTitle(Path.GetFileName(path), language, gameVersion),
            ArchivePath = path,
            Size = new FileInfo(path).Length,
        };

        List<ScanCache.CachedEntry>? cached = cache.TryGet(path);
        bool fromCache = cached is not null;
        if (cached is null)
        {
            cached = [];
            using FileStream f = File.OpenRead(path);
            List<ArchiveEntry> rawEntries = SceneArchive.ReadEntries(f);
            if (rawEntries.Count == 0 && gameVersion == GameVersion.HollywoodMonsters)
                rawEntries = SceneArchive.ReadHeaderlessScreenEntries(f.Length);
            var pairedWithPrev = new HashSet<int>();
            Span<byte> head = stackalloc byte[16];

            int? sceneWidth = null;
            int? sceneHeight = null;

            for (int i = 0; i < rawEntries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ArchiveEntry entry = rawEntries[i];

                if (pairedWithPrev.Contains(i))
                {
                    cached.Add(ScanCache.CachedEntry.From(entry, EntryClassification.DataEntry));
                    continue;
                }

                bool isR2SpritePair = false;
                if (gameVersion == GameVersion.Runaway2 && i + 1 < rawEntries.Count && entry.Size >= 16)
                {
                    ArchiveEntry nextEntry = rawEntries[i + 1];
                    if (entry.Offset + entry.Size == nextEntry.Offset)
                    {
                        f.Position = entry.Offset;
                        if (f.Read(head) == 16)
                        {
                            ushort fc = BinaryPrimitives.ReadUInt16LittleEndian(head);
                            uint fo0 = BinaryPrimitives.ReadUInt32LittleEndian(head.Slice(2));
                            if (fc > 0 && fc <= 2000 && fo0 == 0 && entry.Size == 2 + fc * 14)
                            {
                                long totalSize = entry.Size + nextEntry.Size;
                                if (totalSize <= 50_000_000)
                                {
                                    var combined = new byte[totalSize];
                                    f.Position = entry.Offset;
                                    f.ReadExactly(combined);
                                    if (SpriteAsset.Parse(combined) is { } sprite)
                                    {
                                        isR2SpritePair = true;
                                        pairedWithPrev.Add(i + 1);
                                        var combinedEntry = new ArchiveEntry(entry.Index, entry.Offset, totalSize);
                                        var classification = new EntryClassification(EntryKind.Animation, new ImageInfo
                                        {
                                            X = sprite.Bounds.X,
                                            Y = sprite.Bounds.Y,
                                            Width = sprite.Bounds.Width,
                                            Height = sprite.Bounds.Height,
                                            Frames = sprite.FrameCount,
                                        });
                                        cached.Add(ScanCache.CachedEntry.From(combinedEntry, classification));
                                    }
                                }
                            }
                        }
                    }
                }

                if (!isR2SpritePair)
                {
                    var buffer = new byte[entry.Size];
                    f.Position = entry.Offset;
                    f.ReadExactly(buffer);
                    EntryClassification c;
                    try
                    {
                        c = EntryClassifier.Classify(buffer, sceneWidth, sceneHeight, gameVersion);
                    }
                    catch (Exception)
                    {
                        // A malformed entry must not take the whole archive down; it is simply "data".
                        c = EntryClassification.DataEntry;
                    }
                    if (c.Kind == EntryKind.Background && sceneWidth is null && c.Image is not null)
                    {
                        sceneWidth = c.Image.Width;
                        sceneHeight = c.Image.Height;
                    }
                    cached.Add(ScanCache.CachedEntry.From(entry, c));
                }
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

    private static FsNode BuildAudioArchive(string path, EntryKind kind, AudioInfo pcm, bool dedupe, string language, GameVersion gameVersion)
    {
        var node = new FsNode
        {
            NodeType = FsNodeType.Directory,
            Name = Path.GetFileName(path),
            FriendlyName = SceneCatalog.GetAudioTitle(Path.GetFileName(path), language, gameVersion),
            ArchivePath = path,
            Size = new FileInfo(path).Length,
        };

        List<AudioArchiveEntry> entries;
        using (FileStream f = File.OpenRead(path))
        {
            if (gameVersion == GameVersion.HollywoodMonsters)
            {
                var head = new byte[(int)Math.Min(f.Length, 1 << 16)];
                f.ReadExactly(head);
                entries = AudioArchive.ReadHollywoodMonstersEntries(head, f.Length);
            }
            else
            {
                entries = AudioArchive.ReadAudioEntries(f);
            }
        }

        var seen = new HashSet<long>();
        foreach (AudioArchiveEntry e in entries)
        {
            if (dedupe && !seen.Add(e.Offset))
                continue;

            AudioInfo audioInfo;
            if (e.Format == AudioFormat.Mp3)
            {
                audioInfo = new AudioInfo { Format = AudioFormat.Mp3, SampleRate = 44_100, Channels = 2, BitsPerSample = 16 };
            }
            else if (e.Format == AudioFormat.Wav)
            {
                audioInfo = new AudioInfo { Format = AudioFormat.Wav, SampleRate = 44_100, Channels = 2, BitsPerSample = 16 };
            }
            else
            {
                audioInfo = pcm;
            }

            var child = new FsNode
            {
                NodeType = FsNodeType.File | FsNodeType.InArchive,
                Kind = kind,
                Name = $"t{e.Index:000}",
                Offset = e.Offset,
                Size = e.Size,
                EntryIndex = e.Index,
                ArchivePath = path,
                Audio = audioInfo,
            };
            child.FriendlyName = $"{child.Name}  {FormatDuration(audioInfo.DurationSeconds(e.Size))}";
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

    private static FsNode BuildGlobalArchive(string path, GameVersion gameVersion = GameVersion.Runaway1)
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
            entries = GlobalArchive.ReadEntries(f, gameVersion);

        // Build an index by slot for fast neighbour lookup.
        var byIndex = new Dictionary<int, ArchiveEntry>();
        foreach (ArchiveEntry e in entries)
            byIndex[e.Index] = e;

        // Pre-classify: probe pairs for font glyph tables, and individual entries for cursor atlas.
        var fontBitmapSlots = new HashSet<int>();
        var fontTableSlots = new HashSet<int>();
        var cursorAtlasSlots = new HashSet<int>();

        if (gameVersion == GameVersion.Runaway1)
        {
            using FileStream f = File.OpenRead(path);

            foreach (ArchiveEntry e in entries)
            {
                // Font detection: if the next slot exists and its contents validate as a glyph table
                // whose records tile this entry exactly, mark both slots.
                if (byIndex.TryGetValue(e.Index + 1, out ArchiveEntry next))
                {
                    if (next.Size >= FontDecoder.RecordSize && next.Size <= 10_000 && next.Size % FontDecoder.RecordSize == 0
                        && e.Size > 0 && e.Size <= 200_000)
                    {
                        var tableData = new byte[next.Size];
                        f.Position = next.Offset;
                        f.ReadExactly(tableData);
                        if (FontDecoder.IsGlyphTable(tableData, e.Size))
                        {
                            fontBitmapSlots.Add(e.Index);
                            fontTableSlots.Add(next.Index);
                        }
                    }
                }

                // Cursor atlas detection: 904×540 RGB565 with key colour present.
                if (e.Size == CursorAtlasDecoder.AtlasWidth * CursorAtlasDecoder.AtlasHeight * 2)
                {
                    var head = new byte[Math.Min((int)e.Size, CursorAtlasDecoder.AtlasWidth * 20 * 2)];
                    f.Position = e.Offset;
                    f.ReadExactly(head);
                    if (CursorAtlasDecoder.LooksLikeAtlas(head))
                        cursorAtlasSlots.Add(e.Index);
                }
            }
        }

        foreach (ArchiveEntry e in entries)
        {
            // Hollywood Monsters keeps its resident sound effects -- the ones that must be audible in every
            // scene -- in the tail of RESOURCE.000 rather than in a sound bank, at the same 8-bit 11,025 Hz
            // shape as RESOURCE.S<nn>. Several slots are aliases of one another, byte for byte.
            bool residentSound = gameVersion == GameVersion.HollywoodMonsters
                && e.Index >= ResidentSoundFirstEntry && e.Index <= ResidentSoundLastEntry;

            EntryKind kind;
            ImageInfo? image = null;
            string label;

            if (residentSound)
            {
                kind = EntryKind.Ambient;
                label = $"e{e.Index:000}  {FormatSize(e.Size)}";
            }
            else if (fontBitmapSlots.Contains(e.Index))
            {
                kind = EntryKind.Font;
                label = $"e{e.Index:000}  font bitmap, {FormatSize(e.Size)}";
            }
            else if (fontTableSlots.Contains(e.Index))
            {
                int glyphs = (int)(e.Size / FontDecoder.RecordSize);
                kind = EntryKind.Font;
                label = $"e{e.Index:000}  glyph table, {glyphs} glyphs";
            }
            else if (cursorAtlasSlots.Contains(e.Index))
            {
                kind = EntryKind.Background;
                image = new ImageInfo
                {
                    Width = CursorAtlasDecoder.AtlasWidth,
                    Height = CursorAtlasDecoder.AtlasHeight,
                };
                label = $"e{e.Index:000}  cursor atlas {CursorAtlasDecoder.AtlasWidth}×{CursorAtlasDecoder.AtlasHeight}";
            }
            else if (gameVersion == GameVersion.Runaway1 && TryGlobalRaster(e.Size, out int gw, out int gh))
            {
                kind = EntryKind.Background;
                image = new ImageInfo { Width = gw, Height = gh };
                label = $"e{e.Index:000}  {GlobalRasterName(e.Index, gw)} {gw}×{gh}";
            }
            else
            {
                kind = EntryKind.GlobalData;
                label = $"e{e.Index:000}  {FormatSize(e.Size)}";
            }

            Attach(node, new FsNode
            {
                NodeType = FsNodeType.File | FsNodeType.InArchive,
                Kind = kind,
                Name = $"e{e.Index:000}",
                FriendlyName = label,
                Offset = e.Offset,
                Size = e.Size,
                EntryIndex = e.Index,
                ArchivePath = path,
                Image = image,
                Audio = residentSound ? HollywoodMonstersSoundPcm : null,
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

    /// <summary>
    /// Builds the <em>Hollywood Monsters</em> script tree from <c>RESOURCE.003</c>: one folder per scene,
    /// holding that scene's hotspot labels and its spoken lines. A line that the scene's cues pair with a
    /// recording carries the voice-bank slot, so the viewer can point at the clip.
    /// </summary>
    private static FsNode BuildHollywoodScriptArchive(string path)
    {
        var node = new FsNode
        {
            NodeType = FsNodeType.Directory,
            Name = Path.GetFileName(path),
            ArchivePath = path,
            Size = new FileInfo(path).Length,
        };

        List<HollywoodScript.Stage> stages;
        using (FileStream f = File.OpenRead(path))
            stages = HollywoodScript.ReadStages(f);

        foreach (HollywoodScript.Stage stage in stages)
        {
            var group = new FsNode
            {
                NodeType = FsNodeType.Directory,
                Name = $"scene {stage.SceneNumber}",
                FriendlyName = $"scene {stage.SceneNumber}  ({stage.Lines.Count} lines, {stage.Labels.Count} labels)",
                ArchivePath = path,
                EntryIndex = stage.Index,
            };
            Attach(node, group);

            foreach (HollywoodScript.Label label in stage.Labels)
            {
                Attach(group, new FsNode
                {
                    NodeType = FsNodeType.File | FsNodeType.InArchive,
                    Kind = EntryKind.Dialogue,
                    Name = $"label {label.Id:000}",
                    FriendlyName = $"label {label.Id:000}  \"{Preview(label.Text)}\"",
                    Size = HollywoodScript.SmallRowSize,
                    EntryIndex = label.Id,
                    ArchivePath = path,
                    Subtitle = label.Text,
                });
            }

            foreach (HollywoodScript.Line line in stage.Lines)
            {
                Attach(group, new FsNode
                {
                    NodeType = FsNodeType.File | FsNodeType.InArchive,
                    Kind = EntryKind.Dialogue,
                    Name = $"line {line.Id:0000}",
                    FriendlyName = $"line {line.Id:0000}  \"{Preview(line.Text)}\"",
                    Size = HollywoodScript.LargeRowSize,
                    EntryIndex = line.Id,
                    ArchivePath = path,
                    Subtitle = line.Text,
                    VoiceClipIndex = line.VoiceClipId,
                });
            }
        }

        return node;
    }

    private static string Preview(string text) =>
        text.Length > 40 ? text[..37] + "..." : text;

    private static (FsNode Node, Dictionary<int, string> Transcripts) BuildPhraseArchive(string path, GameVersion gameVersion)
    {
        var node = new FsNode
        {
            NodeType = FsNodeType.Directory,
            Name = Path.GetFileName(path),
            ArchivePath = path,
            Size = new FileInfo(path).Length,
        };

        var transcripts = new Dictionary<int, string>();
        List<PhraseArchive.Phrase> phrases;
        using (FileStream f = File.OpenRead(path))
            phrases = PhraseArchive.ReadPhrases(f);

        var groups = new Dictionary<int, FsNode>();
        foreach (PhraseArchive.Phrase phrase in phrases)
        {
            if (!string.IsNullOrEmpty(phrase.Text))
                transcripts[phrase.Index] = phrase.Text;

            int g = phrase.Index / DialogueGroupSize;
            if (!groups.TryGetValue(g, out FsNode? group))
            {
                int lo = g * DialogueGroupSize;
                group = new FsNode
                {
                    NodeType = FsNodeType.Directory,
                    Name = $"{lo:00000}-{lo + DialogueGroupSize - 1:00000}",
                    ArchivePath = path,
                };
                groups[g] = group;
                Attach(node, group);
            }

            long recOffset = 4 + (long)phrases.Count * 4 + (long)phrase.Index * PhraseArchive.RecordSize;
            string preview = phrase.Text.Length > 40 ? phrase.Text[..37] + "..." : phrase.Text;
            var phraseNode = new FsNode
            {
                NodeType = FsNodeType.File | FsNodeType.InArchive,
                Kind = EntryKind.Dialogue,
                Name = $"d{phrase.Index:00000}",
                FriendlyName = string.IsNullOrEmpty(phrase.Text)
                    ? $"d{phrase.Index:00000} (ID {phrase.Id})"
                    : $"d{phrase.Index:00000} (ID {phrase.Id})  \"{preview}\"",
                Offset = recOffset,
                Size = PhraseArchive.RecordSize,
                EntryIndex = phrase.Index,
                ArchivePath = path,
                Subtitle = phrase.Text,
            };
            Attach(group, phraseNode);
        }

        return (node, transcripts);
    }

    private static int BuildVoice(FsNode voice, string dataaDir, GameVersion gameVersion, Dictionary<int, string>? transcripts = null)
    {
        if (gameVersion is GameVersion.TheNextBigThing or GameVersion.Yesterday)
        {
            var dataaFiles = Directory.EnumerateFiles(dataaDir, "DATAA*.000", SearchOption.AllDirectories).OrderBy(f => f).ToList();
            if (dataaFiles.Count == 0)
                return 0;

            var clips = new SortedDictionary<int, VoiceArchive.VoiceClip>();
            foreach (string file in dataaFiles)
            {
                var fileClips = VoiceArchive.ReadNamedArchiveClips(file);
                foreach (var kvp in fileClips)
                {
                    if (!clips.ContainsKey(kvp.Key))
                        clips[kvp.Key] = kvp.Value;
                }
            }

            if (clips.Count == 0)
                return 0;

            var groups = new Dictionary<int, FsNode>();
            var voiceMp3 = new AudioInfo { Format = AudioFormat.Mp3, SampleRate = 44_100, Channels = 1, BitsPerSample = 16 };

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
                    Audio = voiceMp3,
                };
                if (transcripts is not null && transcripts.TryGetValue(clip.Index, out string? sub) && !string.IsNullOrEmpty(sub))
                {
                    node.Subtitle = sub;
                    string preview = sub.Length > 35 ? sub[..32] + "..." : sub;
                    node.FriendlyName = $"{node.Name}  \"{preview}\"";
                }
                else
                {
                    node.FriendlyName = $"{node.Name}  {FormatDuration(VoicePcm.DurationSeconds(clip.Size))}";
                }
                Attach(group, node);
            }
            return clips.Count;
        }

        if (gameVersion is GameVersion.Runaway2 or GameVersion.Runaway3)
        {
            string? dataaa = Directory.EnumerateFiles(dataaDir)
                .FirstOrDefault(f => Path.GetFileName(f).Equals("Dataaa.000", StringComparison.OrdinalIgnoreCase));
            if (dataaa is null)
                return 0;

            SortedDictionary<int, VoiceArchive.VoiceClip> clips = VoiceArchive.ReadSingleArchiveClips(dataaa);
            var groups = new Dictionary<int, FsNode>();
            var voiceMp3 = new AudioInfo { Format = AudioFormat.Mp3, SampleRate = 44_100, Channels = 1, BitsPerSample = 16 };

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
                    Audio = voiceMp3,
                };
                if (transcripts is not null && transcripts.TryGetValue(clip.Index, out string? sub) && !string.IsNullOrEmpty(sub))
                {
                    node.Subtitle = sub;
                    string preview = sub.Length > 35 ? sub[..32] + "..." : sub;
                    node.FriendlyName = $"{node.Name}  \"{preview}\"";
                }
                else
                {
                    node.FriendlyName = $"{node.Name}  {FormatDuration(VoicePcm.DurationSeconds(clip.Size))}";
                }
                Attach(group, node);
            }
            return clips.Count;
        }

        var r1Shards = VoiceArchive.ShardNames()
            .Select(name => Directory.EnumerateFiles(dataaDir).FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Where(path => path is not null)
            .Cast<string>()
            .ToList();

        if (r1Shards.Count == 0)
            return 0;

        SortedDictionary<int, VoiceArchive.VoiceClip> r1Clips = VoiceArchive.ReadClips(r1Shards);
        var r1Groups = new Dictionary<int, FsNode>();
        foreach (VoiceArchive.VoiceClip clip in r1Clips.Values)
        {
            int g = clip.Index / VoiceGroupSize;
            if (!r1Groups.TryGetValue(g, out FsNode? group))
            {
                int lo = g * VoiceGroupSize;
                group = new FsNode
                {
                    NodeType = FsNodeType.Directory,
                    Name = $"{lo:00000}-{lo + VoiceGroupSize - 1:00000}",
                };
                r1Groups[g] = group;
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
            if (transcripts is not null && transcripts.TryGetValue(clip.Index, out string? sub) && !string.IsNullOrEmpty(sub))
            {
                node.Subtitle = sub;
                string preview = sub.Length > 35 ? sub[..32] + "..." : sub;
                node.FriendlyName = $"{node.Name}  \"{preview}\"";
            }
            else
            {
                node.FriendlyName = $"{node.Name}  {FormatDuration(VoicePcm.DurationSeconds(clip.Size))}";
            }
            Attach(group, node);
        }
        return r1Clips.Count;
    }

    private static int BuildVideos(FsNode video, string datavDir, VideoKeyfile? keyfile, GameVersion gameVersion)
    {
        int count = 0;
        string[] files = Directory.GetFiles(datavDir);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (string path in files)
        {
            string name = Path.GetFileName(path);
            bool isPlainBink = gameVersion is GameVersion.Runaway3 or GameVersion.TheNextBigThing or GameVersion.Yesterday;
            if (!isPlainBink && VideoKeyfile.IsKeyfileName(name))
                continue;

            var fi = new FileInfo(path);
            bool restorable = isPlainBink || keyfile?.HeaderFor(name) is not null;
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
            if (PngDecoder.IsPng(bytes))
                image = PngDecoder.Decode(bytes);
            else if (JpegDecoder.IsJpeg(bytes))
                image = JpegDecoder.Decode(bytes);
            else if (GameVersion == GameVersion.HollywoodMonsters)
                image = RasterDecoder.DecodeIndexed(bytes, info.Width, info.Height, ScenePaletteFor(entry0) ?? IndexedPalette.Grayscale);
            else
                image = RasterDecoder.Decode(bytes, info.Width, info.Height);
        }

        lock (_backgroundGate)
            _backgroundCache[archive.ArchivePath] = image;
        return image;
    }

    /// <summary>
    /// The colour table to decode one node of a Hollywood Monsters scene archive with.
    /// <para>
    /// A palette block applies to the entries that <b>follow</b> it, up to the next block: the table is
    /// loaded as the archive is read, so each block recolours everything after it. An archive usually has
    /// one, at entry 1, but sixteen have more -- either a second cast palette part-way through
    /// (<c>RESOURCE.A03</c> e08) or a whole second scene, background and palette included
    /// (<c>RESOURCE.C07</c> e33/e34). Picking the "best" block for the whole archive instead renders every
    /// entry on the wrong side of the boundary in the wrong colours.
    /// </para>
    /// <para>
    /// Entries before the first block -- entry 0, the background -- take that first block. The chosen block
    /// fills the bottom of the table; when it defines fewer than 256 colours (the usual 176), the rest come
    /// from the matching shared block in <c>RESOURCE.000</c>, which is where the characters' colours live.
    /// <see langword="null"/> for any other game, and for an archive with no palette block.
    /// </para>
    /// </summary>
    public IndexedPalette? ScenePaletteFor(FsNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (GameVersion != GameVersion.HollywoodMonsters)
            return null;

        bool isArchiveNode = node.IsDirectory && node.Parent?.Name == ScenesFolder;
        FsNode? archive = isArchiveNode ? node : node.Parent;
        if (archive is null || archive.Parent?.Name != ScenesFolder || archive.ArchivePath is null)
            return null;

        // An archive node itself (its background) is coloured by the archive's first block.
        int entryIndex = isArchiveNode ? int.MinValue : node.EntryIndex;
        List<(int Index, IndexedPalette Palette)> blocks = ArchivePalettes(archive);
        if (blocks.Count == 0)
            return null;

        // The nearest block at or before the entry; entries ahead of the first block take that first one.
        (int Index, IndexedPalette Palette) chosen = blocks[0];
        foreach ((int index, IndexedPalette palette) in blocks)
        {
            if (index > entryIndex)
                break;
            chosen = (index, palette);
        }
        return chosen.Palette;
    }

    /// <summary>
    /// Every valid palette block of a scene archive, in entry order, each already completed with the shared
    /// tail. Read once per archive: an entry that merely has a palette's size but does not parse as one is
    /// skipped, so it cannot displace the block that actually governs the entries around it.
    /// </summary>
    private List<(int Index, IndexedPalette Palette)> ArchivePalettes(FsNode archive)
    {
        string path = archive.ArchivePath!;
        lock (_paletteGate)
        {
            if (_archivePalettes.TryGetValue(path, out List<(int, IndexedPalette)>? cached))
                return cached;
        }

        var blocks = new List<(int Index, IndexedPalette Palette)>();
        foreach (FsNode entry in archive.Children)
        {
            if (TryReadPalette(entry) is not { } palette)
                continue;
            if (palette.DefinedCount < IndexedPalette.Colors &&
                SharedPalette(IndexedPalette.Colors - palette.DefinedCount) is { } tail)
            {
                palette = palette.WithTail(tail);
            }
            blocks.Add((entry.EntryIndex, palette));
        }
        blocks.Sort((a, b) => a.Index.CompareTo(b.Index));

        lock (_paletteGate)
            _archivePalettes[path] = blocks;
        return blocks;
    }

    /// <summary>The <c>RESOURCE.000</c> block that defines exactly <paramref name="colors"/> colours, if there is one.</summary>
    private IndexedPalette? SharedPalette(int colors)
    {
        lock (_paletteGate)
        {
            if (_sharedPalettes is null)
            {
                _sharedPalettes = [];
                FsNode? globalArchive = Root.Children
                    .FirstOrDefault(c => c.Name == GlobalFolder)?.Children
                    .FirstOrDefault(c => c.Name.Equals("RESOURCE.000", StringComparison.OrdinalIgnoreCase));
                if (globalArchive is not null)
                {
                    foreach (FsNode entry in globalArchive.Children)
                    {
                        if (TryReadPalette(entry) is { } p)
                            _sharedPalettes.TryAdd(p.DefinedCount, p);
                    }
                }
            }
            return _sharedPalettes.GetValueOrDefault(colors);
        }
    }

    private IndexedPalette? TryReadPalette(FsNode entry)
    {
        if (!entry.IsFile ||
            entry.Size < IndexedPalette.MinBlockBytes ||
            entry.Size > IndexedPalette.FullBlockBytes ||
            entry.Size % IndexedPalette.BytesPerColor != 0)
        {
            return null;
        }

        try
        {
            return IndexedPalette.TryParse(ReadBytes(entry));
        }
        catch (IOException)
        {
            return null;
        }
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
        if (node.Kind != EntryKind.Video || node.ArchivePath is null)
            return false;

        if (GameVersion is GameVersion.Runaway3 or GameVersion.TheNextBigThing or GameVersion.Yesterday || Keyfile is null)
        {
            using FileStream src = File.OpenRead(node.ArchivePath);
            src.CopyTo(output);
            return true;
        }

        return Keyfile.Restore(node.ArchivePath, output);
    }

    // ---------------------------------------------------------------------------------------------
    // Formatting helpers shared by the labels
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The geometries the raw RGB565 rasters of Runaway 1's <c>RESOURCE.000</c> come in. Nothing in the
    /// archive records a width, so an entry is recognised by its exact size -- see
    /// <c>docs/formats/global-data.md</c>.
    /// </summary>
    private static readonly (int Width, int Height)[] GlobalRasterGeometries =
    [
        (Rgb565.ScreenWidth, Rgb565.ScreenHeight), // menu panel, inventory screen, item close-ups
        (904, 480),                                // inventory item icon sheets
        (700, 74),                                 // UI sprite strips
    ];

    private static bool TryGlobalRaster(long size, out int width, out int height)
    {
        foreach ((int w, int h) in GlobalRasterGeometries)
        {
            if (size == (long)w * h * 2)
            {
                (width, height) = (w, h);
                return true;
            }
        }

        (width, height) = (0, 0);
        return false;
    }

    /// <summary>What a recognised <c>RESOURCE.000</c> raster is, for the tree label.</summary>
    private static string GlobalRasterName(int index, int width) => index switch
    {
        // Easy to get backwards: 131 looks like a title backdrop, but 132 is the one carrying the volume
        // knob, the brightness lever and the setting holes the menu draws its entries over.
        131 => "inventory screen",
        132 => "menu panel",
        _ when width == 904 => "item icons",
        _ when width == 700 => "UI strip",
        _ => "screen",
    };

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
