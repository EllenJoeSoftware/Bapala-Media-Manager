using BapalaApp.Services;

namespace BapalaApp;

public partial class App : Application
{
    public App(BapalaApiService api)
    {
        InitializeComponent();

        // Choose start page based on whether we have a saved token
        MainPage = api.IsAuthenticated
            ? new AppShell()
            : new NavigationPage(new Views.LoginPage(new ViewModels.LoginViewModel(api)));
    }
}
