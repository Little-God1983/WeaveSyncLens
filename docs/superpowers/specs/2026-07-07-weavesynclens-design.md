# WeaveSyncLens — Design Spec

**Date:** 2026-07-07
**Status:** Approved by user (brainstorming session)

## 1. Overview

WeaveSyncLens is a Windows desktop app (WPF, .NET 8). The user drops in an MP3 or MP4 file; the app extracts/decodes the audio, transcribes it locally with word-level timestamps, then plays it back showing a karaoke-style scrolling transcript — the currently spoken/sung word is highlighted — with a pluggable audio visualization strip at the bottom. The window is fully resizable with a true fullscreen mode, and the layout auto-scales to any aspect ratio (16:9, 32:9, …).

**v1 scope:** file import → local transcription → synced highlighted scrolling text + spectrum-bars visualizer + fullscreen mode.
**Step 2 (designed-for, not built in v1):** transcript editor and NSFW bracket-marking with visual hiding and audio mute/beep.
**Later options:** cloud transcription provider, vocal-isolation preprocessing (Mel-Band RoFormer sidecar), video display for MP4s.

## 2. Technology choices

| Concern | Choice | Notes |
|---|---|---|
| UI | WPF on .NET 8 | Windows-only per user decision; best text rendering control |
| Transcription | Whisper.net (whisper.cpp binding), in-process | Word-level timestamps; model auto-downloaded on first run; model size selectable |
| Playback + DSP | NAudio | MP3 decode, playback, sample tap → FFT |
| Container handling | FFMpegCore + bundled ffmpeg.exe | MP4 audio extraction; conversion to 16 kHz mono WAV for Whisper |
| Persistence | JSON sidecar files | `song.mp3` → `song.transcript.json` |

Rationale: single deployable app, fully offline and private, no Python/CLI dependencies in v1. The transcriber and visualizer are both behind interfaces so faster/cloud transcribers and new visualizations can be added without touching the UI.

## 3. Components

### MediaImporter
Accepts a file via drag-and-drop or Open dialog. For MP4 (or anything non-WAV/MP3), uses FFMpegCore to extract the audio track. Always produces a 16 kHz mono WAV for transcription input; playback uses the original audio (NAudio) directly.

### ITranscriber
```
Transcript Transcribe(string wavPath, IProgress<double> progress, CancellationToken ct)
```
Returns a `Transcript`: ordered list of `Word { Text, StartTime, EndTime, Flags }`.
- **v1 implementation:** `WhisperLocalTranscriber` (Whisper.net). Model size configurable (tiny/base/small/medium/large); model files downloaded on demand with progress UI.
- **Later:** cloud API implementation behind the same interface; optional vocal-isolation preprocessing hook that runs before transcription (Mel-Band RoFormer via external sidecar tool).

### Transcript store (sidecar)
JSON file next to the media: `<name>.transcript.json`. Contains a format `version`, source-file metadata, model used, and the word list. `Flags` on each word reserves room for Step-2 markers (e.g. NSFW). Reopening a media file with an existing sidecar skips transcription. Version field guards against format changes.

### PlaybackEngine
NAudio-based. Play / pause / seek; exposes `CurrentTime` (the sync clock for highlighting) and taps the sample stream into an FFT provider (e.g. 2048-sample windows) consumed by visualizers. Designed with a seam so a video renderer can attach later for MP4 files.

### KaraokeView
The scrolling transcript. Words laid out in wrapped lines sized to the viewport; the word whose `[StartTime, EndTime)` contains `CurrentTime` is highlighted; the view smoothly auto-scrolls to keep the active line vertically centered. Font size scales with window size. Clicking a word seeks playback to it.

### IVisualizer (pluggable)
```
void Render(VisualizerFrame frame, DrawingContext dc, Size area)
```
`VisualizerFrame` carries FFT magnitudes and raw waveform samples. Implementations are registered and selectable in settings/UI. **v1 implementation:** `SpectrumBarsVisualizer` (classic FFT frequency bars). Future: waveform, circular, etc. — adding a visualization means adding a class, not touching the app.

### Main window
Drag-and-drop target, transport controls (play/pause, seek bar, elapsed/total time), transcription progress overlay, settings (model size, visualizer choice), fullscreen toggle.

### Fullscreen mode
- Toggle via **F11**, double-click, or a button; **Esc** exits.
- True borderless fullscreen: window chrome, title bar, and transport controls disappear — only transcript + visualizer remain.
- Mouse movement briefly reveals the transport controls as an overlay; after a few seconds of inactivity they fade out and the cursor hides.

## 4. Data flow

```
File dropped
  → ffmpeg extract/convert (progress)
  → Whisper transcribes (progress)          [skipped if sidecar exists]
  → transcript saved as sidecar JSON
  → playback starts
  → per UI frame (~60 fps):
      playback position → KaraokeView highlight + scroll
      FFT frames → active IVisualizer draws
```

## 5. Layout & scaling

Vertical grid: transcript area ~75% of height, visualizer strip the remainder. Text uses viewport-relative sizing so fullscreen at 16:9 or 32:9 works without configuration. At ultrawide ratios the transcript column is capped at a readable max width and centered; the visualizer stretches full-width.

## 6. Error handling

- Unsupported/corrupt file → clear user-facing message.
- Transcription failure → offer retry, optionally with a different model size.
- Missing Whisper model → download prompt with progress bar.
- Sidecar version mismatch → re-transcribe (old file kept as backup).

## 7. Testing

Core logic lives in a WPF-free class library so it is unit-testable: transcript model + sidecar serialization round-trip, word-lookup-by-time, FFT binning for the bars. UI behavior and Whisper integration verified manually with known test files.

## 8. Step 2 (future, designed-for)

Transcript editor view (editable words/lines) to fix misheard lyrics. NSFW bracket-marking on word ranges, stored as `Flags` in the sidecar. During playback, flagged ranges render hidden/blurred and the audio is muted or beeped. Purely additive on top of the sidecar format — no v1 rework expected.

## 9. Effort estimate

| Piece | Effort |
|---|---|
| Project setup, playback, file import | ~1 day |
| Whisper.net integration + sidecar format | ~1 day |
| KaraokeView (highlight + smooth scroll + scaling) | ~1–2 days |
| Visualizer framework + spectrum bars | ~0.5–1 day |
| Polish (fullscreen, settings, errors) | ~1 day |
| **v1 total** | **~4–6 focused days** |
| Step 2 (editor + NSFW hide/beep) | ~2–3 days |
| Optional: vocal isolation sidecar / cloud transcriber / video display | ~1–2 days each |

Main risk: lyric transcription quality on dense music mixes — mitigated by the Step-2 editor and the vocal-isolation option.
