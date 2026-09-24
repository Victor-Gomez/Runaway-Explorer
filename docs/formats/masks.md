# Scene masks and walkboxes

Scene masks partition backgrounds into interactive zones, walkboxes (character pathfinding meshes), depth planes (walk-behind occluders), and material/acoustics boundaries.

## Purpose

Entry 1 (`e01`) and occasional secondary entries define spatial properties for scenes. Rather than testing polygon geometry, the engine samples these pixel maps at runtime to determine:
- If a mouse click hit a clickable item/hotspot (`Hotspot` layer).
- If a character can walk on a specific floor point (`Walk` layer).
- Which foreground elements occlude a character (`Depth` / `Occluder` layer).
- What footsteps sound to trigger (`Material` layer).

## Encodings

### 1. 3-byte continuous RLE (*Hollywood Monsters*, *Runaway 1–3*)

A flat stream of 3-byte run-length encoded records:

```text
N × {
    u8  Id                      -- zone identifier (0–255)
    u16 Length                  -- run length in pixels (little-endian)
}
```

#### Scanline bounding rule

Runs are **scanline-bounded**: the sum of run lengths in any row equals the scene width (e.g. 1024 or 1280) with zero remainder. Runs do not wrap across scanlines.

#### Ambiguity with palette blocks (*Hollywood Monsters*)

A *Hollywood Monsters* palette block is a whole number of 3-byte records too, and its bytes can satisfy the
scanline rule, so the classifier tests for a palette block ([palettes.md](palettes.md)) **before** it tests
for a mask. With that order reversed, a scene's colour table is read as a garbage mask.

#### What the ids mean (*Hollywood Monsters*)

In *Hollywood Monsters* the run stream is not a mask per zone type but a single **region map**: entry
`e02` expands to one byte for every pixel of the 1024×480 screen, and that byte is an index into seven
256-entry lookup tables packed into entry `e03` (1,792 bytes, `7 × 256`):

| Offset | Page | Meaning of `table[id]` |
|---|---|---|
| `0x000` | Region map | Walkable-region number; a value above the scene's maximum is not walkable |
| `0x100` | Colour -> item | Scene item (hotspot) index under the cursor, 0 for none |
| `0x200` | Colour -> actor depth class | Whether the actor draws in front of or behind scenery there |
| `0x300` | Colour -> palette delta class | Selects a brightness delta for the actor's colours |
| `0x400` | Colour -> palette adjustment class | Selects a recolouring set for the actor's colours |
| `0x500` | Colour -> footstep sound | Surface material under the actor's feet |
| `0x600` | Presentation palette remap | Index remap applied when a presentation replaces the scene |

So one map serves hotspots, walkability, depth sorting, footstep audio and the actor's lighting at once;
the layers the explorer lets you filter are views of the same pixels through different pages. This also
explains why the run values cluster in a narrow range: they are region ids, not colour indices, and they
are unrelated to the scene palette.

The 1,536-byte attribute table described below is the *Runaway 1*/*2* equivalent of these pages.

### 2. 4-byte RLE (*Yesterday*)

In *Yesterday*, high-resolution 1920×1080 masks use 4-byte records:

```text
N × {
    u16 Id                      -- 16-bit zone identifier
    u16 Length                  -- run length in pixels (summing to 1920 per row)
}
```

### 3. Sparse masks (*Runaway 3*)

Used for intricate depth occluders and antialiased silhouette edges:

```text
Header:
    u16 RunCount                -- number of span records

Runs (RunCount records):
    u16 X                       -- screen column
    u16 Y                       -- screen row
    u8  Flag                    -- 2 = solid run, 4 = antialiased run
    u16 Count                   -- number of pixels in the span
    [u8 Alpha[Count] if Flag == 4]  -- per-pixel alpha bytes for antialiased edges
```

- **Flag 2**: Solid run of `Count` pixels with `Alpha = 255`.
- **Flag 4**: Variable transparency run where each pixel has an explicit 8-bit alpha/weight value.

### 4. Grayscale PNG masks (*The Next BIG Thing*, *Yesterday*)

In *Yesterday*, several scene archives (such as `RESOURCE.C02\e04`, `RESOURCE.D02\e04-06`) store mask layers as standard 8-bit or 24-bit PNG images. The grayscale luminance value of each pixel corresponds directly to the functional zone identifier.

## Zone attribute table (`1536 bytes`)

In *Runaway 1* and *2*, scene archives contain a recurring 1,536-byte data entry. This table consists of 256 records of 6 bytes each:

```text
Table:
    Record[256] × {
        u8 WalkFlag             -- 1 if walkable
        u8 HotspotFlag          -- 1 if interactive
        u8 DepthPlane           -- z-order depth sort key
        u8 MaterialId           -- surface acoustics (stone, wood, carpet, metal)
        u16 ScriptAction        -- associated trigger index
    }
```

The explorer reads this table to allow filtering masks by individual functional layers (`Walk`, `Hotspot`, `Depth`, `Material`, `Occluder`).

## Palette generation

Because IDs are discrete integer indices, the explorer assigns distinct high-contrast colors using golden-angle hue distribution:

$$\text{Hue}(id) = (id \times 137.508^\circ) \bmod 360^\circ$$

Masks can be previewed standalone or composited at 55% opacity over the scene background art (`e00`).

## Decoders

- [`RleMaskDecoder`](../../src/RunawayExplorer.Core/Formats/RleMaskDecoder.cs)
- [`SparseMaskDecoder`](../../src/RunawayExplorer.Core/Formats/SparseMaskDecoder.cs)
- [`PngDecoder`](../../src/RunawayExplorer.Core/Formats/PngDecoder.cs)
- Tests: [`RleMaskDecoderTests`](../../tests/RunawayExplorer.Core.Tests/RleMaskDecoderTests.cs), [`SpanMaskDecoderTests`](../../tests/RunawayExplorer.Core.Tests/SpanMaskDecoderTests.cs), [`HollywoodMonstersTests`](../../tests/RunawayExplorer.Core.Tests/HollywoodMonstersTests.cs)
