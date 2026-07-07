namespace WeaveSyncLens.Core.Audio;

/// <summary>Maps FFT magnitudes into log-spaced 0..1 bar heights for display.</summary>
public static class SpectrumBinner
{
    private const double MinFreq = 60;
    private const double MaxFreq = 16000;
    private const float DbFloor = -60f; // magnitudes this quiet (or quieter) render as 0

    public static float[] BinToBars(float[] magnitudes, int barCount, int sampleRate, int fftLength)
    {
        var bars = new float[barCount];
        double binWidth = (double)sampleRate / fftLength;

        for (int bar = 0; bar < barCount; bar++)
        {
            double f0 = MinFreq * Math.Pow(MaxFreq / MinFreq, (double)bar / barCount);
            double f1 = MinFreq * Math.Pow(MaxFreq / MinFreq, (double)(bar + 1) / barCount);
            int b0 = Math.Clamp((int)(f0 / binWidth), 0, magnitudes.Length - 1);
            int b1 = Math.Clamp((int)Math.Ceiling(f1 / binWidth), b0 + 1, magnitudes.Length);

            float max = 0;
            for (int i = b0; i < b1; i++)
                if (magnitudes[i] > max) max = magnitudes[i];

            if (max <= 0)
            {
                bars[bar] = 0;
                continue;
            }
            float db = 20f * (float)Math.Log10(max);
            bars[bar] = Math.Clamp((db - DbFloor) / -DbFloor, 0f, 1f);
        }
        return bars;
    }
}
