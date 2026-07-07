using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WeaveSyncLens.Core;
using WeaveSyncLens.Core.Audio;
using WeaveSyncLens.Core.Import;
using WeaveSyncLens.Core.Models;
using WeaveSyncLens.Core.Transcription;

namespace WeaveSyncLens.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly MediaSessionLoader _loader;
    private readonly DispatcherTimer _syncTimer;
    private Transcript? _transcript;

    public PlaybackEngine Playback { get; } = new();
    public ObservableCollection<WordViewModel> Words { get; } = new();

    [ObservableProperty] private int _activeWordIndex = -1;
    [ObservableProperty] private string _statusText = "Drop an MP3 or MP4 file here";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _busyFraction;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _positionSeconds;
    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private bool _isMediaLoaded;

    public MainViewModel()
    {
        FfmpegLocator.Configure(AppContext.BaseDirectory);
        _loader = new MediaSessionLoader(
            new FfmpegMediaImporter(),
            new WhisperLocalTranscriber(WhisperModelSize.Base));

        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _syncTimer.Tick += (_, _) => SyncTick();
        _syncTimer.Start();
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var dlg = new OpenFileDialog { Filter = "Media files|*.mp3;*.mp4;*.wav;*.m4a" };
        if (dlg.ShowDialog() == true)
            await LoadFileAsync(dlg.FileName);
    }

    public async Task LoadFileAsync(string path)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            StatusText = $"Preparing {Path.GetFileName(path)}…";
            var progress = new Progress<TranscriptionProgress>(p =>
            {
                BusyFraction = p.Fraction;
                StatusText = p.Stage == "DownloadingModel"
                    ? $"Downloading Whisper model… {p.Fraction:P0}"
                    : $"Transcribing… {p.Fraction:P0}";
            });

            _transcript = await Task.Run(() => _loader.LoadAsync(path, progress, CancellationToken.None));

            Words.Clear();
            foreach (var w in _transcript.Words)
                Words.Add(new WordViewModel(w.Text, w.Start));
            ActiveWordIndex = -1;

            Playback.Load(path);
            DurationSeconds = Playback.TotalTimeSeconds;
            IsMediaLoaded = true;
            StatusText = Path.GetFileName(path);
            Playback.Play();
        }
        catch (Exception ex)
        {
            StatusText = "Failed to load file";
            MessageBox.Show(ex.Message, "WeaveSyncLens", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            BusyFraction = 0;
        }
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (!IsMediaLoaded) return;
        if (Playback.IsPlaying) Playback.Pause();
        else Playback.Play();
    }

    [RelayCommand]
    private void Seek(double seconds) => Playback.Seek(seconds);

    [RelayCommand]
    private void SeekToWord(WordViewModel word) => Playback.Seek(word.Start);

    private void SyncTick()
    {
        IsPlaying = Playback.IsPlaying;
        PositionSeconds = Playback.CurrentTimeSeconds;

        if (_transcript is null || Words.Count == 0) return;
        int index = _transcript.FindActiveWordIndex(PositionSeconds, ActiveWordIndex);
        if (index == ActiveWordIndex) return;

        if (ActiveWordIndex >= 0 && ActiveWordIndex < Words.Count)
            Words[ActiveWordIndex].IsActive = false;
        if (index >= 0 && index < Words.Count)
            Words[index].IsActive = true;
        ActiveWordIndex = index;
    }
}
