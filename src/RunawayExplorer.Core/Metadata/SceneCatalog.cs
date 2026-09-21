using System.Globalization;
using RunawayExplorer.Core.FileSystem;

namespace RunawayExplorer.Core.Metadata;

/// <summary>
/// Human-readable names and official chapter information for Runaway's assets.
/// Provides bilingual support (English and Spanish, the game's original development language).
/// </summary>
public static class SceneCatalog
{
    public const string DefaultLanguage = "en";

    public record ChapterInfo(int Number, string TitleEn, string TitleEs);

    public record SceneInfo(string ArchiveName, int Chapter, string NameEn, string NameEs);

    public static readonly IReadOnlyList<ChapterInfo> Chapters =
    [
        new(1, "Wake Me Before Dying", "Despiértame antes de morir"),
        new(2, "The Mysterious Crucifix", "El extraño crucifijo"),
        new(3, "The Great Escape", "La gran evasión"),
        new(4, "Close Encounters of the Fourth Kind", "Encuentros en la cuarta fase"),
        new(5, "Gifts from the Crypt", "La cripta sagrada"),
        new(6, "The Indian, the Nun and the Finger", "El indio, la monja y el dedo"),
    ];

    private static readonly Dictionary<int, ChapterInfo> ChaptersByNumber =
        Chapters.ToDictionary(c => c.Number);

    private static readonly Dictionary<string, SceneInfo> ScenesByName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Chapter 1: Wake Me Before Dying / Despiértame antes de morir (Hospital)
            ["RESOURCE.A00"] = new("RESOURCE.A00", 1, "Hospital: Gina's Room (Intro)", "Hospital: Habitación de Gina (Inicio)"),
            ["RESOURCE.A01"] = new("RESOURCE.A01", 1, "Hospital: Gina's Room", "Hospital: Habitación de Gina"),
            ["RESOURCE.A02"] = new("RESOURCE.A02", 1, "Hospital: 2nd Floor Corridor", "Hospital: Pasillo de la 2.ª planta"),
            ["RESOURCE.A03"] = new("RESOURCE.A03", 1, "Hospital: Storeroom & Restroom", "Hospital: Almacén y baño"),

            // Chapter 2: The Mysterious Crucifix / El extraño crucifijo (Chicago Museum of Natural History)
            ["RESOURCE.B01"] = new("RESOURCE.B01", 2, "Museum: Restoration Lab", "Museo: Laboratorio de restauración"),
            ["RESOURCE.B02"] = new("RESOURCE.B02", 2, "Museum: Mayan Exhibition Hall", "Museo: Sala de exposiciones mayas"),
            ["RESOURCE.B03"] = new("RESOURCE.B03", 2, "Museum: Main Lobby (Ground Floor)", "Museo: Vestíbulo principal (Planta baja)"),
            ["RESOURCE.B04"] = new("RESOURCE.B04", 2, "Museum: Main Lobby (Upper Floor)", "Museo: Vestíbulo principal (Planta superior)"),
            ["RESOURCE.B05"] = new("RESOURCE.B05", 2, "Museum: Analysis Lab", "Museo: Laboratorio de análisis"),
            ["RESOURCE.B06"] = new("RESOURCE.B06", 2, "Museum: Basement & Boiler Room", "Museo: Sótano y sala de calderas"),
            ["RESOURCE.B07"] = new("RESOURCE.B07", 2, "Museum: Security Office & Storage", "Museo: Oficina de seguridad y almacén"),
            ["RESOURCE.B08"] = new("RESOURCE.B08", 2, "Museum: Clive's Office", "Museo: Despacho de Clive"),

