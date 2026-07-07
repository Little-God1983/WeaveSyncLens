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
