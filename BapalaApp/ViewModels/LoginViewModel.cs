using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BapalaApp.Models;
using BapalaApp.Services;

namespace BapalaApp.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly BapalaApiService _api;
    private readonly IServiceProvider _services;
    private readonly IServerDiscoveryService? _discovery;

    // ── Observable state ─────────────────────────────────────────────────────

    [ObservableProperty] private string _serverUrl  = "http://";
    [ObservableProperty] private string _username   = "admin";
    [ObservableProperty] private string _password   = string.Empty;
    [ObservableProperty] private string _errorText  = string.Empty;
    [ObservableProperty] private bool   _isScanning = false;
    [ObservableProperty] private string _scanStatus = "Scanning for servers on this network…";

    /// <summary>Servers discovered on the LAN, bound to the CollectionView.</summary>
    public ObservableCollection<DiscoveredServer> DiscoveredServers { get; } = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    public LoginViewModel(
        BapalaApiService api,
        IServiceProvider services,
        IServerDiscoveryService? discovery = null)
    {
        _api       = api;
        _services  = services;
        _discovery = discovery;
        Title      = "Sign In";

        // Pre-fill server URL from previous session
        var saved = Preferences.Get("bapala_server_url", string.Empty);
        if (!string.IsNullOrEmpty(saved))
            ServerUrl = saved;

        // Start discovering as soon as the VM is created (non-blocking)
        if (_discovery != null)
        {
            _discovery.ServerFound += OnServerFound;
            _discovery.ServerLost  += OnServerLost;
            _ = StartDiscoveryAsync();
        }
    }

    // ── Discovery ─────────────────────────────────────────────────────────────

    private async Task StartDiscoveryAsync()
    {
        if (_discovery == null) return;

        IsScanning = true;
        ScanStatus = "Scanning for servers on this network…";
        DiscoveredServers.Clear();

        await _discovery.StartAsync();

        // After 8 seconds, stop scanning and update the status label
        await Task.Delay(15_000);
        _discovery.Stop();
        IsScanning = false;

        if (DiscoveredServers.Count == 0)
            ScanStatus = "No servers found. Enter the address manually below.";
        else
            ScanStatus = $"{DiscoveredServers.Count} server{(DiscoveredServers.Count == 1 ? "" : "s")} found:";
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        _discovery?.Stop();
        DiscoveredServers.Clear();
        await StartDiscoveryAsync();
    }

    /// <summary>Called when the user taps a discovered server card.</summary>
    [RelayCommand]
    private void SelectServer(DiscoveredServer server)
    {
        ServerUrl = server.BaseUrl;
        // Optionally stop scanning once a server is selected
        _discovery?.Stop();
        IsScanning = false;
        ScanStatus = $"Selected: {server.Name}";
    }

    private void OnServerFound(object? sender, DiscoveredServer server)
    {
        if (!DiscoveredServers.Contains(server))
        {
            DiscoveredServers.Add(server);
            ScanStatus = $"{DiscoveredServers.Count} server{(DiscoveredServers.Count == 1 ? "" : "s")} found:";
        }
    }

    private void OnServerLost(object? sender, DiscoveredServer server)
    {
        var existing = DiscoveredServers.FirstOrDefault(s => s.Name == server.Name);
        if (existing != null)
            DiscoveredServers.Remove(existing);
    }

    // ── Login ─────────────────────────────────────────────────────────────────

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

        // Stop discovery while logging in — saves resources
        _discovery?.Stop();

        try
        {
            var (success, error, _) = await _api.LoginAsync(ServerUrl, Username, Password);

            if (!success)
            {
                ErrorText = error ?? "Login failed.";
                return;
            }

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
            ErrorText = $"Sign-in error ({ex.GetType().Name}): {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
