using RunawayExplorer.Core.FileSystem;

namespace RunawayExplorer.Services;

/// <summary>
/// A selectable "show only this kind of entry" category for the tree's type filter. Runaway's entries
/// have no extensions, so the filter works on <see cref="FsNode.Kind"/>. <see cref="Kinds"/> is
/// <see langword="null"/> for the "All Types" entry.
/// </summary>
public sealed record ResourceTypeFilter(string Label, EntryKind[]? Kinds, string? IconResourceKey = null)
{
    public static readonly ResourceTypeFilter All = new("All Types", null, "FolderClosedTypeIcon");
    public static readonly ResourceTypeFilter AllEs = new("Todos los tipos", null, "FolderClosedTypeIcon");

    public static readonly IReadOnlyList<ResourceTypeFilter> Categories =
    [
        All,
        new("Backgrounds", [EntryKind.Background], "ImageTypeIcon"),
        new("Scene masks", [EntryKind.Mask], "ImageTypeIcon"),
        new("Overlays", [EntryKind.Overlay], "ImageTypeIcon"),
        new("Animations", [EntryKind.Animation], "AnimationTypeIcon"),
        new("All images", [EntryKind.Background, EntryKind.Mask, EntryKind.Overlay, EntryKind.Animation], "ImageTypeIcon"),
        new("Music", [EntryKind.Music], "SoundTypeIcon"),
        new("Ambient & SFX", [EntryKind.Ambient], "SoundTypeIcon"),
        new("Cinematic audio", [EntryKind.Cinematic], "SoundTypeIcon"),
        new("Voice", [EntryKind.Voice], "SoundTypeIcon"),
        new("Video", [EntryKind.Video], "VideoTypeIcon"),
        new("Lip-sync", [EntryKind.Viseme], "DataTypeIcon"),
        new("Dialogue", [EntryKind.Dialogue], "DataTypeIcon"),
        new("Data (undecoded)", [EntryKind.Data, EntryKind.GlobalData, EntryKind.RawFile], "RawFileTypeIcon"),
    ];

    public static IReadOnlyList<ResourceTypeFilter> GetCategories(string? language = null)
    {
        bool es = RunawayExplorer.Core.Metadata.SceneCatalog.IsSpanish(language);
        if (!es)
            return Categories;

        return
        [
            AllEs,
            new("Fondos", [EntryKind.Background], "ImageTypeIcon"),
            new("Máscaras de escena", [EntryKind.Mask], "ImageTypeIcon"),
            new("Capas", [EntryKind.Overlay], "ImageTypeIcon"),
            new("Animaciones", [EntryKind.Animation], "AnimationTypeIcon"),
            new("Todas las imágenes", [EntryKind.Background, EntryKind.Mask, EntryKind.Overlay, EntryKind.Animation], "ImageTypeIcon"),
            new("Música", [EntryKind.Music], "SoundTypeIcon"),
            new("Sonido ambiente y efectos", [EntryKind.Ambient], "SoundTypeIcon"),
            new("Audio de cinemáticas", [EntryKind.Cinematic], "SoundTypeIcon"),
            new("Voces", [EntryKind.Voice], "SoundTypeIcon"),
            new("Vídeos", [EntryKind.Video], "VideoTypeIcon"),
            new("Sincronización labial", [EntryKind.Viseme], "DataTypeIcon"),
            new("Diálogos", [EntryKind.Dialogue], "DataTypeIcon"),
            new("Datos (sin decodificar)", [EntryKind.Data, EntryKind.GlobalData, EntryKind.RawFile], "RawFileTypeIcon"),
        ];
    }

    public bool Matches(FsNode node)
    {
        if (Kinds is null)
            return true;
        return Array.IndexOf(Kinds, node.Kind) >= 0;
    }

    public override string ToString() => Label;
}
