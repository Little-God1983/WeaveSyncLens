using WeaveSyncLens.Core.Audio;
using Xunit;

namespace WeaveSyncLens.Core.Tests;

public class FftTests
{
    private static float[] Sine(double freq, int sampleRate, int count)
    {
        var s = new float[count];
        for (int i = 0; i < count; i++)
            s[i] = (float)Math.Sin(2 * Math.PI * freq * i / sampleRate);
        return s;
    }

    [Fact]
    public void FftProcessor_RaisesEventOncePerFullWindow()
    {
        var fft = new FftProcessor(fftLength: 1024);
        int events = 0;
        fft.FftCalculated += _ => events++;

        var samples = Sine(440, 44100, 2500);
        fft.AddSamples(samples, 0, samples.Length, channels: 1);

        Assert.Equal(2, events); // 2500 / 1024 = 2 full windows
    }

    [Fact]
    public void FftProcessor_PeakBinMatchesSineFrequency()
    {
        const int sampleRate = 44100, fftLength = 2048;
        const double freq = 1000;
        var fft = new FftProcessor(fftLength);
        float[]? mags = null;
        fft.FftCalculated += m => mags ??= m;

        var samples = Sine(freq, sampleRate, fftLength);
        fft.AddSamples(samples, 0, samples.Length, channels: 1);

        Assert.NotNull(mags);
        int peak = Array.IndexOf(mags!, mags!.Max());
        double peakFreq = (double)peak * sampleRate / fftLength;
        Assert.InRange(peakFreq, freq - 50, freq + 50);
    }

    [Fact]
    public void FftProcessor_StereoIsDownmixed()
    {
        var fft = new FftProcessor(fftLength: 1024);
        int events = 0;
        fft.FftCalculated += _ => events++;

        var stereo = new float[2048]; // 1024 frames of stereo silence
        fft.AddSamples(stereo, 0, stereo.Length, channels: 2);

        Assert.Equal(1, events);
    }

    [Fact]
    public void BinToBars_ReturnsRequestedCountInRange()
    {
        var mags = new float[1024];
        mags[100] = 5f;
        var bars = SpectrumBinner.BinToBars(mags, barCount: 48, sampleRate: 44100, fftLength: 2048);

        Assert.Equal(48, bars.Length);
        Assert.All(bars, b => Assert.InRange(b, 0f, 1f));
        Assert.Contains(bars, b => b > 0f);
    }

    [Fact]
    public void BinToBars_SilenceIsAllZero()
    {
        var bars = SpectrumBinner.BinToBars(new float[1024], 48, 44100, 2048);
        Assert.All(bars, b => Assert.Equal(0f, b));
    }
}
