# Global and UI data

Shared global assets — fonts, cursor art, interface icons, phrase lookup tables, lip-sync visemes, the
character sprite library, and *Hollywood Monsters*' shared colours — reside in numbered global resources
(`RESOURCE.000` through `RESOURCE.004`). The numbering is not stable across the games: what `RESOURCE.001`
and `RESOURCE.004` hold in *Runaway 1* is not what they hold in *Hollywood Monsters* (see below).

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

The `u32` at `0x014` is entry 0's offset, and because entry 0 begins immediately after the table it
always reads as `0xFB4` (4,020). That is also a valid *Runaway 2* `table_half_bytes`, so a reader that
sniffs for the *Runaway 2* header before recognising this layout takes the file apart into hundreds of
plausible fragments instead of its 151 real entries — and whether it does so depends on how many header
bytes it happened to read, since the *Runaway 2* branch needs 8,064 of them. Recognise `0x014 == 0xFB4`
as *Runaway 1* first.

#### Hollywood Monsters layout

*Hollywood Monsters* puts a single byte in front of the table and uses 100 slots, so entry 0 begins at 801:

```text
0x000: u8          Header
0x001: u32[100]    Offsets     -- absolute byte offsets
0x191: u32[100]    Sizes       -- byte sizes
0x321: <entry data>
```

Its entries are UI and font art, plus the shared 80-colour palette block that completes every scene's
colour table — see [palettes.md](palettes.md).

#### Known payload slots (*Runaway 1*) (**verified**)

Quoting these as raw file offsets hides that every one of them is simply the start of a slot, so
they are listed by slot index here. The offsets are the ones a 500-slot table yields on the
shipped Spanish Steam build.

| Slot | Offset | Size | Purpose | Encoding |
|---|---|---|---|---|
| 112–115 | `0x35EB4EC`… | 976,320 each | Mouse cursor atlas, 904×540 | Raw RGB565 over key `0x6841` |
| 116, 117 | `0x39A4BEC`, `0x3A789EC` | 867,840 each | Inventory item icons, 904×480 | Raw RGB565, same key |
| 125 | `0x3D7CF4C` | 50,719 | Regular font glyph bitmap, 181 glyphs | Outline/fill shade, see below |
| 126 | `0x3D8956B` | 905 | Glyph table for slot 125 | 181 × 5-byte records |
| 127–130 | `0x3D898F4`… | 103,600 each | UI sprite strips, 700×74 | Raw RGB565 |
| 131 | `0x3DEEBB4` | 1,228,800 | Inventory screen, 1024×600 | Raw RGB565 |
| 132 | `0x3F1ABB4` | 1,228,800 | Main menu / options panel, 1024×600 | Raw RGB565 |
| 143–147, 151, 158 | `0x50E107C`… | 1,228,800 each | Full-screen item close-ups, 1024×600 | Raw RGB565 |
| 148 | `0x56BD07C` | 39,848 | Bold font glyph bitmap, 181 glyphs | Outline/fill shade, see below |
| 149 | `0x56C6C24` | 905 | Glyph table for slot 148 | 181 × 5-byte records |
| 161 | `0x692FCF5` | 15,842 | Small font glyph bitmap, 150 glyphs | Outline/fill shade, see below |
| 162 | `0x6933AD7` | 750 | Glyph table for slot 161 | 150 × 5-byte records |

Slot 131 is the **inventory** screen — the leather wall with the logo and the character portrait
oval — and slot 132 is the **menu**, which is easy to get backwards: 131 looks like a title
backdrop, but 132 carries the volume knob, the brightness lever and the two columns of five
setting holes that the shipped menu draws its LOAD / SAVE / ERASE / PLAY / EXIT column over.

#### Fonts (**verified, byte-exact on all three atlases**)

A font is **two** slots: a glyph bitmap and, in the slot immediately after it, a glyph table. The
table is what makes the bitmap readable, and looking for it is what turns the font from
"~184 glyphs separated by blank rows" into an exact decode.

```text
Glyph table (905 bytes = 181 records, or 750 = 150 for the small font):
    Record[N] × {
        u16 Offset      -- byte offset into the glyph bitmap
        u8  Top         -- blank rows above the glyph on its line
        u8  Height
        u8  Width
    }
```

