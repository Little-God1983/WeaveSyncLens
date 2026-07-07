using System.Windows;
using System.Windows.Media;
using WeaveSyncLens.App.Visualizers;
using WeaveSyncLens.Core.Audio;

namespace WeaveSyncLens.App.Controls;

/// <summary>Renders the active IVisualizer every frame with the latest FFT data.</summary>
public class VisualizerHost : FrameworkElement
{
    private float[]? _latestMagnitudes;
    private PlaybackEngine? _engine;
    private Action<float[]>? _fftHandler;
    private EventHandler? _renderingHandler;
    private readonly object _lock = new();

    public IVisualizer Visualizer { get; set; } = new SpectrumBarsVisualizer();

    public VisualizerHost()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
        {
            _renderingHandler = (_, _) => InvalidateVisual();
            CompositionTarget.Rendering += _renderingHandler;
        }

        if (_fftHandler is null && _engine is not null)
            SubscribeFft();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_renderingHandler is not null)
        {
            CompositionTarget.Rendering -= _renderingHandler;
            _renderingHandler = null;
        }

        if (_fftHandler is not null && _engine is not null)
        {
            _engine.Fft.FftCalculated -= _fftHandler;
            _fftHandler = null;
        }
    }

    public void Attach(PlaybackEngine engine)
    {
        if (_fftHandler is not null && _engine is not null)
            _engine.Fft.FftCalculated -= _fftHandler;

        _engine = engine;
        SubscribeFft();
    }

    private void SubscribeFft()
    {
        if (_engine is null)
            return;

        _fftHandler = mags =>
        {
            lock (_lock) _latestMagnitudes = mags; // audio thread; swap reference only
        };
        _engine.Fft.FftCalculated += _fftHandler;
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Establish render bounds and enable hit-testing.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        float[]? mags;
        lock (_lock) mags = _latestMagnitudes;
        if (mags is null || _engine is null) return;

        Visualizer.Render(new VisualizerFrame(mags, _engine.SampleRate, mags.Length * 2), dc, RenderSize);
    }
}
