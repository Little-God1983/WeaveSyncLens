using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WeaveSyncLens.App.ViewModels;

namespace WeaveSyncLens.App;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    /// <summary>True while the user is dragging the seek thumb; suppresses VM position pushes.</summary>
    private bool _isDraggingSeek;

    /// <summary>True while code-behind is pushing the VM position into the slider; suppresses seek-back.</summary>
    private bool _updatingFromVm;

    /// <summary>Tracks fullscreen state.</summary>
    private bool _isFullscreen;

    /// <summary>Saved window state before entering fullscreen.</summary>
    private WindowState _savedState;

    /// <summary>Saved window style before entering fullscreen.</summary>
    private WindowStyle _savedStyle;

    /// <summary>Saved resize mode before entering fullscreen.</summary>
    private ResizeMode _savedResizeMode;

    /// <summary>Timer to auto-hide UI chrome after 3 seconds of inactivity.</summary>
    private readonly System.Windows.Threading.DispatcherTimer _hideUiTimer = new()
    {
        Interval = TimeSpan.FromSeconds(3),
    };

    public MainWindow()
    {
        InitializeComponent();
        Visualizer.Attach(Vm.Playback);
        Vm.PropertyChanged += Vm_PropertyChanged;

        SizeChanged += (_, _) => UpdateTranscriptScaling();
        Loaded += (_, _) => UpdateTranscriptScaling();

        _hideUiTimer.Tick += (_, _) => { _hideUiTimer.Stop(); HideChrome(); };
        MouseMove += (_, _) =>
        {
            if (!_isFullscreen) return;
            ShowChrome();
            _hideUiTimer.Stop();
            _hideUiTimer.Start();
        };
        MouseDoubleClick += (_, e) =>
        {
            // Only toggle fullscreen if the double-click did not originate on the transport bar
            if (e.OriginalSource is not DependencyObject source || !IsDescendantOf(source, TransportBar))
                ToggleFullscreen();
        };
    }

    /// <summary>Scales the karaoke font with window height and caps the transcript width on
    /// ultrawide screens. Invoked on load (so it's correct before the first resize) and on
    /// every SizeChanged.</summary>
    private void UpdateTranscriptScaling()
    {
        Karaoke.WordFontSize = Math.Clamp(ActualHeight * 0.045, 18, 72);
        // Cap transcript width on ultrawide screens; keep it centered.
        TranscriptHost.MaxWidth = Math.Max(800, ActualHeight * 1.8);
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.PositionSeconds) || _isDraggingSeek)
            return;
        _updatingFromVm = true;
        try { SeekSlider.Value = Vm.PositionSeconds; }
        finally { _updatingFromVm = false; }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await Vm.LoadFileAsync(files[0]);
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Covers track clicks / LargeChange nudges. Ignores changes pushed from the
        // VM position and changes during a thumb drag (DragCompleted seeks once instead).
        if (!_updatingFromVm && !_isDraggingSeek)
            Vm.SeekCommand.Execute(e.NewValue);
    }

    private void SeekSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        => _isDraggingSeek = true;

    private void SeekSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        Vm.SeekCommand.Execute(SeekSlider.Value);
        _isDraggingSeek = false;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                Vm.TogglePlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.Escape when _isFullscreen:
                ExitFullscreen();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Toggles fullscreen mode on/off.</summary>
    private void ToggleFullscreen()
    {
        if (_isFullscreen) ExitFullscreen();
        else EnterFullscreen();
    }

    /// <summary>Enters fullscreen mode with borderless window covering the taskbar.
    /// Re-hosts the transport bar as a bottom-aligned overlay spanning all rows so that
    /// hiding/showing it never reflows the transcript and visualizer rows.</summary>
    private void EnterFullscreen()
    {
        _savedState = WindowState;
        _savedStyle = WindowStyle;
        _savedResizeMode = ResizeMode;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Normal;    // toggle forces WPF to re-measure over the taskbar
        WindowState = WindowState.Maximized;

        // Overlay the transport bar over the content instead of occupying its own row.
        System.Windows.Controls.Grid.SetRow(TransportBar, 0);
        System.Windows.Controls.Grid.SetRowSpan(TransportBar, 3);
        TransportBar.VerticalAlignment = VerticalAlignment.Bottom;

        _isFullscreen = true;
        _hideUiTimer.Start();
    }

    /// <summary>Exits fullscreen mode and restores the previous window state, style,
    /// resize mode, and transport bar row placement.</summary>
    private void ExitFullscreen()
    {
        WindowStyle = _savedStyle;
        ResizeMode = _savedResizeMode;
        WindowState = _savedState;

        // Restore the transport bar to its own layout row.
        System.Windows.Controls.Grid.SetRow(TransportBar, 2);
        System.Windows.Controls.Grid.SetRowSpan(TransportBar, 1);
        TransportBar.VerticalAlignment = VerticalAlignment.Stretch;

        _isFullscreen = false;
        _hideUiTimer.Stop();
        ShowChrome();
    }

    /// <summary>Shows the transport bar and cursor.</summary>
    private void ShowChrome()
    {
        TransportBar.Visibility = Visibility.Visible;
        Cursor = System.Windows.Input.Cursors.Arrow;
    }

    /// <summary>Hides the transport bar and cursor, only when in fullscreen.</summary>
    private void HideChrome()
    {
        if (!_isFullscreen) return;
        TransportBar.Visibility = Visibility.Collapsed;
        Cursor = System.Windows.Input.Cursors.None;
    }

    /// <summary>Helper to check if a DependencyObject is a descendant of a parent element.
    /// Handles content elements (e.g. a Run inside a TextBlock), which are not Visuals and
    /// would make VisualTreeHelper.GetParent throw ArgumentException.</summary>
    private static bool IsDescendantOf(DependencyObject child, DependencyObject parent)
    {
        var current = child;
        while (current != null)
        {
            if (current == parent) return true;
            if (current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            else if (current is FrameworkContentElement fce)
                current = fce.Parent;
            else
                break;
        }
        return false;
    }

    /// <summary>Handles fullscreen button click.</summary>
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void Window_Closing(object? sender, CancelEventArgs e) => Vm.Shutdown();
}
