# Animated sprites

Animated characters, ambient animations (torches, water ripples, flags), and interactive objects are stored as segment-encoded multi-frame sprite assets.

They are not only a scene format: *Runaway 1*, *2* and *3* keep interface animations -- pointing hands,
menu widgets, and in the later two whole characters -- in the same shape inside `RESOURCE.000`, one asset
per slot rather than split over the header/data pair *Runaway 2*'s scenes use. See
[global-data.md](global-data.md).

## Purpose

The format achieves high compression and efficient DirectDraw/hardware rendering by storing only the lit pixels in each frame as a series of horizontal segments, coupled with per-frame bounding boxes.

## Container architecture

Every sprite asset consists of:
1. **Optional descriptor records**: Skipped header blocks on panoramic scenes.
2. **Record table**: 14 bytes per frame describing frame offsets and bounding boxes.
3. **Segment data payload**: The sequential pixel spans for each frame.

```text
[ Optional descriptor records (0 or more) ]
    u32 0
    u16 SceneWidth              -- 1372 .. 2048
    i16 -(SceneWidth - 1)
    u16 SceneHeight             -- 600 or 900
    u16 0, u16 0

Record Table (N frames * 14 bytes):
    u32 FrameOffset             -- offset to frame segments (see table below)
    u16 X0                      -- frame bounding box left column
    u16 W                       -- frame bounding box width
    u16 Y0                      -- frame bounding box top row
    u16 Y1                      -- frame bounding box bottom row (inclusive)
    u16 SegmentCount            -- number of segment records in this frame

Frame Segment Payloads:
    <frame 0 segments>
    <frame 1 segments>
    ...
```

### Frame offset semantics

- **Runaway 1 & 2**: `FrameOffset` is relative to the **end of the record table** (`DataOffset`). Frame 0 always starts with `FrameOffset == 0`.
- **Runaway 3**: `FrameOffset` is an **absolute offset** from the beginning of the file.

### Complete frames

**Every frame is completely self-contained.** There is no inter-frame delta encoding or canvas accumulation. Every segment specifies absolute screen coordinates `(X, Y)` and lies entirely inside the frame's bounding box `(X0, Y0, W, Y1 - Y0 + 1)`.

The animation's overall bounding box is the union of all individual frame bounding boxes:

```text
UnionBounds = union(FrameRecord[0].Box, ..., FrameRecord[N-1].Box)
```

### Blank frames

A record with `SegmentCount == 0` is a **legal blank frame**: a beat in the animation that draws
nothing. It still carries a bounding box (`W == 0` never occurs in *Runaway 1*), and it shares its
`FrameOffset` with the frame that follows it, because it contributes no payload.

Two consequences, and they are the easiest way to misread the format:

- A zero segment count is **not** a table terminator.
- Frame offsets are **non-decreasing**, not strictly increasing.

Treating either as impossible truncates the record table silently -- the frames that survive still
tile their region byte-exactly, so the asset looks valid and merely comes out short. Measured over
*Runaway 1*'s 74 scene archives: blank frames appear in **58 of the 438** sprite assets, 1,430 of
them in total, and the strict reading loses records in every one of those 58. The worst cases are
`RESOURCE.E07` entry 6 (945 records read as 2), `RESOURCE.I03` entries 7-11 (501 read as 1) and
`RESOURCE.E07` entry 7 (952 read as 434).

`SpriteDecoder.cs` gets both right; this section is here because nothing said so.

## Finding the record count

The record table has **no count field** and nothing before it says how long it is. The table is
recovered by reading records while they still look like records -- offset non-decreasing and below
the asset size, `X0 + W` and `Y1` inside a sane coordinate range, `Y0 <= Y1` -- and then checking
that the frames **tile the asset exactly**: frame *k* runs from `DataOffset + FrameOffset[k]` to
`DataOffset + FrameOffset[k+1]`, the last one to the end of the asset, and walking each frame's
segments must land on that boundary to the byte.

That byte-exact tiling is also what tells a sprite from any other entry in an archive, which is how
a decoder can probe a container whose slot contents are otherwise unlabelled. Over *Runaway 1* the
scan's first guess is right for all **438** assets -- 13 of them behind descriptor records -- with
no entry needing the count walked back and no false positive among the backgrounds, overlays, masks
or data tables.

The tiling check is not optional, and neither is reading the whole entry to do it. A cheap probe
that reads only the head of the record table -- say the first 64 KB -- and stops when a record
stops looking like one gets *Runaway 1* badly wrong in both directions: it calls **147** entries
sprites that are not, and misses **41** real ones whose record table alone is longer than the
probe. The false positives are the blank-frame rule biting back. An all zero 14-byte record is a
valid blank frame, so any stretch of zeroed bytes reads as an endless animation that draws
nothing; the 43,659-byte scene logic table opens with 23 zeroed 80-byte records and is classified
as a sprite every time. Exact tiling rejects all of them, with one degenerate case left over:
an entry of *N* x 14 zeroed bytes does tile, as *N* blank frames, so also require the asset's
bounding box to be non-empty.

## Evolution of segment formats

Across Pendulo Studios' releases, the segment header and pixel payload evolved to support higher color depths, alpha transparency, and wider spans:

