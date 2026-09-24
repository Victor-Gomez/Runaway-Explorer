# Overlays

Overlays represent non-fullscreen scene artwork: foreground scenery elements, interactive props, doors, inventory icons, and UI dialog elements placed at specific screen coordinates over the scene background.

## Purpose

Rather than storing a complete full-screen image for every visual change in a scene, the engine stores positioned cutout graphics. Transparent areas consume no memory or blit bandwidth.

## Layouts

### 1. Positioned row-record overlays (*Runaway 1–3*)

Stored as an array of horizontal spans with screen coordinates and RGB565 pixel payloads.

#### Layout

```text
Header:
    u16 RecordCount             -- number of span records (never 0)

Records (RecordCount records):
    u16 X                       -- screen column of the first pixel
    u16 Y                       -- screen row of the span
    u16 PixelCount              -- number of pixels in this row (never 0)
    u16 Pixels[PixelCount]      -- little-endian RGB565 values
```

The records must consume the entry data exactly. If extra trailing bytes exist or if the record stream terminates prematurely, the entry is not a valid overlay.

#### Span types

- **Rectangular overlays**: One span per scanline with constant `X` and `PixelCount` (e.g. title cards, inventory cards, inset panels).
- **Sparse overlays**: Multiple spans per row with varying `X` and gaps, representing irregular foreground artwork such as lamps, trees, or characters.

#### Coordinates and blitting

`(X, Y)` represent absolute screen coordinates:
- In *Runaway 1*, positions are mapped onto a 1024×600 canvas (with occasional coordinates up to the scene width on scrolling screens).
- In *Runaway 2* and *3*, positions scale with widescreen canvas dimensions (e.g. 1280×720).
- Transparent pixels outside the spans are not stored; decoding starts with a fully transparent BGRA canvas.

### 2. PNG overlays (*The Next BIG Thing*, *Yesterday*)

In *The Next BIG Thing* and *Yesterday*, props and foreground overlays are stored as standard PNG images directly within the scene archive.

#### Layout

```text
0x00: 89 50 4E 47 0D 0A 1A 0A   -- Standard PNG magic signature
...
IHDR chunk:
    u32 Width
    u32 Height
    u8  BitDepth
    u8  ColorType               -- 6 (RGBA) or 2 (RGB)
...
IDAT chunk                      -- Compressed pixel data
...
IEND chunk                      -- Chunk terminator
```

RGBA PNGs (ColorType 6) contain per-pixel alpha channels allowing smooth antialiased blending over background scenery.

## Decoders

- [`OverlayDecoder`](../../src/RunawayExplorer.Core/Formats/OverlayDecoder.cs)
- [`PngDecoder`](../../src/RunawayExplorer.Core/Formats/PngDecoder.cs)
- Tests: [`OverlayDecoderTests`](../../tests/RunawayExplorer.Core.Tests/OverlayDecoderTests.cs), [`TnbtAndYesterdayTests`](../../tests/RunawayExplorer.Core.Tests/TnbtAndYesterdayTests.cs)
