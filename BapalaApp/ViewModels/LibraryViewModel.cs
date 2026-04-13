using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BapalaApp.Models;
using BapalaApp.Services;

namespace BapalaApp.ViewModels;

public partial class LibraryViewModel : BaseViewModel
{
    private readonly BapalaApiService _api;

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<MediaItem> _items = [];
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string? _selectedType;        // null = "All"
    [ObservableProperty] private bool   _showFavorites;
    [ObservableProperty] private int    _currentPage   = 1;
    [ObservableProperty] private int    _totalItems;
    [ObservableProperty] private bool   _isRefreshing;
    [ObservableProperty] private bool   _hasPreviousPage;
    [ObservableProperty] private bool   _hasNextPage;
    [ObservableProperty] private string _sortBy   = "dateAdded";
    [ObservableProperty] private bool   _sortDesc = true;

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
            var result = await _api.GetMediaAsync(
                page:      CurrentPage,
                limit:     PageSize,
                type:      SelectedType,
                search:    SearchText,
                favorites: ShowFavorites,
                sortBy:    SortBy,
                sortDesc:  SortDesc);

            Items.Clear();
            foreach (var item in result.Items) Items.Add(item);

            TotalItems     = result.Total;
            HasPreviousPage = CurrentPage > 1;
            HasNextPage    = CurrentPage * PageSize < TotalItems;
            OnPropertyChanged(nameof(PageLabel));
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
