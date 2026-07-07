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
        FfmpegLocator.Configure(AppRoot.Resolve());
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
            using (var reader = new NAudio.Wave.WaveFileReader(wav))
            {
                Assert.Equal(16000, reader.WaveFormat.SampleRate);
                Assert.Equal(1, reader.WaveFormat.Channels);
            }
            File.Delete(wav);
        }
        finally { File.Delete(mp3); }
    }
}
