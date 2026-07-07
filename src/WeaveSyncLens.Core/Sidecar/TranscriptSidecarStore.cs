using System.Text.Json;
using System.Text.Json.Serialization;
using WeaveSyncLens.Core.Models;

namespace WeaveSyncLens.Core.Sidecar;

/// <summary>Reads/writes the JSON transcript sidecar next to a media file.</summary>
public static class TranscriptSidecarStore
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed class SidecarDto
    {
        public int Version { get; set; }
        public string? SourceFile { get; set; }
        public string? Model { get; set; }
        public DateTime CreatedUtc { get; set; }
        public List<WordDto> Words { get; set; } = new();
    }

    private sealed class WordDto
    {
        public string Text { get; set; } = "";
        public double Start { get; set; }
        public double End { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public WordFlags Flags { get; set; }
    }

    public static string GetSidecarPath(string mediaPath) =>
        Path.Combine(Path.GetDirectoryName(mediaPath)!,
                     Path.GetFileNameWithoutExtension(mediaPath) + ".transcript.json");

    public static void Save(string mediaPath, Transcript transcript, string modelName)
    {
        var dto = new SidecarDto
        {
            Version = CurrentVersion,
            SourceFile = Path.GetFileName(mediaPath),
            Model = modelName,
            CreatedUtc = DateTime.UtcNow,
            Words = transcript.Words
                .Select(w => new WordDto { Text = w.Text, Start = w.Start, End = w.End, Flags = w.Flags })
                .ToList(),
        };
        File.WriteAllText(GetSidecarPath(mediaPath), JsonSerializer.Serialize(dto, JsonOptions));
    }

    public static Transcript? TryLoad(string mediaPath)
    {
        var path = GetSidecarPath(mediaPath);
        if (!File.Exists(path)) return null;

        var json = File.ReadAllText(path);

        // Check the version field before attempting full deserialization: a future-version
        // sidecar may contain enum values (e.g. Flags) that this build doesn't know about,
        // which would throw during full-DTO binding. Reading the version first lets us
        // detect and quarantine forward-incompatible files without ever attempting (and
        // failing) a full parse that could otherwise be mistaken for corruption.
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("version", out var versionProp) ||
                versionProp.ValueKind != JsonValueKind.Number ||
                !versionProp.TryGetInt32(out var version) ||
                version != CurrentVersion)
            {
                MoveToBackup(path);
                return null;
            }
        }
        catch (JsonException)
        {
            MoveToBackup(path);
            return null;
        }

        SidecarDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SidecarDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            MoveToBackup(path);
            return null;
        }
        if (dto is null)
        {
            MoveToBackup(path);
            return null;
        }

        return new Transcript(dto.Words
            .Select(w => new Word(w.Text, w.Start, w.End, w.Flags))
            .ToList());
    }

    private static void MoveToBackup(string path) => File.Move(path, path + ".bak", overwrite: true);
}
