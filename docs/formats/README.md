# Runaway: A Road Adventure — file formats

Pendulo Studios, 2001. PC, Windows, DirectDraw. Everything below was
reverse-engineered from the Steam re-release; nothing is official.

All integers are **little-endian**. `u16`/`u32` unsigned, `i16` signed.
Offsets are absolute file positions unless stated otherwise.

Each section says how it was verified. "**Verified**" means a decoder
reproduces the data byte-exact or the game's own behaviour; "**from
notes**" means it is documented in `runaway.md` from earlier work and is
believed correct but was not re-checked for this document; "**open**"
means unknown.

The decoders in [`src/RunawayExplorer.Core/`](../../src/RunawayExplorer.Core/)
implement everything marked verified: `FileSystem/Archives.cs` (§2.1, 2.2,
2.3, 2.6, 2.7), `FileSystem/VideoKeyfile.cs` (§2.8), `Formats/RasterDecoder.cs`
(§3.1), `Formats/OverlayDecoder.cs` (§3.2), `Formats/SpriteDecoder.cs` (§3.3),
`Formats/RleMaskDecoder.cs` (§3.4) and `Formats/WavWriter.cs` (§4). The tests under `tests/RunawayExplorer.Core.Tests/`
build synthetic entries from these descriptions and check the decoders against
them, and the decoders were cross-checked entry for entry against the original
Python exporter on a full install (1,062 of 1,062 entries agree; all 40,276
sprite frames walk byte-exact).

---

## 1. File map

| Path | Contents | Section |
|---|---|---|
| `Resource/RESOURCE.<L><nn>` | One scene each: backgrounds, scene masks, overlays, sprite animations, scene data | §2.1, §3 |
| `Resource/RESOURCE.M<nn>` | Music, 16 kHz stereo PCM | §2.2, §4.1 |
| `Resource/RESOURCE.S<nn>` | Ambient / SFX, 22 kHz stereo PCM | §2.2, §4.1 |
| `Resource/RESOURCE.000` | Global data: fonts, UI atlas, localised bitmaps, misc tables | §2.3, §3.5–3.7 |
| `Resource/Resource.001` | Character sprite library (different codec) | §2.4, §3.5 |
| `Resource/RESOURCE.002` | Cinematic audio, 22 kHz stereo PCM | §2.2, §4.1 |
| `Resource/RESOURCE.003` | Per-scene phrase / dialogue lookup tables | §2.5 |
| `Resource/RESOURCE.004` | Lip-sync viseme tracks | §2.6 |
| `Dataa/DATAACA<0-6>.000` | Voice lines, 8-bit mono PCM, sharded | §2.7, §4.2 |
| `Datav/DATAV*.<nnn>` | Bink video with the first 1024 bytes removed | §2.8, §4.3 |
| `Datav/DATAVC00.<nnn>` | The removed video headers, XOR-chained | §2.8 |

`<L>` is a scene-area letter A–I; `<nn>` a two-digit number. There are 74
scene archives. Case varies on disk (`RESOURCE.H09` vs `Resource.h13`);
treat names case-insensitively.

---

## 2. Containers

### 2.1 Scene archives — `RESOURCE.<L><nn>` (**verified**)

```
offset 0        u32 offset[N/2]      absolute file offsets, unused = 0
offset N*2      u32 size[N/2]        byte size of the matching entry
offset N*4      entry data...
```

`N` is derived from the first word: `offset[0]` is where entry 0 begins,
which is immediately after the table, so **`table_len = offset[0]`** and
`N = table_len / 4`. 73 archives have `table_len = 320` (40 pairs); one
has 400 (50 pairs). Between 9 and 35 pairs are populated per archive.

```python
tbl  = u32_at(0)
half = tbl // 8
offs  = u32[half] at 0
sizes = u32[half] at half*4
entries = [(offs[i], sizes[i]) for i in range(half) if offs[i] and sizes[i]]
```

`size[i]` equals the gap to the next-highest offset for every populated
entry in every archive — the sizes are exact, not padded.

> **Do not read the table as a flat list of offsets.** That misreads every
> size value as a pointer into the middle of another entry. Because entry
> 0 is a 1024-wide raster with 2048-byte rows starting right after the
> table, those phantom "entries" decode as plausible-looking, mis-aligned
> fragments of real images. This cost the project a long detour chasing a
> "horizontal shift" that does not exist. Both earlier extractors made
> this mistake in different ways.

Entry 0 is always the scene's main 1024×600 background (§3.1). What
follows varies: more rasters, overlays (§3.2), sprite animations (§3.3),
and non-image data. Three data entries recur:

