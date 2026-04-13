using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BapalaApp.Models;
using BapalaApp.Services;

namespace BapalaApp.ViewModels;

[QueryProperty(nameof(MediaId), "MediaId")]
public partial class PlayerViewModel : BaseViewModel
{
    private readonly BapalaApiService _api;

    [ObservableProperty] private MediaItem? _media;
    [ObservableProperty] private string?    _streamUrl;
    [ObservableProperty] private double     _resumePosition;
    [ObservableProperty] private bool       _hasDescription;

    private IDispatcherTimer? _saveTimer;
    private double _currentPositionSeconds;
    private double _durationSeconds;

    public int MediaId { set => _ = LoadMediaAsync(value); }

    public PlayerViewModel(BapalaApiService api)
    {
        _api  = api;
        Title = "Playing";
    }

    private async Task LoadMediaAsync(int id)
    {
        IsBusy = true;
        try
        {
            var (item, progressSeconds) = await (
                _api.GetMediaByIdAsync(id),
                _api.GetProgressAsync(id)
            ).WhenAll();

            if (item == null) return;

            Media          = item;
            StreamUrl      = _api.GetStreamUrl(id);
            HasDescription = !string.IsNullOrWhiteSpace(item.Description);
            Title          = item.Title;
            ResumePosition = progressSeconds;
        }
        finally { IsBusy = false; }
    }

    // ── Position tracking ─────────────────────────────────────────────────────

    public void OnPositionChanged(double positionSeconds, double totalSeconds)
    {
        _currentPositionSeconds = positionSeconds;
        _durationSeconds        = totalSeconds;
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    public void OnPlaybackStopped() => _ = SaveProgressNowAsync();

    // ── Progress-saving strategy ──────────────────────────────────────────────

    private bool ShouldSaveProgress()
    {
        // Too early — don't overwrite a real saved position from a misclick
        if (_currentPositionSeconds < 30) return false;

        // Near the end — treat as finished; next watch should start from beginning
        if (_durationSeconds > 0 && _currentPositionSeconds / _durationSeconds > 0.95)
            return false;

        return true;
    }

    // ── Timer ─────────────────────────────────────────────────────────────────

    public void StartSaveTimer()
    {
        _saveTimer = Application.Current!.Dispatcher.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromSeconds(10);
        _saveTimer.Tick += async (_, _) => await SaveProgressNowAsync();
    }

    public void StopSaveTimer()
    {
        _saveTimer?.Stop();
        _saveTimer = null;
    }

    private async Task SaveProgressNowAsync()
    {
        if (Media == null || !ShouldSaveProgress()) return;
        await _api.SaveProgressAsync(Media.Id, (long)_currentPositionSeconds);
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task GoBackAsync()
    {
        await SaveProgressNowAsync();
        await Shell.Current.GoToAsync("..");
    }
}

// Await two tasks in parallel without allocating a tuple task
file static class TaskExtensions
{
    public static async Task<(T1, T2)> WhenAll<T1, T2>(this (Task<T1> t1, Task<T2> t2) tasks)
    {
        await Task.WhenAll(tasks.t1, tasks.t2);
        return (tasks.t1.Result, tasks.t2.Result);
    }
}
