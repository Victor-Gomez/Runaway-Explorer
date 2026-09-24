# Full-screen rasters and backgrounds

The game engine displays backgrounds for rooms, exterior vistas, panoramic scrollers, and cutscenes using either uncompressed RGB565 bitmaps or baseline JPEG images.

## Purpose

Entry 0 (`e00`) in almost every scene archive holds the room's background image. In *Runaway 1*, *Runaway 2*, and *Runaway 3*, these are stored as raw RGB565 rasters without headers. In *The Next BIG Thing* and *Yesterday*, backgrounds are stored as 1080p JPEG images.

## Layouts

### 1. Raw RGB565 rasters (*Runaway 1–3*)

Raw rasters contain no header, no magic number, and no dimension metadata:

```text
Payload:
    u16 Pixel[Width * Height]   -- Little-endian RGB565 pixels, row-major
```

Total byte size is exactly:

```text
byte_size = Width * Height * 2
```

Pixel components in 16-bit RGB565:

```text
Bits:
    15 14 13 12 11   10 09 08 07 06 05   04 03 02 01 00
    [   Red (5)  ]   [   Green (6)   ]   [  Blue (5)  ]
```

Conversion to 8-bit BGRA:

```csharp
byte r = (byte)((pixel >> 11) << 3);
byte g = (byte)(((pixel >> 5) & 0x3F) << 2);
byte b = (byte)((pixel & 0x1F) << 3);
byte a = 255;
```

#### Width recovery via stride sweep

Because width is not stored, the decoder determines the correct image width by measuring vertical autocorrelation on the green channel across candidate strides. The green channel has 6 bits of precision and exhibits the strongest spatial coherence in natural art.

For each candidate stride $W$ that divides the total pixel count:

$$\text{Score}(W) = \frac{1}{N} \sum_{i} |G[i] - G[i + W]|$$

The true stride produces a sharp global minimum in the vertical difference metric. The search performs:
1. **Coarse pass**: Checks candidate strides across three evenly distributed sample chunks.
2. **Fine re-score**: Validates candidate strides against edge artifacts, ensuring exact division of the pixel buffer.

Common scene dimensions across the games:
- **Standard**: 1024×600 (*Runaway 1*), 1280×720 (*Runaway 3*), 1920×1080 (*TNBT*, *Yesterday*).
- **Horizontal scrollers**: 1372×600, 1600×600, 2048×600, 2592×600, 3016×770.
- **Tall / vertical scrollers**: 1024×2062 (credits / vertical towers).
- **Insets / cutaways**: 1444×800, 282×188, 204×120.

### 2. Indexed rasters (*Hollywood Monsters*)

Backgrounds in *Hollywood Monsters* are 8-bit palette-indexed bitmaps. Nothing in the entry records the
geometry, so the byte count alone identifies one:

- Dimensions: exactly **1024 × 480** pixels (`RasterDecoder.IndexedScreenWidth` × `IndexedScreenHeight`).
  The game's display is only **640 × 480**: a background is a wide backdrop that the engine scrolls
  horizontally behind a 640-pixel window, which is why sprite coordinates routinely exceed 640.
- Byte size: exactly 491,520 bytes (`1024 * 480 * 1`), or a whole multiple of it for the taller scrolling screens.
- Each byte is an index (0–255) into the scene's colour table; see [palettes.md](palettes.md).

There is no stride sweep here and no sharpness score to compute: a size that is not a whole number of
full screens is not a raster, and nothing else in the archive is that size.

### 3. JPEG backgrounds (*The Next BIG Thing*, *Yesterday*)

Starting with *The Next BIG Thing*, high-definition 1920×1080 painted backgrounds are stored as standard baseline JPEG images directly within the scene archive.

#### Layout

```text
0x00: 0xFF 0xD8                 -- Start of Image (SOI)
0x02: 0xFF 0xE0                 -- JFIF APP0 marker (or standard EXIF/JPEG segments)
...
SOF0 (0xFF 0xC0 / 0xC2):
    u16 Length
    u8  Precision
    u16 Height                  -- 1080
    u16 Width                   -- 1920
    u8  Components              -- 3 (YCbCr)
...
0xFF 0xD9                       -- End of Image (EOI)
```

The decoder inspects the first two bytes for `0xFF 0xD8` and retrieves width and height directly from the Start of Frame (SOF) marker before decoding to a 32-bit BGRA buffer.

## Known unknowns

- A small number of medium-sized rasters (e.g. 1444×800) have no matching camera views in game scripts.

## Decoders

- [`RasterDecoder`](../../src/RunawayExplorer.Core/Formats/RasterDecoder.cs)
- [`JpegDecoder`](../../src/RunawayExplorer.Core/Formats/JpegDecoder.cs)
- Tests: [`ImageDecoderTests`](../../tests/RunawayExplorer.Core.Tests/ImageDecoderTests.cs), [`HollywoodMonstersTests`](../../tests/RunawayExplorer.Core.Tests/HollywoodMonstersTests.cs), [`TheNextBigThingTests`](../../tests/RunawayExplorer.Core.Tests/TheNextBigThingTests.cs), [`YesterdayTests`](../../tests/RunawayExplorer.Core.Tests/YesterdayTests.cs)
