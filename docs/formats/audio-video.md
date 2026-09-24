# Audio and video

The engine stores music, sound effects, voice acting, and cutscene video across several dedicated archives and container formats.

## Audio formats

### 1. Raw PCM audio (*Runaway 1*)

All sound in *Runaway 1* is stored as headerless, uncompressed PCM audio:

- **Music (`RESOURCE.M<nn>`)**: 16-bit signed stereo PCM at **16,000 Hz**.
- **Ambient & SFX (`RESOURCE.S<nn>`)**: 16-bit signed stereo PCM at **22,050 Hz**.
- **Cinematic audio (`RESOURCE.002`)**: 16-bit signed stereo PCM at **22,050 Hz**.
- **Voice acting (`DATAACA<0-6>.000`)**: 8-bit unsigned mono PCM at **16,000 Hz**.

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
- Tests: [`WavWriterTests`](../../tests/RunawayExplorer.Core.Tests/WavWriterTests.cs)
