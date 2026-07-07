using System.Windows;
using System.Windows.Media;

namespace WeaveSyncLens.App.Visualizers;

public record VisualizerFrame(float[] Magnitudes, int SampleRate, int FftLength);

/// <summary>A pluggable visualization. Add a class + register it in MainWindow to add a new style.</summary>
public interface IVisualizer
{
    string Name { get; }
    void Render(VisualizerFrame frame, DrawingContext dc, Size area);
}
