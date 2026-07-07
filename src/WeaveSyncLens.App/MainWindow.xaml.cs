using System.Windows;
using System.Windows.Input;
using WeaveSyncLens.App.ViewModels;

namespace WeaveSyncLens.App;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow() => InitializeComponent();

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

    private void SeekSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        => Vm.SeekCommand.Execute(SeekSlider.Value);

    private void SeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
        => Vm.SeekCommand.Execute(SeekSlider.Value);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            Vm.TogglePlayPauseCommand.Execute(null);
            e.Handled = true;
        }
    }
}
