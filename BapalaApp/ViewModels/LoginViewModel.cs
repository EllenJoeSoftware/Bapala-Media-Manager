using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BapalaApp.Services;

namespace BapalaApp.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly BapalaApiService _api;

    [ObservableProperty] private string _serverUrl = "http://";
    [ObservableProperty] private string _username  = "admin";
    [ObservableProperty] private string _password  = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;

    public LoginViewModel(BapalaApiService api)
    {
        _api = api;
        Title = "Sign In";

        // Pre-fill server URL from previous session
        var saved = Preferences.Get("bapala_server_url", string.Empty);
        if (!string.IsNullOrEmpty(saved))
            ServerUrl = saved;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        ErrorText = string.Empty;

        if (string.IsNullOrWhiteSpace(ServerUrl) || ServerUrl == "http://")
        { ErrorText = "Enter the server address (e.g. http://192.168.1.5:8484)."; return; }

        if (string.IsNullOrWhiteSpace(Username))
        { ErrorText = "Username is required."; return; }

        if (IsBusy) return;
        IsBusy = true;

        try
        {
            var (success, error, _) = await _api.LoginAsync(ServerUrl, Username, Password);

            if (!success)
            { ErrorText = error ?? "Login failed."; return; }

            // Replace root page — use Windows[0].Page (MAUI .NET 9 pattern)
            // so Back cannot navigate back to the login screen.
            if (Application.Current?.Windows.Count > 0)
                Application.Current.Windows[0].Page = new AppShell();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
