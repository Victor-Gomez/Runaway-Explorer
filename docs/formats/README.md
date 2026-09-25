# Pendulo Studios game engine file format reference

Notes on the reverse-engineered file formats used by Pendulo Studios across their adventure games:
- **Hollywood Monsters** (1997)
- **Runaway: A Road Adventure** (2001)
- **Runaway 2: The Dream of the Turtle** (2006)
- **Runaway: A Twist of Fate** (2009)
- **The Next BIG Thing** (2011)
- **Yesterday** (2012)

These notes document what the decoders under [`src/RunawayExplorer.Core/`](../../src/RunawayExplorer.Core/) actually parse; there is no official specification.

Everything below is little-endian unless stated otherwise. Integers (`u8`, `u16`, `u32`, `i16`) match standard .NET binary reader conventions.

## Format index

| File / container | Kind | Games | Doc |
| --- | --- | --- | --- |
| `RESOURCE.<L><nn>`, `M<nn>`, `S<nn>`, `DATAACA*.000`, `DATAVC00.*` | Archive containers — scene archives, audio archives, voice shards, and video keyfiles | All | [archives.md](archives.md) |
| Entry 0 (`e00`) | Full-screen scene backgrounds — raw RGB565 rasters, 1024×480 indexed bitmaps, and 1080p JPEG | All | [rasters.md](rasters.md) |
| Positioned row records & PNG | Overlays — foreground elements, props, inventory icons, and UI panels | All | [overlays.md](overlays.md) |
| Multi-frame segment streams | Animated sprites — frame bounding boxes, segment streams (HM 8-bit, R1 5-byte, R2 6-byte alpha, R3 7-byte, TNBT/Yesterday flags 6 & 7) | All | [sprites.md](sprites.md) |
| 3-byte / 4-byte RLE, Sparse, PNG | Scene masks — walkboxes, depth planes, occluders, and clickable hotspots | All | [masks.md](masks.md) |
| Raw PCM, MP3, Bink Video (`.bik`) | Audio & Video — synthesized WAV streams, MP3 audio, and XOR-restored Bink cutscenes | All | [audio-video.md](audio-video.md) |
| `RESOURCE.000` .. `RESOURCE.004` | Global data — fonts (outlined glyph bitmaps plus their glyph tables), the cursor atlas, UI sprites, character sprite library, phrase tables, and lip-sync visemes | HM, R1–R3 | [global-data.md](global-data.md) |
| Blocks of 6-bit RGB triples | Palettes — the per-entry colour table and the shared character colours | HM | [palettes.md](palettes.md) |

## Game-by-game summary

One row per title, for reading a single game rather than a single format. Every column heads into the doc
that describes it in full; "—" means the game has nothing of that kind.

| | [Scene table](archives.md) | [Background](rasters.md) | [Overlay](overlays.md) | [Sprite segment](sprites.md) | [Mask](masks.md) | [Audio](audio-video.md) | [Video](audio-video.md) | [Voice](archives.md) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **Hollywood Monsters** (1997) | Variant A, plus one tableless archive (`RESOURCE.I02`) | 1024×480 8-bit indexed, coloured by the nearest preceding palette block ([palettes.md](palettes.md)) | Row records, 1 byte/px | 5-byte header, 1 byte/px | 3-byte RLE | Raw PCM mono, terminated offset tables: 16-bit music and 8-bit SFX at 11,025 Hz | — | `RESOURCE.004`, one clip per slot, 8-bit 22,050 Hz |
| **Runaway 1** (2001) | Variant A | 1024×600 RGB565, width by stride sweep | Row records, RGB565 | 5-byte header, RGB565 | 3-byte RLE | Raw PCM 16-bit stereo: music 16,000 Hz, SFX and cinematic 22,050 Hz; plain offset tables | Bink, headers XOR-chained in a keyfile | 7 shards, `DATAACA0-6.000`, 8-bit mono |
| **Runaway 2** (2006) | Variant B (4-byte header) | 1280×720 RGB565 | Row records, RGB565 | 6-byte header, `Flag` selects RGB565 or +alpha | 3-byte RLE | MP3 or WAV, size and format byte per entry | Bink, keyfile | Single `Dataaa.000`, MP3 |
| **Runaway 3** (2009) | Variant A | 1280×720 RGB565 | Row records, RGB565 | 7-byte header, `u16 Count`, RGB565 / alpha / RGBA | Sparse spans, `Flag` 2 solid or 4 antialiased | MP3 or WAV, size and format byte per entry | Plain Bink, no keyfile | Single `Dataaa.000`, MP3 |
| **The Next BIG Thing** (2011) | Variant A | 1920×1080 JPEG | PNG | 7-byte header, `Flag` 6 BGR24 or 7 RGBA | Grayscale PNG | MP3 or WAV, size and format byte per entry | Plain Bink, no keyfile | Named `DATAA*.000` archives, MP3 |
| **Yesterday** (2012) | Variant A | 1920×1080 JPEG | PNG | 7-byte header, `Flag` 6 BGR24 or 7 RGBA | 4-byte RLE and grayscale PNG | MP3 or WAV, size and format byte per entry | Plain Bink, no keyfile | Named `DATAA*.000` archives, MP3 |

