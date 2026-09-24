# Archive containers

The game engine uses nameless offset-table archives with no extensions, file paths, or magic headers. Every archive is a flat container indexed by entry number (`e00`, `e01`, …).

## Purpose

All game assets are packed into archives in the `Resource/`, `Dataa/`, or `Datav/` directories. Different categories of data use distinct table conventions, but all entry data is stored uncompressed (except for natively compressed formats like JPEG, PNG, MP3, or Bink in later releases).

## Archive types

### 1. Scene archives (`RESOURCE.<L><nn>`)

Present in all games (*Hollywood Monsters*, *Runaway 1*, *Runaway 2*, *Runaway 3*, *The Next BIG Thing*, *Yesterday*). Each scene in the game is encapsulated into its own archive, where `<L>` is an area letter (A–I) and `<nn>` is a scene index (e.g. `RESOURCE.A01`, `RESOURCE.SP1`).

#### Layout variants

##### Variant A: Hollywood Monsters, Runaway 1, 3, The Next BIG Thing, Yesterday

The table starts immediately at offset 0:

```text
Table:
    u32 Offset[N]       -- absolute byte offsets to each entry data
    u32 Size[N]         -- exact byte sizes of each entry data

Data:
    <entry 0 payload>
    <entry 1 payload>
    ...
```

The table length is determined by `Offset[0]`, because Entry 0 immediately follows the table header:

```text
table_bytes = Offset[0]
entry_count = table_bytes / 8
```

For example, an archive with `Offset[0] == 320` has 40 entries (`320 / 8`). An unused entry slot has `Offset[i] == 0` and `Size[i] == 0`.

##### Variant B: Runaway 2

In *Runaway 2*, the table begins with a 4-byte header specifying the half-table size, followed by the entry 0 offset:

```text
Header:
    u32 TableHalfBytes  -- size of the offset half in bytes (Count * 4)
    u32 Entry0Offset    -- 4 + (TableHalfBytes * 2), identical to the start of entry 0 data

Table:
    u32 Offset[Count]   -- at byte 4
    u32 Size[Count]     -- at byte 4 + TableHalfBytes

Data:
    <entry 0 payload>   -- begins at Entry0Offset
    ...
```

> **Critical layout rule**: In both variants, the table is split into two halves: **offsets first, then sizes**. Reading it as a single flat array of offsets results in every size value being misinterpreted as an offset, creating hundreds of corrupt, misaligned phantom slices.

##### Special case: Hollywood Monsters headerless screen (`RESOURCE.I02`)

One *Hollywood Monsters* scene archive (`RESOURCE.I02`) has no table at all. It consists of a bare 1024×480 indexed raster (491,520 bytes) followed immediately by its 768-byte palette (`IndexedPalette.FullBlockBytes`), totaling exactly 492,288 bytes.

##### Hollywood Monsters playable-scene chunk layout

In *Hollywood Monsters* the first five entries of a playable scene archive are **fixed by index**, not
discovered by classification, and the table always declares 40 slots (`table_len == 320`):

| Entry | Contents | Size |
|---|---|---|
| `e00` | Background framebuffer | 491,520 bytes (1024×480) |
| `e01` | Scene palette block ([palettes.md](palettes.md)) | 528 or 768 bytes |
| `e02` | Region-map fill runs ([masks.md](masks.md)) | variable, 3-byte RLE |
| `e03` | Region lookup tables ([masks.md](masks.md)) | 1,792 bytes (7 × 256) |
| `e04` | Scene metadata tables (hotspots, routes, item points) | variable, usually > 24 KiB |
| `e05`.. | Sprites, overlays, and any further palette blocks | variable |

Verified against the shipped game: 79 of the 102 tabled archives have `e03` exactly 1,792 bytes long. The
23 exceptions are the `?00` chapter-opening archives and the `I*` intro/special archives, which are
presentation scenes rather than playable ones and do not follow the layout.

Entry `e04` is itself a pack of fixed-offset tables, the scene's compiled logic:

| Offset | Table |
|---|---|
| `0x0000` | Actor depth thresholds |
| `0x002a` | Palette delta table |
| `0x003f` | Palette adjustment table |
| `0x007d` | Route boundary points |
| `0x1529` | Route boundary steps |
| `0x35e4` | Scene item default strip |
| `0x35f9` | Scene item interaction points |
| `0x364d` | Scene item approach points |
| `0x36a1` | Scene item facing |
| `0x36b6` | Verb action records |
| `0x3956` | Relation records |
| `0x610a` | Mode-2 relation overlay |

A scene carries 21 items, and the route tables are sized `21 x 21` by candidate or step count — walk
routing is precomputed between every pair of walkable regions rather than solved at runtime. This is why
`e04` is usually larger than 24 KiB: 91 of the 102 archives clear `0x610a`.

The explorer does not rely on this layout -- it classifies every entry structurally, so it also reads the
archives that deviate -- but the layout explains *why* `e01` is always a palette and `e02`/`e03` are always
the region data, and it names two entries that would otherwise be filed as anonymous data.