| size | present in | contents |
|---|---|---|
| 1536 | every archive (66 distinct) | per-scene table, mostly zero — **open** |
| 43659 | 66 archives (43 distinct) | per-scene table — **open** |
| 704 | every archive, **byte-identical** everywhere | u16 pairs, looks like coordinates — **open** |

### 2.2 Audio archives — `RESOURCE.M<nn>`, `S<nn>`, `002` (**verified**)

Plain offset table, no size half:

```
offset 0        u32 offset[N]        absolute; slot 0 is always empty (0)
offset N*4      entry data...
```

Because slot 0 is zero, `table_len` is the **smallest non-zero** word in
the table. Entry size is the gap to the next-highest offset. `M`: 100
slots. `S`: 1000 slots. `002`: 100 slots, several entries alias the same
offset (deduplicate on extraction).

### 2.3 `RESOURCE.000` (**from notes**, loader decompiled at RVA 0x0e060)

```
0x000   byte[20]    header, starts 03 01 06 01 01 01 ...
0x014   u32[500]    offsets
0x7F4   u32[500]    sizes
0xFB4   entry data
```

154 of 500 slots are populated. Most entries are 0.8–1 MB. Known fixed
contents (offsets are absolute):

| offset | contents | section |
|---|---|---|
| `0x3d7cf4c` | regular font atlas, ~184 glyphs, 49 KB | §3.6 |
| `0x56bd07c` | bold font atlas, ~182 glyphs, 39 KB | §3.6 |
| `0x3d898f4` | UI sprite atlas, 700 px wide RGB565 | §3.7 |
| `0x3d7fa41` … | ~46 MB language-varying region: pre-rendered localised bitmaps in the font RLE format | §3.6 |

### 2.4 `Resource.001` (**from notes**)

108 MB character-sprite library. Two 864-byte tables are loaded at
startup (RVA 0x0e060 reads `0x360` bytes twice). Entry data uses the
8-bit sprite codec of §3.5. Offsets cluster in 16 MB banks. Asset 402 is
a 2,148-frame walk cycle for the player character.

### 2.5 `RESOURCE.003` — phrase tables (**from notes, partial**)

Not an offset-table archive. Header = a 321-byte delta template followed
by a 4084-byte offset table indexed by `(scene_id − 1000) / 10`; only ~94
of 301 non-zero slots are valid. Per scene: 5-byte phrase records
`{u16 marker, u8 mid, u16 tail}` where `marker == 0xFFFF` means empty,
then counts and 41-byte / 321-byte delta-coded records. About 55 scenes
decode usably. The exe's "PHRASE NOT FOUND" string is the fallback for an
empty marker.

### 2.6 `RESOURCE.004` — visemes (**verified**)

```
0x0000   { u32 offset, u16 size }[6000]     36000-byte catalogue
0x8CA0   data
```

Each entry is a byte sequence with values 0–5: one mouth shape per
animation tick, six shapes total. Entry `k` pairs with voice clip `k`.

### 2.7 `Dataa/DATAACA<0-6>.000` — voice (**verified**)

Seven independent archives, each a plain offset table of 12,000 slots
(48,000 bytes). A clip index may appear in more than one shard; an offset
≥ that shard's file size is a reference to another shard and is skipped.
About 11,000 of 12,000 indices are populated somewhere. Payload is raw
8-bit unsigned mono PCM; the rate is not stored -- 16,000 Hz sounds right (22,050 is rushed).

### 2.8 `Datav/` — video (**verified**)

Every `DATAV*.<nnn>` is a Bink file whose first 1024 bytes have been
replaced with junk. The real headers live in `DATAVC00.<nnn>`:

```
u32 data_offset      = 8 + N*15; its low byte is the XOR seed (0x21)
u32 N                number of videos (87)
byte[N][15]          filename records: 13-byte NUL-padded name + 2 bytes
byte[N][2048]        header blocks; first 1024 bytes are the real header
```

Records and header blocks are XOR-chained: `plain[0] = raw[0] ^ seed`,
`plain[i] = raw[i] ^ raw[i-1]`. Key index = position in the record list;
the exe matches by filename. Restoration is
`decoded_header + original[1024:]`.

---

## 3. Image formats

All colour images are **RGB565**: `u16` per pixel, `RRRRRGGG GGGBBBBB`,
red in the top 5 bits. To 8-bit: `r = (p >> 11) << 3`,
`g = ((p >> 5) & 63) << 2`, `b = (p & 31) << 3`. The game converts to
RGB555 for display; the files are 565.

The screen is **1024 × 600**. Scrolling scenes are wider (up to 2592, e.g. `RESOURCE.H40` at 2592×600)
and tall/extended scenes can be up to 2062 high (e.g. `RESOURCE.I03` outro credits at 1024×2062).

### 3.1 Raw raster (**verified**)

