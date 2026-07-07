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
