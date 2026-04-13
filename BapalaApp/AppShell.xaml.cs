using BapalaApp.Views;

namespace BapalaApp;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register routes that are navigated to imperatively (not in tabs)
        Routing.RegisterRoute("player", typeof(PlayerPage));
        Routing.RegisterRoute("login",  typeof(LoginPage));
    }
}
