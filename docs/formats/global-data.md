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

> In *Hollywood Monsters*, `RESOURCE.003` holds the game's script rather than a dialogue tree, and
> `RESOURCE.001`, `002` and `004` are audio archives (ambient, cinematic and the voice bank) rather than
> the sprite library, phrase table and viseme index described here. Its own layout is documented in
> [Hollywood Monsters script text](#4-resource003-in-hollywood-monsters--script-text) below. The explorer
> browses it as scenes of labels and lines.

Stores dialogue tree node structures and audio clip references.

<a id="4-resource003-in-hollywood-monsters--script-text"></a>

#### Layout

```text
0x000: byte[321] DeltaTemplate  -- base template for dialogue state reconstruction
0x141: u32[1021] SceneOffsets   -- indexed by (SceneId - 1000) / 10
...
Per-scene block:
    5-byte phrase records: { u16 Marker, u8 Mid, u16 Tail }
    Marker == 0xFFFF denotes an empty or unpopulated dialogue slot.
```

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
