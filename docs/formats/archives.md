# Archive containers

The game engine uses nameless offset-table archives with no extensions, file paths, or magic headers. Every archive is a flat container indexed by entry number (`e00`, `e01`, …).

## Purpose

All game assets are packed into archives in the `Resource/`, `Dataa/`, or `Datav/` directories. Different categories of data use distinct table conventions, but all entry data is stored uncompressed (except for natively compressed formats like JPEG, PNG, MP3, or Bink in later releases).

## Archive types

### 1. Scene archives (`RESOURCE.<L><nn>`)

Present in all games (*Runaway 1*, *Runaway 2*, *Runaway 3*, *The Next BIG Thing*, *Yesterday*). Each scene in the game is encapsulated into its own archive, where `<L>` is an area letter (A–I) and `<nn>` is a scene index (e.g. `RESOURCE.A01`, `RESOURCE.SP1`).

#### Layout

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

> **Critical layout rule**: The table is split into two halves: **offsets first, then sizes**. Reading it as a single array of offsets results in every size value being misinterpreted as an offset, creating hundreds of corrupt, misaligned phantom slices.

#### Common scene entries

- **Entry 0 (`e00`)**: Scene background art. Full-screen RGB565 raster (R1–R3) or 1080p JPEG (*The Next BIG Thing*, *Yesterday*).
- **Entry 1 (`e01`)**: Primary scene interaction mask (RLE walkboxes, hotspots, depth planes).
- **Subsequent entries**: Additional overlays, sprite animations, secondary masks, or auxiliary scene tables.
- **Fixed data entries**:
  - `1536 bytes`: Per-scene attribute table (hotspot properties, walkbox flags, depth levels).
  - `43659 bytes`: Scene script/logic table.
  - `704 bytes`: Coordinate mapping table.

### 2. Audio archives (`RESOURCE.M<nn>`, `RESOURCE.S<nn>`, `RESOURCE.002`)

Hold background music tracks (`M`), sound effects and ambient loops (`S`), and cinematic audio (`002`).

#### Layout

```text
Table:
    u32 Offset[N]       -- absolute byte offsets; slot 0 is always 0

Data:
    <audio payloads>
```

Audio archives do not have a size half. Because slot 0 is always 0, the total table size in bytes is determined by the **smallest non-zero offset** in the table:

```text
table_bytes = min(Offset[i] for Offset[i] > 0)
slot_count  = table_bytes / 4
```

The size of each entry is computed as the gap to the next highest offset in file order. Multiple slots in `RESOURCE.002` may point to the exact same offset (aliased audio).

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
- Tests: [`ArchivesTests`](../../tests/RunawayExplorer.Core.Tests/ArchivesTests.cs)
