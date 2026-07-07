using NAudio.Dsp;

namespace WeaveSyncLens.Core.Audio;

/// <summary>
/// Accumulates playback samples and raises FFT magnitude frames.
/// Thread note: AddSamples is called on the audio thread; FftCalculated fires there too.
/// </summary>
public class FftProcessor
{
    private readonly int _fftLength;
    private readonly int _m; // log2(fftLength)
    private readonly float[] _window;
    private int _pos;

    public event Action<float[]>? FftCalculated;

    public FftProcessor(int fftLength = 2048)
    {
        if ((fftLength & (fftLength - 1)) != 0)
            throw new ArgumentException("FFT length must be a power of two", nameof(fftLength));
        _fftLength = fftLength;
        _m = (int)Math.Log2(fftLength);
        _window = new float[fftLength];
    }

    public void AddSamples(float[] buffer, int offset, int count, int channels)
    {
        for (int i = 0; i + channels <= count; i += channels)
        {
            float mono = 0;
            for (int c = 0; c < channels; c++)
                mono += buffer[offset + i + c];
            _window[_pos++] = mono / channels;

            if (_pos == _fftLength)
            {
                _pos = 0;
                Compute();
            }
        }
    }

    private void Compute()
    {
        var complex = new Complex[_fftLength];
        for (int i = 0; i < _fftLength; i++)
        {
            complex[i].X = (float)(_window[i] * FastFourierTransform.HannWindow(i, _fftLength));
            complex[i].Y = 0;
        }
        FastFourierTransform.FFT(true, _m, complex);

        var magnitudes = new float[_fftLength / 2];
        for (int i = 0; i < magnitudes.Length; i++)
            magnitudes[i] = (float)Math.Sqrt(complex[i].X * complex[i].X + complex[i].Y * complex[i].Y);

        FftCalculated?.Invoke(magnitudes);
    }
}