            // Chapter 3: The Great Escape / La gran evasión (Desert & Outskirts)
            ["RESOURCE.D01"] = new("RESOURCE.D01", 3, "Desert: Thugs' Cabin", "Desierto: Cabaña de los matones"),
            ["RESOURCE.D02"] = new("RESOURCE.D02", 3, "Desert: Divas' Bus & Camp", "Desierto: Autobús y campamento de las divas"),
            ["RESOURCE.D04"] = new("RESOURCE.D04", 3, "Desert: Aircraft Graveyard", "Desierto: Cementerio de aviones"),
            ["RESOURCE.D08"] = new("RESOURCE.D08", 3, "Desert: Abandoned Hangar", "Desierto: Hangar abandonado"),
            ["RESOURCE.D09"] = new("RESOURCE.D09", 3, "Desert: Abandoned Train Car", "Desierto: Vagón de tren abandonado"),
            ["RESOURCE.D11"] = new("RESOURCE.D11", 3, "Desert: Oil Well", "Desierto: Pozo de petróleo"),

            // Chapter 4: Close Encounters of the Fourth Kind / Encuentros en la cuarta fase (Douglasville)
            ["RESOURCE.D03"] = new("RESOURCE.D03", 4, "Douglasville: Saloon (Ground Floor)", "Douglasville: Saloon (Planta baja)"),
            ["RESOURCE.D05"] = new("RESOURCE.D05", 4, "Douglasville: Main Street & Bank Exterior", "Douglasville: Calle principal y exterior del banco"),
            ["RESOURCE.D06"] = new("RESOURCE.D06", 4, "Douglasville: Sheriff's Office & Jail", "Douglasville: Oficina del sheriff y cárcel"),
            ["RESOURCE.D07"] = new("RESOURCE.D07", 4, "Douglasville: Mama Dorita's Hotel", "Douglasville: Hotel de Mama Dorita"),
            ["RESOURCE.D10"] = new("RESOURCE.D10", 4, "Douglasville: Abandoned Mine Entrance", "Douglasville: Entrada a la mina abandonada"),
            ["RESOURCE.D12"] = new("RESOURCE.D12", 4, "Douglasville: Saloon (Upper Floor & Studio)", "Douglasville: Saloon (Planta superior y taller)"),
            ["RESOURCE.D13"] = new("RESOURCE.D13", 4, "Douglasville: Mama Dorita's House & Well", "Douglasville: Casa de Mama Dorita y pozo"),
            ["RESOURCE.D14"] = new("RESOURCE.D14", 4, "Desert: Meteor Crater & Joshua's Camp", "Desierto: Cráter del meteorito y campamento de Joshua"),
            ["RESOURCE.D15"] = new("RESOURCE.D15", 4, "Douglasville: Derailed Locomotive", "Douglasville: Locomotora descarrilada"),

            // Chapter 5: Gifts from the Crypt / La cripta sagrada (Hopi Village & Sacred Crypts)
            ["RESOURCE.E01"] = new("RESOURCE.E01", 5, "Hopi Village: Cliff & Mine Exit", "Poblado hopi: Precipicio y salida de la mina"),
            ["RESOURCE.E02"] = new("RESOURCE.E02", 5, "Hopi Village: Cliff Overlook", "Poblado hopi: Mirador del acantilado"),
            ["RESOURCE.E03"] = new("RESOURCE.E03", 5, "Hopi Village: Village Square & Statue", "Poblado hopi: Plaza del poblado y estatua"),
            ["RESOURCE.E04"] = new("RESOURCE.E04", 5, "Hopi Village: Chieftain's House", "Poblado hopi: Casa del jefe"),
            ["RESOURCE.E05"] = new("RESOURCE.E05", 5, "Hopi Village: Workshop & Ancient Hut", "Poblado hopi: Taller y cabaña ancestral"),
            ["Resource.e06"] = new("Resource.e06", 5, "Sacred Crypts: Crypt Entrance & Monolith", "Criptas sagradas: Entrada a la cripta y monolito"),
            ["RESOURCE.E07"] = new("RESOURCE.E07", 5, "Sacred Crypts: Sanctuary", "Criptas sagradas: Santuario"),
            ["RESOURCE.E08"] = new("RESOURCE.E08", 5, "Sacred Crypts: Statues Chamber", "Criptas sagradas: Cámara de las estatuas"),
            ["RESOURCE.E10"] = new("RESOURCE.E10", 5, "Sacred Crypts: Great Crypt", "Criptas sagradas: Gran cripta"),
            ["RESOURCE.E11"] = new("RESOURCE.E11", 5, "Sacred Crypts: Hidden Passage", "Criptas sagradas: Pasaje oculto"),
            ["RESOURCE.E13"] = new("RESOURCE.E13", 5, "Sacred Crypts: Labyrinth", "Criptas sagradas: Laberinto"),
            ["RESOURCE.E14"] = new("RESOURCE.E14", 5, "Sacred Crypts: Altar Chamber", "Criptas sagradas: Cámara del altar"),
            ["RESOURCE.E16"] = new("RESOURCE.E16", 5, "Sacred Crypts: Inner Vault", "Criptas sagradas: Bóveda interior"),
            ["RESOURCE.E17"] = new("RESOURCE.E17", 5, "Hopi Village: Mountain Pass", "Poblado hopi: Paso de montaña"),

