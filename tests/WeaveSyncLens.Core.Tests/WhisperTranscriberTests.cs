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
