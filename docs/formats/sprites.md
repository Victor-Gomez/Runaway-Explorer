# Animated sprites

Animated characters, ambient animations (torches, water ripples, flags), and interactive objects are stored as segment-encoded multi-frame sprite assets.

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

## Evolution of segment formats

Across Pendulo Studios' releases, the segment header and pixel payload evolved to support higher color depths, alpha transparency, and wider spans:

| Game | Header size | Header fields | Pixel format | Color depth |
|---|---|---|---|---|
| **Runaway 1** | 5 bytes | `u16 X, u16 Y, u8 Count` | RGB565 | 16-bit (2 BPP) |
| **Runaway 2** | 6 bytes | `u16 X, u16 Y, u8 Flag, u8 Count` | RGB565 or RGB565+Alpha | 16-bit / 24-bit (2 or 3 BPP) |
| **Runaway 3** | 7 bytes | `u16 X, u16 Y, u8 Flag, u16 Count` | RGB565 / Alpha / RGBA | 16-bit to 32-bit |
| **TNBT / Yesterday** | 7 bytes | `u16 X, u16 Y, u8 Flag, u16 Count` | BGR24 (Flag 6) / RGBA (Flag 7) | 24-bit / 32-bit (3 or 4 BPP) |

### 1. Runaway 1 segment (5 bytes)

```text
u16 X                           -- screen column
u16 Y                           -- screen row
u8  Count                       -- pixel count (0 means 1 pixel)
u16 Pixels[Count]               -- little-endian RGB565 pixels (2 bytes each)
```

### 2. Runaway 2 segment (6 bytes)

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

### 3. Runaway 3, TNBT & Yesterday segment (7 bytes)

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
- Tests: [`SpriteDecoderTests`](../../tests/RunawayExplorer.Core.Tests/SpriteDecoderTests.cs), [`TnbtAndYesterdayTests`](../../tests/RunawayExplorer.Core.Tests/TnbtAndYesterdayTests.cs)