            // Chapter 6: The Indian, the Nun and the Finger / El indio, la monja y el dedo (Douglasville & Military Outpost)
            ["RESOURCE.F03"] = new("RESOURCE.F03", 6, "Military Camp: Perimeter Fence", "Campamento militar: Perímetro"),
            ["RESOURCE.F08"] = new("RESOURCE.F08", 6, "Military Camp: Outer Compound", "Campamento militar: Recinto exterior"),
            ["RESOURCE.F09"] = new("RESOURCE.F09", 6, "Military Camp: Command Tent", "Campamento militar: Tienda de mando"),
            ["RESOURCE.F13"] = new("RESOURCE.F13", 6, "Military Camp: Storage Depot", "Campamento militar: Depósito de suministros"),
            ["RESOURCE.F18"] = new("RESOURCE.F18", 6, "Military Outpost: Guard Post", "Puesto avanzado: Puesto de guardia"),
            ["RESOURCE.F19"] = new("RESOURCE.F19", 6, "Military Outpost: Communications Post", "Puesto avanzado: Puesto de comunicaciones"),
            ["RESOURCE.F20"] = new("RESOURCE.F20", 6, "Military Outpost: Motor Pool", "Puesto avanzado: Parque de vehículos"),
            ["RESOURCE.F21"] = new("RESOURCE.F21", 6, "Military Outpost: Lookout Post", "Puesto avanzado: Puesto de vigía"),
            ["RESOURCE.F22"] = new("RESOURCE.F22", 6, "Military Outpost: Roadblock", "Puesto avanzado: Control de carretera"),
            ["RESOURCE.F24"] = new("RESOURCE.F24", 6, "Desert: Johnny's Trailer Caravan", "Desierto: Caravana abandonada de Johnny"),
            ["RESOURCE.F25"] = new("RESOURCE.F25", 6, "Douglasville: Saturn's Studio & Catapult", "Douglasville: Taller de Saturno y catapulta"),
            ["RESOURCE.F26"] = new("RESOURCE.F26", 6, "Douglasville: Rutger's Spot (Saloon)", "Douglasville: Rincón de Rutger (Saloon)"),
            ["RESOURCE.F27"] = new("RESOURCE.F27", 6, "Douglasville: Sushi's Hotel Room", "Douglasville: Habitación de Sushi en el hotel"),
            ["RESOURCE.F28"] = new("RESOURCE.F28", 6, "Douglasville: Bank Entrance", "Douglasville: Entrada al banco"),
            ["RESOURCE.F29"] = new("RESOURCE.F29", 6, "Douglasville: Bank Interior", "Douglasville: Interior del banco"),
            ["RESOURCE.F30"] = new("RESOURCE.F30", 6, "Douglasville: Bank Vault", "Douglasville: Cámara acorazada del banco"),
            ["RESOURCE.F35"] = new("RESOURCE.F35", 6, "Douglasville: Secret Escape Tunnel", "Douglasville: Túnel secreto de huida"),
            ["RESOURCE.F37"] = new("RESOURCE.F37", 6, "Military Outpost: Surveillance Room", "Puesto avanzado: Sala de vigilancia"),
            ["RESOURCE.F39"] = new("RESOURCE.F39", 6, "Desert: Sunset Lookout", "Desierto: Mirador al atardecer"),

