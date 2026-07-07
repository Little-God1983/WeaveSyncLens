using System.Text.Json;
using System.Text.Json.Serialization;
using WeaveSyncLens.Core.Transcription;

namespace WeaveSyncLens.Core.Settings;

public class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Overridable for tests; defaults to %APPDATA%/WeaveSyncLens/settings.json.</summary>
    public static string SettingsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WeaveSyncLens", "settings.json");

    public WhisperModelSize ModelSize { get; set; } = WhisperModelSize.Base;
    public string VisualizerName { get; set; } = "Spectrum Bars";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
        }
        catch (JsonException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
