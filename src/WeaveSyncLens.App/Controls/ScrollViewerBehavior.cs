using System.Windows;
using System.Windows.Controls;

namespace WeaveSyncLens.App.Controls;

/// <summary>Attached property so ScrollViewer.VerticalOffset can be animated.</summary>
public static class ScrollViewerBehavior
{
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.RegisterAttached("VerticalOffset", typeof(double),
            typeof(ScrollViewerBehavior),
            new PropertyMetadata(0.0, (d, e) =>
            {
                if (d is ScrollViewer sv) sv.ScrollToVerticalOffset((double)e.NewValue);
            }));

    public static void SetVerticalOffset(DependencyObject d, double value) =>
        d.SetValue(VerticalOffsetProperty, value);

    public static double GetVerticalOffset(DependencyObject d) =>
        (double)d.GetValue(VerticalOffsetProperty);
}
