using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BapalaApp.Models;
using BapalaApp.Services;
using Microsoft.AspNetCore.SignalR.Client;

namespace BapalaApp.ViewModels;

public partial class LibraryViewModel : BaseViewModel
{
    private readonly BapalaApiService _api;

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<MediaItem>             _items             = [];
    [ObservableProperty] private ObservableCollection<ContinueWatchingItem> _continueWatching  = [];
    [ObservableProperty] private string   _searchText    = string.Empty;
    [ObservableProperty] private string?  _selectedType;        // null = "All"
    [ObservableProperty] private bool     _showFavorites;
    [ObservableProperty] private int      _currentPage   = 1;
    [ObservableProperty] private int      _totalItems;
    [ObservableProperty] private bool     _isRefreshing;
    [ObservableProperty] private bool     _hasPreviousPage;
    [ObservableProperty] private bool     _hasNextPage;
    [ObservableProperty] private string   _sortBy        = "dateAdded";
    [ObservableProperty] private bool     _sortDesc      = true;

    // ── Stats ─────────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStats))]
    private LibraryStats? _stats;

    public bool HasStats             => _stats != null;
    public bool HasContinueWatching  => ContinueWatching.Count > 0;

    // ── TMDB bulk refresh progress ────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TmdbProgressLabel))]
    private int _tmdbProcessed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TmdbProgressLabel))]
    [NotifyPropertyChangedFor(nameof(TmdbProgressFraction))]
    private int _tmdbTotal;

    [ObservableProperty] private bool   _isTmdbRefreshing;
    [ObservableProperty] private string _tmdbStatusText = string.Empty;

    public string TmdbProgressLabel  => TmdbTotal > 0 ? $"{_tmdbProcessed} / {TmdbTotal}" : string.Empty;
    public double TmdbProgressFraction => TmdbTotal > 0 ? Math.Clamp((double)_tmdbProcessed / TmdbTotal, 0, 1) : 0;

    private HubConnection? _hubConnection;

    private const int PageSize = 24;

    // ── Filter chip active states (for XAML button style binding) ────────────

    public bool AllActive        => SelectedType == null && !ShowFavorites;
    public bool MoviesActive     => SelectedType == "Movie";
    public bool SeriesActive     => SelectedType == "Series";
    public bool DocsActive       => SelectedType == "Documentary";
    public bool EducationActive  => SelectedType == "Education";
    public bool MusicActive      => SelectedType == "MusicVideo";
    public bool FavoritesActive  => ShowFavorites;

    public string PageLabel => $"Page {CurrentPage}  •  {TotalItems} items";

    public LibraryViewModel(BapalaApiService api)
    {
        _api = api;
        Title = "Library";
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            // Load main grid + continue-watching + stats concurrently
            var mediaTask  = _api.GetMediaAsync(
                page:      CurrentPage,
                limit:     PageSize,
                type:      SelectedType,
                search:    SearchText,
                favorites: ShowFavorites,
                sortBy:    SortBy,
                sortDesc:  SortDesc);

            // Only fetch continue-watching and stats on the first page / no active filters
            bool isDefaultView = CurrentPage == 1
                && SelectedType == null
                && !ShowFavorites
                && string.IsNullOrWhiteSpace(SearchText);

            var continueTask = isDefaultView
                ? _api.GetContinueWatchingAsync(10)
                : Task.FromResult(new List<ContinueWatchingItem>());

            var statsTask = isDefaultView
                ? _api.GetStatsAsync()
                : Task.FromResult<LibraryStats?>(null);

            await Task.WhenAll(mediaTask, continueTask, statsTask);

            var result = await mediaTask;
            Items.Clear();
            foreach (var item in result.Items) Items.Add(item);

            TotalItems      = result.Total;
            HasPreviousPage = CurrentPage > 1;
            HasNextPage     = CurrentPage * PageSize < TotalItems;
            OnPropertyChanged(nameof(PageLabel));

            if (isDefaultView)
            {
                var cw = await continueTask;
                ContinueWatching.Clear();
                foreach (var cw_item in cw) ContinueWatching.Add(cw_item);
                OnPropertyChanged(nameof(HasContinueWatching));

                Stats = await statsTask;
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Failed to load media: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy       = false;
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        CurrentPage  = 1;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!HasNextPage) return;
        CurrentPage++;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (!HasPreviousPage) return;
        CurrentPage--;
        await LoadAsync();
    }

    // ── Filter chips ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SetFilterAsync(string? type)
    {
        SelectedType   = SelectedType == type ? null : type;
        ShowFavorites  = false;
        CurrentPage    = 1;
        NotifyFilterProperties();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleFavoritesAsync()
    {
        ShowFavorites  = !ShowFavorites;
        SelectedType   = null;
        CurrentPage    = 1;
        NotifyFilterProperties();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        SelectedType   = null;
        ShowFavorites  = false;
        SearchText     = string.Empty;
        CurrentPage    = 1;
        NotifyFilterProperties();
        await LoadAsync();
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SearchAsync()
    {
        CurrentPage = 1;
        await LoadAsync();
    }

    // ── Sort ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SetSortAsync(string field)
    {
        if (SortBy == field) SortDesc = !SortDesc;
        else { SortBy = field; SortDesc = field == "dateAdded"; }
        CurrentPage = 1;
        await LoadAsync();
    }

    // ── Navigate to player ────────────────────────────────────────────────────

    [RelayCommand]
    private async Task PlayAsync(MediaItem item)
    {
        await Shell.Current.GoToAsync("player", new Dictionary<string, object>
        {
            ["MediaId"] = item.Id
        });
    }

    [RelayCommand]
    private async Task ResumeContinueWatchingAsync(ContinueWatchingItem item)
    {
        await Shell.Current.GoToAsync("player", new Dictionary<string, object>
        {
            ["MediaId"] = item.Id
        });
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleItemFavoriteAsync(MediaItem item)
    {
        try
        {
            var newState = await _api.ToggleFavoriteAsync(item.Id);
            item.IsFavorite = newState;
            // Refresh the item in the collection so the icon updates
            var idx = Items.IndexOf(item);
            if (idx >= 0) { Items.RemoveAt(idx); Items.Insert(idx, item); }
        }
        catch { /* ignore */ }
    }

    [RelayCommand]
    private async Task DeleteItemAsync(MediaItem item)
    {
        bool confirmed = await Shell.Current.DisplayAlert(
            "Remove from Library",
            $"Remove \"{item.Title}\"?\n\nThe file on disk will NOT be deleted.",
            "Remove", "Cancel");

        if (!confirmed) return;

        try
        {
            await _api.DeleteMediaAsync(item.Id);
            Items.Remove(item);
            TotalItems--;
            OnPropertyChanged(nameof(PageLabel));
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Delete failed: {ex.Message}", "OK");
        }
    }

    // ── Bulk TMDB refresh ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshAllTmdbAsync(bool force = false)
    {
        if (IsTmdbRefreshing) return;

        bool confirmed = await Shell.Current.DisplayAlert(
            "Refresh TMDB Metadata",
            force
                ? "Re-fetch metadata for ALL items, including those already enriched?\nThis may take several minutes."
                : "Fetch missing posters, descriptions and ratings from TMDB?\nItems that already have full metadata will be skipped.",
            "Continue", "Cancel");

        if (!confirmed) return;

        // Connect to the SignalR hub so we receive live progress
        await ConnectHubAsync();

        IsTmdbRefreshing = true;
        TmdbStatusText   = "Starting…";
        TmdbProcessed    = 0;
        TmdbTotal        = 0;

        var started = await _api.RefreshTmdbAllAsync(force);
        if (!started)
        {
            IsTmdbRefreshing = false;
            TmdbStatusText   = string.Empty;
            await Shell.Current.DisplayAlert("Error", "Could not start TMDB refresh. Check server connectivity.", "OK");
        }
        // Completion is handled by TmdbRefreshCompleted SignalR event
    }

    private async Task ConnectHubAsync()
    {
        if (_hubConnection?.State == HubConnectionState.Connected) return;

        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{_api.ServerUrl}/hubs/scan", opts =>
            {
                opts.AccessTokenProvider = () => Task.FromResult(_api.CachedToken);
            })
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<object>("TmdbRefreshStarted", data =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TmdbStatusText = "Fetching metadata…";
            });
        });

        _hubConnection.On("TmdbRefreshProgress", (int processed, int total, string title, bool success) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TmdbProcessed  = processed;
                TmdbTotal      = total;
                TmdbStatusText = success ? $"✓ {title}" : $"— {title}";
                OnPropertyChanged(nameof(TmdbProgressLabel));
                OnPropertyChanged(nameof(TmdbProgressFraction));
            });
        });

        _hubConnection.On("TmdbRefreshCompleted", (int updated, int skipped, int failed, int total) =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                IsTmdbRefreshing = false;
                TmdbStatusText   = $"Done — {updated} updated, {skipped} skipped, {failed} failed";
                await LoadAsync();    // refresh the grid with new posters
            });
        });

        _hubConnection.On("TmdbRefreshError", (string error) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsTmdbRefreshing = false;
                TmdbStatusText   = $"Error: {error}";
            });
        });

        try { await _hubConnection.StartAsync(); }
        catch { /* hub is best-effort; progress won't show but refresh still runs */ }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void NotifyFilterProperties()
    {
        OnPropertyChanged(nameof(AllActive));
        OnPropertyChanged(nameof(MoviesActive));
        OnPropertyChanged(nameof(SeriesActive));
        OnPropertyChanged(nameof(DocsActive));
        OnPropertyChanged(nameof(EducationActive));
        OnPropertyChanged(nameof(MusicActive));
        OnPropertyChanged(nameof(FavoritesActive));
    }
}
