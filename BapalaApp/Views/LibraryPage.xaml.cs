using BapalaApp.Models;
using BapalaApp.ViewModels;

namespace BapalaApp.Views;

public partial class LibraryPage : ContentPage
{
    private readonly LibraryViewModel _vm;

    public LibraryPage(LibraryViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Reload on every appearance so changes from the Player are reflected immediately.
        _vm.LoadCommand.Execute(null);
    }

    // ── Card tap → navigate to player ────────────────────────────────────────
    // Wired here because RelativeSource AncestorType inside a DataTemplate is
    // fragile in MAUI — the visual tree may not be fully built when a recycled
    // cell is first bound, causing a NullReferenceException.

    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
        // The BindingContext of the card Grid is the MediaItem for that cell.
        if (sender is View v && v.BindingContext is MediaItem item)
            _vm.PlayCommand.Execute(item);
    }

    // ── Favorite button ───────────────────────────────────────────────────────

    private void OnFavoriteClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.BindingContext is MediaItem item)
            _vm.ToggleItemFavoriteCommand.Execute(item);
    }

    // ── Continue Watching tap ─────────────────────────────────────────────────

    private void OnContinueWatchingTapped(object? sender, TappedEventArgs e)
    {
        if (sender is View v && v.BindingContext is ContinueWatchingItem item)
            _vm.ResumeContinueWatchingCommand.Execute(item);
    }
}
