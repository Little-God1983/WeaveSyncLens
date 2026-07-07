using CommunityToolkit.Mvvm.ComponentModel;

namespace WeaveSyncLens.App.ViewModels;

public partial class WordViewModel : ObservableObject
{
    public string Text { get; }
    public double Start { get; }

    [ObservableProperty]
    private bool _isActive;

    public WordViewModel(string text, double start)
    {
        Text = text;
        Start = start;
    }
}