No header. Exactly `W × H × 2` bytes of RGB565, row-major, top to bottom.

Width is **not stored**. Recover it as the stride at which vertically
adjacent pixels agree — `score(W) = mean |g[i] − g[i+W]|` over the green
channel has a razor-sharp minimum at the true width — then require that
`W` divides the pixel count exactly. Rasters whose pixel count is a multiple
of `1024 × 600` may be double-width scenes (such as `RESOURCE.I01` at 2048×900)
or stacked full screens (used as a fallback for flat title cards that defeat
stride detection).

Counts in the scene archives include standard 1024×600 screens, wide scrollers
up to 2592×600, double-width landscapes (2048×900), tall scrollable scenes
(1024×2062), medium insets (1444×800), and small thumbnails (282×188, 204×120).

A raster whose colour channels are decorrelated (neighbouring pixels in
unrelated colours) is an **index layer** rather than artwork — the
`_mask` suffix in the exporter. Only one entry in the scene archives
qualifies after correct decoding; the earlier finding of "four full-screen
mask layers" was an artefact of the §2.1 table misread.

### 3.2 Overlay — positioned row records (**verified, byte-exact on 225 entries**)

```
u16 n                              number of records
n × {
    u16 x                          screen column of first pixel
    u16 y                          screen row
    u16 count                      pixels in this record (never 0)
    u16 rgb565[count]
}
```

Records consume the entry exactly — that is the format check. A
rectangular image (title card, inventory icon) is one record per row with
constant `x` and `count`; a prop or foreground layer is sparse, with one
record per lit span. `(x, y)` are positions on the 1024×600 screen (a
few exceed 1024 on wide scenes). Everything not covered by a record is
transparent.

This is the record shape consumed by the blitter at RVA `0x32ea0`
(`DEC_A` in the notes: `{u16 x, u16 y, u16 pixel_count}`). It is the
same idea as the sprite segment of §3.3 with a 16-bit count and no
delta semantics.

Counts: 225 entries in the scene archives — 51 rectangular, 174 sparse.
Sizes range from an 18×22 icon to an 810×591 foreground layer.

### 3.3 Animated sprite (**verified, byte-exact on all 442 assets / 40,276 frames**)

```
[ descriptor records, 0 or more ]
    u32 0
    u16 W                          scene width (1372 .. 2048)
    i16 -(W - 1)
    u16 H                          600 (or 900)
    u16 0, u16 0

record[N]                          14 bytes each
    u32 frame_offset               from the end of the record table
    u16 x0, w                      this frame's bounding box: columns x0 .. x0+w-1
    u16 y0, y1                     rows y0 .. y1 inclusive
    u16 c                          number of segments in this frame

frame data
    for each frame, c × {
        u16 x                      screen column of the first pixel
        u16 y                      screen row
        u8  count                  pixels that follow; 0 means 1
        u16 rgb565[count]
    }
```

**Every frame is a complete sprite.** Segments are absolute screen
positions, in pixels, and every segment of a frame lies inside that
frame's record box. Most assets keep one box for the whole animation;
69 of 442 change it per frame, so the animation's overall extent is the
union of its records' boxes. Nothing carries over between frames; a frame
decodes from a blank canvas. A row wider than 255 pixels is simply two
consecutive segments on the same `y`, the second starting where the
first ended.

Identification: the first record's `frame_offset` is 0. The table has no
length field — it is read until a record stops making sense (offset
decreases or exceeds the asset; `w == 0`; `y1 < y0`). **`x0 == 0` is a
legal position, not a terminator.** Then frame 0 must walk exactly to
frame 1's offset; that separates a real animation from a data table
that happens to start with a zero word.

The **descriptor records** open 13 assets belonging to wide scenes. They
have zero segments and `W` equals the scene's raster width; their purpose
is not known. They are skipped.

> **What previous decoders got wrong.** `extract_runaway.py`, and the
> first version of `runaway_images.py` that copied it, read `x` as a
> *byte* offset and halved it. That placed every sprite at half its
> true column — invisible after cropping, but on wide scenes it also
> folded x=1136 into the 1024-wide canvas, hiding that sprites sit past
> the screen edge. It then needed two more heuristics to cope: a
> "same-row correction" (adding the previous segment's count back to x,
> which is only necessary once x has been halved), and a persistent
> canvas with a 5-frame expiry window to explain frames that looked
> incomplete. That window is what produced ghost trails. And treating
> `x0 == 0` as end-of-table truncated 8 assets, which is why `E01`
> asset 12 was reported as "a different sub-format" — it is 31 ordinary
> frames.

Verification: for every one of the 40,276 frames, walking `c` segments
lands exactly on the next frame's recorded offset, and every segment is
inside the record's bounding box. Zero exceptions.

