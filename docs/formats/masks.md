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

#### Scanline bounding rule, and where it does not hold

Runs are often **scanline-bounded**: the sum of run lengths in any row equals the scene width
(e.g. 1024 or 1280) with zero remainder, and no run wraps across a scanline.

*Runaway 1* mixes both encodings, freely, within the same game. Of its 56 scene masks, **34 are
scanline-bounded and 21 are continuous** -- their runs wrap onto the next row and only the grand
total is meaningful. (One more, `RESOURCE.F27` entry 1, is neither: its runs total 716,800 px
against a 1024x600 background, and it has not been explained.) So the scanline rule cannot be
used to *recognise* a *Runaway 1* mask; it misses more than a third of them.

Two practical consequences:

- **Decode continuously always.** Fill the id map in reading order and let runs wrap. A mask that
  does respect scanlines decodes identically that way, because its runs land on the row
  boundaries of their own accord. There is no need to detect which kind you have.
- **Detect by the pixel total.** The test that holds for every mask in the game is that the run
  lengths sum to exactly `width * height`, with no zero-length run. That is also enough to tell a
  mask from the sprite or raster that sits in the same slot in the archives that carry no mask.

The mask, where a scene has one, is entry 1 and the attribute table entry 2, in every *Runaway 1*
archive that has them. 18 archives have neither -- their entry 1 is a sprite -- and those are the
cutscene and close-up archives.

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

In *Runaway 1* and *2*, scene archives contain a recurring 1,536-byte data entry: entry 2, beside
the mask at entry 1. It is **six 256-byte pages**, one page per attribute, indexed

```text
table[page * 256 + id]        -- page 0..5, id 0..255
```

which is the same shape as the *Hollywood Monsters* seven-page table above, one page shorter.
`RleMaskDecoder` reads it this way.

> This section previously described the table as 256 records of 6 bytes (`{u8 WalkFlag, u8
> HotspotFlag, u8 DepthPlane, u8 MaterialId, u16 ScriptAction}`), which is wrong, and disagreed
> with the decoder beside it. Pooling all 114 tables in *Runaway 1* settles it: read as pages the
> fields come out tightly bounded (maxima 19, 28, 9, 9, 255 and 7), read as records all six smear
> across the full byte range, which is what slicing across a structure looks like.

Two pages are established, by tinting a scene by each page in turn and looking at the result
(`RESOURCE.F13`, Mama Dorita's, 31 zone ids). Figures are pooled over the 55 *Runaway 1* scenes
that carry both a mask and a table:

| Page | Meaning | Evidence |
|---|---|---|
| 0 | **Walkable region**, 0 = not walkable | Set on 45% of a scene's ids, ~4 regions per scene. Its value ranks with the zone's mean screen row 76% of the time, i.e. the floor is cut into bands front to back. Tinting lights the ground and nothing else. |
| 1 | **Scene item / hotspot index**, 0 = nothing | Set on 54% of ids, ~10 items per scene, and groups of zone ids collapse onto one item number, which is how one object owns several zones. In F13 it lights exactly the door, the well, the skull on the stick and the exit strip; probing the picture returns item 1 for the exit, 2 for the door, 3 for the well, 5 for the skull. |
| 2 | *unidentified* -- plausibly the walk-behind occluders | The sparse page: set on only 19% of a scene's ids, ~3 values. |
| 3 | *unidentified* | Dense (91% of ids), values 1..6, no relation to screen row. |
| 4 | *unidentified* | The only page using the whole byte range: 142 distinct values, 74% of ids set. In F13 the values repeat with a period of four in the zone id, so it is probably not a small class number like the others. |
| 5 | *unidentified* -- plausibly the footstep material | Dense, values 0..7, and constant across every zone of a scene in the scenes checked (2 everywhere in F13). That is what the equivalent *Hollywood Monsters* page means, but one scene of dirt is not enough to call it. |

Pages 2, 3, 4 and 5 are named by position rather than by guess on purpose. The earlier
`DepthPlane` / `MaterialId` / `ScriptAction` labels came from the record-major reading and have
never been checked against the data.

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
