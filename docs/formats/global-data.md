# Global and UI data

Shared global assets — fonts, cursor art, interface icons, phrase lookup tables, lip-sync visemes, and the character sprite library — reside in numbered global resources (`RESOURCE.000` through `RESOURCE.004`).

## Containers and formats

### 1. `RESOURCE.000` — UI atlas and font assets

Contains global textures and localized typography.

#### Layout

```text
0x000: byte[20] Header          -- begins: 03 01 06 01 01 01 ...
0x014: u32[500] Offsets         -- absolute byte offsets
0x7F4: u32[500] Sizes           -- byte sizes
0xFB4: <entry data>
```

#### Known payload offsets (*Runaway 1*)

| Offset | Size | Purpose | Encoding |
|---|---|---|---|
| `0x3D7CF4C` | 49 KB | Regular font atlas (~184 glyphs) | 17-level alpha RLE |
| `0x56BD07C` | 39 KB | Bold font atlas (~182 glyphs) | 17-level alpha RLE |
| `0x3D898F4` | ~1.5 MB | UI sprite atlas (700 px wide) | Raw RGB565 |
| `0x3D7FA41`… | ~46 MB | Localized text bitmap region | Font RLE strings |

#### Font RLE encoding

Glyphs and pre-rendered string bitmaps use a dedicated run-length encoding with 17 alpha shades:

```text
byte 0x00 .. 0x10: Pixel with alpha value (0x00 = transparent, 0x10 = opaque)
byte 0x11:         End of scanline
byte 0x12:         Background fill pixel
```

### 2. `Resource.001` — character sprite library

A 108 MB global repository of character motion cycles (e.g. the 2,148-frame walk and gesture cycle for Brian Basco).

- Header: Two 864-byte lookup tables loaded at startup.
- Offsets cluster in 16 MB memory banks.
- Format: Packed 8-bit character sprite spans:
  ```text
  N × {
      u16 X
      u16 Y
      u16 Count
      u8  Value[Count]
  }
  ```
  New frames are signaled when `Y` decreases.

### 3. `RESOURCE.003` — dialogue phrase tables

Stores dialogue tree node structures and audio clip references.

#### Layout

```text
0x000: byte[321] DeltaTemplate  -- base template for dialogue state reconstruction
0x141: u32[1021] SceneOffsets   -- indexed by (SceneId - 1000) / 10
...
Per-scene block:
    5-byte phrase records: { u16 Marker, u8 Mid, u16 Tail }
    Marker == 0xFFFF denotes an empty or unpopulated dialogue slot.
```

The game executable references the string `"PHRASE NOT FOUND"` when attempting to read an empty marker.

### 4. `RESOURCE.004` — lip-sync visemes

Encodes mouth posture keyframes synchronized with spoken voice lines.

#### Layout

```text
0x0000: { u32 Offset, u16 Size }[6000]  -- 36,000-byte index table
0x8CA0: <viseme stream data>
```

Each entry is a stream of bytes with values from `0` to `5`, corresponding to six phoneme mouth shapes (closed, slightly open, wide, round, teeth, open wide). Clip `k` pairs with dialogue audio clip `k`.

## Decoders

- [`PhraseArchive`](../../src/RunawayExplorer.Core/FileSystem/PhraseArchive.cs)
- [`VisemeArchive`](../../src/RunawayExplorer.Core/FileSystem/Archives.cs)
- [`GlobalArchive`](../../src/RunawayExplorer.Core/FileSystem/Archives.cs)
- Tests: [`ArchivesTests`](../../tests/RunawayExplorer.Core.Tests/ArchivesTests.cs)
