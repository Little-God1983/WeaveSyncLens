namespace WeaveSyncLens.Core;

/// <summary>Resolves the application root for bundled assets (ffmpeg, models):
/// the nearest ancestor of BaseDirectory containing WeaveSyncLens.sln (dev runs),
/// falling back to BaseDirectory itself (deployed builds).</summary>
public static class AppRoot
{
    public static string Resolve()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WeaveSyncLens.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
