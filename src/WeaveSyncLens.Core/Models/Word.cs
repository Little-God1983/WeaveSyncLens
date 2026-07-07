namespace WeaveSyncLens.Core.Models;

[Flags]
public enum WordFlags
{
    None = 0,
    Nsfw = 1,
}

/// <summary>One transcribed word. Times are seconds from media start.</summary>
public record Word(string Text, double Start, double End, WordFlags Flags = WordFlags.None);
