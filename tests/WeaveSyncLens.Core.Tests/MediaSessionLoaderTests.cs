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
        public string? LastWavPath;
        public Task<string> PrepareForTranscriptionAsync(string mediaPath, CancellationToken ct)
        {
            Calls++;
            var wav = Path.Combine(Path.GetTempPath(), $"fake-{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(wav, new byte[] { 0 });
            LastWavPath = wav;
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

    private sealed class ThrowingTranscriber : ITranscriber
    {
        public string? LastWavPath;
        public string ModelName => "throwing";
        public Task<Transcript> TranscribeAsync(string wavPath, IProgress<TranscriptionProgress>? p, CancellationToken ct)
        {
            LastWavPath = wavPath;
            throw new InvalidOperationException("boom");
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
        Assert.Equal(importer.LastWavPath, transcriber.LastWavPath); // importer path == transcriber path
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

    [Fact]
    public async Task TranscriberThrows_StillDeletesTempWavAndPropagates()
    {
        var importer = new FakeImporter();
        var transcriber = new ThrowingTranscriber();
        var loader = new MediaSessionLoader(importer, transcriber);

        await Assert.ThrowsAsync<InvalidOperationException>(() => loader.LoadAsync(MediaPath, null, CancellationToken.None));

        Assert.False(File.Exists(transcriber.LastWavPath!)); // temp WAV cleaned up despite exception
        Assert.False(File.Exists(TranscriptSidecarStore.GetSidecarPath(MediaPath))); // no sidecar created
    }
}