### What the numbered resources hold

The numbering is reused across the games for unrelated things, so it is worth reading per title
([global-data.md](global-data.md)):

| | `RESOURCE.000` | `001` | `002` | `003` | `004` |
| --- | --- | --- | --- | --- | --- |
| **Hollywood Monsters** | UI and font art, plus the shared 80-colour palette block; 1-byte header, 100 slots | Ambient audio | Cinematic audio | Script text: obfuscated rows + speech cues ([global-data.md](global-data.md)) | Voice bank |
| **Runaway 1** | Font atlases and UI art; 20-byte header, 500 slots | Character sprite library | Cinematic audio | Dialogue phrase tables, scene-indexed like Hollywood Monsters' script; not decoded | Lip-sync visemes |
| **Runaway 2** | Font atlases and UI art; 24-byte header, 312 slots | A scene archive, not a global one | Cinematic audio | Dialogue phrase tables | Lip-sync visemes |

Sample rates and channel counts are never stored in any of these games; the values above are the decoders'
documented choices, with the reasoning in [audio-video.md](audio-video.md).

## Reading these docs

Every document follows a uniform structure:

1. **Purpose** — what the format holds and where it appears in a game installation.
2. **Layout** — on-disk byte layout in reader order with C-like struct pseudocode.
3. **Semantics** — how to interpret the bytes: coordinate spaces, segment decoding, color channels, stride sweep autocorrelation, and blending rules.
4. **Known unknowns** — unverified fields or unresolved tables.
5. **Decoders & tests** — links to the implementing C# classes in `RunawayExplorer.Core` and their xUnit test suites.

## What the formats share

## Where this comes from

Everything here was derived by reading the shipped files, and every structural claim is checked against a
full install before it is written down. Two *Hollywood Monsters* sections also cite the ScummVM `hollywood`
engine, a separate and still unfinished reverse-engineering effort, where it names something the files
alone cannot reveal — the fixed chunk layout, the meaning of the region-map lookup pages, and the fact
that character palettes and alternate palettes are bound per scene by the executable. Those claims were
re-verified against the game's own data wherever the data can speak; where it cannot (the audio sample
rates), the disagreement is recorded rather than resolved.

Across 15 years of engine evolution (from *Hollywood Monsters* in 1997 to *Yesterday* in 2012), the engine maintained core design principles:

- **Nameless archives**: Files on disk are flat indexed containers without file names or extensions. Resources are referenced purely by entry index (`e00`, `e01`, …).
- **The offset-then-size table rule**: Scene archives place all absolute entry offsets in the first half of the table and all entry byte sizes in the second half. `table_bytes = Offset[0]`.
- **Absolute screen coordinates**: Sprite segments and overlay rows specify absolute screen coordinates `(X, Y)` rather than canvas-relative coordinates.
- **Independent frame composition**: Sprite animation frames are completely self-contained. No inter-frame delta accumulation or persistent state is used.
- **Scanline-bounded RLE**: Continuous RLE runs in scene masks sum exactly to the scene width on each row without wrapping across scanlines.
- **Structural identification**: An entry's kind is decided by walking its bytes exactly — a record stream that consumes the entry to the last byte, a size that is a whole number of screens — never by scoring the pixels. Where two formats can both satisfy such a walk, as 1- and 2-byte pixels can, the reader is told which game it is reading instead of guessing.

## A note on method

The single most expensive pitfall in reverse-engineering this engine was initially reading scene archive headers as a flat list of offsets instead of an offset-half followed by a size-half. Because entry 0 is a full-screen image starting right after the header, interpreting sizes as offsets created hundreds of plausible-looking, horizontally shifted fragments.

If a decoder needs a heuristic or shift table to find where an image starts, look again at the container.
