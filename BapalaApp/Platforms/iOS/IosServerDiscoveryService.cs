using Foundation;
using BapalaApp.Models;
using BapalaApp.Services;

namespace BapalaApp.Platforms.iOS;

/// <summary>
/// iOS / macOS Catalyst implementation of server discovery using
/// <see cref="NSNetServiceBrowser"/> (Bonjour / mDNS).
///
/// The server broadcasts <c>_bapala._tcp.</c>; this class browses for that
/// service type, calls Resolve on each found service to obtain the IP + port,
/// then raises <see cref="IServerDiscoveryService.ServerFound"/>.
///
/// NSNetServiceBrowser must run on the main run loop — all calls to it are
/// marshalled onto the main thread via <c>MainThread.BeginInvokeOnMainThread</c>.
/// </summary>
internal sealed class IosServerDiscoveryService : IServerDiscoveryService
{
    private const string ServiceType   = "_bapala._tcp.";
    private const string ServiceDomain = "local.";

    private NSNetServiceBrowser? _browser;
    private readonly Dictionary<string, NSNetService> _resolving  = new();
    private readonly Dictionary<string, DiscoveredServer> _found   = new();

    public event EventHandler<DiscoveredServer>? ServerFound;
    public event EventHandler<DiscoveredServer>? ServerLost;
    public bool IsScanning { get; private set; }

    // ── IServerDiscoveryService ───────────────────────────────────────────────

    public Task StartAsync()
    {
        if (IsScanning) return Task.CompletedTask;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _browser = new NSNetServiceBrowser();
            _browser.FoundService   += OnFoundService;
            _browser.RemovedService += OnRemovedService;
            _browser.SearchStarted  += (_, _) => IsScanning = true;
            _browser.SearchStopped  += (_, _) => IsScanning = false;
            _browser.NotSearched    += (_, _) => IsScanning = false;

            _browser.SearchForServices(ServiceType, ServiceDomain);
        });

        IsScanning = true;
        return Task.CompletedTask;
    }

    public void Stop()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _browser?.Stop();
            _browser?.Dispose();
            _browser = null;

            foreach (var svc in _resolving.Values)
            {
                svc.Stop();
                svc.Dispose();
            }
            _resolving.Clear();
        });

        IsScanning = false;
    }

    // ── Browser callbacks ─────────────────────────────────────────────────────

    private void OnFoundService(object? sender, NSNetServiceEventArgs e)
    {
        var svc = e.Service;
        svc.AddressResolved += OnAddressResolved;
        svc.ResolveFailure  += OnResolveFailed;
        svc.Resolve(5.0); // 5-second timeout

        lock (_resolving) { _resolving[svc.Name] = svc; }
    }

    private void OnRemovedService(object? sender, NSNetServiceEventArgs e)
    {
        var name = e.Service.Name;
        lock (_resolving) { _resolving.Remove(name); }

        DiscoveredServer? server;
        lock (_found)
        {
            if (!_found.TryGetValue(name, out server)) return;
            _found.Remove(name);
        }

        ServerLost?.Invoke(this, server);
    }

    // ── Service resolution ────────────────────────────────────────────────────

    private void OnAddressResolved(object? sender, EventArgs e)
    {
        if (sender is not NSNetService svc) return;

        try
        {
            var host = ResolveHost(svc);
            if (host == null) return;

            var server = new DiscoveredServer(svc.Name, host, (int)svc.Port);
            lock (_found) { _found[svc.Name] = server; }
            ServerFound?.Invoke(this, server);
        }
        finally
        {
            svc.Stop();
        }
    }

    private void OnResolveFailed(object? sender, NSNetServiceErrorEventArgs e)
    {
        if (sender is NSNetService svc)
        {
            svc.Stop();
            lock (_resolving) { _resolving.Remove(svc.Name); }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Extract the first usable IPv4 address from the resolved NSNetService.
    /// NSNetService.Addresses returns an array of NSData blobs that encode
    /// sockaddr structs — we parse out the address bytes manually.
    /// </summary>
    private static string? ResolveHost(NSNetService svc)
    {
        if (svc.HostName != null)
        {
            // Prefer the hostname if already resolved to a dotted-decimal form
            if (System.Net.IPAddress.TryParse(svc.HostName, out _))
                return svc.HostName;
        }

        // Fall back to the raw addresses array (sockaddr_in / sockaddr_in6 blobs)
        if (svc.Addresses == null) return null;

        foreach (var addrData in svc.Addresses)
        {
            var bytes = addrData.ToArray();
            if (bytes.Length >= 8 && bytes[1] == 2) // AF_INET (IPv4)
            {
                // bytes 4–7 are the IPv4 address
                return $"{bytes[4]}.{bytes[5]}.{bytes[6]}.{bytes[7]}";
            }
        }

        return svc.HostName; // last resort — might be a .local hostname
    }
}
