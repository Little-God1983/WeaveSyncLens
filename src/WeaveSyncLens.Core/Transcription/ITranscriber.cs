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
