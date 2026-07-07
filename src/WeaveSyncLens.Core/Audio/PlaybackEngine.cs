using NAudio.Wave;

namespace WeaveSyncLens.Core.Audio;

/// <summary>NAudio playback with position clock and FFT sample tap.</summary>
public class PlaybackEngine : IDisposable
{
    private WaveStream? _reader;
    private WaveOutEvent? _output;
    private EventHandler<StoppedEventArgs>? _stoppedHandler;

    public FftProcessor Fft { get; } = new(fftLength: 2048);

    public event Action? PlaybackStopped;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public double CurrentTimeSeconds => _reader?.CurrentTime.TotalSeconds ?? 0;
    public double TotalTimeSeconds => _reader?.TotalTime.TotalSeconds ?? 0;
    public int SampleRate => _reader?.WaveFormat.SampleRate ?? 44100;

    public void Load(string mediaPath)
    {
        Unload();
        Fft.Reset(); // discard partial samples from the previous track
        _reader = Path.GetExtension(mediaPath).ToLowerInvariant() switch
        {
            ".mp3" or ".wav" => new AudioFileReader(mediaPath),
            _ => new MediaFoundationReader(mediaPath), // MP4/M4A via Windows Media Foundation
        };
        ISampleProvider sp = _reader is AudioFileReader afr ? afr : _reader.ToSampleProvider();
        var tap = new SampleTapProvider(sp,
            (buf, off, cnt, ch) => Fft.AddSamples(buf, off, cnt, ch));
        _output = new WaveOutEvent { DesiredLatency = 150 };
        _output.Init(tap);
        _stoppedHandler = (_, _) => PlaybackStopped?.Invoke();
        _output.PlaybackStopped += _stoppedHandler;
    }

    public void Play() => _output?.Play();
    public void Pause() => _output?.Pause();

    public void Seek(double seconds)
    {
        if (_reader is null) return;
        seconds = Math.Clamp(seconds, 0, TotalTimeSeconds);
        _reader.CurrentTime = TimeSpan.FromSeconds(seconds);
    }

    private void Unload()
    {
        // Unsubscribe before disposing: WaveOutEvent.Dispose() calls Stop(), which
        // would otherwise raise a spurious PlaybackStopped when switching tracks.
        if (_output is not null && _stoppedHandler is not null)
            _output.PlaybackStopped -= _stoppedHandler;
        _stoppedHandler = null;
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
    }

    public void Dispose() => Unload();
}
