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
    private readonly object _lock = new();

    public IVisualizer Visualizer { get; set; } = new SpectrumBarsVisualizer();

    public VisualizerHost()
    {
        if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            CompositionTarget.Rendering += (_, _) => InvalidateVisual();
    }

    public void Attach(PlaybackEngine engine)
    {
        _engine = engine;
        engine.Fft.FftCalculated += mags =>
        {
            lock (_lock) _latestMagnitudes = mags; // audio thread; swap reference only
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Background so the strip is visible even when silent.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        float[]? mags;
        lock (_lock) mags = _latestMagnitudes;
        if (mags is null || _engine is null) return;

        Visualizer.Render(new VisualizerFrame(mags, _engine.SampleRate, mags.Length * 2), dc, RenderSize);
    }
}
