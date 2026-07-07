using Whisper.net.Ggml;

namespace WeaveSyncLens.Core.Transcription;

/// <summary>Downloads and caches Whisper GGML models under models/.</summary>
public static class WhisperModelStore
{
    public static string ModelsDirectory { get; set; } =
        Path.Combine(AppRoot.Resolve(), "models");

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
        using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(ToGgmlType(size), cancellationToken: ct);
        var tmp = path + ".download";
        try
        {
            await using (var file = File.Create(tmp))
                await CopyWithProgressAsync(modelStream, file, size, progress, ct);
            File.Move(tmp, path, overwrite: true);
            progress?.Report(new TranscriptionProgress { Stage = "DownloadingModel", Fraction = 1 });
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
        return path;
    }

    private static async Task CopyWithProgressAsync(
        Stream source, Stream destination, WhisperModelSize size,
        IProgress<TranscriptionProgress>? progress, CancellationToken ct)
    {
        var approxSize = ApproximateSizeBytes(size);
        var buffer = new byte[81920]; // ~80 KB chunks
        long totalCopied = 0;
        long lastReportedBytes = 0;
        const long reportInterval = 2097152; // ~2 MB

        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await destination.WriteAsync(buffer, 0, bytesRead, ct);
            totalCopied += bytesRead;

            if (totalCopied - lastReportedBytes >= reportInterval)
            {
                var fraction = Math.Min(0.99, (double)totalCopied / approxSize);
                progress?.Report(new TranscriptionProgress { Stage = "DownloadingModel", Fraction = fraction });
                lastReportedBytes = totalCopied;
            }
        }
    }

    private static long ApproximateSizeBytes(WhisperModelSize size) => size switch
    {
        WhisperModelSize.Tiny => 78L * 1024 * 1024,
        WhisperModelSize.Base => 148L * 1024 * 1024,
        WhisperModelSize.Small => 488L * 1024 * 1024,
        WhisperModelSize.Medium => 1533L * 1024 * 1024,
        WhisperModelSize.LargeV3 => 3095L * 1024 * 1024,
        _ => 148L * 1024 * 1024,
    };
}
