namespace WeaveSyncLens.Core.Import;

public interface IMediaImporter
{
    /// <summary>Produces a 16 kHz mono WAV for transcription. Returns the WAV path (temp dir).</summary>
    Task<string> PrepareForTranscriptionAsync(string mediaPath, CancellationToken ct);
}
