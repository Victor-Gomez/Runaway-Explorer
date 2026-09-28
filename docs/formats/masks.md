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

Five of the six are established, every one of them by rendering a scene flat by the page and
comparing it against the painting -- see the note below the table on why the statistics found
none of them. Figures are pooled over the 55 *Runaway 1* scenes that carry both a mask and a
table, counting only the ids a scene's mask actually paints:

| Page | Meaning | Evidence |
|---|---|---|
| 0 | **Walkable region**, 0 = not walkable | Set on 45% of a scene's ids, ~4 regions per scene. Tinting lights the ground and nothing else. It numbers the regions; it does **not** sort them front to back -- see the note below the table. |
| 1 | **Scene item / hotspot index**, 0 = nothing | Set on 54% of ids, ~10 items per scene, and groups of zone ids collapse onto one item number, which is how one object owns several zones. In F13 it lights exactly the door, the well, the skull on the stick and the exit strip; probing the picture returns item 1 for the exit, 2 for the door, 3 for the well, 5 for the skull. |
| 2 | **Depth plane**, larger = nearer, 0 = furthest | The sparse page: set on only 19% of a scene's ids, ~3 values. An actor takes the value of the zone under its feet, and every pixel with a greater value is drawn over it. In F13 the page marks exactly the totem pole (2), the skull on the stick (2) and the well with its frame and bucket (1) -- the three walk-behind objects -- and nothing else. In G04 it marks the near rock wall at the cave mouth (1) and the foreground rock and bushes (2). |
| 3 | **Actor brightness class**, 0..6 | Dense (91% of ids) and unrelated to screen row, because it is a lighting *field* rather than an object map. In B02, the darkened hospital room, the whole scene is one value except a single quadrilateral lying exactly on the pool of moonlight the window throws on the floor. In E03 the page is concentric ellipses centred on the floor -- a lamp's falloff quantised into bands. Neither follows the scenery. The *Hollywood Monsters* page indexes signed deltas added to the actor's palette; *Runaway 1* is 16-bit colour, so how the class becomes a brightness is still open. |
| 4 | **Not a page. Never read it.** | The entry is 1,536 bytes for six pages but only five are written; this slot keeps whatever was in the exporter's buffer -- see the note below the table. |
| 5 | **Footstep surface material**, 0..7 | Dense (98% of ids) and constant across the whole scene in 40 of 56 scenes, which is what one floor material looks like. Where a scene has two, the boundary is the material's edge in the painting and not an object: in F18 the dirt street is 2 and the wooden boardwalk is 3, and the border traces the front edge of the planks. The per-scene sets group by location -- the desert scenes E02, E05, E07 and E08 all use 2 alone. In *Hollywood Monsters* the same page indexes the scene's resident sound effects, played on the walk cycle's two footfall cels. |

#### These pages do not rank with screen row

Rank correlation is the wrong tool for finding the depth page, and it is what kept page 2
unidentified. Page 2 is not a y-sort: an occluder's plane says which scenery an actor passes
behind, not how far down the picture it sits, so a pole at the back of the scene and a rock in
the foreground can share a value. Over the 114 tables the best page scores 59.6%, which is
noise. Rendering a scene tinted by the page settles it in one look.

The same mistake is why page 0 was described here as ranking with the zone's mean screen row
76% of the time. That figure counted the ids the page leaves at zero -- the sky and the walls,
which sit at the top of the picture, so they drag the correlation up for free. Restricted to
the ids page 0 actually sets, it is **40.3%**, i.e. slightly *inverted*. Any test of a page
has to exclude the ids the page does not set.

Page 4 was described here as using the whole byte range and repeating "with a period of four in
the zone id". The period claim was one scene read by eye and is wrong -- measured over used
ids it holds in 0 of 56 scenes. The range claim was right but means the opposite of what it
looked like.

#### Page 4 is not a page

The other five are zero for every id a scene never paints, and their last nonzero entry stops at
the scene's highest live id. Page 4 is nonzero on **64%** of the dead ids and runs to entry
251-255 in all 64 tables, whether the scene uses 1 id or 63. It is a function of no other page
(4-13% of scenes), correlates with nothing geometric (51.7% against screen row, i.e. noise), is
never constant, and read as 4-byte little-endian words it holds recognisable 32-bit Windows
addresses -- `0x0012Fxxx` stack, `0x004xxxxx` image, `0x77Dxxxxx` system DLL -- with 36% of its
nonzero words falling in those ranges.

So the exporter allocates 1,536 bytes for six pages and writes only five, leaving whatever was
in the buffer. The contents are per-file rather than random (the only duplicate page 4s across
the game are the mirrored chapter pairs `F13`/`h13`, `F18`/`h18`, `F26`/`H26`), which just means
it was written once at export time. The *Hollywood Monsters* page at the same offset is the
palette recolouring class, which a 16-bit-colour game has no use for, so the slot kept the
layout and was never filled in.

The earlier `DepthPlane` / `MaterialId` / `ScriptAction` labels came from the record-major
reading. Page 2 turns out to carry what `DepthPlane` claimed and page 5 roughly what
`MaterialId` claimed, but both were checked against the data before being named here;
`ScriptAction` was pointing at the unwritten slot.

The explorer reads this table to allow filtering masks by individual functional layers (`Walk`, `Hotspot`, `Depth`, `Light`, `Material`, `Occluder`). Page 4 is not offered as a layer, because it holds nothing.

## Palette generation

Because IDs are discrete integer indices, the explorer assigns distinct high-contrast colors using golden-angle hue distribution:

$$\text{Hue}(id) = (id \times 137.508^\circ) \bmod 360^\circ$$

Masks can be previewed standalone or composited at 55% opacity over the scene background art (`e00`).

## Decoders

- [`RleMaskDecoder`](../../src/RunawayExplorer.Core/Formats/RleMaskDecoder.cs)
- [`SparseMaskDecoder`](../../src/RunawayExplorer.Core/Formats/SparseMaskDecoder.cs)
- [`PngDecoder`](../../src/RunawayExplorer.Core/Formats/PngDecoder.cs)
- Tests: [`RleMaskDecoderTests`](../../tests/RunawayExplorer.Core.Tests/RleMaskDecoderTests.cs), [`SpanMaskDecoderTests`](../../tests/RunawayExplorer.Core.Tests/SpanMaskDecoderTests.cs), [`HollywoodMonstersTests`](../../tests/RunawayExplorer.Core.Tests/HollywoodMonstersTests.cs)
