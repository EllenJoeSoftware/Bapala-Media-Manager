using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BapalaApp.Services;

namespace BapalaApp.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly BapalaApiService _api;

    [ObservableProperty] private string _serverUrl  = "http://";
    [ObservableProperty] private string _username   = "admin";
    [ObservableProperty] private string _password   = string.Empty;
    [ObservableProperty] private string _errorText  = string.Empty;

    public LoginViewModel(BapalaApiService api)
    {
        _api = api;
        Title = "Sign In";

        // Pre-fill server URL if we've connected before
        if (!string.IsNullOrEmpty(Preferences.Get("bapala_server_url", null)))
            ServerUrl = Preferences.Get("bapala_server_url", "http://");
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
            var (success, error, serverName) = await _api.LoginAsync(ServerUrl, Username, Password);

            if (!success)
            { ErrorText = error ?? "Login failed."; return; }

            // Replace the navigation root so Back can't return to Login
            Application.Current!.MainPage = new AppShell();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
