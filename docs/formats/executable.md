# The executable: where the game logic lives

Every other doc here describes a container. This one describes what is **not** in any container.

Repeated attempts to find a scene script — the data that says which animation plays when, what a verb
does to an item, when a chapter title appears — have come up empty in the archives. This doc records
where that logic actually is, how it was established, and what reading the executable confirms about
the file formats.

Addresses are for *Runaway 1*'s `Runaway.exe` (1,875,968 bytes, image base `0x400000`). RVAs are given
where a virtual address is not more convenient; both appear as `0x4xxxxx` (VA) or `0x0xxxxx` (RVA).

---

## 1. There is no scene script

**The per-scene logic is compiled code, one block per scene.** It is not a bytecode, not a table, and
not a data file. Four independent observations, any one of which would be suggestive and which together
are conclusive:

**The archives have no room for it.** Surveying all 74 scene archives of *Runaway 1* (1,062 live
entries), exactly three entry sizes recur across many archives:

| size | in how many archives | what it is |
| --- | --- | --- |
| 1,228,800 | 41 | a 1024×600 RGB565 background |
| 43,659 | 59 | the walk route table |
| 1,536 | 64 | the zone attribute table |

Nothing else recurs in more than 20. There is no per-scene verb table, item table or event list.

**There is no dispatch table.** All 74 scene archive names appear in the executable as individual
string literals (`"RESOURCE.A00"`, `"RESOURCE.B01"`, …), referenced 129 times from `.text` between
RVA `0x03da34` and `0x17f4a8`. Scanning the whole image for runs of consecutive pointers into `.text`
finds no table of 74 (or 66, or any scene-like count) function pointers — the longest run anywhere is
26, and those are ordinary `switch` jump tables. Each scene is reached by a call site compiled against
its own name.

**There is nowhere to put it.** `.data` has a virtual size of 5,279,788 bytes but only 24,576 bytes of
raw initialised data; the rest is BSS — runtime buffers, several of them 1024×600×2. `.rdata` is
117,214 bytes. The executable carries no large static data. What it carries is 1,712,432 bytes of code.

**The sister game confirms it.** *Hollywood Monsters* runs on the same engine family and its scene
archives use the same table convention. The ScummVM `hollywood` engine, which is a working port,
implements its scenes as **90 hand-written C++ files, 54,279 lines**, one per scene, full of
transcribed constants — entry coordinates, facings, ambient cue indices, state ids. That is what
porting compiled per-scene logic looks like.

### What *Hollywood Monsters* keeps in data, and Runaway does not

HM is not purely code-driven: it has a per-scene metadata blob holding, at fixed offsets, the route
tables **and** the item and verb data:

| offset | contents |
| --- | --- |
| `0x0000` | actor depth thresholds |
| `0x002a` | palette delta table |
| `0x003f` | palette adjust table |
| `0x007d` | route boundary points, 441 × 12 |
| `0x1529` | route boundary steps, 441 × 19 |
| `0x35e4` | scene item default caption strip, 21 bytes |
| `0x35f9` | item interaction points, 21 × 4 |
| `0x364d` | item approach points, 21 × 4 |
| `0x36a1` | item facing, 21 bytes |
| `0x36b6` | verb action records, 21 items × 8 verbs × 4 bytes |
| `0x3956` | inventory relation records, 121 × 21 × 4 |
| `0x610a` | a second relation overlay, same shape |

A verb action record is `{u16 actionHandlerId, u16 movementMode}` — so even in HM the *data* only names
a handler, and the handler itself is code.

