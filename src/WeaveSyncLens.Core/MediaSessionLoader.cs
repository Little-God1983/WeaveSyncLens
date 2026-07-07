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
