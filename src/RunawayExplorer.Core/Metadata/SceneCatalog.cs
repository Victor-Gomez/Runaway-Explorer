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

    public static readonly IReadOnlyList<ChapterInfo> ChaptersR1 = Chapters;

    public static readonly IReadOnlyList<ChapterInfo> ChaptersR2 =
    [
        new(1, "Mala Island", "La isla de Mala"),
        new(2, "Surfin' Mala", "Surfeando en Mala"),
        new(3, "Simpler than an Amoeba", "Más simple que una ameba"),
        new(4, "The Guy Who Can Speak to Platypuses", "El hombre que habla con los ornitorrincos"),
        new(5, "Allerton Court", "Allerton Court"),
        new(6, "The Hidden Temple", "El templo oculto"),
    ];

    public static readonly IReadOnlyList<ChapterInfo> ChaptersR3 =
    [
        new(1, "Brian Basco Is Dead", "Brian Basco ha muerto"),
        new(2, "The Art of Running Away", "El arte de escapar"),
        new(3, "A Timely Suicide", "Un suicidio bien oportuno"),
        new(4, "An Unexpected Ally", "Un aliado inesperado"),
        new(5, "Deconstructing Brian", "Brian en todos sus estados"),
        new(6, "The End Is Here", "El final está cerca"),
        new(7, "Epilogue & Flashbacks", "Epílogo y recuerdos"),
    ];

    private static readonly Dictionary<int, ChapterInfo> ChaptersByNumber =
        Chapters.ToDictionary(c => c.Number);

    private static readonly Dictionary<int, ChapterInfo> ChaptersByNumberR2 =
        ChaptersR2.ToDictionary(c => c.Number);

    private static readonly Dictionary<int, ChapterInfo> ChaptersByNumberR3 =
        ChaptersR3.ToDictionary(c => c.Number);

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

    private static readonly Dictionary<string, SceneInfo> ScenesByNameR2 =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Intro & Prologue
            ["RESOURCE.001"] = new("RESOURCE.001", 1, "Intro: Airplane Interior", "Intro: Interior del avión"),
            ["RESOURCE.I01"] = new("RESOURCE.I01", 0, "Cutscene: Brian & Gina Flight", "Cinemática: Vuelo de Brian y Gina"),
            ["RESOURCE.I02"] = new("RESOURCE.I02", 0, "Cutscene: Cockpit Emergency", "Cinemática: Emergencia en cabina"),

            // Chapter 1: Mala Island / La isla de Mala
            ["RESOURCE.A05"] = new("RESOURCE.A05", 1, "Jungle: Crashed Plane & Lake", "Selva: Avión estrellado y lago"),
            ["RESOURCE.A08"] = new("RESOURCE.A08", 1, "Jungle: Lemur Path", "Selva: Sendero de los lémures"),
            ["RESOURCE.A09"] = new("RESOURCE.A09", 1, "Jungle: Quicksand", "Selva: Arenas movedizas"),
            ["RESOURCE.A10"] = new("RESOURCE.A10", 1, "Jungle: Monkey Tree", "Selva: Árbol del mono"),
            ["RESOURCE.A11"] = new("RESOURCE.A11", 1, "Jungle: Rope Bridge & Gorge", "Selva: Puente colgante y desfiladero"),

            // Chapter 2: Surfin' Mala / Surfeando en Mala
            ["RESOURCE.B01"] = new("RESOURCE.B01", 2, "Beach: Luana's Surf Shack & Bar", "Playa: Caseta de surf de Luana y bar"),
            ["RESOURCE.B03"] = new("RESOURCE.B03", 2, "Beach: Tiki Hut & Pier", "Playa: Cabaña tiki y muelle"),
            ["RESOURCE.B04"] = new("RESOURCE.B04", 2, "Military Camp: Guard Post & Exterior", "Campamento militar: Puesto de guardia y exterior"),
            ["RESOURCE.B04A"] = new("RESOURCE.B04A", 2, "Military Camp: Guard Post (Night)", "Campamento militar: Puesto de guardia (Noche)"),
            ["RESOURCE.B05"] = new("RESOURCE.B05", 2, "Military Camp: Colonel's Office", "Campamento militar: Despacho del coronel"),
            ["RESOURCE.B06"] = new("RESOURCE.B06", 2, "Military Camp: Detention Center", "Campamento militar: Centro de detención"),
            ["RESOURCE.B07"] = new("RESOURCE.B07", 2, "Military Camp: Communications Center", "Campamento militar: Centro de comunicaciones"),
            ["RESOURCE.B08"] = new("RESOURCE.B08", 2, "Military Camp: Vehicle Depot", "Campamento militar: Parque de vehículos"),
            ["RESOURCE.B09"] = new("RESOURCE.B09", 2, "Military Camp: Airstrip & Hangars", "Campamento militar: Pista y hangares"),
            ["RESOURCE.B20"] = new("RESOURCE.B20", 2, "Beach: Sunset Overlook", "Playa: Mirador al atardecer"),

            // Chapter 3: Simpler than an Amoeba / Más simple que una ameba
            ["RESOURCE.C01"] = new("RESOURCE.C01", 3, "Yacht: Deck & Ocean", "Yate: Cubierta y océano"),
            ["RESOURCE.C02"] = new("RESOURCE.C02", 3, "Yacht: Bridge & Controls", "Yate: Puente de mando"),
            ["RESOURCE.C03"] = new("RESOURCE.C03", 3, "Yacht: Engine Room", "Yate: Sala de máquinas"),
            ["RESOURCE.C04"] = new("RESOURCE.C04", 3, "Yacht: Rosa's Cabin", "Yate: Camarote de Rosa"),
            ["RESOURCE.C05"] = new("RESOURCE.C05", 3, "Underwater: Sunken Galleon", "Bajo el agua: Galeón hundido"),
            ["RESOURCE.C05B"] = new("RESOURCE.C05B", 3, "Underwater: Galleon Hold", "Bajo el agua: Bodega del galeón"),
            ["RESOURCE.C05C"] = new("RESOURCE.C05C", 3, "Underwater: Coral Reef", "Bajo el agua: Arrecife de coral"),
            ["RESOURCE.C09"] = new("RESOURCE.C09", 3, "Ocean: Sea Floor & Cave", "Océano: Fondo marino y cueva"),
            ["RESOURCE.C10"] = new("RESOURCE.C10", 3, "Yacht: Cargo Hold", "Yate: Bodega de carga"),
            ["RESOURCE.C11"] = new("RESOURCE.C11", 3, "Underwater: Sea Trench", "Bajo el agua: Fosa marina"),
            ["RESOURCE.C12"] = new("RESOURCE.C12", 3, "Yacht: Galley", "Yate: Cocina"),
            ["RESOURCE.C13"] = new("RESOURCE.C13", 3, "Underwater: Submarine Dock", "Bajo el agua: Muelle de submarinos"),
            ["RESOURCE.C14"] = new("RESOURCE.C14", 3, "Underwater: Deep Cavern", "Bajo el agua: Caverna profunda"),
            ["RESOURCE.C15"] = new("RESOURCE.C15", 3, "Underwater: Submarine Interior", "Bajo el agua: Interior del submarino"),
            ["RESOURCE.C16"] = new("RESOURCE.C16", 3, "Underwater: Submarine Airlock", "Bajo el agua: Esclusa del submarino"),
            ["RESOURCE.C17"] = new("RESOURCE.C17", 3, "Underwater: Sunken Ruins", "Bajo el agua: Ruinas sumergidas"),
            ["RESOURCE.C18"] = new("RESOURCE.C18", 3, "Underwater: Deep Trench Overlook", "Bajo el agua: Mirador de la fosa"),
            ["RESOURCE.C19"] = new("RESOURCE.C19", 3, "Underwater: Hidden Alcove", "Bajo el agua: Nicho oculto"),
            ["RESOURCE.C20"] = new("RESOURCE.C20", 3, "Underwater: Ancient Altar", "Bajo el agua: Altar ancestral"),
            ["RESOURCE.C21"] = new("RESOURCE.C21", 3, "Underwater: Escape Hatch", "Bajo el agua: Escotilla de escape"),

            // Chapter 4: The Guy Who Can Speak to Platypuses / El hombre que habla con los ornitorrincos
            ["RESOURCE.D06"] = new("RESOURCE.D06", 4, "Sanctuary: Platypus Lagoon", "Santuario: Laguna de los ornitorrincos"),
            ["RESOURCE.D08"] = new("RESOURCE.D08", 4, "Sanctuary: Joshua's Machine", "Santuario: La máquina de Joshua"),
            ["RESOURCE.D09"] = new("RESOURCE.D09", 4, "Sanctuary: Cliff Path", "Santuario: Sendero del acantilado"),
            ["RESOURCE.D10"] = new("RESOURCE.D10", 4, "Sanctuary: Archibald's Hut", "Santuario: Choza de Archibald"),
            ["RESOURCE.D11"] = new("RESOURCE.D11", 4, "Sanctuary: Lookout Point", "Santuario: Mirador"),
            ["RESOURCE.D12"] = new("RESOURCE.D12", 4, "Sanctuary: Rope Bridge", "Santuario: Puente de cuerda"),
            ["RESOURCE.D13"] = new("RESOURCE.D13", 4, "Sanctuary: Platypus Nest", "Santuario: Nido de ornitorrinco"),
            ["RESOURCE.D14"] = new("RESOURCE.D14", 4, "Sanctuary: Mountain Trail", "Santuario: Camino de montaña"),
            ["RESOURCE.D15"] = new("RESOURCE.D15", 4, "Sanctuary: Cave Entrance", "Santuario: Entrada de la cueva"),
            ["RESOURCE.D16"] = new("RESOURCE.D16", 4, "Sanctuary: Abandoned Campsite", "Santuario: Campamento abandonado"),
            ["RESOURCE.D17"] = new("RESOURCE.D17", 4, "Sanctuary: Waterfall & Stream", "Santuario: Cascada y arroyo"),

            // Chapter 5: Allerton Court / Allerton Court
            ["RESOURCE.E01"] = new("RESOURCE.E01", 5, "Allerton Court: Grand Foyer", "Allerton Court: Gran vestíbulo"),
            ["RESOURCE.E02"] = new("RESOURCE.E02", 5, "Allerton Court: Library", "Allerton Court: Biblioteca"),
            ["RESOURCE.E03"] = new("RESOURCE.E03", 5, "Allerton Court: Dining Room", "Allerton Court: Comedor"),
            ["RESOURCE.E04"] = new("RESOURCE.E04", 5, "Allerton Court: Study & Desk", "Allerton Court: Estudio y escritorio"),
            ["RESOURCE.E05"] = new("RESOURCE.E05", 5, "Allerton Court: Cellar & Crypt", "Allerton Court: Sótano y cripta"),
            ["RESOURCE.E07"] = new("RESOURCE.E07", 5, "Allerton Court: Conservatory", "Allerton Court: Invernadero"),
            ["RESOURCE.E08"] = new("RESOURCE.E08", 5, "Allerton Court: Upstairs Gallery", "Allerton Court: Galería superior"),
            ["RESOURCE.E10"] = new("RESOURCE.E10", 5, "Allerton Court: Hidden Vault", "Allerton Court: Cámara acorazada oculta"),
            ["RESOURCE.E11"] = new("RESOURCE.E11", 5, "Allerton Court: Secret Laboratory", "Allerton Court: Laboratorio secreto"),

            // Chapter 6: The Hidden Temple / El templo oculto
            ["RESOURCE.G01"] = new("RESOURCE.G01", 6, "Temple: Outer Courtyard", "Templo: Patio exterior"),
            ["RESOURCE.G02"] = new("RESOURCE.G02", 6, "Temple: Carved Antechamber", "Templo: Antecámara tallada"),
            ["RESOURCE.G03"] = new("RESOURCE.G03", 6, "Temple: Great Pyramid Hall", "Templo: Sala de la gran pirámide"),
            ["RESOURCE.G03B"] = new("RESOURCE.G03B", 6, "Temple: Pyramid Hall (Altar Lit)", "Templo: Sala de la pirámide (Altar encendido)"),
            ["RESOURCE.G04"] = new("RESOURCE.G04", 6, "Temple: Alien Portal", "Templo: Portal alienígena"),
            ["RESOURCE.G05"] = new("RESOURCE.G05", 6, "Temple: Celestial Observatory", "Templo: Observatorio celeste"),
            ["RESOURCE.G06"] = new("RESOURCE.G06", 6, "Temple: Crystal Chamber", "Templo: Cámara de cristal"),
            ["RESOURCE.G07"] = new("RESOURCE.G07", 6, "Temple: Power Conduits", "Templo: Conductos de energía"),
            ["RESOURCE.G08"] = new("RESOURCE.G08", 6, "Temple: Ancient Mechanism", "Templo: Mecanismo ancestral"),
            ["RESOURCE.G10"] = new("RESOURCE.G10", 6, "Spaceship: Command Bridge", "Nave espacial: Puente de mando"),
            ["RESOURCE.G11"] = new("RESOURCE.G11", 6, "Spaceship: Stasis Bay", "Nave espacial: Bahía de éxtasis"),
            ["RESOURCE.G12"] = new("RESOURCE.G12", 6, "Spaceship: Engine Core", "Nave espacial: Núcleo del motor"),
            ["RESOURCE.G13"] = new("RESOURCE.G13", 6, "Spaceship: Observation Deck", "Nave espacial: Cubierta de observación"),

            // Epilogue & Localized Overlays
            ["RESOURCE.H02"] = new("RESOURCE.H02", 6, "Epilogue: Island Shore", "Epílogo: Costa de la isla"),
            ["RESOURCE.H04"] = new("RESOURCE.H04", 6, "Epilogue: Finale Scene", "Epílogo: Escena final"),
            ["RESOURCE.H06"] = new("RESOURCE.H06", 6, "Epilogue: Credits Sequence", "Epílogo: Créditos"),
            ["RESOURCE.SP1"] = new("RESOURCE.SP1", 1, "Spanish Overlay: Chapter 1", "Textos español: Capítulo 1"),
            ["RESOURCE.SP2"] = new("RESOURCE.SP2", 2, "Spanish Overlay: Chapter 2", "Textos español: Capítulo 2"),
            ["RESOURCE.SP3"] = new("RESOURCE.SP3", 3, "Spanish Overlay: Chapter 3", "Textos español: Capítulo 3"),
            ["RESOURCE.SP4"] = new("RESOURCE.SP4", 4, "Spanish Overlay: Chapter 4", "Textos español: Capítulo 4"),
            ["RESOURCE.SP5"] = new("RESOURCE.SP5", 5, "Spanish Overlay: Chapter 5", "Textos español: Capítulo 5"),
        };

    private static readonly Dictionary<string, SceneInfo> ScenesByNameR3 =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Chapter 1: Brian Basco Is Dead / Brian Basco ha muerto
            ["RESOURCE.A01"] = new("RESOURCE.A01", 1, "Cemetery: Brian's Grave", "Cementerio: Tumba de Brian"),
            ["RESOURCE.A02"] = new("RESOURCE.A02", 1, "Cemetery: Main Gate & Path", "Cementerio: Puerta principal y camino"),
            ["RESOURCE.A03"] = new("RESOURCE.A03", 1, "Cemetery: Crypt Exterior", "Cementerio: Exterior de la cripta"),
            ["RESOURCE.A04"] = new("RESOURCE.A04", 1, "Cemetery: Crypt Interior", "Cementerio: Interior de la cripta"),
            ["RESOURCE.A05"] = new("RESOURCE.A05", 1, "Cemetery: Mausoleum Hall", "Cementerio: Sala del mausoleo"),
            ["RESOURCE.A06"] = new("RESOURCE.A06", 1, "Cemetery: Chapel", "Cementerio: Capilla"),
            ["RESOURCE.A07"] = new("RESOURCE.A07", 1, "Cemetery: Caretaker's Hut", "Cementerio: Caseta del sepulturero"),
            ["RESOURCE.A08"] = new("RESOURCE.A08", 1, "Cemetery: Stone Wall & Trees", "Cementerio: Muro de piedra y árboles"),
            ["RESOURCE.A09"] = new("RESOURCE.A09", 1, "Cemetery: Graveyard Grounds", "Cementerio: Terreno del camposanto"),
            ["RESOURCE.A10"] = new("RESOURCE.A10", 1, "Cemetery: Memorial Walk", "Cementerio: Paseo memorial"),
            ["RESOURCE.A11"] = new("RESOURCE.A11", 1, "Cemetery: Escape Route", "Cementerio: Ruta de escape"),
            ["RESOURCE.A12"] = new("RESOURCE.A12", 1, "Cemetery: Secret Passage", "Cementerio: Pasadizo secreto"),

            // Chapter 2: The Art of Running Away / El arte de escapar
            ["RESOURCE.B00"] = new("RESOURCE.B00", 2, "Happy Dale: Exterior Overview", "Happy Dale: Vista exterior"),
            ["RESOURCE.B01"] = new("RESOURCE.B01", 2, "Happy Dale: Brian's Room", "Happy Dale: Habitación de Brian"),
            ["RESOURCE.B02"] = new("RESOURCE.B02", 2, "Happy Dale: Main Corridor", "Happy Dale: Pasillo principal"),
            ["RESOURCE.B03"] = new("RESOURCE.B03", 2, "Happy Dale: Common Recreation Room", "Happy Dale: Sala de recreo"),
            ["RESOURCE.B04"] = new("RESOURCE.B04", 2, "Happy Dale: Nurse Station", "Happy Dale: Control de enfermería"),
            ["RESOURCE.B05"] = new("RESOURCE.B05", 2, "Happy Dale: Dr. Bennett's Office", "Happy Dale: Despacho del Dr. Bennett"),
            ["RESOURCE.B07"] = new("RESOURCE.B07", 2, "Happy Dale: Security Checkpoint", "Happy Dale: Control de seguridad"),
            ["RESOURCE.B08"] = new("RESOURCE.B08", 2, "Happy Dale: Courtyard & Garden", "Happy Dale: Patio y jardín"),
            ["RESOURCE.B10"] = new("RESOURCE.B10", 2, "Happy Dale: Therapy Room", "Happy Dale: Sala de terapia"),
            ["RESOURCE.B11"] = new("RESOURCE.B11", 2, "Happy Dale: Ernie's Room", "Happy Dale: Habitación de Ernie"),
            ["RESOURCE.B12"] = new("RESOURCE.B12", 2, "Happy Dale: Gabbo's Room", "Happy Dale: Habitación de Gabbo"),
            ["RESOURCE.B13"] = new("RESOURCE.B13", 2, "Happy Dale: Marcelo's Quarters", "Happy Dale: Cuarto de Marcelo"),
            ["RESOURCE.B14"] = new("RESOURCE.B14", 2, "Happy Dale: Restrooms", "Happy Dale: Baños"),
            ["RESOURCE.B15"] = new("RESOURCE.B15", 2, "Happy Dale: Storage & Utility", "Happy Dale: Almacén y mantenimiento"),
            ["RESOURCE.B16"] = new("RESOURCE.B16", 2, "Happy Dale: Laundry Room", "Happy Dale: Lavandería"),
            ["RESOURCE.B19"] = new("RESOURCE.B19", 2, "Happy Dale: Ventilation Shaft", "Happy Dale: Conducto de ventilación"),
            ["RESOURCE.B20"] = new("RESOURCE.B20", 2, "Happy Dale: Perimeter Wall & Fence", "Happy Dale: Valla perimetral"),

            // Chapter 3: A Timely Suicide / Un suicidio bien oportuno
            ["RESOURCE.C01"] = new("RESOURCE.C01", 3, "Lake Cabin: Main Living Area", "Cabaña del lago: Salón principal"),
            ["RESOURCE.C02"] = new("RESOURCE.C02", 3, "Lake Cabin: Kitchen & Counter", "Cabaña del lago: Cocina y encimera"),
            ["RESOURCE.C03"] = new("RESOURCE.C03", 3, "Lake Cabin: Bedroom", "Cabaña del lago: Dormitorio"),
            ["RESOURCE.C06"] = new("RESOURCE.C06", 3, "Lake Cabin: Porch & Deck", "Cabaña del lago: Porche y terraza"),
            ["RESOURCE.C07"] = new("RESOURCE.C07", 3, "Lake Shore: Wooden Pier", "Orilla del lago: Muelle de madera"),
            ["RESOURCE.C08"] = new("RESOURCE.C08", 3, "Lake Shore: Boathouse & Shed", "Orilla del lago: Cobertizo de botes"),
            ["RESOURCE.C09"] = new("RESOURCE.C09", 3, "Lake Forest: Wooded Path", "Bosque del lago: Sendero arbolado"),
            ["RESOURCE.C10"] = new("RESOURCE.C10", 3, "Lake Stream: Old Wooden Bridge", "Arroyo del lago: Puente viejo"),
            ["RESOURCE.C11"] = new("RESOURCE.C11", 3, "Lake Forest: Forest Clearing", "Bosque del lago: Claro del bosque"),
            ["RESOURCE.C12"] = new("RESOURCE.C12", 3, "Lake Cabin: Storage Annex", "Cabaña del lago: Anexo de almacenaje"),
            ["RESOURCE.C15"] = new("RESOURCE.C15", 3, "Lake Cabin: Investigation Room", "Cabaña del lago: Sala de investigación"),

            // Chapter 4: An Unexpected Ally / Un aliado inesperado
            ["RESOURCE.D00"] = new("RESOURCE.D00", 4, "Morgue: Basement Hallway", "Depósito de cadáveres: Pasillo del sótano"),
            ["RESOURCE.D01"] = new("RESOURCE.D01", 4, "Morgue: Autopsy Lab", "Depósito de cadáveres: Sala de autopsias"),
            ["RESOURCE.D02"] = new("RESOURCE.D02", 4, "Morgue: Cold Storage & Lockers", "Depósito de cadáveres: Cámaras frigoríficas"),
            ["RESOURCE.D03"] = new("RESOURCE.D03", 4, "Morgue: Incinerator Room", "Depósito de cadáveres: Sala del incinerador"),
            ["RESOURCE.D04"] = new("RESOURCE.D04", 4, "Morgue: Preparation Chamber", "Depósito de cadáveres: Sala de preparación"),
            ["RESOURCE.D05"] = new("RESOURCE.D05", 4, "Morgue: Medical Records Office", "Depósito de cadáveres: Archivo médico"),
            ["RESOURCE.D06"] = new("RESOURCE.D06", 4, "Morgue: Supply Closet", "Depósito de cadáveres: Armario de suministros"),
            ["RESOURCE.D07"] = new("RESOURCE.D07", 4, "Morgue: Loading Bay", "Depósito de cadáveres: Bahía de carga"),
            ["RESOURCE.D10"] = new("RESOURCE.D10", 4, "Morgue: Hatch & Air Duct", "Depósito de cadáveres: Escotilla y conducto"),
            ["RESOURCE.D11"] = new("RESOURCE.D11", 4, "Morgue: Emergency Exit", "Depósito de cadáveres: Salida de emergencia"),

            // Chapter 5: Deconstructing Brian / Brian en todos sus estados
            ["RESOURCE.E01"] = new("RESOURCE.E01", 5, "Flashback: Tanton Manor & Camp", "Recuerdo: Mansión Tanton y campamento"),

            // Chapter 6: The End Is Here / El final está cerca
            ["RESOURCE.F01"] = new("RESOURCE.F01", 6, "New York: Dark Alleyways", "Nueva York: Callejones oscuros"),
            ["RESOURCE.F03"] = new("RESOURCE.F03", 6, "New York: Construction Crane & Rooftops", "Nueva York: Grúa de construcción y azoteas"),
            ["RESOURCE.F04"] = new("RESOURCE.F04", 6, "New York: Final Showdown", "Nueva York: Desenlace final"),

            // Epilogue & Cutscenes & Localized Overlays
            ["RESOURCE.H01"] = new("RESOURCE.H01", 7, "Epilogue: Flashback & Revelation", "Epílogo: Recuerdos y revelación"),
            ["RESOURCE.002"] = new("RESOURCE.002", 0, "Cinematic Cutscene Animations", "Animaciones de cinemáticas"),
            ["RESOURCE.SP1"] = new("RESOURCE.SP1", 1, "Spanish Overlay: Chapter 1", "Textos español: Capítulo 1"),
            ["RESOURCE.SP2"] = new("RESOURCE.SP2", 2, "Spanish Overlay: Chapter 2", "Textos español: Capítulo 2"),
            ["RESOURCE.SP3"] = new("RESOURCE.SP3", 3, "Spanish Overlay: Chapter 3", "Textos español: Capítulo 3"),
            ["RESOURCE.SP5"] = new("RESOURCE.SP5", 5, "Spanish Overlay: Chapter 5", "Textos español: Capítulo 5"),
            ["RESOURCE.SP6"] = new("RESOURCE.SP6", 6, "Spanish Overlay: Chapter 6", "Textos español: Capítulo 6"),
            ["RESOURCE.SP7"] = new("RESOURCE.SP7", 7, "Spanish Overlay: Epilogue", "Textos español: Epílogo"),
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
            ["RESOURCE.S05"] = ("Pueblo Ambience & SFX", "Pueblo: Ambiente y efectos"),
            ["RESOURCE.S06"] = ("Crypts & Caves Ambience & SFX", "Criptas y cuevas: Ambiente y efectos"),
            ["RESOURCE.S07"] = ("Hopi Village Ambience & SFX", "Poblado hopi: Ambiente y efectos"),
            ["RESOURCE.S08"] = ("Military Camp Ambience & SFX", "Campamento militar: Ambiente y efectos"),
            ["RESOURCE.S09"] = ("Action Ambience & SFX", "Acción: Ambiente y efectos"),
            ["RESOURCE.S10"] = ("Misc Sound Effects", "Efectos de sonido varios"),
        };

    private static readonly Dictionary<string, (string En, string Es)> AudioNamesR2 =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["RESOURCE.M01"] = ("Mala Island Theme", "Tema de la isla de Mala"),
            ["RESOURCE.M02"] = ("Surfin' Mala Theme", "Tema de Surfeando en Mala"),
            ["RESOURCE.M03"] = ("Under the Sea & Galleon Theme", "Bajo el mar y el galeón"),
            ["RESOURCE.M04"] = ("Platypus Bay Theme", "Bahía de los ornitorrincos"),
            ["RESOURCE.M05"] = ("Allerton Court Manor Theme", "Mansión Allerton Court"),
            ["RESOURCE.M06"] = ("The Hidden Temple & Finale Theme", "El templo oculto y tema final"),
            ["RESOURCE.S00"] = ("Jungle & Nature Ambience", "Ambiente de selva y naturaleza"),
            ["RESOURCE.S01"] = ("Camp & Military Ambience", "Ambiente militar y campamento"),
            ["RESOURCE.S02"] = ("Underwater & Ocean Ambience", "Ambiente oceánico y submarino"),
            ["RESOURCE.S03"] = ("Sanctuary & Island Ambience", "Ambiente del santuario y la isla"),
            ["RESOURCE.S04"] = ("Manor & Indoors Ambience", "Ambiente de la mansión e interiores"),
            ["RESOURCE.S05"] = ("Ancient Temple Ambience", "Ambiente del templo ancestral"),
            ["RESOURCE.S06"] = ("Sound Effects", "Efectos de sonido"),
        };

    private static readonly Dictionary<string, (string En, string Es)> AudioNamesR3 =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["RESOURCE.S00"] = ("Menu & Interface Sounds", "Sonidos de menú e interfaz"),
            ["RESOURCE.S01"] = ("Chapter 1 Sound Effects", "Efectos de sonido: Capítulo 1"),
            ["RESOURCE.S02"] = ("Chapter 2 Sound Effects", "Efectos de sonido: Capítulo 2"),
            ["RESOURCE.S03"] = ("Chapter 3 Sound Effects", "Efectos de sonido: Capítulo 3"),
            ["RESOURCE.S05"] = ("Chapter 5 Sound Effects", "Efectos de sonido: Capítulo 5"),
            ["RESOURCE.S06"] = ("Chapter 6 Sound Effects", "Efectos de sonido: Capítulo 6"),
            ["RESOURCE.S07"] = ("Epilogue Sound Effects", "Efectos de sonido: Epílogo"),
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
            [VirtualFileSystem.DialogueFolder] = ("Dialogue", "Diálogos"),
            [VirtualFileSystem.GlobalFolder] = ("Global Data", "Datos globales"),
        };

    private static readonly IReadOnlyDictionary<int, ChapterInfo>[] ChaptersPerGame =
        [ChaptersByNumber, ChaptersByNumberR2, ChaptersByNumberR3];

    private static readonly IReadOnlyDictionary<string, SceneInfo>[] ScenesPerGame =
        [ScenesByName, ScenesByNameR2, ScenesByNameR3];

    private static readonly IReadOnlyDictionary<string, (string En, string Es)>[] AudioPerGame =
        [AudioNames, AudioNamesR2, AudioNamesR3];

    public static bool IsSpanish(string? language) =>
        string.Equals(language, "es", StringComparison.OrdinalIgnoreCase) ||
        (language is null && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the official chapter information if known.</summary>
    public static ChapterInfo? GetChapter(int chapterNumber, GameVersion game = GameVersion.Runaway1) =>
        ChaptersPerGame[Math.Clamp((int)game, 0, 2)].GetValueOrDefault(chapterNumber);

    /// <summary>Returns friendly scene title with readable name first and internal name after (e.g. "Ch. 1 (\"Wake Up, Brian!\"): Hospital: Gina's Room (RESOURCE.A01)").</summary>
    public static string GetSceneTitle(string archiveName, string? language = null, GameVersion game = GameVersion.Runaway1)
    {
        string baseName = Path.GetFileName(archiveName);
        bool es = IsSpanish(language);
        int gameIdx = Math.Clamp((int)game, 0, 2);
        var scenes = ScenesPerGame[gameIdx];
        var chapters = ChaptersPerGame[gameIdx];

        if (scenes.TryGetValue(baseName, out SceneInfo? info))
        {
            string loc = es ? info.NameEs : info.NameEn;
            if (info.Chapter > 0 && chapters.TryGetValue(info.Chapter, out ChapterInfo? ch))
            {
                string chTitle = es ? ch.TitleEs : ch.TitleEn;
                return $"Ch. {info.Chapter} (\"{chTitle}\"): {loc} ({baseName})";
            }
            return $"{loc} ({baseName})";
        }

        return baseName;
    }

    /// <summary>Returns friendly title for an audio archive with readable name first (e.g. "Main Theme & Title (RESOURCE.M01)").</summary>
    public static string GetAudioTitle(string archiveName, string? language = null, GameVersion game = GameVersion.Runaway1)
    {
        string baseName = Path.GetFileName(archiveName);
        bool es = IsSpanish(language);
        var audio = AudioPerGame[Math.Clamp((int)game, 0, 2)];

        if (audio.TryGetValue(baseName, out var pair))
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
                if (img.X == 0 && img.Y == 0)
                {
                    string spriteDesc = es ? "capa" : "sprite";
                    return $"{spriteDesc} {img.Width}×{img.Height} ({entry.Name})";
                }
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

            case EntryKind.Dialogue:
            {
                string desc = es ? "frase" : "phrase";
                return string.IsNullOrEmpty(entry.Subtitle)
                    ? $"{desc} ({entry.Name})"
                    : $"{desc} ({entry.Name})  \"{entry.Subtitle}\"";
            }

            default:
            {
                if (entry.Size == 1536)
                {
                    string attrWord = es ? "tabla de atributos de escena" : "scene attribute table";
                    return $"{attrWord} ({entry.Name})";
                }
                string dataWord = es ? "datos" : "data";
                return $"{dataWord}, {VirtualFileSystem.FormatSize(entry.Size)} ({entry.Name})";
            }
        }
    }
}
