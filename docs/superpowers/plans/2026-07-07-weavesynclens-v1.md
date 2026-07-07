# WeaveSyncLens v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows WPF app that takes an MP3/MP4, transcribes it locally with word timestamps, and plays it back with a karaoke-style highlighted scrolling transcript plus a pluggable spectrum-bars visualizer, including fullscreen mode.

**Architecture:** Three projects — `WeaveSyncLens.Core` (WPF-free class library: transcript model, sidecar store, FFT binning, import/transcription services), `WeaveSyncLens.App` (WPF UI: playback view, KaraokeView, visualizer host, fullscreen), `WeaveSyncLens.Core.Tests` (xUnit). Media flows: file → ffmpeg → 16 kHz WAV → Whisper.net → JSON sidecar → NAudio playback drives word highlighting and FFT frames.

**Tech Stack:** .NET 8, WPF, Whisper.net 1.7.4, NAudio 2.2.1, FFMpegCore 5.1.0, CommunityToolkit.Mvvm 8.2.2, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-07-weavesynclens-design.md`

## Global Constraints

- Target framework: `net8.0` for Core/Tests, `net8.0-windows` with `<UseWPF>true</UseWPF>` for App.
- Windows-only; everything runs offline (only network use: one-time Whisper model download).
- Package versions: Whisper.net **1.7.4**, Whisper.net.Runtime **1.7.4**, NAudio **2.2.1**, FFMpegCore **5.1.0**, CommunityToolkit.Mvvm **8.2.2**.
- Sidecar file naming: `<mediaPathWithoutExtension>.transcript.json`, format `version: 1`.
- Namespaces: `WeaveSyncLens.Core.*` and `WeaveSyncLens.App.*` matching folder names.
- All times in the transcript model are **seconds as `double`**.
- ffmpeg.exe lives at `third_party/ffmpeg/ffmpeg.exe` (git-ignored), fetched by `scripts/setup-ffmpeg.ps1`; fall back to PATH if missing.
- Run tests with `dotnet test` from repo root; it must pass before every commit.
- External-library note: if a Whisper.net/FFMpegCore method named here differs slightly in the installed package (e.g. `WhisperGgmlDownloader.Default.GetGgmlModelAsync` vs static), check the package's IntelliSense/source and use the equivalent — do not change package versions.

---

### Task 1: Solution scaffold

**Files:**
- Create: `WeaveSyncLens.sln`, `src/WeaveSyncLens.Core/WeaveSyncLens.Core.csproj`, `src/WeaveSyncLens.App/WeaveSyncLens.App.csproj` (+ generated `App.xaml`, `MainWindow.xaml`), `tests/WeaveSyncLens.Core.Tests/WeaveSyncLens.Core.Tests.csproj`, `.gitignore`

**Interfaces:**
- Consumes: nothing
- Produces: buildable solution all later tasks compile inside

- [ ] **Step 1: Create projects and solution**

```powershell
cd e:\Repos\AudioVisualizer
dotnet new classlib -n WeaveSyncLens.Core -o src/WeaveSyncLens.Core -f net8.0
dotnet new wpf -n WeaveSyncLens.App -o src/WeaveSyncLens.App -f net8.0
dotnet new xunit -n WeaveSyncLens.Core.Tests -o tests/WeaveSyncLens.Core.Tests -f net8.0
dotnet new sln -n WeaveSyncLens
dotnet sln add src/WeaveSyncLens.Core src/WeaveSyncLens.App tests/WeaveSyncLens.Core.Tests
dotnet add src/WeaveSyncLens.App reference src/WeaveSyncLens.Core
dotnet add tests/WeaveSyncLens.Core.Tests reference src/WeaveSyncLens.Core
```

Delete the template files `src/WeaveSyncLens.Core/Class1.cs` and `tests/WeaveSyncLens.Core.Tests/UnitTest1.cs`.

- [ ] **Step 2: Add packages**

```powershell
dotnet add src/WeaveSyncLens.Core package NAudio --version 2.2.1
dotnet add src/WeaveSyncLens.Core package Whisper.net --version 1.7.4
dotnet add src/WeaveSyncLens.Core package Whisper.net.Runtime --version 1.7.4
dotnet add src/WeaveSyncLens.Core package FFMpegCore --version 5.1.0
dotnet add src/WeaveSyncLens.App package CommunityToolkit.Mvvm --version 8.2.2
```

- [ ] **Step 3: Write `.gitignore`**

```gitignore
bin/
obj/
*.user
third_party/ffmpeg/
models/
.vs/
```

- [ ] **Step 4: Verify build and tests run**

Run: `dotnet build` → Expected: `Build succeeded. 0 Error(s)`
Run: `dotnet test` → Expected: passes (0 tests is fine at this point)

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "chore: scaffold WeaveSyncLens solution (Core, App, Tests)"
```

---

### Task 2: Transcript model + active-word lookup

**Files:**
- Create: `src/WeaveSyncLens.Core/Models/Word.cs`, `src/WeaveSyncLens.Core/Models/Transcript.cs`
- Test: `tests/WeaveSyncLens.Core.Tests/TranscriptTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces:
  - `record Word(string Text, double Start, double End, WordFlags Flags = WordFlags.None)` and `[Flags] enum WordFlags { None = 0, Nsfw = 1 }`
  - `class Transcript { IReadOnlyList<Word> Words; int FindActiveWordIndex(double timeSeconds, int hintIndex = -1); }` — returns index of word whose `[Start, End)` contains the time, else the last word already started, else `-1`.

- [ ] **Step 1: Write the failing tests**

`tests/WeaveSyncLens.Core.Tests/TranscriptTests.cs`:

```csharp
using WeaveSyncLens.Core.Models;
using Xunit;

namespace WeaveSyncLens.Core.Tests;

public class TranscriptTests
{
    private static Transcript Sample() => new(new[]
    {
        new Word("Hello", 0.5, 0.9),
        new Word("world", 1.0, 1.4),
        new Word("again", 2.0, 2.6),
    });

    [Fact]
    public void FindActiveWordIndex_BeforeFirstWord_ReturnsMinusOne()
        => Assert.Equal(-1, Sample().FindActiveWordIndex(0.2));

    [Fact]
    public void FindActiveWordIndex_InsideWord_ReturnsThatWord()
        => Assert.Equal(1, Sample().FindActiveWordIndex(1.2));

    [Fact]
    public void FindActiveWordIndex_InGapBetweenWords_ReturnsPreviousWord()
        => Assert.Equal(1, Sample().FindActiveWordIndex(1.7));

    [Fact]
    public void FindActiveWordIndex_AfterLastWord_ReturnsLastWord()
        => Assert.Equal(2, Sample().FindActiveWordIndex(99.0));

    [Fact]
    public void FindActiveWordIndex_WithHint_ReturnsSameResult()
    {
        var t = Sample();
        Assert.Equal(t.FindActiveWordIndex(2.1), t.FindActiveWordIndex(2.1, hintIndex: 1));
        Assert.Equal(t.FindActiveWordIndex(0.6), t.FindActiveWordIndex(0.6, hintIndex: 2));
    }

    [Fact]
    public void EmptyTranscript_ReturnsMinusOne()
        => Assert.Equal(-1, new Transcript(Array.Empty<Word>()).FindActiveWordIndex(1.0));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter TranscriptTests`
Expected: FAIL — compile error, `Word`/`Transcript` not defined.

- [ ] **Step 3: Implement the model**

`src/WeaveSyncLens.Core/Models/Word.cs`:

```csharp
namespace WeaveSyncLens.Core.Models;

[Flags]
public enum WordFlags
{
    None = 0,
    Nsfw = 1,
}

/// <summary>One transcribed word. Times are seconds from media start.</summary>
public record Word(string Text, double Start, double End, WordFlags Flags = WordFlags.None);
```

`src/WeaveSyncLens.Core/Models/Transcript.cs`:

```csharp
namespace WeaveSyncLens.Core.Models;

public class Transcript
{
    public IReadOnlyList<Word> Words { get; }

    public Transcript(IReadOnlyList<Word> words) => Words = words;

