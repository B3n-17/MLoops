# MLoops

A cross-platform desktop application for analyzing, visualizing, and exporting audio files with full metadata and loop point preservation.

## Features

- **Multi-Format Support** — Load and analyze WAV, FLAC, and OGG (Vorbis/Opus) files with automatic format detection
- **Metadata Parsing** — Deep inspection of RIFF chunks, FLAC blocks, Vorbis comments, broadcast extensions, cue points, and more
- **Loop Points** — Extract, display, and preserve loop points from WAV smpl chunks and JSON-encoded Vorbis comments
- **Waveform Visualization** — Real-time waveform rendering with peak detection, clipping indicators, and loop point overlay
- **Audio Playback** — Native playback with loop region support, crossfading, and 4-channel stereo pair control
- **Export** — Convert to FLAC, OGG Vorbis, or Opus with metadata preservation, resampling, and bit depth control

## Requirements

- [.NET 10.0](https://dotnet.microsoft.com/download) SDK or Runtime
- [FFmpeg](https://ffmpeg.org/) — required for decoding, waveform generation, and export

### FFmpeg Setup

FFmpeg must be placed next to the executable or selected via the file picker on first startup. It is resolved in order:

1. App-local bundle: place FFmpeg in `./ffmpeg/bin/` or `./ffmpeg/` next to the executable
2. CLI argument: `--ffmpeg-path=<directory>`
3. System `PATH`
4. Manual selection via startup prompt if not found automatically

## Build & Run

```bash
# Restore and run
dotnet run

# Publish a self-contained single-file executable
dotnet publish -c Release
```

## Tech Stack

| Component | Version |
|---|---|
| [Avalonia](https://avaloniaui.net/) | 11.3.12 |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.0 |
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 |

**Platforms:** Windows (x64), Linux (x64)

## Project Structure

```
src/
├── Models/          Metadata types for WAV, FLAC, OGG, export options
├── Services/        Audio parsing, playback, export, FFmpeg, waveform generation
├── ViewModels/      MVVM view models (main window, metadata, export, waveform)
└── Views/           Avalonia UI definitions
```

## Export Formats

| Format | Details |
|---|---|
| FLAC | Compression 1–12 (default 12), configurable bit depth (8/16/24) |
| OGG Vorbis | Quality Q10, 48 kHz output |
| Opus | 384 kbps VBR standard / max quality mode, 48 kHz |

All exports preserve standard tags (title, artist, album, genre, date, comments) and loop points as JSON in Vorbis comments.

## License

See [LICENSE](LICENSE) for details.
