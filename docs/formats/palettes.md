# Palettes (*Hollywood Monsters*)

*Hollywood Monsters* is the only game in the family whose images are palette-indexed. Every background,
overlay and sprite frame stores one byte per pixel, and that byte is an index into a 256-entry colour
table assembled from two blocks: one in the scene archive and one shared across the whole game.

## Purpose

The game runs in 8-bit colour, so the colours themselves are data like any other entry. Decoding any
*Hollywood Monsters* image therefore has two steps: read the indices out of the entry, then resolve them
through the table for that scene. Without the table there is no image — only a grid of small integers.

## Layout

A palette block is a bare run of 6-bit VGA triples, with no header, count or terminator:

```text
N × {
    u8 R                        -- 0..63
    u8 G                        -- 0..63
    u8 B                        -- 0..63
}
```

The block fills slots `0 .. N-1` of the table. Two sizes occur in practice:

| Bytes | Colours | Where | Meaning |
|---|---|---|---|
| 528 | 176 | Scene archives | The scene's own colours: background art, props, overlays |
| 240 | 80 | `RESOURCE.000` | The shared character colours |
| 768 | 256 | A few scene archives, and `RESOURCE.I02` | A complete table needing no tail |

### Identifying a block

There is no signature, so a block is recognised structurally: a length between 96 and 768 bytes that is a
whole number of triples, with **every byte ≤ 63**. The 6-bit ceiling is what makes this reliable — real
image or table data exceeds it within a few dozen bytes.

This test must run **before** the mask test, because a palette block is also a whole number of 3-byte RLE
mask records (see [masks.md](masks.md)).

### Which block governs an entry

An archive usually has one palette block, at entry 1. Sixteen have more, and there a block applies to the
entries that **follow** it, up to the next block — the table is loaded as the archive is read, so each
block recolours everything after it. Entries ahead of the first block (entry 0, the background) take that
first block.

Two shapes occur:

- **A second cast palette part-way through.** `RESOURCE.A03` carries 176 colours at e01 and another 176 at
  e08; e09 and e10 are the chef and the blonde woman, and under e01's colours they come out in garish
  yellows and greens.
- **Two whole scenes in one archive.** `RESOURCE.C07` holds a background at e00 with its palette at e01,
  then a second background at e33 with a 256-colour palette at e34. Entries 2–23 belong to the first
  scene, 35–36 to the second.

Choosing one block per archive — the one defining the most colours, say — gets both cases wrong, and in
`C07` the 256-colour block at e34 would win and wash out all twenty-odd sprites of the first scene.

"Nearest preceding block" is the explorer's rule, not the engine's. The engine loads `e01` and then swaps
in one specific alternate chunk when a game-state flag says so, again by an index compiled into the
executable. The two agree on the pairings that matter, because an alternate block is always placed ahead
of the entries it recolours — but the ordering rule is a sound heuristic, not a transcription of what the
engine does.

### Assembling a scene's table

A 176-colour scene block leaves slots 176–255 undefined, and that is exactly the range the characters are
drawn from. The missing colours come from the 80-colour block in `RESOURCE.000`, placed at the **top** of
the table:

```text
table[0   .. 175] = scene block
table[176 .. 255] = shared block
```

In general the tail of `256 - N` colours is taken from the `RESOURCE.000` block that defines exactly that
many colours. A scene whose own block already defines 256 colours takes no tail.

Skipping this step is not a subtle error: every character in the game renders as a black silhouette,
because slots 176–255 are all zero.

#### How the engine fills the same range

The shared tail is really two bands, filled from two different places:

| Slots | Bytes | Source | Contents |
|---|---|---|---|
| 176–207 | 96 | `RESOURCE.000` entry 49, first 96 bytes | Inventory-object and UI colours |
| 208–255 | 144 | A 144-byte block in `RESOURCE.000`, chosen per scene | The active player character's colours |

`RESOURCE.000` entry 49 is 240 bytes and covers the whole 176–255 range, which is why loading it at slot
176 produces a usable table — it is exactly where the engine puts it. The engine then **overwrites the top
48 colours** with the 144-byte block belonging to whichever character the scene stars. The shipped file
holds three candidates: entry 49's own last 144 bytes, entry 51 and entry 66.

Which one a scene uses is **not derivable from the archive**: the binding is a table index compiled into
`Monsters.exe`, one per scene. The consequence is visible. The same sprite rendered with entry 51 is a
detective in a tan trenchcoat with brown hair; with entry 66 the identical pixels are a blonde figure in a
green coat. The explorer uses entry 49's tail, which is close to entry 51, so scenes starring the other
character are tinted wrong above slot 207.

Of the 498 sprite-shaped entries in the game, 45 reach into slots 176–207 and 296 reach 208 or above, so
this band is almost entirely character art.

### Expanding to 8 bits

Components are 6-bit and are expanded by replicating the high bits, so full intensity reaches 255 rather
than 252:

```text
out = (v << 2) | (v >> 4)
```

## Known unknowns

- Which 144-byte character block a scene should use is a per-scene index inside `Monsters.exe`, so it
  cannot be recovered from the archives alone (see the table above). The explorer's single fallback is
  right for most scenes and wrong for those starring the other character.
- Seven archives hold an entry with a palette's size and shape whose bytes exceed 6 bits
  (`RESOURCE.I09` e02, for one). Each also carries a real block, so these are ordinary data entries that
  happen to be a multiple of three bytes long; what they hold is unknown.
- The engine **does** animate the table at runtime: it fades in and out by stepping every component
  towards or away from its target by 3 per tick, and it rotates sub-ranges in place for effects such as
  the fire in the opening montage (colours 144–159, one step every 300 ms). None of that is stored in the
  archives — it is code — so a still frame is the only faithful thing a browser can show.

## Decoders & tests

- [`IndexedPalette`](../../src/RunawayExplorer.Core/Formats/IndexedPalette.cs) — parsing, the 6-bit
  expansion, and `WithTail` for the shared block.
- [`VirtualFileSystem.ScenePaletteFor`](../../src/RunawayExplorer.Core/FileSystem/VirtualFileSystem.cs) —
  resolves and caches one table per scene archive.
- Tests: [`HollywoodMonstersTests`](../../tests/RunawayExplorer.Core.Tests/HollywoodMonstersTests.cs)
