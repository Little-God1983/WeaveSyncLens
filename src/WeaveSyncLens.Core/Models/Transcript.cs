namespace WeaveSyncLens.Core.Models;

public class Transcript
{
    public IReadOnlyList<Word> Words { get; }

    public Transcript(IReadOnlyList<Word> words) => Words = words;

    /// <summary>
    /// Index of the word active at <paramref name="timeSeconds"/>: the word whose
    /// [Start, End) contains it, otherwise the most recent word already started.
    /// -1 if playback is before the first word (or transcript is empty).
    /// <paramref name="hintIndex"/> is a previous result used to skip the search
    /// during normal forward playback; any value is safe.
    /// </summary>
    public int FindActiveWordIndex(double timeSeconds, int hintIndex = -1)
    {
        if (Words.Count == 0) return -1;

        // Fast path: time still inside hint word or the next one (normal playback tick).
        if (hintIndex >= 0 && hintIndex < Words.Count && timeSeconds >= Words[hintIndex].Start)
        {
            if (hintIndex + 1 >= Words.Count || timeSeconds < Words[hintIndex + 1].Start)
                return hintIndex;
            if (hintIndex + 2 >= Words.Count || timeSeconds < Words[hintIndex + 2].Start)
                return hintIndex + 1;
        }

        // Binary search for the last word with Start <= time.
        int lo = 0, hi = Words.Count - 1, result = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (Words[mid].Start <= timeSeconds) { result = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return result;
    }
}
