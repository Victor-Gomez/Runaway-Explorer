# Audio and video

The engine stores music, sound effects, voice acting, and cutscene video across several dedicated archives and container formats.

## Audio formats

### 1. Raw PCM audio (*Runaway 1* & *Hollywood Monsters*)

All sound in *Runaway 1* and *Hollywood Monsters* is stored as headerless, uncompressed PCM audio:

#### Runaway 1
- **Music (`RESOURCE.M<nn>`)**: 16-bit signed stereo PCM at **16,000 Hz**.
- **Ambient & SFX (`RESOURCE.S<nn>`)**: 16-bit signed stereo PCM at **22,050 Hz**.
- **Cinematic audio (`RESOURCE.002`)**: 16-bit signed stereo PCM at **22,050 Hz**.
- **Voice acting (`DATAACA<0-6>.000`)**: 8-bit unsigned mono PCM at **16,000 Hz**.

#### Hollywood Monsters
- **Music & Cinematic audio** (`RESOURCE.M<nn>`, `RESOURCE.002`): 16-bit signed mono PCM at **11,025 Hz**.
- **Sound effects** (`RESOURCE.S<nn>`, `RESOURCE.001`): 8-bit unsigned mono PCM at **11,025 Hz**.
- **Voice acting** (`RESOURCE.004`): 8-bit unsigned mono PCM at **22,050 Hz** — twice the effect rate.
- **Resident sound effects** (`RESOURCE.000` entries `0x55`–`0x62`): the 14 effects that must be audible
  in every scene live in the tail of the global archive rather than in a sound bank, at the same 8-bit
  11,025 Hz shape. Several slots are byte-for-byte aliases of one another (`0x55`, `0x59` and `0x5d` are
  one sound; `0x57`, `0x5a`, `0x5e` and `0x61` another), which is how the engine's effect table addresses
  16 effect ids across 14 entries. The explorer plays and exports them like any other clip.

Voice is the one bank that runs at 22,050 Hz, so it carries its own PCM shape rather than sharing the
sound-effect one. And unlike *Runaway*'s voice lines, *Hollywood Monsters*' are not subject to the
adjustable voice sample rate in Settings: that setting exists because *Runaway* stores no rate at all.

No rate or channel count is stored anywhere. The streams are mono rather than interleaved stereo: the
autocorrelation of the 16-bit music decays smoothly from lag 1, which it would not do if consecutive
samples belonged to different channels. There are no videos — the game ships no Bink data and no keyfile.

The rates were not obvious. `Monsters.exe` builds its `WAVEFORMATEX` at runtime rather than storing one,
so there is no structure to read off, and both 11,025 and 22,050 appear in the binary as bare constants.
Two independent checks settled them:

- **Sound effects: an embedded header.** One slot of `RESOURCE.S01` (offset 1,867,614, 25,572 bytes) was
  shipped as a complete RIFF/WAVE file rather than a bare stream, and its `fmt ` chunk states PCM, mono,
  **11,025 Hz, 8-bit**. It is the only self-describing audio in the game, and it describes the bank it
  sits in.
- **Music and voice: by ear.** Exporting the same bytes under both rates and listening is decisive for
  tempo and pitch: music is right at 11,025 Hz and plays at double tempo at 22,050, while voice is right
  at 22,050 Hz and drags at 11,025.

This matches the ScummVM `hollywood` engine, a separate work-in-progress reverse-engineering effort, which
reads music and effects as 11,025 Hz and Spanish speech as 22,050 Hz. Note that it reads the **Italian**
release's speech as 11,025 Hz 16-bit instead, so the voice rate may be per-release; the figures above were
checked against a Spanish install.

#### WAV encapsulation

To play or export raw PCM streams, the decoder synthesizes a standard 44-byte RIFF/WAVE header:

```text
0x00: "RIFF"
0x04: u32 (PayloadSize + 36)
0x08: "WAVE"
0x0C: "fmt "
0x10: u32 16                    -- format chunk size
0x14: u16 1                     -- PCM format code
0x16: u16 Channels              -- 1 (mono) or 2 (stereo)
0x18: u32 SampleRate            -- 16000 or 22050
0x1C: u32 ByteRate              -- SampleRate * Channels * (BitsPerSample / 8)
0x20: u16 BlockAlign            -- Channels * (BitsPerSample / 8)
0x22: u16 BitsPerSample         -- 8 or 16
0x24: "data"
0x28: u32 PayloadSize
0x2C: <raw PCM bytes>
```

### 2. MPEG-1 Audio Layer III / MP3 (*Runaway 2 & 3*)

Starting with *Runaway 2*, dialogue and music tracks are encoded as standard MP3 frames:
- Sync word: `0xFF 0xFB` or `0xFF 0xFA` (11-bit MPEG sync).
- Decoded in-process via NLayer or routed to LibVLC for streaming playback.

## Video format: Bink Video (`.bik`)

Cutscenes in `Datav/` are standard RAD Game Tools Bink Video files ("BIKi" or "KB2" fourcc).

### Header encryption and restoration

On disk, the first 1024 bytes of every `DATAV*.<nnn>` file are zeroed or filled with obfuscated dummy data to prevent direct opening with standard video players.

The real 1024-byte headers are packed in the encrypted `DATAVC00.<nnn>` keyfile (see [archives.md](archives.md)). The explorer restores the original `.bik` file in memory:

```csharp
byte[] restoredHeader = keyfile.GetHeader(videoFilename); // 1024 bytes
Stream videoStream = new ConcatenatedStream(
    new MemoryStream(restoredHeader),
    new SubStream(fileStream, offset: 1024)
);
```

Video playback is rendered via LibVLC with audio synchronization and direct hardware acceleration.

## Decoders

- [`WavWriter`](../../src/RunawayExplorer.Core/Formats/WavWriter.cs)
- [`VideoKeyfile`](../../src/RunawayExplorer.Core/FileSystem/VideoKeyfile.cs)
- Tests: [`WriterTests`](../../tests/RunawayExplorer.Core.Tests/WriterTests.cs), [`HollywoodMonstersTests`](../../tests/RunawayExplorer.Core.Tests/HollywoodMonstersTests.cs)
