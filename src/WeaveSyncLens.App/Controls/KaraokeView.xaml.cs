using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WeaveSyncLens.App.ViewModels;

namespace WeaveSyncLens.App.Controls;

public partial class KaraokeView : UserControl
{
    public static readonly DependencyProperty WordsSourceProperty =
        DependencyProperty.Register(nameof(WordsSource), typeof(IEnumerable), typeof(KaraokeView));

    public static readonly DependencyProperty ActiveIndexProperty =
        DependencyProperty.Register(nameof(ActiveIndex), typeof(int), typeof(KaraokeView),
            new PropertyMetadata(-1, (d, _) => ((KaraokeView)d).ScrollToActive()));

    public static readonly DependencyProperty SeekCommandProperty =
        DependencyProperty.Register(nameof(SeekCommand), typeof(ICommand), typeof(KaraokeView));

    public static readonly DependencyProperty WordFontSizeProperty =
        DependencyProperty.Register(nameof(WordFontSize), typeof(double), typeof(KaraokeView),
            new PropertyMetadata(32.0));

    public IEnumerable WordsSource
    {
        get => (IEnumerable)GetValue(WordsSourceProperty);
        set => SetValue(WordsSourceProperty, value);
    }

    public int ActiveIndex
    {
        get => (int)GetValue(ActiveIndexProperty);
        set => SetValue(ActiveIndexProperty, value);
    }

    public ICommand? SeekCommand
    {
        get => (ICommand?)GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    public double WordFontSize
    {
        get => (double)GetValue(WordFontSizeProperty);
        set => SetValue(WordFontSizeProperty, value);
    }

    public KaraokeView() => InitializeComponent();

    private void Word_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WordViewModel word })
            SeekCommand?.Execute(word);
    }

    private void ScrollToActive()
    {
        int index = ActiveIndex;
        if (index < 0) return;
        var container = WordsHost.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
        if (container is null) return;

        // Word's vertical center relative to scrolled content → keep it mid-viewport.
        var transform = container.TransformToAncestor(Scroller);
        double yInViewport = transform.Transform(new Point(0, 0)).Y + container.ActualHeight / 2;
        double target = Scroller.VerticalOffset + yInViewport - Scroller.ViewportHeight / 2;
        target = Math.Clamp(target, 0, Scroller.ScrollableHeight);

        var anim = new DoubleAnimation(Scroller.VerticalOffset, target,
            new Duration(TimeSpan.FromMilliseconds(350)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        Scroller.BeginAnimation(ScrollViewerBehavior.VerticalOffsetProperty, anim);
    }
}
