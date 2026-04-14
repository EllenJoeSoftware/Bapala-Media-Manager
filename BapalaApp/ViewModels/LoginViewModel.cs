using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BapalaApp.Services;

namespace BapalaApp.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly BapalaApiService _api;
    private readonly IServiceProvider _services;

    [ObservableProperty] private string _serverUrl = "http://";
    [ObservableProperty] private string _username  = "admin";
    [ObservableProperty] private string _password  = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;

    public LoginViewModel(BapalaApiService api, IServiceProvider services)
    {
        _api      = api;
        _services = services;
        Title     = "Sign In";

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
            {
                ErrorText = error ?? "Login failed.";
                return;
            }

            // Login succeeded — swap the root page to AppShell.
            // Wrapped separately because a navigation failure is a different
            // problem from a network/auth failure.
            try
            {
                if (Application.Current?.Windows.Count > 0)
                    Application.Current.Windows[0].Page =
                        _services.GetRequiredService<AppShell>();
            }
            catch (Exception navEx)
            {
                ErrorText = $"Navigation failed: {navEx.GetType().Name}: {navEx.Message}";
            }
        }
        catch (Exception ex)
        {
            // CommunityToolkit AsyncRelayCommand swallows unhandled exceptions by
            // default — they disappear silently. Catch everything here so the user
            // always sees what went wrong instead of a silent no-op.
            ErrorText = $"Sign-in error ({ex.GetType().Name}): {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