            // G-Series: Cutscenes and Story Sequences
            ["RESOURCE.G01"] = new("RESOURCE.G01", 0, "Story: Prologue & Hospital Arrival", "Historia: Prólogo y llegada al hospital"),
            ["RESOURCE.G02"] = new("RESOURCE.G02", 0, "Story: Highway Journey & Chicago", "Historia: Viaje en carretera y Chicago"),
            ["RESOURCE.G03"] = new("RESOURCE.G03", 0, "Story: Ambush & Desert Capture", "Historia: Emboscada y captura en el desierto"),
            ["RESOURCE.G04"] = new("RESOURCE.G04", 0, "Story: The Fall & Hopi Legend", "Historia: La caída y leyenda hopi"),
            ["RESOURCE.G05"] = new("RESOURCE.G05", 0, "Story: The Medium Séance & Truth", "Historia: Sesión de espiritismo y la verdad"),

            // H-Series: Detail Views, Close-Up Puzzles & Inset Screens
            ["RESOURCE.H09"] = new("RESOURCE.H09", 0, "Close-up: Hospital Desk & Nightstand", "Detalle: Escritorio y mesita del hospital"),
            ["RESOURCE.H10"] = new("RESOURCE.H10", 0, "Close-up: Hospital Window & Cornice", "Detalle: Ventana y cornisa del hospital"),
            ["Resource.h13"] = new("Resource.h13", 0, "Close-up: Hospital Chart & Board", "Detalle: Historial clínico y panel"),
            ["Resource.h18"] = new("Resource.h18", 0, "Close-up: Museum Lock & Tools", "Detalle: Cerradura y herramientas del museo"),
            ["RESOURCE.H19"] = new("RESOURCE.H19", 0, "Close-up: Mayan Mask & Ruby", "Detalle: Máscara maya y rubí"),
            ["RESOURCE.H20"] = new("RESOURCE.H20", 0, "Close-up: Laboratory Microscope", "Detalle: Microscopio de laboratorio"),
            ["RESOURCE.H21"] = new("RESOURCE.H21", 0, "Close-up: Liquid Nitrogen & Thermal Chamber", "Detalle: Nitrógeno líquido y cámara térmica"),
            ["RESOURCE.H22"] = new("RESOURCE.H22", 0, "Close-up: Analysis Lab Keypad", "Detalle: Teclado de seguridad del laboratorio"),
            ["Resource.h23"] = new("Resource.h23", 0, "Close-up: Saloon Piano & Mechanism", "Detalle: Piano y mecanismo del saloon"),
            ["RESOURCE.H24"] = new("RESOURCE.H24", 0, "Close-up: Locomotive Controls & Boiler", "Detalle: Mandos y caldera de la locomotora"),
            ["RESOURCE.H26"] = new("RESOURCE.H26", 0, "Close-up: Bank Vault Combination Lock", "Detalle: Cerradura de combinación del banco"),
            ["RESOURCE.H29"] = new("RESOURCE.H29", 0, "Close-up: Mining Gear & Detonator", "Detalle: Equipo minero y detonador"),
            ["RESOURCE.H30"] = new("RESOURCE.H30", 0, "Close-up: Joshua's Telepathic Helmet", "Detalle: Casco telepático de Joshua"),
            ["RESOURCE.H35"] = new("RESOURCE.H35", 0, "Close-up: Monolith Mouth & Crucifix Slot", "Detalle: Boca del monolito y ranura del crucifijo"),
            ["RESOURCE.H37"] = new("RESOURCE.H37", 0, "Close-up: Sanctuary Altar Relief", "Detalle: Relieve del altar del santuario"),
            ["RESOURCE.H38"] = new("RESOURCE.H38", 4, "Douglasville: Sushi's Hacker Loft & Workshop", "Douglasville: Loft y taller de Sushi"),
            ["RESOURCE.H40"] = new("RESOURCE.H40", 0, "Close-up: Military Terminal & Files", "Detalle: Terminal militar y archivos"),
            ["RESOURCE.H41"] = new("RESOURCE.H41", 0, "Close-up: Security Keycard Reader", "Detalle: Lector de tarjetas de seguridad"),
            ["RESOURCE.H42"] = new("RESOURCE.H42", 0, "Close-up: Escape Hatch & Mechanism", "Detalle: Trampilla de huida y mecanismo"),

