using System.Windows;
using System.Windows.Media;
using WeaveSyncLens.Core.Audio;

namespace WeaveSyncLens.App.Visualizers;

public class SpectrumBarsVisualizer : IVisualizer
{
    private const int BarCount = 48;
    private const float Attack = 0.6f;  // rise speed (0..1 per frame)
    private const float Decay = 0.08f;  // fall speed

    private readonly float[] _levels = new float[BarCount];
    private static readonly Brush BarBrush = CreateBrush();

    public string Name => "Spectrum Bars";

    private static Brush CreateBrush()
    {
        var brush = new LinearGradientBrush(
            Color.FromRgb(0xFF, 0xD3, 0x4D), Color.FromRgb(0xFF, 0x6A, 0x3D),
            new Point(0, 1), new Point(0, 0));
        brush.Freeze();
        return brush;
    }

    public void Render(VisualizerFrame frame, DrawingContext dc, Size area)
    {
        var target = SpectrumBinner.BinToBars(frame.Magnitudes, BarCount, frame.SampleRate, frame.FftLength);

        double barWidth = area.Width / BarCount;
        double gap = Math.Max(1, barWidth * 0.15);

        for (int i = 0; i < BarCount; i++)
        {
            float t = target[i];
            _levels[i] += (t - _levels[i]) * (t > _levels[i] ? Attack : Decay);

            double h = _levels[i] * (area.Height - 4);
            if (h < 1) continue;
            dc.DrawRoundedRectangle(BarBrush, null,
                new Rect(i * barWidth + gap / 2, area.Height - h, barWidth - gap, h), 2, 2);
        }
    }
}
