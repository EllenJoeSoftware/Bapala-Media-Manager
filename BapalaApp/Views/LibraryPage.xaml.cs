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
        // Reload on every appearance so changes made in the Player
        // (e.g. watch-progress updates) are reflected immediately.
        _vm.LoadCommand.Execute(null);
    }
}
