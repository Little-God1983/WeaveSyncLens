using NAudio.Wave;

namespace WeaveSyncLens.Core.Audio;

/// <summary>Pass-through sample provider that mirrors every buffer to a callback (for FFT).</summary>
public class SampleTapProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Action<float[], int, int, int> _onSamples;

    public SampleTapProvider(ISampleProvider source, Action<float[], int, int, int> onSamples)
    {
        _source = source;
        _onSamples = onSamples;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read > 0)
            _onSamples(buffer, offset, read, WaveFormat.Channels);
        return read;
    }
}