The records **tile the glyph bitmap exactly**: each one ends where the next begins and the last ends
on the final byte of the entry. That is the format check, and it holds on all three *Runaway 1*
fonts (181 + 181 + 150 glyphs, 50,719 + 39,848 + 15,842 bytes).

`Top + Height` is the baseline and is near enough constant per font — 23 for the regular face, 22
for the bold one — so `Top` positions a glyph on the line and the line height is `max(Top +
Height)`.

##### The glyph bitmap is not run-length encoded, and it is not an alpha map

There is no RLE and no scanline marker. The bitmap is **one byte per pixel**, `Width` bytes per
row, `Height` rows:

```text
byte 0x00 .. 0x10: Shade, 17 steps along a ramp from the OUTLINE colour (0x00)
                   to the FILL colour (0x10). All of these are opaque.
byte 0x11:         Transparent — hugs the glyph and fills its counters
byte 0x12:         Transparent — the space beyond
```

The glyphs are **outlined, not antialiased**, so drawing one takes two colours rather than one.
Reading the byte as an alpha value and blending towards the background throws the outline away:
`0x00` is the darkest ink, not "invisible". A bold `O` makes it plain — `0x00` traces both the
outer and the inner edge of the ring, `0x10` fills the stroke, `0x11` sits in the counter and in
a one-pixel band all round, and `0x12` is everything further out:

```text
12 12 12 12 12 12 11 11 11 11 11 11 11 12 12 12 12 12
12 12 12 12 12 11 00 00 00 00 00 00 00 11 11 12 12 12
12 12 12 11 11 00 01 05 07 07 07 06 02 00 00 11 12 12
12 12 11 00 00 07 0f 10 10 10 10 10 10 0a 01 00 11 12
12 11 00 01 0d 10 10 10 10 10 10 10 10 10 0d 01 00 11
...
```

`0x11` and `0x12` are both transparent when drawing; the distinction is presumably what the
original renderer used to lay the outline down.

Reading `0x11` as "end of scanline" is the other trap. It decodes with no unexpected bytes —
every byte is a valid code either way — and produces a tall narrow column that segments into
hundreds of fragments instead of glyphs, so the error looks like a segmentation problem rather
than a wrong premise. There is nothing to segment: the table already gives every glyph's width
and height.

##### Glyph order

The glyphs are ordered by the **Spanish alphabet**, not by any character set, so a character maps
to an index only through a table:

| Index | Glyphs |
|---|---|
| 0–25 | `A`–`Z` |
| 26–39 | `a`–`n` |
| 40 | `ñ` |
| 41–52 | `o`–`z` |
| 53–57 | `á é í ó ú` |
| 58–67 | `0`–`9` |
| 68–79 | `(` `)` `-` `+` `=` `<` `>` `/` `\` `'` `;` `.` |
| 80–95 | `:` `"` `¡` `!` `¿` `?` `Ñ` `[` `]` `#` `´` `▶` `` ` `` `ß` `Ç` `ç` |
| 96–111 | `à è ì ò ù` `Á É Í Ó Ú` `©` `¯` `ü Ü` `«` `»` |
| 112–118 | `$` `&` `®` `▲` `▼` `●` `@` |
| 119–180 | Czech, Hungarian and Polish letters, `Œ`/`œ`, the remaining accented vowels, ending on an empty box |

`ñ` sitting between `n` and `o` rather than after `z` is the giveaway that this is a collation
order and not a code page. All three fonts share it — the small font is the same list truncated at
150 — so it is a property of the engine, not of one atlas. The bold face is caps-only: its
lower-case slots draw capitals.

The empty box at the end of the full tables is the obvious fallback for an unmapped character.

##### Which font goes where

The shipped menu is set in the **bold** face — which is the caps-only one, and every label there
is caps. Sampled off a screenshot of the original, it draws the fill white (254, 253, 253) over a
black outline (12, 0, 0), and an entry that is unavailable in white's place gets a muted warm grey
(118, 102, 100), outline and all.

The glyphs carry no advance width, and laying the bold face out with zero letter spacing
reproduces the original's label widths, so the engine simply butts the glyphs together. A space
has no glyph at all.

##### Pre-rendered strings

`FORMATS.md` describes the ~46 MB language-varying region from `0x3D7FA41` as pre-rendered string
bitmaps in "the font RLE format". That offset falls **inside slot 125**, so it is not the start of
anything; the claim is about the region of slots that differs between localised builds, not about
a distinct payload. With the glyph tables decoded, a reader can compose strings from the fonts
directly and does not need those bitmaps.

##### Other games

Checked and **not** present: *Runaway 2* and *Runaway 3* have no slot whose contents chain as a
glyph table of this shape, so their fonts use some other scheme.

#### Cursor atlas (*Runaway 1*) (**verified**)

Slot 112 is a 904×540 RGB565 raster holding every mouse cursor, over the key colour `0x6841`
(RGB 104, 8, 8), and it carries no index — the sprites have to be found by scanning.

Rows that are entirely key colour separate horizontal bands; only the first band (y 8–53) holds
cursors, and the bands below it hold other interface art. Within the band, columns that are
entirely key colour separate the sprites, but **not every gap is a separator**: gaps inside a
sprite (between the arms of the crosshair) are 1 px and gaps between sprites are 12 px or more, so
runs closer together than a threshold anywhere in 3–9 px belong to the same sprite. Every
threshold in that range yields the same **15** cursors, 23–46 px wide: crosshair, magnifier, hand,
speech balloon, four diagonal exit arrows, then a run of morph frames.

Hotspots are not in the atlas; they live in the executable.

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

The name covers two unrelated layouts. *Runaway 2* and *3* store a flat list of phrase records, which the
explorer decodes; *Runaway 1* stores a scene-indexed table that it does not.

#### Runaway 2 and Runaway 3 — phrase records

```text
u32                     PhraseCount     -- 10,454 in Runaway 2; 7,228 in Runaway 3
u32[PhraseCount]        PhraseIds       -- e.g. 100, 110, 120 ... 90230000
byte[PhraseCount][401]  Records         -- XOR-chained, NUL-terminated, Windows-1252
```

Each record is exactly 401 bytes and the text starts at byte 0, recovered with an index-seeded XOR chain:

```text
seed     = recordIndex & 0xff
plain[0] = raw[0] ^ seed
plain[i] = raw[i] ^ raw[i - 1]
```

Phrase index `k` pairs 1:1 with voice clip `k` in `Dataa/Dataaa.000`, so no lookup table is needed.
Read by `PhraseArchive`.

#### Runaway 1 — scene-indexed, not decoded

*Runaway 1* uses the shape its engine also gave *Hollywood Monsters*: a 321-byte leading block, then 1,021
scene offsets, then per-scene 5-byte records.

```text
0x000: byte[321]  DeltaTemplate   -- 321 is also the Hollywood Monsters row and key length
0x141: u32[1021]  SceneOffsets    -- indexed by scene number
...
Per-scene block:
    5-byte records: { u16 Marker, u8 Mid, u16 Tail }
```

Those 5-byte records have the same shape as the *Hollywood Monsters* speech cues below
(`{ u16 TextRecordId, u8 ContinuationCount, u16 VoiceSampleId }`), which suggests the two files are the
same format and that the *Hollywood Monsters* decoder may read this one as well. That has not been tested
against a *Runaway 1* install, so the explorer still shows the file as bytes.

### 4. `RESOURCE.003` in *Hollywood Monsters* — script text

The same container holds *Hollywood Monsters*' entire script: every line of narration and dialogue, plus
the inventory item names. The text is **obfuscated**, which is why a raw dump shows nothing readable.

#### Obfuscation

Each row is enciphered with a per-column subtractive key, and the key is simply **the first 321 bytes of
the file itself**:

```text
plain[column] = (cipher[column] - key[column]) & 0xff      key = file[0 .. 0x140]
```

The key length equals the large-row length (`0x141` = 321), so column `c` of every row always uses key
byte `c`. There is no per-row salt and nothing depends on position in the file.

#### Layout

```text
0x000:  byte[0x141]  DecodeKey        -- also the cipher key (see above)
0x141:  u32[1021]    StageOffsets     -- indexed by StageIndex; 0 means "no such stage"

Per stage, at StageOffsets[StageIndex]:
    byte[0x186a0] SpeechCueDescriptors -- 20,000 x 5-byte cue records
    u8            SmallRowCount
    u16           LargeRowCount
    byte[SmallRowCount][0x29]  SmallRows   -- 41-byte rows, enciphered
    byte[LargeRowCount][0x141] LargeRows   -- 321-byte rows, enciphered