    /// <summary>
    /// Index of the word active at <paramref name="timeSeconds"/>: the word whose
    /// [Start, End) contains it, otherwise the most recent word already started.
    /// -1 if playback is before the first word (or transcript is empty).
    /// <paramref name="hintIndex"/> is a previous result used to skip the search
    /// during normal forward playback; any value is safe.
    /// </summary>
    public int FindActiveWordIndex(double timeSeconds, int hintIndex = -1)
    {
        if (Words.Count == 0) return -1;

        // Fast path: time still inside hint word or the next one (normal playback tick).
        if (hintIndex >= 0 && hintIndex < Words.Count && timeSeconds >= Words[hintIndex].Start)
        {
            if (hintIndex + 1 >= Words.Count || timeSeconds < Words[hintIndex + 1].Start)
                return hintIndex;
            if (hintIndex + 2 >= Words.Count || timeSeconds < Words[hintIndex + 2].Start)
                return hintIndex + 1;
        }

        // Binary search for the last word with Start <= time.
        int lo = 0, hi = Words.Count - 1, result = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (Words[mid].Start <= timeSeconds) { result = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return result;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter TranscriptTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat: transcript model with active-word lookup"
```

---

### Task 3: Sidecar store

**Files:**
- Create: `src/WeaveSyncLens.Core/Sidecar/TranscriptSidecarStore.cs`
- Test: `tests/WeaveSyncLens.Core.Tests/TranscriptSidecarStoreTests.cs`

**Interfaces:**
- Consumes: `Word`, `WordFlags`, `Transcript` (Task 2)
- Produces:
  - `static class TranscriptSidecarStore` with:
    - `string GetSidecarPath(string mediaPath)` → `<dir>/<name-without-ext>.transcript.json`
    - `void Save(string mediaPath, Transcript transcript, string modelName)`
    - `Transcript? TryLoad(string mediaPath)` → null if missing or version ≠ 1 (mismatched file renamed to `*.transcript.json.bak`)

- [ ] **Step 1: Write the failing tests**

`tests/WeaveSyncLens.Core.Tests/TranscriptSidecarStoreTests.cs`:

```csharp
using WeaveSyncLens.Core.Models;
using WeaveSyncLens.Core.Sidecar;
using Xunit;

namespace WeaveSyncLens.Core.Tests;

public class TranscriptSidecarStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wsl-test").FullName;
    private string MediaPath => Path.Combine(_dir, "song.mp3");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void GetSidecarPath_ReplacesExtension()
        => Assert.Equal(Path.Combine(_dir, "song.transcript.json"),
                        TranscriptSidecarStore.GetSidecarPath(MediaPath));

    [Fact]
    public void SaveThenLoad_RoundTripsWordsAndFlags()
    {
        var t = new Transcript(new[]
        {
            new Word("Hello", 0.5, 0.9),
            new Word("world", 1.0, 1.4, WordFlags.Nsfw),
        });
        TranscriptSidecarStore.Save(MediaPath, t, "base");

        var loaded = TranscriptSidecarStore.TryLoad(MediaPath);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Words.Count);
        Assert.Equal(new Word("Hello", 0.5, 0.9), loaded.Words[0]);
        Assert.Equal(WordFlags.Nsfw, loaded.Words[1].Flags);
    }

    [Fact]
    public void TryLoad_NoFile_ReturnsNull()
        => Assert.Null(TranscriptSidecarStore.TryLoad(MediaPath));

    [Fact]
    public void TryLoad_WrongVersion_ReturnsNullAndKeepsBackup()
    {
        var sidecar = TranscriptSidecarStore.GetSidecarPath(MediaPath);
        File.WriteAllText(sidecar, """{"version": 999, "words": []}""");

        Assert.Null(TranscriptSidecarStore.TryLoad(MediaPath));
        Assert.False(File.Exists(sidecar));
        Assert.True(File.Exists(sidecar + ".bak"));
    }

    [Fact]
    public void TryLoad_CorruptJson_ReturnsNull()
    {
        File.WriteAllText(TranscriptSidecarStore.GetSidecarPath(MediaPath), "not json {");
        Assert.Null(TranscriptSidecarStore.TryLoad(MediaPath));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter TranscriptSidecarStoreTests`
Expected: FAIL — `TranscriptSidecarStore` not defined.

- [ ] **Step 3: Implement the store**

`src/WeaveSyncLens.Core/Sidecar/TranscriptSidecarStore.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaveSyncLens.Core.Models;

namespace WeaveSyncLens.Core.Sidecar;

/// <summary>Reads/writes the JSON transcript sidecar next to a media file.</summary>
public static class TranscriptSidecarStore
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed class SidecarDto
    {
        public int Version { get; set; }
        public string? SourceFile { get; set; }
        public string? Model { get; set; }
        public DateTime CreatedUtc { get; set; }
        public List<WordDto> Words { get; set; } = new();
    }

    private sealed class WordDto
    {
        public string Text { get; set; } = "";
        public double Start { get; set; }
        public double End { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public WordFlags Flags { get; set; }
    }

    public static string GetSidecarPath(string mediaPath) =>
        Path.Combine(Path.GetDirectoryName(mediaPath)!,
                     Path.GetFileNameWithoutExtension(mediaPath) + ".transcript.json");

    public static void Save(string mediaPath, Transcript transcript, string modelName)
    {
        var dto = new SidecarDto
        {
            Version = CurrentVersion,
            SourceFile = Path.GetFileName(mediaPath),
            Model = modelName,
            CreatedUtc = DateTime.UtcNow,
            Words = transcript.Words
                .Select(w => new WordDto { Text = w.Text, Start = w.Start, End = w.End, Flags = w.Flags })
                .ToList(),
        };
        File.WriteAllText(GetSidecarPath(mediaPath), JsonSerializer.Serialize(dto, JsonOptions));
    }

    public static Transcript? TryLoad(string mediaPath)
    {
        var path = GetSidecarPath(mediaPath);
        if (!File.Exists(path)) return null;

        SidecarDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SidecarDto>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        if (dto is null) return null;

        if (dto.Version != CurrentVersion)
        {
            File.Move(path, path + ".bak", overwrite: true);
            return null;
        }

        return new Transcript(dto.Words
            .Select(w => new Word(w.Text, w.Start, w.End, w.Flags))
            .ToList());
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter TranscriptSidecarStoreTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat: JSON transcript sidecar store with versioning"
```

---

### Task 4: FFT processing + spectrum binning

**Files:**
- Create: `src/WeaveSyncLens.Core/Audio/FftProcessor.cs`, `src/WeaveSyncLens.Core/Audio/SpectrumBinner.cs`
- Test: `tests/WeaveSyncLens.Core.Tests/FftTests.cs`

**Interfaces:**
- Consumes: `NAudio.Dsp.FastFourierTransform`, `NAudio.Dsp.Complex`
- Produces:
  - `class FftProcessor { FftProcessor(int fftLength = 2048); event Action<float[]>? FftCalculated; void AddSamples(float[] buffer, int offset, int count, int channels); }` — downmixes interleaved samples to mono, Hann-windows, raises `FftCalculated` with `fftLength/2` magnitudes each time the buffer fills.
  - `static class SpectrumBinner { static float[] BinToBars(float[] magnitudes, int barCount, int sampleRate, int fftLength); }` — log-spaced frequency bins (60 Hz–16 kHz), each bar 0..1.

- [ ] **Step 1: Write the failing tests**

`tests/WeaveSyncLens.Core.Tests/FftTests.cs`:

```csharp
using WeaveSyncLens.Core.Audio;
using Xunit;

namespace WeaveSyncLens.Core.Tests;

public class FftTests
{
    private static float[] Sine(double freq, int sampleRate, int count)
    {
        var s = new float[count];
        for (int i = 0; i < count; i++)
            s[i] = (float)Math.Sin(2 * Math.PI * freq * i / sampleRate);
        return s;
    }

    [Fact]
    public void FftProcessor_RaisesEventOncePerFullWindow()
    {
        var fft = new FftProcessor(fftLength: 1024);
        int events = 0;
        fft.FftCalculated += _ => events++;

        var samples = Sine(440, 44100, 2500);
        fft.AddSamples(samples, 0, samples.Length, channels: 1);

        Assert.Equal(2, events); // 2500 / 1024 = 2 full windows
    }

    [Fact]
    public void FftProcessor_PeakBinMatchesSineFrequency()
    {
        const int sampleRate = 44100, fftLength = 2048;
        const double freq = 1000;
        var fft = new FftProcessor(fftLength);
        float[]? mags = null;
        fft.FftCalculated += m => mags ??= m;

        var samples = Sine(freq, sampleRate, fftLength);
        fft.AddSamples(samples, 0, samples.Length, channels: 1);

        Assert.NotNull(mags);
        int peak = Array.IndexOf(mags!, mags!.Max());
        double peakFreq = (double)peak * sampleRate / fftLength;
        Assert.InRange(peakFreq, freq - 50, freq + 50);
    }

    [Fact]
    public void FftProcessor_StereoIsDownmixed()
    {
        var fft = new FftProcessor(fftLength: 1024);
        int events = 0;
        fft.FftCalculated += _ => events++;

        var stereo = new float[2048]; // 1024 frames of stereo silence
        fft.AddSamples(stereo, 0, stereo.Length, channels: 2);

        Assert.Equal(1, events);
    }

    [Fact]
    public void BinToBars_ReturnsRequestedCountInRange()
    {
        var mags = new float[1024];
        mags[100] = 5f;
        var bars = SpectrumBinner.BinToBars(mags, barCount: 48, sampleRate: 44100, fftLength: 2048);

        Assert.Equal(48, bars.Length);
        Assert.All(bars, b => Assert.InRange(b, 0f, 1f));
        Assert.Contains(bars, b => b > 0f);
    }

    [Fact]
    public void BinToBars_SilenceIsAllZero()
    {
        var bars = SpectrumBinner.BinToBars(new float[1024], 48, 44100, 2048);
        Assert.All(bars, b => Assert.Equal(0f, b));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FftTests`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement**

`src/WeaveSyncLens.Core/Audio/FftProcessor.cs`:

```csharp
using NAudio.Dsp;

namespace WeaveSyncLens.Core.Audio;

/// <summary>
/// Accumulates playback samples and raises FFT magnitude frames.
/// Thread note: AddSamples is called on the audio thread; FftCalculated fires there too.
/// </summary>
public class FftProcessor
{
    private readonly int _fftLength;
    private readonly int _m; // log2(fftLength)
    private readonly float[] _window;
    private int _pos;

    public event Action<float[]>? FftCalculated;

    public FftProcessor(int fftLength = 2048)
    {
        if ((fftLength & (fftLength - 1)) != 0)
            throw new ArgumentException("FFT length must be a power of two", nameof(fftLength));
        _fftLength = fftLength;
        _m = (int)Math.Log2(fftLength);
        _window = new float[fftLength];
    }

    public void AddSamples(float[] buffer, int offset, int count, int channels)
    {
        for (int i = 0; i + channels <= count; i += channels)
        {
            float mono = 0;
            for (int c = 0; c < channels; c++)
                mono += buffer[offset + i + c];
            _window[_pos++] = mono / channels;

            if (_pos == _fftLength)
            {
                _pos = 0;
                Compute();
            }
        }
    }

    private void Compute()
    {
        var complex = new Complex[_fftLength];
        for (int i = 0; i < _fftLength; i++)
        {
            complex[i].X = (float)(_window[i] * FastFourierTransform.HannWindow(i, _fftLength));
            complex[i].Y = 0;
        }
        FastFourierTransform.FFT(true, _m, complex);

        var magnitudes = new float[_fftLength / 2];
        for (int i = 0; i < magnitudes.Length; i++)
            magnitudes[i] = (float)Math.Sqrt(complex[i].X * complex[i].X + complex[i].Y * complex[i].Y);

        FftCalculated?.Invoke(magnitudes);
    }
}
```

`src/WeaveSyncLens.Core/Audio/SpectrumBinner.cs`:

```csharp
namespace WeaveSyncLens.Core.Audio;

/// <summary>Maps FFT magnitudes into log-spaced 0..1 bar heights for display.</summary>
public static class SpectrumBinner
{
    private const double MinFreq = 60;
    private const double MaxFreq = 16000;
    private const float DbFloor = -60f; // magnitudes this quiet (or quieter) render as 0

    public static float[] BinToBars(float[] magnitudes, int barCount, int sampleRate, int fftLength)
    {
        var bars = new float[barCount];
        double binWidth = (double)sampleRate / fftLength;

        for (int bar = 0; bar < barCount; bar++)
        {
            double f0 = MinFreq * Math.Pow(MaxFreq / MinFreq, (double)bar / barCount);
            double f1 = MinFreq * Math.Pow(MaxFreq / MinFreq, (double)(bar + 1) / barCount);
            int b0 = Math.Clamp((int)(f0 / binWidth), 0, magnitudes.Length - 1);
            int b1 = Math.Clamp((int)Math.Ceiling(f1 / binWidth), b0 + 1, magnitudes.Length);

            float max = 0;
            for (int i = b0; i < b1; i++)
                if (magnitudes[i] > max) max = magnitudes[i];

            if (max <= 0)
            {
                bars[bar] = 0;
                continue;
            }
            float db = 20f * (float)Math.Log10(max);
            bars[bar] = Math.Clamp((db - DbFloor) / -DbFloor, 0f, 1f);
        }
        return bars;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FftTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat: FFT processor and log-spaced spectrum binning"
```

---

### Task 5: ffmpeg setup + MediaImporter

**Files:**
- Create: `scripts/setup-ffmpeg.ps1`, `src/WeaveSyncLens.Core/Import/IMediaImporter.cs`, `src/WeaveSyncLens.Core/Import/FfmpegMediaImporter.cs`
- Test: `tests/WeaveSyncLens.Core.Tests/MediaImporterTests.cs` (integration test, skipped when ffmpeg absent)

**Interfaces:**
- Consumes: FFMpegCore
- Produces:
  - `interface IMediaImporter { Task<string> PrepareForTranscriptionAsync(string mediaPath, CancellationToken ct); }` — returns path of a 16 kHz mono WAV in the user temp dir.
  - `static class FfmpegLocator { static void Configure(string repoOrAppRoot); }` — points FFMpegCore at `third_party/ffmpeg/` if present, else relies on PATH.
  - `class FfmpegMediaImporter : IMediaImporter` — supported input extensions: `.mp3 .mp4 .wav .m4a`; throws `NotSupportedException` otherwise.

- [ ] **Step 1: Write the ffmpeg setup script**

`scripts/setup-ffmpeg.ps1`:

```powershell
# Downloads ffmpeg release essentials and places ffmpeg.exe in third_party/ffmpeg/
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root 'third_party/ffmpeg'
if (Test-Path (Join-Path $target 'ffmpeg.exe')) { Write-Host 'ffmpeg already present.'; exit 0 }

$zip = Join-Path $env:TEMP 'ffmpeg-release-essentials.zip'
Invoke-WebRequest 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile $zip
$extract = Join-Path $env:TEMP 'ffmpeg-extract'
Expand-Archive $zip -DestinationPath $extract -Force
New-Item -ItemType Directory -Force $target | Out-Null
$exe = Get-ChildItem $extract -Recurse -Filter ffmpeg.exe | Select-Object -First 1
Copy-Item $exe.FullName (Join-Path $target 'ffmpeg.exe')
Remove-Item $zip; Remove-Item $extract -Recurse -Force
Write-Host "ffmpeg.exe installed to $target"
```

Run: `powershell -ExecutionPolicy Bypass -File scripts/setup-ffmpeg.ps1`
Expected: `ffmpeg.exe installed to ...third_party\ffmpeg`

- [ ] **Step 2: Write the failing/integration test**

`tests/WeaveSyncLens.Core.Tests/MediaImporterTests.cs`:

```csharp
using WeaveSyncLens.Core.Import;
using Xunit;

namespace WeaveSyncLens.Core.Tests;

public class MediaImporterTests
{
    [Fact]
    public async Task UnsupportedExtension_Throws()
    {
        var importer = new FfmpegMediaImporter();
        await Assert.ThrowsAsync<NotSupportedException>(
            () => importer.PrepareForTranscriptionAsync("file.xyz", CancellationToken.None));
    }

    [SkippableFact]
    public async Task Mp3_ConvertsTo16kMonoWav()
    {
        FfmpegLocator.Configure(FindRepoRoot());
        Skip.IfNot(FfmpegLocator.IsAvailable, "ffmpeg not installed — run scripts/setup-ffmpeg.ps1");

        // Generate a 2s test tone MP3 with ffmpeg itself.
        var mp3 = Path.Combine(Path.GetTempPath(), $"wsl-test-{Guid.NewGuid():N}.mp3");
        await FfmpegLocator.RunFfmpegAsync(
            $"-f lavfi -i \"sine=frequency=440:duration=2\" -q:a 5 \"{mp3}\"");

        try
        {
            var importer = new FfmpegMediaImporter();
            var wav = await importer.PrepareForTranscriptionAsync(mp3, CancellationToken.None);

            Assert.True(File.Exists(wav));
            using var reader = new NAudio.Wave.WaveFileReader(wav);
            Assert.Equal(16000, reader.WaveFormat.SampleRate);
            Assert.Equal(1, reader.WaveFormat.Channels);
            File.Delete(wav);
        }
        finally { File.Delete(mp3); }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WeaveSyncLens.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
```

Add the Skippable package: `dotnet add tests/WeaveSyncLens.Core.Tests package Xunit.SkippableFact --version 1.4.13`

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter MediaImporterTests`
Expected: FAIL — types not defined.

- [ ] **Step 4: Implement**

`src/WeaveSyncLens.Core/Import/IMediaImporter.cs`:

```csharp
namespace WeaveSyncLens.Core.Import;

public interface IMediaImporter
{
    /// <summary>Produces a 16 kHz mono WAV for transcription. Returns the WAV path (temp dir).</summary>
    Task<string> PrepareForTranscriptionAsync(string mediaPath, CancellationToken ct);
}
```

`src/WeaveSyncLens.Core/Import/FfmpegMediaImporter.cs`:

```csharp
using System.Diagnostics;
using FFMpegCore;

namespace WeaveSyncLens.Core.Import;

/// <summary>Locates ffmpeg.exe (third_party/ffmpeg first, then PATH) and configures FFMpegCore.</summary>
public static class FfmpegLocator
{
    public static bool IsAvailable { get; private set; }

    public static void Configure(string appRoot)
    {
        var bundled = Path.Combine(appRoot, "third_party", "ffmpeg");
        if (File.Exists(Path.Combine(bundled, "ffmpeg.exe")))
        {
            GlobalFFOptions.Configure(new FFOptions { BinaryFolder = bundled });
            IsAvailable = true;
            return;
        }
        // Fall back to PATH.
        try
        {
            using var p = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            p!.WaitForExit(5000);
            IsAvailable = p.ExitCode == 0;
        }
        catch { IsAvailable = false; }
    }

    /// <summary>Runs a raw ffmpeg command (used by tests to synthesize fixtures).</summary>
    public static async Task RunFfmpegAsync(string arguments)
    {
        var exe = File.Exists(Path.Combine(GlobalFFOptions.Current.BinaryFolder, "ffmpeg.exe"))
            ? Path.Combine(GlobalFFOptions.Current.BinaryFolder, "ffmpeg.exe")
            : "ffmpeg";
        using var p = Process.Start(new ProcessStartInfo(exe, "-y " + arguments)
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        await p.WaitForExitAsync();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed: {await p.StandardError.ReadToEndAsync()}");
    }
}

public class FfmpegMediaImporter : IMediaImporter
{
    private static readonly string[] SupportedExtensions = { ".mp3", ".mp4", ".wav", ".m4a" };

    public async Task<string> PrepareForTranscriptionAsync(string mediaPath, CancellationToken ct)
    {
        var ext = Path.GetExtension(mediaPath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext))
            throw new NotSupportedException($"Unsupported file type: {ext}");

        var outPath = Path.Combine(Path.GetTempPath(),
            $"weavesynclens-{Path.GetFileNameWithoutExtension(mediaPath)}-{Guid.NewGuid():N}.wav");

        await FFMpegArguments
            .FromFileInput(mediaPath)
            .OutputToFile(outPath, overwrite: true, o => o
                .WithAudioSamplingRate(16000)
                .WithCustomArgument("-ac 1")
                .ForceFormat("wav"))
            .CancellableThrough(ct)
            .ProcessAsynchronously();

        return outPath;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter MediaImporterTests`
Expected: PASS, 2 tests (second one skipped only if ffmpeg missing — it should NOT be skipped since Step 1 installed it).

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat: ffmpeg setup script and media importer producing 16kHz mono WAV"
```

---

### Task 6: PlaybackEngine (NAudio)

**Files:**
- Create: `src/WeaveSyncLens.Core/Audio/PlaybackEngine.cs`, `src/WeaveSyncLens.Core/Audio/SampleTapProvider.cs`
- Test: `tests/WeaveSyncLens.Core.Tests/SampleTapProviderTests.cs` (the tap is unit-tested; device playback is verified manually in Task 9)

**Interfaces:**
- Consumes: NAudio (`AudioFileReader`, `MediaFoundationReader`, `WaveOutEvent`), `FftProcessor` (Task 4)
- Produces:
  - `class SampleTapProvider(ISampleProvider source, Action<float[], int, int, int> onSamples) : ISampleProvider` — callback args: buffer, offset, count, channels.
  - `class PlaybackEngine : IDisposable` with:
    - `void Load(string mediaPath)` (`.mp3`/`.wav` → `AudioFileReader`, else `MediaFoundationReader`)
    - `void Play()`, `void Pause()`, `void Seek(double seconds)`
    - `double CurrentTimeSeconds { get; }`, `double TotalTimeSeconds { get; }`, `bool IsPlaying { get; }`
    - `FftProcessor Fft { get; }` (samples are tapped into it automatically)
    - `int SampleRate { get; }`
    - `event Action? PlaybackStopped`

- [ ] **Step 1: Write the failing test for the tap**

`tests/WeaveSyncLens.Core.Tests/SampleTapProviderTests.cs`:

```csharp
using NAudio.Wave;
using WeaveSyncLens.Core.Audio;
using Xunit;

namespace WeaveSyncLens.Core.Tests;

public class SampleTapProviderTests
{
    private sealed class FakeSource : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++) buffer[offset + i] = 0.5f;
            return count;
        }
    }

    [Fact]
    public void Read_PassesSamplesThroughAndInvokesCallback()
    {
        int cbCount = 0, cbChannels = 0;
        var tap = new SampleTapProvider(new FakeSource(),
            (buf, off, cnt, ch) => { cbCount = cnt; cbChannels = ch; });

        var buffer = new float[512];
        int read = tap.Read(buffer, 0, 512);

        Assert.Equal(512, read);
        Assert.Equal(0.5f, buffer[100]);
        Assert.Equal(512, cbCount);
        Assert.Equal(2, cbChannels);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SampleTapProviderTests`
Expected: FAIL — type not defined.

- [ ] **Step 3: Implement tap and engine**

`src/WeaveSyncLens.Core/Audio/SampleTapProvider.cs`:

```csharp
using NAudio.Wave;

namespace WeaveSyncLens.Core.Audio;

/// <summary>Pass-through sample provider that mirrors every buffer to a callback (for FFT).</summary>
public class SampleTapProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Action<float[], int, int, int> _onSamples;

    public SampleTapProvider(ISampleProvider source, Action<float[], int, int, int> onSamples)
    {
        _source = source;
        _onSamples = onSamples;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read > 0)
            _onSamples(buffer, offset, read, WaveFormat.Channels);
        return read;
    }
}
```

`src/WeaveSyncLens.Core/Audio/PlaybackEngine.cs`:

```csharp
using NAudio.Wave;

namespace WeaveSyncLens.Core.Audio;

/// <summary>NAudio playback with position clock and FFT sample tap.</summary>
public class PlaybackEngine : IDisposable
{
    private WaveStream? _reader;
    private WaveOutEvent? _output;

    public FftProcessor Fft { get; } = new(fftLength: 2048);

    public event Action? PlaybackStopped;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public double CurrentTimeSeconds => _reader?.CurrentTime.TotalSeconds ?? 0;
    public double TotalTimeSeconds => _reader?.TotalTime.TotalSeconds ?? 0;
    public int SampleRate => _reader?.WaveFormat.SampleRate ?? 44100;

    public void Load(string mediaPath)
    {
        Unload();
        _reader = Path.GetExtension(mediaPath).ToLowerInvariant() switch
        {
            ".mp3" or ".wav" => new AudioFileReader(mediaPath),
            _ => new MediaFoundationReader(mediaPath), // MP4/M4A via Windows Media Foundation
        };
        var tap = new SampleTapProvider(_reader.ToSampleProvider(),
            (buf, off, cnt, ch) => Fft.AddSamples(buf, off, cnt, ch));
        _output = new WaveOutEvent { DesiredLatency = 150 };
        _output.Init(tap);
        _output.PlaybackStopped += (_, _) => PlaybackStopped?.Invoke();
    }

    public void Play() => _output?.Play();
    public void Pause() => _output?.Pause();

    public void Seek(double seconds)
    {
        if (_reader is null) return;
        seconds = Math.Clamp(seconds, 0, TotalTimeSeconds);
        _reader.CurrentTime = TimeSpan.FromSeconds(seconds);
    }

    private void Unload()
    {
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
    }

    public void Dispose() => Unload();
}
```

Note: `AudioFileReader.ToSampleProvider()` — `AudioFileReader` already implements `ISampleProvider`; if the extension call is ambiguous, use `(_reader as ISampleProvider) ?? _reader.ToSampleProvider()` pattern by declaring: `ISampleProvider sp = _reader is AudioFileReader afr ? afr : _reader.ToSampleProvider();`

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS — all tests including SampleTapProviderTests.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat: NAudio playback engine with FFT sample tap"
```

---

### Task 7: WhisperLocalTranscriber

**Files:**
- Create: `src/WeaveSyncLens.Core/Transcription/ITranscriber.cs`, `src/WeaveSyncLens.Core/Transcription/WhisperModelStore.cs`, `src/WeaveSyncLens.Core/Transcription/WhisperLocalTranscriber.cs`
- Test: `tests/WeaveSyncLens.Core.Tests/WhisperTranscriberTests.cs` (integration, skipped in CI-like runs without opt-in)

**Interfaces:**
- Consumes: Whisper.net, `Word`, `Transcript` (Task 2)
- Produces:
  - `enum WhisperModelSize { Tiny, Base, Small, Medium, LargeV3 }`
  - `class TranscriptionProgress { public string Stage { get; init; } = ""; public double Fraction { get; init; } }` — Stage is `"DownloadingModel"` or `"Transcribing"`.
  - `interface ITranscriber { Task<Transcript> TranscribeAsync(string wavPath, IProgress<TranscriptionProgress>? progress, CancellationToken ct); string ModelName { get; } }`
  - `static class WhisperModelStore { static string ModelsDirectory; static Task<string> EnsureModelAsync(WhisperModelSize size, IProgress<TranscriptionProgress>? progress, CancellationToken ct); }` — models stored in `models/` under the app root (git-ignored).
  - `class WhisperLocalTranscriber(WhisperModelSize size) : ITranscriber`

- [ ] **Step 1: Write the integration test**

`tests/WeaveSyncLens.Core.Tests/WhisperTranscriberTests.cs`:

```csharp
using WeaveSyncLens.Core.Import;
using WeaveSyncLens.Core.Transcription;
using Xunit;

namespace WeaveSyncLens.Core.Tests;

public class WhisperTranscriberTests
{
    // Real model download + transcription: opt in with WSL_RUN_WHISPER_TESTS=1.
    [SkippableFact]
    public async Task TranscribesSpokenWavIntoTimedWords()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("WSL_RUN_WHISPER_TESTS") == "1",
            "Set WSL_RUN_WHISPER_TESTS=1 to run Whisper integration test");

        // Synthesize speech-like audio is unreliable; use Windows built-in TTS to make a WAV.
        var wav = Path.Combine(Path.GetTempPath(), $"wsl-whisper-{Guid.NewGuid():N}.wav");
        await MakeTtsWav("hello world this is a test", wav);

        try
        {
            var transcriber = new WhisperLocalTranscriber(WhisperModelSize.Tiny);
            var transcript = await transcriber.TranscribeAsync(wav, null, CancellationToken.None);

            Assert.True(transcript.Words.Count >= 4);
            Assert.Contains(transcript.Words, w => w.Text.Contains("hello", StringComparison.OrdinalIgnoreCase));
            Assert.All(transcript.Words, w => Assert.True(w.End >= w.Start));
            // Words are ordered in time.
            for (int i = 1; i < transcript.Words.Count; i++)
                Assert.True(transcript.Words[i].Start >= transcript.Words[i - 1].Start);
        }
        finally { File.Delete(wav); }
    }

    private static async Task MakeTtsWav(string text, string outPath)
    {
        // PowerShell System.Speech TTS → 16kHz mono WAV (no extra packages needed).
        var script = $@"
Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$s.SetOutputToWaveFile('{outPath}', $fmt)
$s.Speak('{text}')
$s.Dispose()";
        var psi = new System.Diagnostics.ProcessStartInfo("powershell", $"-NoProfile -Command \"{script.Replace("\"", "\\\"")}\"")
            { UseShellExecute = false, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        await p.WaitForExitAsync();
        if (!File.Exists(outPath)) throw new InvalidOperationException("TTS WAV generation failed");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter WhisperTranscriberTests`
Expected: FAIL — types not defined. (After implementation it will SKIP unless `WSL_RUN_WHISPER_TESTS=1`.)

- [ ] **Step 3: Implement**

`src/WeaveSyncLens.Core/Transcription/ITranscriber.cs`:

```csharp
using WeaveSyncLens.Core.Models;

namespace WeaveSyncLens.Core.Transcription;

public enum WhisperModelSize { Tiny, Base, Small, Medium, LargeV3 }

public class TranscriptionProgress
{
    public string Stage { get; init; } = "";   // "DownloadingModel" | "Transcribing"
    public double Fraction { get; init; }      // 0..1
}

public interface ITranscriber
{
    string ModelName { get; }
    Task<Transcript> TranscribeAsync(string wavPath, IProgress<TranscriptionProgress>? progress, CancellationToken ct);
}
```

`src/WeaveSyncLens.Core/Transcription/WhisperModelStore.cs`:

```csharp
using Whisper.net.Ggml;

namespace WeaveSyncLens.Core.Transcription;

/// <summary>Downloads and caches Whisper GGML models under models/.</summary>
public static class WhisperModelStore
{
    public static string ModelsDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "models");

    public static GgmlType ToGgmlType(WhisperModelSize size) => size switch
    {
        WhisperModelSize.Tiny => GgmlType.Tiny,
        WhisperModelSize.Base => GgmlType.Base,
        WhisperModelSize.Small => GgmlType.Small,
        WhisperModelSize.Medium => GgmlType.Medium,
        WhisperModelSize.LargeV3 => GgmlType.LargeV3,
        _ => GgmlType.Base,
    };

    public static async Task<string> EnsureModelAsync(
        WhisperModelSize size, IProgress<TranscriptionProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(ModelsDirectory);
        var path = Path.Combine(ModelsDirectory, $"ggml-{size}.bin".ToLowerInvariant());
        if (File.Exists(path)) return path;

        progress?.Report(new TranscriptionProgress { Stage = "DownloadingModel", Fraction = 0 });
        // NOTE: in some Whisper.net versions this is WhisperGgmlDownloader.Default.GetGgmlModelAsync(...)
        using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(ToGgmlType(size), cancellationToken: ct);
        var tmp = path + ".download";
        await using (var file = File.Create(tmp))
            await modelStream.CopyToAsync(file, ct);
        File.Move(tmp, path, overwrite: true);
        progress?.Report(new TranscriptionProgress { Stage = "DownloadingModel", Fraction = 1 });
        return path;
    }
}
```

`src/WeaveSyncLens.Core/Transcription/WhisperLocalTranscriber.cs`:

```csharp
using WeaveSyncLens.Core.Models;
using Whisper.net;

namespace WeaveSyncLens.Core.Transcription;

/// <summary>In-process Whisper (whisper.cpp) transcription with word-level timestamps.</summary>
public class WhisperLocalTranscriber : ITranscriber
{
    private readonly WhisperModelSize _size;

    public WhisperLocalTranscriber(WhisperModelSize size) => _size = size;

    public string ModelName => _size.ToString().ToLowerInvariant();

    public async Task<Transcript> TranscribeAsync(
        string wavPath, IProgress<TranscriptionProgress>? progress, CancellationToken ct)
    {
        var modelPath = await WhisperModelStore.EnsureModelAsync(_size, progress, ct);

        using var factory = WhisperFactory.FromPath(modelPath);
        await using var processor = factory.CreateBuilder()
            .WithLanguage("auto")
            .WithTokenTimestamps()
            .SplitOnWord()
            .WithMaxSegmentLength(1)   // 1 token per segment + split-on-word => one word per segment
            .WithProgressHandler(p => progress?.Report(
                new TranscriptionProgress { Stage = "Transcribing", Fraction = p / 100.0 }))
            .Build();

        var words = new List<Word>();
        await using var fileStream = File.OpenRead(wavPath);
        await foreach (var segment in processor.ProcessAsync(fileStream, ct))
        {
            var text = segment.Text.Trim();
            if (text.Length == 0) continue;
            words.Add(new Word(text, segment.Start.TotalSeconds, segment.End.TotalSeconds));
        }
        return new Transcript(words);
    }
}
```

- [ ] **Step 4: Build, then run the integration test for real**

Run: `dotnet build`
Expected: `Build succeeded.`

```powershell
$env:WSL_RUN_WHISPER_TESTS = '1'
dotnet test --filter WhisperTranscriberTests
Remove-Item Env:WSL_RUN_WHISPER_TESTS
```
Expected: PASS (downloads the ~75 MB tiny model on first run; give it a few minutes). If the download API name differs, fix per the Global Constraints note.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat: local Whisper transcriber with word timestamps and model download"
```

---

### Task 8: MediaSessionLoader (pipeline orchestration)

**Files:**
- Create: `src/WeaveSyncLens.Core/MediaSessionLoader.cs`
- Test: `tests/WeaveSyncLens.Core.Tests/MediaSessionLoaderTests.cs`

**Interfaces:**
- Consumes: `IMediaImporter` (Task 5), `ITranscriber` (Task 7), `TranscriptSidecarStore` (Task 3)
- Produces:
  - `class MediaSessionLoader(IMediaImporter importer, ITranscriber transcriber)` with
    `Task<Transcript> LoadAsync(string mediaPath, IProgress<TranscriptionProgress>? progress, CancellationToken ct)`:
    1. If a valid sidecar exists → return it (no import/transcription).
    2. Else import → transcribe → save sidecar → return transcript. Temp WAV deleted afterwards.

- [ ] **Step 1: Write the failing tests**

`tests/WeaveSyncLens.Core.Tests/MediaSessionLoaderTests.cs`:

```csharp
using WeaveSyncLens.Core;
using WeaveSyncLens.Core.Import;
using WeaveSyncLens.Core.Models;
using WeaveSyncLens.Core.Sidecar;
using WeaveSyncLens.Core.Transcription;
using Xunit;

namespace WeaveSyncLens.Core.Tests;

public class MediaSessionLoaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wsl-loader").FullName;
    private string MediaPath => Path.Combine(_dir, "song.mp3");

    public MediaSessionLoaderTests() => File.WriteAllBytes(MediaPath, new byte[] { 1, 2, 3 });
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class FakeImporter : IMediaImporter
    {
        public int Calls;
        public Task<string> PrepareForTranscriptionAsync(string mediaPath, CancellationToken ct)
        {
            Calls++;
            var wav = Path.Combine(Path.GetTempPath(), $"fake-{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(wav, new byte[] { 0 });
            return Task.FromResult(wav);
        }
    }

    private sealed class FakeTranscriber : ITranscriber
    {
        public int Calls;
        public string? LastWavPath;
        public string ModelName => "fake";
        public Task<Transcript> TranscribeAsync(string wavPath, IProgress<TranscriptionProgress>? p, CancellationToken ct)
        {
            Calls++;
            LastWavPath = wavPath;
            return Task.FromResult(new Transcript(new[] { new Word("hi", 0.0, 0.5) }));
        }
    }

    [Fact]
    public async Task NoSidecar_ImportsTranscribesAndSavesSidecar()
    {
        var importer = new FakeImporter();
        var transcriber = new FakeTranscriber();
        var loader = new MediaSessionLoader(importer, transcriber);

        var transcript = await loader.LoadAsync(MediaPath, null, CancellationToken.None);

        Assert.Single(transcript.Words);
        Assert.Equal(1, importer.Calls);
        Assert.Equal(1, transcriber.Calls);
        Assert.True(File.Exists(TranscriptSidecarStore.GetSidecarPath(MediaPath)));
        Assert.False(File.Exists(transcriber.LastWavPath!)); // temp WAV cleaned up
    }

    [Fact]
    public async Task ExistingSidecar_SkipsImportAndTranscription()
    {
        TranscriptSidecarStore.Save(MediaPath,
            new Transcript(new[] { new Word("cached", 0.0, 1.0) }), "base");
        var importer = new FakeImporter();
        var transcriber = new FakeTranscriber();
        var loader = new MediaSessionLoader(importer, transcriber);

        var transcript = await loader.LoadAsync(MediaPath, null, CancellationToken.None);

        Assert.Equal("cached", transcript.Words[0].Text);
        Assert.Equal(0, importer.Calls);
        Assert.Equal(0, transcriber.Calls);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter MediaSessionLoaderTests`
Expected: FAIL — `MediaSessionLoader` not defined.

- [ ] **Step 3: Implement**

`src/WeaveSyncLens.Core/MediaSessionLoader.cs`:

```csharp
using WeaveSyncLens.Core.Import;
using WeaveSyncLens.Core.Models;
using WeaveSyncLens.Core.Sidecar;
using WeaveSyncLens.Core.Transcription;

namespace WeaveSyncLens.Core;

/// <summary>Turns a media file into a Transcript: sidecar cache, else import + transcribe + save.</summary>
public class MediaSessionLoader
{
    private readonly IMediaImporter _importer;
    private readonly ITranscriber _transcriber;

    public MediaSessionLoader(IMediaImporter importer, ITranscriber transcriber)
    {
        _importer = importer;
        _transcriber = transcriber;
    }

    public async Task<Transcript> LoadAsync(
        string mediaPath, IProgress<TranscriptionProgress>? progress, CancellationToken ct)
    {
        var cached = TranscriptSidecarStore.TryLoad(mediaPath);
        if (cached is not null) return cached;

        var wavPath = await _importer.PrepareForTranscriptionAsync(mediaPath, ct);
        try
        {
            var transcript = await _transcriber.TranscribeAsync(wavPath, progress, ct);
            TranscriptSidecarStore.Save(mediaPath, transcript, _transcriber.ModelName);
            return transcript;
        }
        finally
        {
            try { File.Delete(wavPath); } catch (IOException) { }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter MediaSessionLoaderTests`
Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat: media session loader with sidecar caching"
```

---

### Task 9: Main window shell — ViewModel, drag-drop, transport controls, progress overlay

**Files:**
- Create: `src/WeaveSyncLens.App/ViewModels/MainViewModel.cs`, `src/WeaveSyncLens.App/ViewModels/WordViewModel.cs`
- Modify: `src/WeaveSyncLens.App/MainWindow.xaml`, `src/WeaveSyncLens.App/MainWindow.xaml.cs`, `src/WeaveSyncLens.App/App.xaml.cs`

**Interfaces:**
- Consumes: `MediaSessionLoader` (Task 8), `PlaybackEngine` (Task 6), `FfmpegLocator`/`FfmpegMediaImporter` (Task 5), `WhisperLocalTranscriber` (Task 7), `Transcript.FindActiveWordIndex` (Task 2)
- Produces:
  - `partial class WordViewModel : ObservableObject { string Text; double Start; bool IsActive; }`
  - `partial class MainViewModel : ObservableObject` — properties later tasks bind to:
    `ObservableCollection<WordViewModel> Words`, `int ActiveWordIndex` (-1 when none), `string StatusText`, `bool IsBusy`, `double BusyFraction`, `bool IsPlaying`, `double PositionSeconds`, `double DurationSeconds`, `bool IsMediaLoaded`;
    commands: `OpenFileCommand`, `LoadFileAsync(string path)`, `TogglePlayPauseCommand`, `SeekCommand(double seconds)`, `SeekToWordCommand(WordViewModel word)`;
    public `PlaybackEngine Playback { get; }` (visualizer host reads FFT from here).

- [ ] **Step 1: Implement WordViewModel**

`src/WeaveSyncLens.App/ViewModels/WordViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace WeaveSyncLens.App.ViewModels;

public partial class WordViewModel : ObservableObject
{
    public string Text { get; }
    public double Start { get; }

    [ObservableProperty]
    private bool _isActive;

    public WordViewModel(string text, double start)
    {
        Text = text;
        Start = start;
    }
}
```

- [ ] **Step 2: Implement MainViewModel**

`src/WeaveSyncLens.App/ViewModels/MainViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WeaveSyncLens.Core;
using WeaveSyncLens.Core.Audio;
using WeaveSyncLens.Core.Import;
using WeaveSyncLens.Core.Models;
using WeaveSyncLens.Core.Transcription;

namespace WeaveSyncLens.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly MediaSessionLoader _loader;
    private readonly DispatcherTimer _syncTimer;
    private Transcript? _transcript;

    public PlaybackEngine Playback { get; } = new();
    public ObservableCollection<WordViewModel> Words { get; } = new();

    [ObservableProperty] private int _activeWordIndex = -1;
    [ObservableProperty] private string _statusText = "Drop an MP3 or MP4 file here";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _busyFraction;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _positionSeconds;
    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private bool _isMediaLoaded;

    public MainViewModel()
    {
        FfmpegLocator.Configure(AppContext.BaseDirectory);
        _loader = new MediaSessionLoader(
            new FfmpegMediaImporter(),
            new WhisperLocalTranscriber(WhisperModelSize.Base));

        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _syncTimer.Tick += (_, _) => SyncTick();
        _syncTimer.Start();
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var dlg = new OpenFileDialog { Filter = "Media files|*.mp3;*.mp4;*.wav;*.m4a" };
        if (dlg.ShowDialog() == true)
            await LoadFileAsync(dlg.FileName);
    }

    public async Task LoadFileAsync(string path)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            StatusText = $"Preparing {Path.GetFileName(path)}…";
            var progress = new Progress<TranscriptionProgress>(p =>
            {
                BusyFraction = p.Fraction;
                StatusText = p.Stage == "DownloadingModel"
                    ? $"Downloading Whisper model… {p.Fraction:P0}"
                    : $"Transcribing… {p.Fraction:P0}";
            });

            _transcript = await Task.Run(() => _loader.LoadAsync(path, progress, CancellationToken.None));

            Words.Clear();
            foreach (var w in _transcript.Words)
                Words.Add(new WordViewModel(w.Text, w.Start));
            ActiveWordIndex = -1;

            Playback.Load(path);
            DurationSeconds = Playback.TotalTimeSeconds;
            IsMediaLoaded = true;
            StatusText = Path.GetFileName(path);
            Playback.Play();
        }
        catch (Exception ex)
        {
            StatusText = "Failed to load file";
            MessageBox.Show(ex.Message, "WeaveSyncLens", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            BusyFraction = 0;
        }
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (!IsMediaLoaded) return;
        if (Playback.IsPlaying) Playback.Pause();
        else Playback.Play();
    }

    [RelayCommand]
    private void Seek(double seconds) => Playback.Seek(seconds);

    [RelayCommand]
    private void SeekToWord(WordViewModel word) => Playback.Seek(word.Start);

    private void SyncTick()
    {
        IsPlaying = Playback.IsPlaying;
        PositionSeconds = Playback.CurrentTimeSeconds;

        if (_transcript is null || Words.Count == 0) return;
        int index = _transcript.FindActiveWordIndex(PositionSeconds, ActiveWordIndex);
        if (index == ActiveWordIndex) return;

        if (ActiveWordIndex >= 0 && ActiveWordIndex < Words.Count)
            Words[ActiveWordIndex].IsActive = false;
        if (index >= 0 && index < Words.Count)
            Words[index].IsActive = true;
        ActiveWordIndex = index;
    }
}
```

- [ ] **Step 3: Build the main window layout**

`src/WeaveSyncLens.App/MainWindow.xaml` (replace entire file):

```xml
<Window x:Class="WeaveSyncLens.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:WeaveSyncLens.App.ViewModels"
        Title="WeaveSyncLens" Height="720" Width="1280"
        Background="#FF101014" AllowDrop="True"
        Drop="Window_Drop" DragOver="Window_DragOver"
        KeyDown="Window_KeyDown">
    <Window.DataContext>
        <vm:MainViewModel />
    </Window.DataContext>
    <Grid x:Name="RootGrid">
        <Grid.RowDefinitions>
            <RowDefinition Height="3*" />   <!-- transcript -->
            <RowDefinition Height="1*" />   <!-- visualizer -->
            <RowDefinition Height="Auto" /> <!-- transport -->
        </Grid.RowDefinitions>

        <!-- Transcript area: placeholder until Task 10 replaces it with KaraokeView -->
        <Border x:Name="TranscriptHost" Grid.Row="0">
            <TextBlock Text="{Binding StatusText}" Foreground="#FF808090"
                       FontSize="24" HorizontalAlignment="Center" VerticalAlignment="Center" />
        </Border>

        <!-- Visualizer area: placeholder until Task 11 -->
        <Border x:Name="VisualizerHostArea" Grid.Row="1" Background="#FF0B0B0E" />

        <!-- Transport controls -->
        <DockPanel x:Name="TransportBar" Grid.Row="2" Background="#FF18181E" LastChildFill="True">
            <Button Content="Open" Command="{Binding OpenFileCommand}" Margin="8" Padding="12,4" />
            <Button Margin="0,8,8,8" Padding="12,4"
                    Command="{Binding TogglePlayPauseCommand}">
                <Button.Style>
                    <Style TargetType="Button">
                        <Setter Property="Content" Value="Play" />
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding IsPlaying}" Value="True">
                                <Setter Property="Content" Value="Pause" />
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Button.Style>
            </Button>
            <TextBlock DockPanel.Dock="Right" Margin="8" VerticalAlignment="Center" Foreground="White">
                <Run Text="{Binding PositionSeconds, StringFormat={}{0:0}s, Mode=OneWay}" />
                <Run Text=" / " />
                <Run Text="{Binding DurationSeconds, StringFormat={}{0:0}s, Mode=OneWay}" />
            </TextBlock>
            <Slider x:Name="SeekSlider" VerticalAlignment="Center" Margin="8,0"
                    Maximum="{Binding DurationSeconds}"
                    Value="{Binding PositionSeconds, Mode=OneWay}"
                    Thumb.DragCompleted="SeekSlider_DragCompleted"
                    PreviewMouseLeftButtonUp="SeekSlider_MouseUp" />
        </DockPanel>

        <!-- Busy overlay -->
        <Grid Grid.RowSpan="3" Background="#CC101014"
              Visibility="{Binding IsBusy, Converter={StaticResource BoolToVisibility}}">
            <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Width="420">
                <TextBlock Text="{Binding StatusText}" Foreground="White" FontSize="20"
                           TextAlignment="Center" Margin="0,0,0,16" />
                <ProgressBar Height="10" Minimum="0" Maximum="1" Value="{Binding BusyFraction, Mode=OneWay}" />
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

Add the converter resource in `src/WeaveSyncLens.App/App.xaml` inside `<Application.Resources>`:

```xml
<Application.Resources>
    <BooleanToVisibilityConverter x:Key="BoolToVisibility" />
</Application.Resources>
```

- [ ] **Step 4: Code-behind for drop/seek/keys**

`src/WeaveSyncLens.App/MainWindow.xaml.cs` (replace entire file):

```csharp
using System.Windows;
using System.Windows.Input;
using WeaveSyncLens.App.ViewModels;

namespace WeaveSyncLens.App;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow() => InitializeComponent();

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await Vm.LoadFileAsync(files[0]);
    }

    private void SeekSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        => Vm.SeekCommand.Execute(SeekSlider.Value);

    private void SeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
        => Vm.SeekCommand.Execute(SeekSlider.Value);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            Vm.TogglePlayPauseCommand.Execute(null);
            e.Handled = true;
        }
    }
}
```

- [ ] **Step 5: Build and manually verify**

Run: `dotnet build` → Expected: success.
Run: `dotnet run --project src/WeaveSyncLens.App`
Manual check: window opens; drop an MP3 → progress overlay shows model download (first run) then transcription; audio starts playing; time counter advances; Space toggles pause; seek slider works. (No transcript view yet — that's Task 10.)

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat: main window with drag-drop, transcription pipeline, transport controls"
```

---

### Task 10: KaraokeView — highlighted scrolling transcript

**Files:**
- Create: `src/WeaveSyncLens.App/Controls/KaraokeView.xaml`, `src/WeaveSyncLens.App/Controls/KaraokeView.xaml.cs`, `src/WeaveSyncLens.App/Controls/ScrollViewerBehavior.cs`
- Modify: `src/WeaveSyncLens.App/MainWindow.xaml` (replace TranscriptHost placeholder content)

**Interfaces:**
- Consumes: `MainViewModel.Words`, `MainViewModel.ActiveWordIndex`, `MainViewModel.SeekToWordCommand`, `WordViewModel` (Task 9)
- Produces: `KaraokeView` UserControl with DPs `WordsSource` (IEnumerable), `ActiveIndex` (int), `SeekCommand` (ICommand), `WordFontSize` (double)

- [ ] **Step 1: Animated scroll helper**

`src/WeaveSyncLens.App/Controls/ScrollViewerBehavior.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;

namespace WeaveSyncLens.App.Controls;

/// <summary>Attached property so ScrollViewer.VerticalOffset can be animated.</summary>
public static class ScrollViewerBehavior
{
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.RegisterAttached("VerticalOffset", typeof(double),
            typeof(ScrollViewerBehavior),
            new PropertyMetadata(0.0, (d, e) =>
            {
                if (d is ScrollViewer sv) sv.ScrollToVerticalOffset((double)e.NewValue);
            }));

    public static void SetVerticalOffset(DependencyObject d, double value) =>
        d.SetValue(VerticalOffsetProperty, value);

    public static double GetVerticalOffset(DependencyObject d) =>
        (double)d.GetValue(VerticalOffsetProperty);
}
```

- [ ] **Step 2: KaraokeView markup**

`src/WeaveSyncLens.App/Controls/KaraokeView.xaml`:

```xml
<UserControl x:Class="WeaveSyncLens.App.Controls.KaraokeView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Name="Root">
    <ScrollViewer x:Name="Scroller" VerticalScrollBarVisibility="Hidden"
                  HorizontalScrollBarVisibility="Disabled" Focusable="False">
        <ItemsControl x:Name="WordsHost"
                      ItemsSource="{Binding WordsSource, ElementName=Root}"
                      Margin="24,120">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <WrapPanel Orientation="Horizontal" />
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <TextBlock Text="{Binding Text}" Margin="0,2,14,2"
                               FontSize="{Binding WordFontSize, ElementName=Root}"
                               FontWeight="SemiBold" Cursor="Hand"
                               MouseLeftButtonDown="Word_MouseLeftButtonDown">
                        <TextBlock.Style>
                            <Style TargetType="TextBlock">
                                <Setter Property="Foreground" Value="#FF6A6A78" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsActive}" Value="True">
                                        <Setter Property="Foreground" Value="#FFFFD34D" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TextBlock.Style>
                    </TextBlock>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </ScrollViewer>
</UserControl>
```

- [ ] **Step 3: KaraokeView code-behind (DPs + smooth auto-scroll)**

`src/WeaveSyncLens.App/Controls/KaraokeView.xaml.cs`:

```csharp
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WeaveSyncLens.App.ViewModels;

namespace WeaveSyncLens.App.Controls;

public partial class KaraokeView : UserControl
{
    public static readonly DependencyProperty WordsSourceProperty =
        DependencyProperty.Register(nameof(WordsSource), typeof(IEnumerable), typeof(KaraokeView));

    public static readonly DependencyProperty ActiveIndexProperty =
        DependencyProperty.Register(nameof(ActiveIndex), typeof(int), typeof(KaraokeView),
            new PropertyMetadata(-1, (d, _) => ((KaraokeView)d).ScrollToActive()));

    public static readonly DependencyProperty SeekCommandProperty =
        DependencyProperty.Register(nameof(SeekCommand), typeof(ICommand), typeof(KaraokeView));

    public static readonly DependencyProperty WordFontSizeProperty =
        DependencyProperty.Register(nameof(WordFontSize), typeof(double), typeof(KaraokeView),
            new PropertyMetadata(32.0));

    public IEnumerable WordsSource
    {
        get => (IEnumerable)GetValue(WordsSourceProperty);
        set => SetValue(WordsSourceProperty, value);
    }

    public int ActiveIndex
    {
        get => (int)GetValue(ActiveIndexProperty);
        set => SetValue(ActiveIndexProperty, value);
    }

    public ICommand? SeekCommand
    {
        get => (ICommand?)GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    public double WordFontSize
    {
        get => (double)GetValue(WordFontSizeProperty);
        set => SetValue(WordFontSizeProperty, value);
    }

    public KaraokeView() => InitializeComponent();

    private void Word_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WordViewModel word })
            SeekCommand?.Execute(word);
    }

    private void ScrollToActive()
    {
        int index = ActiveIndex;
        if (index < 0) return;
        var container = WordsHost.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
        if (container is null) return;

        // Word's vertical center relative to scrolled content → keep it mid-viewport.
        var transform = container.TransformToAncestor(Scroller);
        double yInViewport = transform.Transform(new Point(0, 0)).Y + container.ActualHeight / 2;
        double target = Scroller.VerticalOffset + yInViewport - Scroller.ViewportHeight / 2;
        target = Math.Clamp(target, 0, Scroller.ScrollableHeight);

        var anim = new DoubleAnimation(Scroller.VerticalOffset, target,
            new Duration(TimeSpan.FromMilliseconds(350)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        Scroller.BeginAnimation(ScrollViewerBehavior.VerticalOffsetProperty, anim);
    }
}
```

- [ ] **Step 4: Wire into MainWindow**

In `src/WeaveSyncLens.App/MainWindow.xaml`, add namespace `xmlns:controls="clr-namespace:WeaveSyncLens.App.Controls"` on the `<Window>` element, then replace the `TranscriptHost` Border contents:

```xml
<Border x:Name="TranscriptHost" Grid.Row="0">
    <Grid>
        <controls:KaraokeView x:Name="Karaoke"
                              WordsSource="{Binding Words}"
                              ActiveIndex="{Binding ActiveWordIndex}"
                              SeekCommand="{Binding SeekToWordCommand}" />
        <TextBlock Text="{Binding StatusText}" Foreground="#FF808090" FontSize="24"
                   HorizontalAlignment="Center" VerticalAlignment="Center"
                   IsHitTestVisible="False">
            <TextBlock.Style>
                <Style TargetType="TextBlock">
                    <Setter Property="Visibility" Value="Collapsed" />
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding IsMediaLoaded}" Value="False">
                            <Setter Property="Visibility" Value="Visible" />
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </TextBlock.Style>
        </TextBlock>
    </Grid>
</Border>
```

Add font scaling + readable max width — in `MainWindow.xaml.cs` add to the constructor after `InitializeComponent()`:

```csharp
SizeChanged += (_, _) =>
{
    Karaoke.WordFontSize = Math.Clamp(ActualHeight * 0.045, 18, 72);
    // Cap transcript width on ultrawide screens; keep it centered.
    TranscriptHost.MaxWidth = Math.Max(800, ActualHeight * 1.8);
};
```

(`TranscriptHost` centers because a Border in a Grid cell with MaxWidth set and default HorizontalAlignment="Stretch" — set `HorizontalAlignment="Center"` plus `Width` binding is unreliable; instead set `HorizontalAlignment="Center"` on TranscriptHost in XAML and give KaraokeView `MinWidth="600"`.) Concretely: add `HorizontalAlignment="Center"` to the `TranscriptHost` Border and `MinWidth="600"` to the KaraokeView element.

- [ ] **Step 5: Build and manually verify**

Run: `dotnet run --project src/WeaveSyncLens.App`
Manual check: load an MP3 with speech; words appear wrapped; the current word turns gold as it's spoken; view smoothly scrolls keeping the active line centered; clicking any word seeks there (highlight follows); resizing the window scales font; very wide window keeps text centered at readable width.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat: karaoke view with word highlighting and smooth auto-scroll"
```

---

### Task 11: Visualizer framework + spectrum bars

**Files:**
- Create: `src/WeaveSyncLens.App/Visualizers/IVisualizer.cs`, `src/WeaveSyncLens.App/Visualizers/SpectrumBarsVisualizer.cs`, `src/WeaveSyncLens.App/Controls/VisualizerHost.cs`
- Modify: `src/WeaveSyncLens.App/MainWindow.xaml` (put VisualizerHost in row 1)

**Interfaces:**
- Consumes: `PlaybackEngine.Fft` / `FftProcessor.FftCalculated`, `SpectrumBinner.BinToBars`, `PlaybackEngine.SampleRate` (Tasks 4/6), `MainViewModel.Playback` (Task 9)
- Produces:
  - `record VisualizerFrame(float[] Magnitudes, int SampleRate, int FftLength);`
  - `interface IVisualizer { string Name { get; } void Render(VisualizerFrame frame, DrawingContext dc, Size area); }`
  - `class VisualizerHost : FrameworkElement { void Attach(PlaybackEngine engine); IVisualizer Visualizer { get; set; } }`
  - `class SpectrumBarsVisualizer : IVisualizer` — 48 bars, log-spaced, decay smoothing.

- [ ] **Step 1: Interface + frame**

`src/WeaveSyncLens.App/Visualizers/IVisualizer.cs`:

```csharp
using System.Windows;
using System.Windows.Media;

namespace WeaveSyncLens.App.Visualizers;

public record VisualizerFrame(float[] Magnitudes, int SampleRate, int FftLength);

/// <summary>A pluggable visualization. Add a class + register it in MainWindow to add a new style.</summary>
public interface IVisualizer
{
    string Name { get; }
    void Render(VisualizerFrame frame, DrawingContext dc, Size area);
}
```

- [ ] **Step 2: Spectrum bars implementation**

`src/WeaveSyncLens.App/Visualizers/SpectrumBarsVisualizer.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using WeaveSyncLens.Core.Audio;

namespace WeaveSyncLens.App.Visualizers;

public class SpectrumBarsVisualizer : IVisualizer
{
    private const int BarCount = 48;
    private const float Attack = 0.6f;  // rise speed (0..1 per frame)
    private const float Decay = 0.08f;  // fall speed

    private readonly float[] _levels = new float[BarCount];
    private static readonly Brush BarBrush = CreateBrush();

    public string Name => "Spectrum Bars";

    private static Brush CreateBrush()
    {
        var brush = new LinearGradientBrush(
            Color.FromRgb(0xFF, 0xD3, 0x4D), Color.FromRgb(0xFF, 0x6A, 0x3D),
            new Point(0, 1), new Point(0, 0));
        brush.Freeze();
        return brush;
    }

    public void Render(VisualizerFrame frame, DrawingContext dc, Size area)
    {
        var target = SpectrumBinner.BinToBars(frame.Magnitudes, BarCount, frame.SampleRate, frame.FftLength);

        double barWidth = area.Width / BarCount;
        double gap = Math.Max(1, barWidth * 0.15);

        for (int i = 0; i < BarCount; i++)
        {
            float t = target[i];
            _levels[i] += (t - _levels[i]) * (t > _levels[i] ? Attack : Decay);

            double h = _levels[i] * (area.Height - 4);
            if (h < 1) continue;
            dc.DrawRoundedRectangle(BarBrush, null,
                new Rect(i * barWidth + gap / 2, area.Height - h, barWidth - gap, h), 2, 2);
        }
    }
}
```

- [ ] **Step 3: VisualizerHost element**

`src/WeaveSyncLens.App/Controls/VisualizerHost.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using WeaveSyncLens.App.Visualizers;
using WeaveSyncLens.Core.Audio;

namespace WeaveSyncLens.App.Controls;

/// <summary>Renders the active IVisualizer every frame with the latest FFT data.</summary>
public class VisualizerHost : FrameworkElement
{
    private float[]? _latestMagnitudes;
    private PlaybackEngine? _engine;
    private readonly object _lock = new();

    public IVisualizer Visualizer { get; set; } = new SpectrumBarsVisualizer();

    public VisualizerHost()
    {
        if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            CompositionTarget.Rendering += (_, _) => InvalidateVisual();
    }

    public void Attach(PlaybackEngine engine)
    {
        _engine = engine;
        engine.Fft.FftCalculated += mags =>
        {
            lock (_lock) _latestMagnitudes = mags; // audio thread; swap reference only
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Background so the strip is visible even when silent.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        float[]? mags;
        lock (_lock) mags = _latestMagnitudes;
        if (mags is null || _engine is null) return;

        Visualizer.Render(new VisualizerFrame(mags, _engine.SampleRate, mags.Length * 2), dc, RenderSize);
    }
}
```

- [ ] **Step 4: Wire into MainWindow**

In `MainWindow.xaml` replace the visualizer placeholder Border content:

```xml
<Border x:Name="VisualizerHostArea" Grid.Row="1" Background="#FF0B0B0E">
    <controls:VisualizerHost x:Name="Visualizer" />
</Border>
```

In `MainWindow.xaml.cs` constructor after `InitializeComponent()`:

```csharp
Visualizer.Attach(Vm.Playback);
```

- [ ] **Step 5: Build and manually verify**

Run: `dotnet run --project src/WeaveSyncLens.App`
Manual check: play a song — gold/orange bars bounce with the music across the full width of the bottom strip; bass on the left, treble on the right; bars fall smoothly on pause.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat: pluggable visualizer framework with spectrum bars"
```

---

### Task 12: Fullscreen mode

**Files:**
- Modify: `src/WeaveSyncLens.App/MainWindow.xaml` (fullscreen button, transport bar as overlay-able element), `src/WeaveSyncLens.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: existing MainWindow members (`TransportBar`, `RootGrid`)
- Produces: F11 / double-click toggles fullscreen; Esc exits; in fullscreen the transport bar and cursor auto-hide after 3 s of mouse inactivity and reappear on mouse move.

- [ ] **Step 1: Add fullscreen state + toggling**

In `MainWindow.xaml.cs` add fields and methods:

```csharp
private bool _isFullscreen;
private WindowState _savedState;
private WindowStyle _savedStyle;
private readonly System.Windows.Threading.DispatcherTimer _hideUiTimer = new()
{
    Interval = TimeSpan.FromSeconds(3),
};

private void ToggleFullscreen()
{
    if (_isFullscreen) ExitFullscreen();
    else EnterFullscreen();
}

private void EnterFullscreen()
{
    _savedState = WindowState;
    _savedStyle = WindowStyle;
    WindowStyle = WindowStyle.None;
    ResizeMode = ResizeMode.NoResize;
    WindowState = WindowState.Normal;    // toggle forces WPF to re-measure over the taskbar
    WindowState = WindowState.Maximized;
    _isFullscreen = true;
    _hideUiTimer.Start();
}

private void ExitFullscreen()
{
    WindowStyle = _savedStyle;
    ResizeMode = ResizeMode.CanResize;
    WindowState = _savedState;
    _isFullscreen = false;
    _hideUiTimer.Stop();
    ShowChrome();
}

private void ShowChrome()
{
    TransportBar.Visibility = Visibility.Visible;
    Cursor = System.Windows.Input.Cursors.Arrow;
}

private void HideChrome()
{
    if (!_isFullscreen) return;
    TransportBar.Visibility = Visibility.Collapsed;
    Cursor = System.Windows.Input.Cursors.None;
}
```

In the constructor after `InitializeComponent()`:

```csharp
_hideUiTimer.Tick += (_, _) => { _hideUiTimer.Stop(); HideChrome(); };
MouseMove += (_, _) =>
{
    if (!_isFullscreen) return;
    ShowChrome();
    _hideUiTimer.Stop();
    _hideUiTimer.Start();
};
MouseDoubleClick += (_, _) => ToggleFullscreen();
```

Extend `Window_KeyDown`:

```csharp
private void Window_KeyDown(object sender, KeyEventArgs e)
{
    switch (e.Key)
    {
        case Key.Space:
            Vm.TogglePlayPauseCommand.Execute(null);
            e.Handled = true;
            break;
        case Key.F11:
            ToggleFullscreen();
            e.Handled = true;
            break;
        case Key.Escape when _isFullscreen:
            ExitFullscreen();
            e.Handled = true;
            break;
    }
}
```

- [ ] **Step 2: Add a fullscreen button to the transport bar**

In `MainWindow.xaml`, inside the `TransportBar` DockPanel after the play/pause button:

```xml
<Button Content="⛶" FontSize="16" Margin="0,8,8,8" Padding="10,2"
        ToolTip="Fullscreen (F11)" Click="Fullscreen_Click" />
```

And in code-behind:

```csharp
private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
```

- [ ] **Step 3: Build and manually verify**

Run: `dotnet run --project src/WeaveSyncLens.App`
Manual check: F11 → borderless fullscreen covering taskbar; transport bar and cursor disappear after ~3 s idle; moving mouse brings them back; double-click toggles; Esc exits restoring the previous window size; text and bars scale to the full display (test 16:9; if available, an ultrawide or a custom-resolution window).

- [ ] **Step 4: Commit**

```powershell
git add -A
git commit -m "feat: fullscreen mode with auto-hiding chrome and cursor"
```

---

### Task 13: Settings (model size + visualizer choice) and error-handling polish

**Files:**
- Create: `src/WeaveSyncLens.Core/Settings/AppSettings.cs`
- Modify: `src/WeaveSyncLens.App/ViewModels/MainViewModel.cs`, `src/WeaveSyncLens.App/MainWindow.xaml`, `src/WeaveSyncLens.App/MainWindow.xaml.cs`
- Test: `tests/WeaveSyncLens.Core.Tests/AppSettingsTests.cs`

**Interfaces:**
- Consumes: `WhisperModelSize` (Task 7)
- Produces:
  - `class AppSettings { WhisperModelSize ModelSize = Base; string VisualizerName = "Spectrum Bars"; static AppSettings Load(); void Save(); static string SettingsPath; }` — JSON at `%APPDATA%/WeaveSyncLens/settings.json`; `Load()` returns defaults on missing/corrupt file.

- [ ] **Step 1: Write the failing tests**

`tests/WeaveSyncLens.Core.Tests/AppSettingsTests.cs`:

```csharp
using WeaveSyncLens.Core.Settings;
using WeaveSyncLens.Core.Transcription;
using Xunit;

namespace WeaveSyncLens.Core.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wsl-settings").FullName;

    public AppSettingsTests() => AppSettings.SettingsPath = Path.Combine(_dir, "settings.json");
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        var s = AppSettings.Load();
        Assert.Equal(WhisperModelSize.Base, s.ModelSize);
        Assert.Equal("Spectrum Bars", s.VisualizerName);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var s = AppSettings.Load();
        s.ModelSize = WhisperModelSize.Small;
        s.VisualizerName = "Waveform";
        s.Save();

        var loaded = AppSettings.Load();
        Assert.Equal(WhisperModelSize.Small, loaded.ModelSize);
        Assert.Equal("Waveform", loaded.VisualizerName);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        File.WriteAllText(AppSettings.SettingsPath, "{broken");
        Assert.Equal(WhisperModelSize.Base, AppSettings.Load().ModelSize);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter AppSettingsTests`
Expected: FAIL — type not defined.

- [ ] **Step 3: Implement settings**

`src/WeaveSyncLens.Core/Settings/AppSettings.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaveSyncLens.Core.Transcription;

namespace WeaveSyncLens.Core.Settings;

public class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Overridable for tests; defaults to %APPDATA%/WeaveSyncLens/settings.json.</summary>
    public static string SettingsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WeaveSyncLens", "settings.json");

    public WhisperModelSize ModelSize { get; set; } = WhisperModelSize.Base;
    public string VisualizerName { get; set; } = "Spectrum Bars";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
        }
        catch (JsonException) { }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter AppSettingsTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Use settings in the app + model-size picker**

In `MainViewModel`: add at top of constructor `var settings = AppSettings.Load();` and use `new WhisperLocalTranscriber(settings.ModelSize)`; add:

```csharp
public IReadOnlyList<WhisperModelSize> ModelSizes { get; } =
    (WhisperModelSize[])Enum.GetValues(typeof(WhisperModelSize));

[ObservableProperty] private WhisperModelSize _selectedModelSize;

partial void OnSelectedModelSizeChanged(WhisperModelSize value)
{
    var s = AppSettings.Load();
    s.ModelSize = value;
    s.Save();
    _loader = new MediaSessionLoader(
        new FfmpegMediaImporter(), new WhisperLocalTranscriber(value));
}
```

(Change `_loader` from `readonly` to mutable, and set `SelectedModelSize = settings.ModelSize;` in the constructor **after** `_loader` is first assigned.)

In `MainWindow.xaml` transport bar, after the fullscreen button:

```xml
<ComboBox ItemsSource="{Binding ModelSizes}" SelectedItem="{Binding SelectedModelSize}"
          Margin="0,8,8,8" VerticalAlignment="Center" MinWidth="90"
          ToolTip="Whisper model (bigger = more accurate, slower). Used on next transcription." />
```

- [ ] **Step 6: Friendly error for missing ffmpeg**

In `MainViewModel.LoadFileAsync`, before the `try` block's `StatusText` line, add:

```csharp
if (!FfmpegLocator.IsAvailable && Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".m4a")
{
    MessageBox.Show(
        "ffmpeg was not found. Run scripts/setup-ffmpeg.ps1 once (or install ffmpeg on PATH) to enable MP4/M4A import.",
        "WeaveSyncLens", MessageBoxButton.OK, MessageBoxImage.Warning);
    return;
}
```

Also for MP3s without ffmpeg the transcription conversion still needs it — apply the same check for `.mp3` when no sidecar exists:

```csharp
bool needsTranscription = WeaveSyncLens.Core.Sidecar.TranscriptSidecarStore.TryLoad(path) is null;
if (!FfmpegLocator.IsAvailable && needsTranscription)
{
    MessageBox.Show(
        "ffmpeg was not found. Run scripts/setup-ffmpeg.ps1 once (or install ffmpeg on PATH) — it is required to prepare audio for transcription.",
        "WeaveSyncLens", MessageBoxButton.OK, MessageBoxImage.Warning);
    return;
}
```

(Use only this second, general check — it covers the MP4 case too. Note `TryLoad` is called again inside the loader; that double-load is fine and keeps the check simple.)

- [ ] **Step 7: Full verification pass**

Run: `dotnet test` → Expected: ALL tests pass.
Run: `dotnet run --project src/WeaveSyncLens.App`
Manual check: model picker shows Tiny…LargeV3 and persists across app restarts (check `%APPDATA%\WeaveSyncLens\settings.json`); renaming `third_party/ffmpeg` away and dropping a new MP3 shows the friendly ffmpeg message (restore afterwards); dropping a `.txt` file shows the unsupported-type error dialog rather than crashing.

- [ ] **Step 8: Commit**

```powershell
git add -A
git commit -m "feat: persistent settings with model picker and ffmpeg guidance"
```

---

### Task 14: README + final end-to-end verification

**Files:**
- Create: `README.md`

**Interfaces:**
- Consumes: everything
- Produces: documented, verified v1

- [ ] **Step 1: Write README.md**

```markdown
# WeaveSyncLens

Drop in an MP3/MP4 → local Whisper transcription → karaoke-style highlighted,
auto-scrolling transcript synced to playback, with a spectrum-bars visualizer.
Windows, WPF, fully offline.

## Setup

1. Install the .NET 8 SDK.
2. Fetch ffmpeg (one time): `powershell -ExecutionPolicy Bypass -File scripts/setup-ffmpeg.ps1`
3. Run: `dotnet run --project src/WeaveSyncLens.App`

First transcription downloads the selected Whisper model (~75 MB tiny … ~3 GB large-v3)
into `models/`.

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
```

- [ ] **Step 2: End-to-end verification**

1. `dotnet test` → all pass.
2. Fresh-clone simulation: `git clean -xdn` (dry-run; confirm only bin/obj/third_party/models would go).
3. `dotnet run --project src/WeaveSyncLens.App`, then verify the full user journey: drop MP3 → transcribe with progress → words highlight and scroll during playback → bars dance → seek via slider and via word click → F11 fullscreen with auto-hiding controls → Esc → close app → reopen → drop same MP3 → starts instantly from sidecar.
4. Drop an MP4 with speech → audio plays, transcript syncs.

- [ ] **Step 3: Commit**

```powershell
git add -A
git commit -m "docs: README with setup, usage, and architecture"
```

---

## Deferred (per spec, not in v1)

- Step 2: transcript editor + NSFW flag UI with mute/beep (sidecar `Flags` field already reserves space).
- Visualizer picker UI — becomes meaningful once a second `IVisualizer` exists; `AppSettings.VisualizerName` already persists the choice.
- Cloud transcriber behind `ITranscriber`.
- Vocal isolation preprocessing (Mel-Band RoFormer sidecar) before `ITranscriber`.
- Video rendering for MP4 (PlaybackEngine seam: it only consumes the audio stream; a video view can attach to the same clock via `CurrentTimeSeconds`).