| Game | Header size | Header fields | Pixel format | Color depth |
|---|---|---|---|---|
| **Hollywood Monsters** | 5 bytes | `u16 X, u16 Y, u8 Count` | Palette index | 8-bit (1 BPP) |
| **Runaway 1** | 5 bytes | `u16 X, u16 Y, u8 Count` | RGB565 | 16-bit (2 BPP) |
| **Runaway 2** | 6 bytes | `u16 X, u16 Y, u8 Flag, u8 Count` | RGB565 or RGB565+Alpha | 16-bit / 24-bit (2 or 3 BPP) |
| **Runaway 3** | 7 bytes | `u16 X, u16 Y, u8 Flag, u16 Count` | RGB565 / Alpha / RGBA | 16-bit to 32-bit |
| **TNBT / Yesterday** | 7 bytes | `u16 X, u16 Y, u8 Flag, u16 Count` | BGR24 (Flag 6) / RGBA (Flag 7) | 24-bit / 32-bit (3 or 4 BPP) |

### 1. Hollywood Monsters segment (5 bytes, indexed)

```text
u16 X                           -- screen column
u16 Y                           -- screen row
u8  Count                       -- pixel count (0 means 1 pixel)
u8  PaletteIndices[Count]       -- 8-bit palette indices (1 byte each)
```

The record table is *Runaway 1*'s, with two differences: there is no leading descriptor record (the first
record's frame offset is 0 and it carries real segments), and the pixel width is told to the parser rather
than inferred, because both 1 and 2 bytes per pixel can satisfy the byte-exact frame walk. The indices are
resolved through the scene's colour table ([palettes.md](palettes.md)); the characters live at the top of
that table, so a sprite decoded against the scene block alone comes out as a black silhouette.

### 2. Runaway 1 segment (5 bytes, RGB565)

> **Confirmed against the original loader.** *Runaway 1*'s `Runaway.exe` walks this structure at
> `0x436000`, stepping 14 bytes per frame record and reading the segment header exactly as described
> above. It then converts each pixel **RGB565 to RGB555** in place, `((p >> 1) & 0x7fe0) | (p & 0x1f)`,
> to suit the game's DirectDraw surface. The stored pixels are RGB565, and keeping them at 565 is more
> faithful than the original. See [executable.md](executable.md).

```text
u16 X                           -- screen column
u16 Y                           -- screen row
u8  Count                       -- pixel count (0 means 1 pixel)
u16 Pixels[Count]               -- little-endian RGB565 pixels (2 bytes each)
```

### 3. Runaway 2 segment (6 bytes)

Introduces an explicit `Flag` byte:
- `Flag == 0`: RGB565 opaque pixels (2 bytes/pixel).
- `Flag == 1`: RGB565 pixel followed by an 8-bit alpha channel byte (`u16 rgb565, u8 alpha`, 3 bytes/pixel).

```text
u16 X
u16 Y
u8  Flag                        -- 0 = opaque RGB565, 1 = RGB565 + alpha
u8  Count
Pixel data:
    if Flag == 0: u16 Pixels[Count]
    if Flag == 1: { u16 rgb565, u8 alpha }[Count]
```

### 4. Runaway 3, TNBT & Yesterday segment (7 bytes)

Upgrades the segment count from 8 bits to 16 bits (`u16 Count`), allowing long horizontal spans without segment fragmentation:

```text
u16 X
u16 Y
u8  Flag
u16 Count
Pixel data:
    Flag 0: u16 RGB565[Count]                (2 BPP)
    Flag 1: { u16 RGB565, u8 Alpha }[Count]  (3 BPP)
    Flag 6: { u8 B, u8 G, u8 R }[Count]      (3 BPP, 24-bit BGR truecolor)
    Flag 7: { u8 B, u8 G, u8 R, u8 A }[Count] (4 BPP, 32-bit RGBA)
```

#### Alpha blending (Flag 7)

Segments using Flag 7 are blended onto the target frame using standard straight alpha blending:

```text
outA = srcA + dstA * (1 - srcA)
outC = (srcC * srcA + dstC * dstA * (1 - srcA)) / outA
```

## Frame timing

Timing is **not stored** anywhere in the asset. Neither records nor segments specify duration. Playback in the explorer and exported animated PNGs (APNG) default to 12 or 15 frames per second, adjustable in Settings.

## Decoders

- [`SpriteAsset`](../../src/RunawayExplorer.Core/Formats/SpriteDecoder.cs)
- [`SpriteRecord`](../../src/RunawayExplorer.Core/Formats/SpriteDecoder.cs)
- [`SpriteSegment`](../../src/RunawayExplorer.Core/Formats/SpriteDecoder.cs)
- Tests: [`SpriteDecoderTests`](../../tests/RunawayExplorer.Core.Tests/ImageDecoderTests.cs), [`HollywoodMonstersTests`](../../tests/RunawayExplorer.Core.Tests/HollywoodMonstersTests.cs), [`YesterdayTests`](../../tests/RunawayExplorer.Core.Tests/YesterdayTests.cs)

---

## The character sprite libraries

*Runaway 1*'s characters are in neither the scene archives nor this format. They live in `Resource.001`
and in a second library inside `RESOURCE.000`, as palette-indexed run streams behind a separate 32-byte
frame record. Both are described in
[global-data.md](global-data.md#2-resource001--the-character-sprite-library-runaway-1).
