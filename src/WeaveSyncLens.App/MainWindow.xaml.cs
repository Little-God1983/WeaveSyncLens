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

    public MainWindow()
    {
        InitializeComponent();
        Vm.PropertyChanged += Vm_PropertyChanged;
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
        if (e.Key == Key.Space)
        {
            Vm.TogglePlayPauseCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => Vm.Shutdown();
}
