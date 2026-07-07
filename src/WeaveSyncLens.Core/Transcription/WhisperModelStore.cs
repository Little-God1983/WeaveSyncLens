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
        using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(ToGgmlType(size), cancellationToken: ct);
        var tmp = path + ".download";
        await using (var file = File.Create(tmp))
            await modelStream.CopyToAsync(file, ct);
        File.Move(tmp, path, overwrite: true);
        progress?.Report(new TranscriptionProgress { Stage = "DownloadingModel", Fraction = 1 });
        return path;
    }
}
