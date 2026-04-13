using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BapalaApp.Models;
using BapalaApp.Services;

namespace BapalaApp.ViewModels;

/// <summary>
/// Handles media metadata loading, stream URL construction, and watch-progress
/// persistence for the player page.
/// </summary>
[QueryProperty(nameof(MediaId), "MediaId")]
public partial class PlayerViewModel : BaseViewModel
{
    private readonly BapalaApiService _api;

    [ObservableProperty] private MediaItem? _media;
    [ObservableProperty] private string?    _streamUrl;
    [ObservableProperty] private double     _resumePosition;   // seconds to seek to on load
    [ObservableProperty] private bool       _hasDescription;

    private IDispatcherTimer? _saveTimer;
    private double _currentPositionSeconds;
    private double _durationSeconds;

    // ── QueryProperty setter — called by Shell navigation ────────────────────
    public int MediaId
    {
        set
        {
            // LoadMedia is async; fire-and-forget is intentional here because
            // QueryProperty setters cannot be async.
            _ = LoadMediaAsync(value);
        }
    }

    public PlayerViewModel(BapalaApiService api)
    {
        _api = api;
        Title = "Playing";
    }

    // ── Initialisation ────────────────────────────────────────────────────────

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
        finally
        {
            IsBusy = false;
        }
    }

    // ── Position tracking (called from the View's MediaElement events) ────────

    /// <summary>
    /// Called periodically by the view as the video plays.
    /// Starts a debounce timer so we don't hammer the server on every tick.
    /// </summary>
    public void OnPositionChanged(double positionSeconds, double totalSeconds)
    {
        _currentPositionSeconds = positionSeconds;
        _durationSeconds        = totalSeconds;

        // Reset the debounce timer on every position change
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    /// <summary>Called when the user pauses or stops the video.</summary>
    public void OnPlaybackStopped() => _ = SaveProgressNowAsync();

    // ── Progress-saving strategy — YOUR TURN ─────────────────────────────────
    //
    // TODO: Implement ShouldSaveProgress().
    //
    // This function decides whether the current position is worth persisting.
    // It is called just before writing to the server, so it avoids unnecessary
    // network traffic.
    //
    // Constraints / things to consider:
    //   - _currentPositionSeconds: where the player is right now (seconds)
    //   - _durationSeconds: total length of the video (seconds, 0 if unknown)
    //   - Return false if the video just started (e.g. < 30 seconds in) so a
    //     misclick doesn't overwrite a real saved position.
    //   - Return false if we're near the end (e.g. > 95 % complete) — the user
    //     has finished watching; next time they should start from the beginning.
    //   - Return true in all other cases.
    //
    // Example skeleton (5-10 lines):
    //
    //   private bool ShouldSaveProgress()
    //   {
    //       if (_currentPositionSeconds < 30) return false;
    //       if (_durationSeconds > 0 && _currentPositionSeconds / _durationSeconds > 0.95) return false;
    //       return true;
    //   }
    //
    private bool ShouldSaveProgress()
    {
        // Replace this stub with your implementation ↑
        throw new NotImplementedException("Implement ShouldSaveProgress() in PlayerViewModel.cs");
    }

    // ─────────────────────────────────────────────────────────────────────────

    public void StartSaveTimer()
    {
        // Auto-save every 10 seconds of active playback, matching the web player
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

    // ── Navigation back ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task GoBackAsync()
    {
        await SaveProgressNowAsync();
        await Shell.Current.GoToAsync("..");
    }
}

// ── Helper: await two tasks together without ValueTuple overhead ───────────
file static class TaskExtensions
{
    public static async Task<(T1, T2)> WhenAll<T1, T2>(this (Task<T1> t1, Task<T2> t2) tasks)
    {
        await Task.WhenAll(tasks.t1, tasks.t2);
        return (tasks.t1.Result, tasks.t2.Result);
    }
}
