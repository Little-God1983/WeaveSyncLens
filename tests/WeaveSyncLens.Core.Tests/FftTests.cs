using System.Linq;
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
    public void FftProcessor_StereoDownmixCancelsOppositeChannels()
    {
        // L[i] = sin(440), R[i] = -L[i]. If downmix is correct, mono = (L+R)/2 = 0
        // exactly, so every FFT magnitude must be ~0. A channel-picking bug (e.g.
        // only reading L) or a sum-without-divide bug would leave the sine visible.
        const int fftLength = 1024;
        var fft = new FftProcessor(fftLength);
        float[]? mags = null;
        fft.FftCalculated += m => mags ??= m;

        var mono = Sine(440, 44100, fftLength);
        var stereo = new float[fftLength * 2];
        for (int i = 0; i < fftLength; i++)
        {
            stereo[i * 2] = mono[i];
            stereo[i * 2 + 1] = -mono[i];
        }
        fft.AddSamples(stereo, 0, stereo.Length, channels: 2);

        Assert.NotNull(mags);
        Assert.All(mags!, m => Assert.True(m < 1e-3, $"expected ~0 magnitude, got {m}"));
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

    [Fact]
    public void EndToEnd_RealisticSine_ProducesReasonableBarLevels()
    {
        // Empirical check for a disputed review finding: does BinToBars's dB
        // normalization (-60 dB floor) saturate every bar to 1.0 for real audio,
        // because raw FFT magnitudes are supposedly >>1? NAudio's forward FFT
        // applies 1/N scaling, so a realistic signal should land in a useful,
        // non-saturated display range.
        const int sampleRate = 44100, fftLength = 2048;
        const double freq = 1000;
        const float amplitude = 0.5f;

        var fft = new FftProcessor(fftLength);
        float[]? mags = null;
        fft.FftCalculated += m => mags ??= m;

        var samples = Sine(freq, sampleRate, fftLength);
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= amplitude;
        fft.AddSamples(samples, 0, samples.Length, channels: 1);

        Assert.NotNull(mags);
        var bars = SpectrumBinner.BinToBars(mags!, barCount: 48, sampleRate: sampleRate, fftLength: fftLength);

        float maxBar = bars.Max();
        Assert.InRange(maxBar, 0.3f, 0.99f);

        int over50 = bars.Count(b => b > 0.5f);
        Assert.True(over50 < bars.Length / 2, $"expected fewer than half the bars > 0.5, got {over50}/{bars.Length}");
    }
}
