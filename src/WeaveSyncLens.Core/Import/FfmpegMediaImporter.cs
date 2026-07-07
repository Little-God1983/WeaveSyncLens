using System.Diagnostics;
using FFMpegCore;

namespace WeaveSyncLens.Core.Import;

/// <summary>Locates ffmpeg.exe (third_party/ffmpeg first, then PATH) and configures FFMpegCore.</summary>
public static class FfmpegLocator
{
    public static bool IsAvailable { get; private set; }

    public static void Configure(string appRoot)
    {
        var bundled = Path.Combine(appRoot, "third_party", "ffmpeg");
        if (File.Exists(Path.Combine(bundled, "ffmpeg.exe")))
        {
            GlobalFFOptions.Configure(new FFOptions { BinaryFolder = bundled });
            IsAvailable = true;
            return;
        }
        // Fall back to PATH.
        try
        {
            using var p = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            if (p!.WaitForExit(5000))
            {
                IsAvailable = p.ExitCode == 0;
            }
            else
            {
                try { p.Kill(); } catch { }
                IsAvailable = false;
            }
        }
        catch { IsAvailable = false; }
    }

    /// <summary>Runs a raw ffmpeg command (used by tests to synthesize fixtures).</summary>
    public static async Task RunFfmpegAsync(string arguments)
    {
        var exe = File.Exists(Path.Combine(GlobalFFOptions.Current.BinaryFolder, "ffmpeg.exe"))
            ? Path.Combine(GlobalFFOptions.Current.BinaryFolder, "ffmpeg.exe")
            : "ffmpeg";
        using var p = Process.Start(new ProcessStartInfo(exe, "-y " + arguments)
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed: {await stderrTask}");
    }
}

public class FfmpegMediaImporter : IMediaImporter
{
    private static readonly string[] SupportedExtensions = { ".mp3", ".mp4", ".wav", ".m4a" };

    public async Task<string> PrepareForTranscriptionAsync(string mediaPath, CancellationToken ct)
    {
        var ext = Path.GetExtension(mediaPath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext))
            throw new NotSupportedException($"Unsupported file type: {ext}");

        var outPath = Path.Combine(Path.GetTempPath(),
            $"weavesynclens-{Path.GetFileNameWithoutExtension(mediaPath)}-{Guid.NewGuid():N}.wav");

        await FFMpegArguments
            .FromFileInput(mediaPath)
            .OutputToFile(outPath, overwrite: true, o => o
                .WithAudioSamplingRate(16000)
                .WithCustomArgument("-ac 1")
                .ForceFormat("wav"))
            .CancellableThrough(ct)
            .ProcessAsynchronously();

        return outPath;
    }
}