```

`StageIndex` is the **scene number divided by 10**, so scene 1010 reads index 101 and scene 2010 reads
index 201. Indexing by anything else lands on the zeros that fill most of the table -- which is the trap
that makes the file look empty. Index `0x32` (50) holds the inventory-owner rows shared by every scene.

Rows are NUL-terminated within their fixed width:

- **Small rows (41 bytes)** are inventory item names and dialogue-menu labels, each stored with a leading
  space (`" escaleras"`, `" caseta de perro"`, `" coche"`).
- **Large rows (321 bytes)** are spoken lines (`"Conducen a la mansión."`).

A cue's `textRecordId` selects between the two tables: ids below 500 index the shared inventory-owner
rows, and ids from 500 up index the stage's own large rows at `id - 500`.

#### Speech cues

A cue record is 5 bytes, and the tables are addressed as fixed-width grids rather than lists:

```text
{
    u16 TextRecordId            -- 0 means "no cue"
    u8  ContinuationCount       -- further rows belonging to the same line
    u16 VoiceSampleId           -- slot in RESOURCE.004, the voice bank
}
```

Stage cues are indexed `(rowIndex * 100 + frameIndex) * 5`, static speech cues `(rowIndex * 10 +
frameIndex) * 5`. `VoiceSampleId` is what ties a written line to its recording.

**Only frame 0 of a row carries a usable pairing.** A row is one utterance and its later frames are
continuations, so the grid is densely populated with records that name a line without being the cue that
plays it. Reading all 20,000 records per stage raises apparent coverage from 42% to 92% of lines, but at
that point two thirds of the lines are ambiguous: 197,660 records disagree with an earlier record about
the same line, and no majority breaks the tie -- every candidate is attested exactly once. At frame 0 the
mapping is near-unique instead. Of the 2,369 distinct line ids cued there in the shipped game only 196
carry more than one voice id, and ids advance in lockstep with the recordings:

```text
stage 101  line 500 -> clip 2227     stage 201  line 500 -> clip 1805
           line 501 -> clip 2228                line 502 -> clip 1807
           line 502 -> clip 2229                line 504 -> clip 1809
```

The off-diagonal candidate for stage 101's line 501 is clip 1567, which also turns up as stage 201's line
501 -- the same stale value in two unrelated scenes, which is what gives the noise away. The explorer
therefore reads frame 0 only and leaves a little over half the lines with no clip: an unlinked line is
obvious to the reader, a line linked to the wrong recording is not.

#### Text encoding

Bytes are **CP850/CP437**, not Windows-1252: `0x82` is `é`, `0xa0` `á`, `0xa1` `í`, `0xa2` `ó` and `0xad` the
opening `¡`. Decoding as CP1252 turns every accent into punctuation. The font's own character map, in
`RESOURCE.000` entry `0xb0`, maps these bytes to glyph indices.

Verified by decoding the shipped Spanish first edition: stage 101 yields *"Bien, ya estamos en la mansión
Hannover. Veamos qué puedo averiguar."*, stage 201 *"¡Vaya choza!..."*.

#### Which slots are real

The offset table is mostly zeros and stale values, so a stage counts only when its rows actually decipher
to text. Judge that on the **large rows alone**: a stage may carry no labels at all (scene 7020, 12 lines
and no labels) or lead with an empty one (scene 1070), and gating on the labels rejects both even though
their dialogue decodes cleanly.

That rule yields 80 stages from the Spanish first edition -- 589 labels and 3,430 lines. Cross-checked
against the scene list in the ScummVM `hollywood` engine, the 74 playable ones account for every playable
scene it knows of except 2060, 3110 and 5130, whose offset slots are zero; the remaining 6 are cutscene
stages (9010, 9100-9130, 9200), which are likewise scenes in its registry. Nothing decodes that is not a
scene, and nothing is missing that is not absent from the file.

The game executable references the string `"PHRASE NOT FOUND"` when attempting to read an empty marker.

### 5. `RESOURCE.004` — lip-sync visemes

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
- Tests: [`ContainerTests`](../../tests/RunawayExplorer.Core.Tests/ContainerTests.cs), [`PhraseArchiveTests`](../../tests/RunawayExplorer.Core.Tests/PhraseArchiveTests.cs), [`HollywoodMonstersTests`](../../tests/RunawayExplorer.Core.Tests/HollywoodMonstersTests.cs)