            // I-Series: Finale & Epilogue
            ["RESOURCE.I01"] = new("RESOURCE.I01", 6, "Finale: Showdown & Resolution", "Final: Desenlace y resolución"),
            ["RESOURCE.I03"] = new("RESOURCE.I03", 0, "Outro: End Credits & Epilogue", "Créditos finales y epílogo"),
        };

    private static readonly Dictionary<string, (string En, string Es)> AudioNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["RESOURCE.M01"] = ("Main Theme & Title", "Tema principal y título"),
            ["RESOURCE.M02"] = ("Hospital & Investigation", "Hospital e investigación"),
            ["RESOURCE.M03"] = ("Museum of Natural History", "Museo de Historia Natural"),
            ["RESOURCE.M04"] = ("Desert & Ghost Town", "Desierto y pueblo fantasma"),
            ["RESOURCE.M05"] = ("Hopi Village & Sacred Crypts", "Poblado hopi y criptas sagradas"),
            ["RESOURCE.M06"] = ("Action & Finale", "Acción y final"),
            ["RESOURCE.002"] = ("Cinematic Audio", "Audio de cinemáticas"),
            ["RESOURCE.S01"] = ("Hospital Ambience & SFX", "Hospital: Ambiente y efectos"),
            ["RESOURCE.S02"] = ("Museum Ambience & SFX", "Museo: Ambiente y efectos"),
            ["RESOURCE.S03"] = ("Desert Ambience & SFX", "Desierto: Ambiente y efectos"),
            ["RESOURCE.S04"] = ("Douglasville Ambience & SFX", "Douglasville: Ambiente y efectos"),
            ["RESOURCE.S05"] = ("Saloon & Town Ambience & SFX", "Saloon y pueblo: Ambiente y efectos"),
            ["RESOURCE.S06"] = ("Crypts & Caves Ambience & SFX", "Criptas y cuevas: Ambiente y efectos"),
            ["RESOURCE.S07"] = ("Hopi Village Ambience & SFX", "Poblado hopi: Ambiente y efectos"),
            ["RESOURCE.S08"] = ("Military Camp Ambience & SFX", "Campamento militar: Ambiente y efectos"),
            ["RESOURCE.S09"] = ("Action Ambience & SFX", "Acción: Ambiente y efectos"),
            ["RESOURCE.S10"] = ("Misc Sound Effects", "Efectos de sonido varios"),
        };

    private static readonly Dictionary<string, (string En, string Es)> CategoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [VirtualFileSystem.ScenesFolder] = ("Scenes", "Escenas"),
            [VirtualFileSystem.MusicFolder] = ("Music", "Música"),
            [VirtualFileSystem.AmbientFolder] = ("Ambient & SFX", "Sonido ambiente y efectos"),
            [VirtualFileSystem.CinematicFolder] = ("Cinematic Audio", "Audio de cinemáticas"),
            [VirtualFileSystem.VoiceFolder] = ("Voice", "Voces"),
            [VirtualFileSystem.LipSyncFolder] = ("Lip-sync", "Sincronización labial"),
            [VirtualFileSystem.VideoFolder] = ("Video", "Vídeos"),
            [VirtualFileSystem.GlobalFolder] = ("Global Data", "Datos globales"),
        };

    public static bool IsSpanish(string? language) =>
        string.Equals(language, "es", StringComparison.OrdinalIgnoreCase) ||
        (language is null && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the official chapter information if known.</summary>
    public static ChapterInfo? GetChapter(int chapterNumber) =>
        ChaptersByNumber.GetValueOrDefault(chapterNumber);

    /// <summary>Returns friendly scene title with readable name first and internal name after (e.g. "Ch. 1 (\"Wake Up, Brian!\"): Hospital: Gina's Room (RESOURCE.A01)").</summary>
    public static string GetSceneTitle(string archiveName, string? language = null)
    {
        string baseName = Path.GetFileName(archiveName);
        bool es = IsSpanish(language);

        if (ScenesByName.TryGetValue(baseName, out SceneInfo? info))
        {
            string loc = es ? info.NameEs : info.NameEn;
            if (info.Chapter > 0 && ChaptersByNumber.TryGetValue(info.Chapter, out ChapterInfo? ch))
            {
                string chTitle = es ? ch.TitleEs : ch.TitleEn;
                return $"Ch. {info.Chapter} (\"{chTitle}\"): {loc} ({baseName})";
            }
            return $"{loc} ({baseName})";
        }

        return baseName;
    }

    /// <summary>Returns friendly title for an audio archive with readable name first (e.g. "Main Theme & Title (RESOURCE.M01)").</summary>
    public static string GetAudioTitle(string archiveName, string? language = null)
    {
        string baseName = Path.GetFileName(archiveName);
        bool es = IsSpanish(language);

        if (AudioNames.TryGetValue(baseName, out var pair))
        {
            string title = es ? pair.Es : pair.En;
            return $"{title} ({baseName})";
        }

        return baseName;
    }

    /// <summary>Returns friendly name for a top-level virtual filesystem folder.</summary>
    public static string GetCategoryTitle(string categoryName, string? language = null)
    {
        bool es = IsSpanish(language);
        if (CategoryNames.TryGetValue(categoryName, out var pair))
            return es ? pair.Es : pair.En;
        return categoryName;
    }

    /// <summary>Formats a scene entry label in the requested language with readable name first and entry ID after.</summary>
    public static string FormatSceneEntryLabel(FsNode entry, string? language = null)
    {
        bool es = IsSpanish(language);
        ImageInfo? img = entry.Image;

        switch (entry.Kind)
        {
            case EntryKind.Background when img is not null:
            {
                string desc = es ? (entry.EntryIndex == 0 ? "fondo de escena" : "fondo alternativo") : "background";
                string mask = img.IsMask ? (es ? " (máscara)" : " (mask)") : "";
                return $"{desc} {img.Width}×{img.Height}{mask} ({entry.Name})";
            }

            case EntryKind.Mask when img is not null:
            {
                string desc = es ? "máscara de escena" : "scene mask";
                return $"{desc} {img.Width}×{img.Height} ({entry.Name})";
            }

            case EntryKind.Overlay when img is not null:
            {
                string desc = es ? "capa" : "overlay";
                string at = es ? "en" : "at";
                return $"{desc} {img.Width}×{img.Height} {at} {img.X},{img.Y} ({entry.Name})";
            }

            case EntryKind.Animation when img is not null:
            {
                string desc = es ? "animación" : "animation";
                string frameWord = es
                    ? (img.Frames == 1 ? "fotograma" : "fotogramas")
                    : (img.Frames == 1 ? "frame" : "frames");
                return $"{desc}, {img.Frames} {frameWord}, {img.Width}×{img.Height} ({entry.Name})";
            }

            default:
            {
                string dataWord = es ? "datos" : "data";
                return $"{dataWord}, {VirtualFileSystem.FormatSize(entry.Size)} ({entry.Name})";
            }
        }
    }
}
