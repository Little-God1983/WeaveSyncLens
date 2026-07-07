using WeaveSyncLens.Core.Models;
using Xunit;

namespace WeaveSyncLens.Core.Tests;

public class TranscriptTests
{
    private static Transcript Sample() => new(new[]
    {
        new Word("Hello", 0.5, 0.9),
        new Word("world", 1.0, 1.4),
        new Word("again", 2.0, 2.6),
    });

    [Fact]
    public void FindActiveWordIndex_BeforeFirstWord_ReturnsMinusOne()
        => Assert.Equal(-1, Sample().FindActiveWordIndex(0.2));

    [Fact]
    public void FindActiveWordIndex_InsideWord_ReturnsThatWord()
        => Assert.Equal(1, Sample().FindActiveWordIndex(1.2));

    [Fact]
    public void FindActiveWordIndex_InGapBetweenWords_ReturnsPreviousWord()
        => Assert.Equal(1, Sample().FindActiveWordIndex(1.7));

    [Fact]
    public void FindActiveWordIndex_AfterLastWord_ReturnsLastWord()
        => Assert.Equal(2, Sample().FindActiveWordIndex(99.0));

    [Fact]
    public void FindActiveWordIndex_WithHint_ReturnsSameResult()
    {
        var t = Sample();
        Assert.Equal(t.FindActiveWordIndex(2.1), t.FindActiveWordIndex(2.1, hintIndex: 1));
        Assert.Equal(t.FindActiveWordIndex(0.6), t.FindActiveWordIndex(0.6, hintIndex: 2));
    }

    [Fact]
    public void EmptyTranscript_ReturnsMinusOne()
        => Assert.Equal(-1, new Transcript(Array.Empty<Word>()).FindActiveWordIndex(1.0));
}
