using WeaveSyncLens.Core.Settings;
using WeaveSyncLens.Core.Transcription;
using Xunit;

namespace WeaveSyncLens.Core.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wsl-settings").FullName;
    private readonly string _originalPath;

    public AppSettingsTests()
    {
        _originalPath = AppSettings.SettingsPath;
        AppSettings.SettingsPath = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        AppSettings.SettingsPath = _originalPath;
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        var s = AppSettings.Load();
        Assert.Equal(WhisperModelSize.Base, s.ModelSize);
        Assert.Equal("Spectrum Bars", s.VisualizerName);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var s = AppSettings.Load();
        s.ModelSize = WhisperModelSize.Small;
        s.VisualizerName = "Waveform";
        s.Save();

        var loaded = AppSettings.Load();
        Assert.Equal(WhisperModelSize.Small, loaded.ModelSize);
        Assert.Equal("Waveform", loaded.VisualizerName);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        File.WriteAllText(AppSettings.SettingsPath, "{broken");
        Assert.Equal(WhisperModelSize.Base, AppSettings.Load().ModelSize);
    }
}
