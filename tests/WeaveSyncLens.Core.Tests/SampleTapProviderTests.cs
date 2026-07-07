using NAudio.Wave;
using WeaveSyncLens.Core.Audio;
using Xunit;

namespace WeaveSyncLens.Core.Tests;

public class SampleTapProviderTests
{
    private sealed class FakeSource : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++) buffer[offset + i] = 0.5f;
            return count;
        }
    }

    [Fact]
    public void Read_PassesSamplesThroughAndInvokesCallback()
    {
        int cbCount = 0, cbChannels = 0;
        var tap = new SampleTapProvider(new FakeSource(),
            (buf, off, cnt, ch) => { cbCount = cnt; cbChannels = ch; });

        var buffer = new float[512];
        int read = tap.Read(buffer, 0, 512);

        Assert.Equal(512, read);
        Assert.Equal(0.5f, buffer[100]);
        Assert.Equal(512, cbCount);
        Assert.Equal(2, cbChannels);
    }
}