#### Common scene entries

- **Entry 0 (`e00`)**: Scene background art. Full-screen RGB565 raster (R1–R3), 1024×480 indexed bitmap (*Hollywood Monsters*), or 1080p JPEG (*The Next BIG Thing*, *Yesterday*).
- **Entry 1 (`e01`)**: Primary scene interaction mask (RLE walkboxes, hotspots, depth planes).
- **Subsequent entries**: Additional overlays, sprite animations, secondary masks, or auxiliary scene tables.
- **Fixed data entries**:
  - `1536 bytes`: Per-scene attribute table (hotspot properties, walkbox flags, depth levels).
  - `43659 bytes`: Scene script/logic table.
  - `704 bytes`: Coordinate mapping table.

### 2. Audio archives (`RESOURCE.M<nn>`, `RESOURCE.S<nn>`, `RESOURCE.002`)

Hold background music tracks (`M`), sound effects and ambient loops (`S`), and cinematic audio (`002`).

#### Runaway 1 layout (plain offsets)

```text
Table:
    u32 Offset[N]       -- absolute byte offsets; slot 0 is always 0

Data:
    <audio payloads>
```

Audio archives in *Runaway 1* do not have a size half. Because slot 0 is always 0, the total table size in bytes is determined by the **smallest non-zero offset** in the table:

```text
table_bytes = min(Offset[i] for Offset[i] > 0)
slot_count  = table_bytes / 4
```

The size of each entry is computed as the gap to the next highest offset in file order. Multiple slots in `RESOURCE.002` may point to the exact same offset (aliased audio).

#### Runaway 2 layout (explicit size and format)

In *Runaway 2*, audio archives carry explicit sizes and format tags:

```text
Header:
    u32 Count           -- number of audio records
    u32 FirstOffset     -- 4 + (Count * 9), pointing to first audio payload

Records (Count records × 9 bytes):
    u32 Offset          -- absolute byte offset
    u32 Size            -- byte size of audio stream
    u8  Format          -- 0 = WAV / raw PCM, 1 = MP3
```

#### Hollywood Monsters layout (terminated offset table)

In *Hollywood Monsters*, audio tables (`RESOURCE.M*`, `S*`, `001`, `002`, `004`) use an offset table with no size half, terminated by a slot holding the file length. Slot 1 always holds the table's own byte length. A clip's size is the gap to the next slot; runs of repeated offsets denote unused entries and are dropped.

### 3. Voice archives (`DATAACA<0-6>.000` / shards)

Dialogue voice clips are sharded across multiple large archives. In *Runaway 1*, seven shards (`DATAACA0.000` to `DATAACA6.000`) provide up to 12,000 clip slots.

#### Layout

```text
Table:
    u32 Offset[12000]   -- 48,000-byte table of absolute file offsets

Data:
    <voice payloads>
```

A clip ID can appear in any shard. If an offset in a shard is greater than or equal to that shard's file size, it indicates the clip resides in another shard. The reader searches shards sequentially until a valid offset is found.

### 4. Video keyfiles (`DATAVC00.<nnn>`)

Bink video files in `Datav/DATAV*.<nnn>` have their first 1024 bytes replaced with obfuscated padding. The legitimate 1024-byte Bink headers are stored in an encrypted catalog file (`DATAVC00.<nnn>`).

#### Layout

```text
Header:
    u32 DataOffset      -- 8 + (N * 15); low byte is the XOR seed (0x21)
    u32 VideoCount      -- number of video header records (N)

Catalogue (N records):
    byte[13] Filename   -- null-padded ASCII filename (e.g. "INTRO.BIK")
    u16      Flags      -- 2 bytes metadata

Header blocks (N blocks):
    byte[2048] Block    -- first 1024 bytes contain the true Bink header
```

The catalog and header blocks are XOR-chained:

```text
plain[0] = cipher[0] ^ seed
plain[i] = cipher[i] ^ cipher[i - 1]
```

Restoring a video consists of reading the decrypted 1024-byte header and appending the video file's payload starting at offset 1024:

```text
restored_bik = decrypted_header[0..1024] + datav_file[1024..]
```

## Known unknowns

- The precise format of the 43,659-byte scene data tables.
- The meaning of the 2-byte trailer flags in the video keyfile filename records.

## Decoders

- [`SceneArchive`](../../src/RunawayExplorer.Core/FileSystem/Archives.cs)
- [`AudioArchive`](../../src/RunawayExplorer.Core/FileSystem/Archives.cs)
- [`VoiceArchive`](../../src/RunawayExplorer.Core/FileSystem/Archives.cs)
- [`VideoKeyfile`](../../src/RunawayExplorer.Core/FileSystem/VideoKeyfile.cs)
- [`ArchiveWindowStream`](../../src/RunawayExplorer.Core/FileSystem/Archives.cs)
- Tests: [`ContainerTests`](../../tests/RunawayExplorer.Core.Tests/ContainerTests.cs), [`HollywoodMonstersTests`](../../tests/RunawayExplorer.Core.Tests/HollywoodMonstersTests.cs)