**Timing is not stored.** Neither the records nor the segments carry a
delay; the engine's own frame rate is not known. The exporter's animated
PNGs default to 15 fps.

**Reassembling an animation.** The union of the records' `(x0, w, y0, y1)`
boxes is the natural canvas. Place frame pixels at `(x − X0, y − Y0)`
where `(X0, Y0)` is the union's origin. The exporter's APNGs do exactly
this using the format's per-frame offsets.

### 3.4 Scene RLE Mask — hotspots, walk-behind & depth planes (**verified**)

No header. Flat stream of 3-byte run-length encoded records:

```
N × {
    u8 id                          zone / hotspot / depth plane identifier (1–255)
    u16 length                     run length in pixels (little-endian)
}
```

Runs are strictly scanline-bounded: their accumulated lengths sum to exactly the
scene width (1024) per row without remainder or overshoot.

Present as entry 1 in at least 18 scene archives (e.g. `RESOURCE.H38\e01`,
`RESOURCE.B08\e01`, `RESOURCE.F13\e01`, `RESOURCE.F22\e01`). Each defines a
1024×600 segmentation map where distinct IDs demarcate walkboxes, obstacles,
depth layers (walk-behind planes), and clickable hotspots. Decoded to BGRA
using a deterministic high-contrast palette. In the viewer, a mask can be
displayed standalone or composited as a semi-transparent overlay (55% opacity)
over the scene's background (`e00`, in either full color or greyscale) to inspect
how zones align with the background art.

### 3.5 `Resource.000` / `.001` sprite codec (**shapes verified, colour open**)

A different codec, not related to §3.3. No record table; pictures are
packed back to back and a new picture is detected by `y` jumping
backwards.

```
{ u16 x, u16 y, u16 count, u8 value[count] }   repeated
```

`x` is a pixel column (not doubled). Each pixel is **one byte**. It was
first assumed to be an index into a 256-colour palette, but the game's
palette buffer never exceeds 38 populated entries while pixel bytes
routinely exceed 40, so the meaning of the byte is **not known**.
Greyscale (byte as intensity) is the honest rendering. `palette.bin` in
the game folder is a 256×4 BGRx dump that does not make these images
correct.

### 3.6 Font atlas — RLE antialiased (**verified**)

Two atlases at the `RESOURCE.000` offsets in §2.3; the ~46 MB localised
region uses the same encoding for pre-rendered strings.

```
byte  0x00 – 0x10     pixel with this alpha (17 levels; 0x10 = opaque)
byte  0x11            end of row
byte  0x12            background pixel (transparent; distinct from 0x00)
```

Glyphs are separated by a run of about 8 all-transparent rows (3 for
strings). No glyph table has been found; the atlas is segmented by that
gap. 184 regular and 182 bold glyphs extract cleanly.

### 3.7 UI atlas (**from notes**)

At `RESOURCE.000 + 0x3d898f4`: a ~2472-byte index followed by packed
RGB565 sprites at width 700 — inventory, settings and arrow icons, menu
backgrounds.

---

## 4. Audio and video payloads

### 4.1 PCM in `RESOURCE.M/S/002` (**verified**)

Headerless 16-bit signed stereo PCM. Music 16,000 Hz; ambient and
cinematic 22,050 Hz. Wrap in a 44-byte RIFF/WAVE header to play.

### 4.2 Voice (**verified**)

Headerless 8-bit unsigned mono PCM, 16,000 Hz (assumed, see §2.7).

### 4.3 Video (**verified**)

Bink ("BIKi"). See §2.8 for header restoration.

---

## 5. What is still open

- The meaning of the per-pixel byte in the `Resource.000/001` sprite codec (§3.4).
  The viewer shows these entries as hex dumps.
- The three recurring data entries in every scene archive (§2.1).
- Where dialogue text is. It is **not** stored as text anywhere: 3,440
  encoding variants of known in-game words were searched across all
  24,161 files with no hit. Localised builds differ only in the exe and
  the `RESOURCE.000` bitmap region, so subtitles are almost certainly
  pre-rendered bitmaps (§3.5).
- The two 1444×800 rasters — larger than any scene; purpose unknown.

---

## 6. A note on method

The single most expensive error in this project was reading the scene
archive table as a flat list of offsets. It produced hundreds of
plausible-looking fragments, and every heuristic built to "fix" them —
sector alignment, width sweeps, seam detection, a shift dictionary
harvested from live memory — was correct *about the fragments* and
wrong about the format. What finally exposed it was a sprite asset that
truncated at 13,972 bytes when its own frame table said it was 15 MB.

If a decoder needs a heuristic to find where an image starts, look again
at the container.
