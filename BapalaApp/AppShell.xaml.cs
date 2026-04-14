using BapalaApp.Views;

namespace BapalaApp;

public partial class AppShell : Shell
{
    public AppShell(LibraryPage libraryPage)
    {
        InitializeComponent();

        // Assign the DI-resolved page directly so MAUI never calls
        // Activator.CreateInstance(typeof(LibraryPage)), which would fail because
        // LibraryPage has no parameterless constructor.
        LibraryContent.Content = libraryPage;

        // Routes for imperative navigation (GoToAsync) — these ARE DI-aware
        // because MAUI's routing engine uses the service provider to resolve pages.
        Routing.RegisterRoute("player", typeof(PlayerPage));
        Routing.RegisterRoute("login",  typeof(LoginPage));
    }
}
