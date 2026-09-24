# Pendulo Studios game engine file format reference

Notes on the reverse-engineered file formats used by Pendulo Studios across their adventure games:
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
| Entry 0 (`e00`) | Full-screen scene backgrounds — raw RGB565 rasters (stride sweep width recovery) and 1080p JPEG | All | [rasters.md](rasters.md) |
| Positioned row records & PNG | Overlays — foreground elements, props, inventory icons, and UI panels | All | [overlays.md](overlays.md) |
| Multi-frame segment streams | Animated sprites — frame bounding boxes, segment streams (R1 5-byte, R2 6-byte alpha, R3 7-byte, TNBT/Yesterday flags 6 & 7) | All | [sprites.md](sprites.md) |
| 3-byte / 4-byte RLE, Sparse, PNG | Scene masks — walkboxes, depth planes, occluders, and clickable hotspots | All | [masks.md](masks.md) |
| Raw PCM, MP3, Bink Video (`.bik`) | Audio & Video — synthesized WAV streams, MP3 audio, and XOR-restored Bink cutscenes | All | [audio-video.md](audio-video.md) |
| `RESOURCE.000` .. `RESOURCE.004` | Global data — font atlases (17-level alpha), UI sprites, character sprite library, phrase tables, and lip-sync visemes | R1–R3 | [global-data.md](global-data.md) |

## Reading these docs

Every document follows a uniform structure:

1. **Purpose** — what the format holds and where it appears in a game installation.
2. **Layout** — on-disk byte layout in reader order with C-like struct pseudocode.
3. **Semantics** — how to interpret the bytes: coordinate spaces, segment decoding, color channels, stride sweep autocorrelation, and blending rules.
4. **Known unknowns** — unverified fields or unresolved tables.
5. **Decoders & tests** — links to the implementing C# classes in `RunawayExplorer.Core` and their xUnit test suites.

## What the formats share

Across 11 years of engine evolution (from *Runaway 1* in 2001 to *Yesterday* in 2012), the engine maintained core design principles:

- **Nameless archives**: Files on disk are flat indexed containers without file names or extensions. Resources are referenced purely by entry index (`e00`, `e01`, …).
- **The offset-then-size table rule**: Scene archives place all absolute entry offsets in the first half of the table and all entry byte sizes in the second half. `table_bytes = Offset[0]`.
- **Absolute screen coordinates**: Sprite segments and overlay rows specify absolute screen coordinates `(X, Y)` rather than canvas-relative coordinates.
- **Independent frame composition**: Sprite animation frames are completely self-contained. No inter-frame delta accumulation or persistent state is used.
- **Scanline-bounded RLE**: Continuous RLE runs in scene masks sum exactly to the scene width on each row without wrapping across scanlines.

## A note on method

The single most expensive pitfall in reverse-engineering this engine was initially reading scene archive headers as a flat list of offsets instead of an offset-half followed by a size-half. Because entry 0 is a full-screen image starting right after the header, interpreting sizes as offsets created hundreds of plausible-looking, horizontally shifted fragments.

If a decoder needs a heuristic or shift table to find where an image starts, look again at the container.
