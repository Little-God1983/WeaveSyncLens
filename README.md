# WeaveSyncLens

Drop in an MP3/MP4 → local Whisper transcription → karaoke-style highlighted,
auto-scrolling transcript synced to playback, with a spectrum-bars visualizer.
Windows, WPF, fully offline.

## Setup

1. Install the .NET 10 SDK.
2. Fetch ffmpeg (one time): `powershell -ExecutionPolicy Bypass -File scripts/setup-ffmpeg.ps1`
3. Run: `dotnet run --project src/WeaveSyncLens.App`

First transcription downloads the selected Whisper model (~75 MB tiny … ~3 GB large-v3)
into `models/` under the app root (the repo root when running via `dotnet run`; the folder
containing the executable in a published build) — same convention as `third_party/ffmpeg/`.
Settings and cached transcripts are stored at `%APPDATA%\WeaveSyncLens\settings.json` and as `<file>.transcript.json` sidecars next to media files, respectively.

## Usage

- Drag a `.mp3` / `.mp4` / `.wav` / `.m4a` onto the window (or click Open).
- Space = play/pause · click a word = seek there · F11 or double-click = fullscreen · Esc = exit fullscreen.
- Transcripts are cached as `<file>.transcript.json` next to the media; delete it to re-transcribe.
- Model size picker is in the bottom bar (bigger = more accurate, slower).

## Tests

`dotnet test` — set `WSL_RUN_WHISPER_TESTS=1` to include the real Whisper integration test.

## Architecture

- `src/WeaveSyncLens.Core` — transcript model, sidecar store, FFT/binning, import, Whisper transcription. No WPF.
- `src/WeaveSyncLens.App` — WPF UI: KaraokeView, pluggable visualizers (`IVisualizer`), fullscreen.
- Spec: `docs/superpowers/specs/2026-07-07-weavesynclens-design.md`