**Runaway 1 keeps the routing half and drops the rest.** Its route entry is 43,659 bytes =
441 × (80 + 19): the same 441-pair matrix and the same 19-byte step list as HM, with a larger point
array (80 bytes, 20 points, against HM's 12). It begins at offset 0 and ends exactly on the last byte,
so there is no region corresponding to HM's `0x35e4` and beyond. The same reduction shows in the zone
table: HM has 7 pages of 256, Runaway has 6, having dropped the palette-remap page a 16-bit game
cannot use.

So *Runaway 1* moved into code what *Hollywood Monsters* had kept in data.

### The actor scale factor is not in the data either

The same search, run for one specific constant, comes back the same way. *Runaway 1*'s characters are
authored at a single size — every frame in `Resource.001` is 418 to 492 px tall, and the second sprite
library in `RESOURCE.000` is that one size too ([global-data.md](global-data.md)) — so the runtime has to
resample them per scene. Where the factor is **not**:

- the 32-byte frame record has no spare field;
- no scene archive carries a recurring per-scene table for it. Listing every entry under 4 KB over all 74
  archives, the only size present in every scene is the 1,536-byte zone attribute table; the rest are
  one-off small sprites;
- **zone table page 3 is not a depth ramp.** Over 38 scenes, restricted to the ids a mask actually paints,
  its value agrees with the zone's mean screen row on 42% of pairs — below chance. It is the lighting
  field ([masks.md](masks.md)). Page 0 scores 35% and page 2 41% on the same measure;
- the 43,659-byte route table's arithmetic is exact (441 × 99) and leaves no spare bytes;
- the executable has no relevant strings.

So it is in the code, along with the animation frame rate, the per-entry palette choice and the verb logic.

---

## 2. A scene's code block

Scene-local state pins the block down. The intro's current-animation and current-frame globals
(`0x5d2ef8`, `0x5d2ef1`) are referenced from 41 and 31 sites respectively, and **every one of them lies
between RVA `0x03cdbb` and `0x03f60b`** — a contiguous block of roughly 10 KB. That block is the intro
scene, and the 129 scene-name references spread over `0x03da34`–`0x17f4a8` mark the others.

### The intro scene, `0x43da20`

Reading it straight through gives the whole shape of a scene:

```
0x43da20  sub esp, 8 …                       ; takes a chapter number as its argument
0x43da33  push "RESOURCE.A00"                ; opens its archive by name
0x43da38  mov word [0x7fc576], 0x400         ; scene width  = 1024
0x43da41  mov word [0x7fc56e], 0x258         ; scene height = 600
0x43da7a  call 0x40bf70                      ; open archive  -> handle
0x43da8e  call 0x58dc60 (0x856420, 1, 0xa0)  ; read the 40-entry offset table
0x43daa0  call 0x58dc60 (0x94f420, 1, 0xa0)  ; read the 40-entry size table
0x43dab4  … loop, ebp = 6 …                  ; load the six animations, entries 0-5
0x43dafd  call 0x58dc60                      ; load the background, entry 6
0x43db10  mov edx, [esi + 0x85643c]          ; 0x856420 + 0x1c = entry 7
0x43db18  call 0x58d920                      ; seek to entry 7 + chapter
0x43db36  call 0x58dc60                      ; load it -> the chapter title overlay
0x43dc45  push "RESOURCE.003"                ; then the text and cue tables
```

Two things are worth extracting from this.

**The chapter title card is entry `7 + n`.** The scene takes a chapter number, masks it to a byte,
scales by 4 and indexes the archive's offset table at `0x85643c`, which is the table base `0x856420`
plus `0x1c` — entry 7. So `RESOURCE.A00` entries 7 to 12 are the six chapter titles, selected by the
scene's own argument. The archive is loaded once and reused between chapters.

**Frame counts are hardcoded.** Six calls to the sprite converter each take a byte from a table at
`0x5a46cc`:

```
b2 23 07 23 23 23   =   178, 35, 7, 35, 35, 35
```

Those are exactly the frame counts of `RESOURCE.A00` entries 0–5 as decoded from the archive. The
executable stores them as a literal table rather than reading them from the file.

---

## 3. The intro's timeline, and why it is not the general mechanism

The intro's per-frame update at `0x43dd40` is a chain of five identical blocks:

```
if (counter > threshold && step == n) { fireCue(1, 0, n + 4, 0x0b); step++; }
```

against a 64-bit counter at `0x8bc170`/`0x8bc174`, with thresholds:

| threshold | 72 | 214 | 356 | 498 | 641 |
| --- | --- | --- | --- | --- | --- |
| gap | | 142 | 142 | 142 | 143 |

The cue arguments identify the lines exactly. `RESOURCE.003` stage 100 (`= RESOURCE.A00`) row 0
frames 4 to 8 are five short exclamations:

| frame | text id | voice id | line | clip |
| --- | --- | --- | --- | --- |
| 4 | 514 | 481 | ¡los líos en los que me iba a ver metido! | 2.00 s |
| 5 | 515 | 482 | ¡los extraños personajes que se iban a cruzar en mi camino! | 2.89 s |
| 6 | 516 | 483 | ¡los cientos de peligros que me acecharían a cada paso! | 3.04 s |
| 7 | 517 | 484 | ¡Sí, toda una aventura! | 1.92 s |
| 8 | 518 | 485 | Pero empecemos por el principio. | 1.71 s |

Five thresholds, five consecutive frames, five short lines of one beat each. The match is not in doubt.

**The counter's unit is not established.** The five clips average 2.31 s against a uniform 142-unit
gap, which suits a ~60 Hz frame counter (2.37 s) far better than the 20 Hz animation tick (7.10 s) —
but the longest of the five is 3.04 s, which a 2.37 s gap would cut off, and the update only tests the
thresholds while no line is playing, so the counter may not advance during speech. Treat the gap as a
floor of unknown unit, not a duration.

**This device appears once in the whole executable.** Scanning `.text` for compares against that
counter finds 22 sites forming exactly one chain of more than two. So the evenly-paced threshold
timeline is a montage device written for this one sequence, **not** how scenes are driven in general.
The general mechanism is ordinary control flow.

### What is still missing

The chapter title's wipe-in is **not** found. Nothing located so far draws that overlay with a moving
horizontal reveal, so its duration and edge softness remain unmeasured.

---

## 4. What the executable confirms about the formats

Reading the loaders is a free check on the decoders, and every check passed.

**Sprites** (`0x436000`). The loop steps `edi` by `0xe` per frame record, reads a `u32` at `+0`, a
`u16` at `+0xc` as the segment count, and per segment reads 4 bytes of coordinates plus a `u8` count at
`+4` followed by `count` 16-bit pixels. That is the 14-byte frame record and the 5-byte segment header
exactly as [sprites.md](sprites.md) has them.

**Overlays** (`0x43cd00`). Reads a `u16` run count, then per run a 6-byte header (`u16 x`, `u16 y`,
`u16 length`) followed by `length` 16-bit pixels — [overlays.md](overlays.md) as written.

**Both loaders convert RGB565 to RGB555 in place**, with

```
pixel = ((p >> 1) & 0x7fe0) | (p & 0x1f)
```

This is worth stating plainly because it is easy to misread as evidence that the files are 555: it is
the opposite. The **files are RGB565**, and the original game degrades them to 555 on load to match its
DirectDraw surface. A decoder — or a re-implementation — that keeps 565 is reproducing the artwork more
faithfully than the original did.

**`RESOURCE.003`** (`0x43dc45` onward). The loader reads a 100,000-byte block (`push 0x186a0`), then a
`u8` and a `u16` count, then computes row strides with `eax*41` and `eax*321` — a 20,000-record cue
table of 5 bytes, a small-row stride of `0x29` and a large-row stride of `0x141`, matching
[global-data.md](global-data.md).

---

## 5. Reproducing this, and continuing it

This was done with a few throwaway Python scripts over `pefile` and `capstone` — no disassembler
install needed — which are not part of this repository. The method matters more than the scripts,
and it is short:

**To read a scene you have not read yet**, find a global that scene alone uses — a current-frame
or current-animation byte near its code — then scan `.text` for every 4-byte little-endian
occurrence of that address and look at where the hits fall. They cluster, and the cluster is the
block: `0x5d2ef8` gives 41 references, all within RVA `0x03cdbb`..`0x03f60b`. Disassemble a couple
of hundred bytes from the block's start, annotating any operand that points into `.rdata` with the
NUL-terminated string it finds there. Scene archive names then label themselves, so a scene's
opening identifies itself within a few instructions.

**Known anchors for Runaway 1:**

| address | what |
| --- | --- |
| `0x43da20` | the intro scene (`RESOURCE.A00`), takes a chapter number |
| `0x43dd40` | its per-frame update, the cue timeline |
| `0x43cd80` | its animation driver — animation index `0x5d2ef8`, frame `0x5d2ef1` |
| `0x450ed0` | `RESOURCE.B02` scene loader (first playable room: hospital) |
| `0x450580` | B02 actor depth scaling function |
| `0x434c20` | `placeActor(x, y, facing, flag, region)` |
| `0x413990` | `fireCue(stage, row, frame)` |
| `0x44e880` | actor walk dispatcher |
| `0x436000` | sprite loader / RGB565→555 converter |
| `0x43cd00` | overlay loader / converter |
| `0x40bf70` | open archive by name → handle |
| `0x58dc60` | read entry into a buffer |
| `0x58d920` | seek to entry |
| `0x5a46cc` | A00's hardcoded frame counts |

`0x32ea0`, `0x40c120`, `0x40dcf0`, `0x40e060` and `0x18dc60` are RVAs from the earlier Frida work
in the game folder's `_re/README.md`, and still check out.

---

## 6. A playable scene's code block: `RESOURCE.B02`

`RESOURCE.B02` (the hospital room in Chapter 1) is the first interactive scene of the game. Its loader
at `0x450ed0` and entry sequence at `0x44eb00` provide the prototype for all 71 playable scenes.

### Scene dimensions and camera
- **Dimensions**: width 1520 (`0x5f0`), height 600 (`0x258`), set at `0x45147f` and `0x451488`.
- **Viewport scroll limit**: `[0x8093f2] = 496` (`0x1f0` = `1520 - 1024`), set at `0x45149a`.
- **Archive layout**:
  - Entry 0: 1520×600 RGB565 background (1,824,000 bytes)
  - Entry 1: 3-byte continuous RLE mask (15,015 bytes = 5,005 runs)
  - Entry 2: 6-page zone attribute table (1,536 bytes)
  - Entry 3: walk route table (43,659 bytes)
  - Entries 4–23: animations, occluders, and overlays

### Player entry pose and placement
- Entry placement is executed at `0x44eb17` via `placeActor(x=1075, y=534, facing=7, 0, 0xff)`:
  - `x = 1075` (`0x433`), `y = 534` (`0x216`), facing 7.
- Character palette: `RESOURCE.000` slot 45 (Costume 2: Brian in red jacket and jeans).

### Actor depth scaling formula
Every playable scene installs a depth-scaling function pointer into `[0xac5a04]`. For B02, `0x4506b4`
installs `0x450580`, which implements a **piecewise linear ramp split at `x = 864` (`0x360`)**:

```
if (x < 864) {
    height = y * 1.2638888 - 302.06946;
} else {
    height = y * 0.44444444 + 188.77777;
}
scaleFactor = height / 455.0;
```

- **Left branch (`x < 864`)**: Hospital room floor.
  - Slope: `1.2638888` (stored at `0x4514cd` as float `0x3fa1c71c`).
  - Intercept: `-302.06946` (stored at `0x4514d7` as float `0xc39708e4`).
  - Corresponds to `horizonY = 239`, `fullY = 599`. At `y = 599`, `height = 455` px (100%). At minimum floor `y = 455`, `height = 273` px (60%).
- **Right branch (`x >= 864`)**: Corridor / doorway transition.
  - Slope: `0.44444444` (`4/9`, stored at `0x451501` as float `0x3ee38e39`).
  - Intercept: `188.77777` (`1700/9`, stored at `0x45150b` as float `0x433cc71c`).
  - At `y = 599`, `height = 455` px (100%). At doorway threshold `y = 411`, `height = 371.5` px (81.6%).
- Global normalization factor is `0.0021978023` = `1 / 455.0` (`fmul` at `0x5a46a8`), where 455 is Brian's authored base height.

### Opening sequence and speech cues
At `0x44eb5c`–`0x44ec1f`, the scene triggers Brian's internal monologue via `fireCue(stage, row, frame)`
at `0x413990`. In `RESOURCE.003` Stage 202 (Row 0):
1. Frame 0 (text 500, voice 598): "Se ha dormido, le han debido hacer efecto los tranquilizantes..."
2. Frame 1 (text 501–505, voice 599): "¡Qué historia! No sé qué pensar..."
3. Frame 2 (text 506–507, voice 604): "...¿cómo actuar en una situación como ésta?"
4. Frame 3 (text 508, voice 606): "Sí, creo que debo hacer algo para proteger la vida de Gina..."

## 7. Consequences

- **A faithful re-implementation cannot be data-driven for scene logic.** There is no script to
  interpret. Each scene has to be written, as the ScummVM `hollywood` engine writes them.
- **The archives are fully accounted for.** Every entry in a *Runaway 1* scene archive is art, a mask,
  a zone table or a route table. Searching them further for behaviour is searching for something that
  is not there — a result, not a gap.
- **The executable is the source for constants**, not just for logic: frame counts, scene dimensions,
  entry indices and cue numbers are all literals in code, and can be read out one scene at a time.
