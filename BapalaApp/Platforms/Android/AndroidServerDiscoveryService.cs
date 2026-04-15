using Android.Content;
using Android.Net.Nsd;
using Android.Net.Wifi;
using BapalaApp.Models;
using BapalaApp.Services;
using Application = Android.App.Application;

namespace BapalaApp.Platforms.Android;

/// <summary>
/// Android implementation of server discovery using <see cref="NsdManager"/>
/// (Network Service Discovery — Android's wrapper around mDNS / DNS-SD).
///
/// The server broadcasts <c>_bapala._tcp</c> via Makaretu mDNS; this class
/// listens for those announcements, resolves the host + port, and raises
/// <see cref="IServerDiscoveryService.ServerFound"/> on the UI thread.
///
/// Android quirk — only ONE active resolution at a time:
///   NsdManager.ResolveService throws FAILURE_ALREADY_ACTIVE if called
///   concurrently.  We serialize resolutions through a simple queue.
///
/// Multicast lock:
///   Android's Wi-Fi chip can filter out multicast packets when the screen
///   is on and no multicast lock is held.  We acquire a lock for the lifetime
///   of the scan to ensure mDNS packets get through reliably.
/// </summary>
internal sealed class AndroidServerDiscoveryService : Java.Lang.Object,
    NsdManager.IDiscoveryListener,
    IServerDiscoveryService
{
    private const string ServiceType = "_bapala._tcp.";

    private readonly NsdManager _nsd;
    private readonly WifiManager.MulticastLock _mcLock;

    private bool _discovering;
    private readonly SemaphoreSlim _resolveSemaphore = new(1, 1);
    private readonly Dictionary<string, DiscoveredServer> _found = new();

    public event EventHandler<DiscoveredServer>? ServerFound;
    public event EventHandler<DiscoveredServer>? ServerLost;
    public bool IsScanning => _discovering;

    public AndroidServerDiscoveryService()
    {
        var ctx = Application.Context;
        _nsd = (NsdManager)ctx.GetSystemService(Context.NsdService)!;

        var wifi = (WifiManager)ctx.GetSystemService(Context.WifiService)!;
        _mcLock = wifi.CreateMulticastLock("bapala_discovery")!;
        _mcLock.SetReferenceCounted(false);
    }

    // ── IServerDiscoveryService ───────────────────────────────────────────────

    public Task StartAsync()
    {
        if (_discovering) return Task.CompletedTask;
        _found.Clear();
        _mcLock.Acquire();
        _nsd.DiscoverServices(ServiceType, NsdProtocol.DnsSd, this);
        _discovering = true;
        return Task.CompletedTask;
    }

    public void Stop()
    {
        if (!_discovering) return;
        try
        {
            _nsd.StopServiceDiscovery(this);
        }
        catch
        {
            // StopServiceDiscovery can throw if the listener was never fully started
        }
        finally
        {
            _mcLock.Release();
            _discovering = false;
        }
    }

    // ── NsdManager.IDiscoveryListener ─────────────────────────────────────────

    public void OnDiscoveryStarted(string? serviceType)
    { /* nothing — just confirms scanning began */ }

    public void OnDiscoveryStopped(string? serviceType)
    { _discovering = false; }

    public void OnStartDiscoveryFailed(string? serviceType, NsdFailure errorCode)
    {
        _discovering = false;
        _mcLock.Release();
    }

    public void OnStopDiscoveryFailed(string? serviceType, NsdFailure errorCode)
    { /* best-effort */ }

    public void OnServiceFound(NsdServiceInfo? serviceInfo)
    {
        if (serviceInfo == null) return;
        // Kick off resolution on a thread-pool thread to keep the callback
        // (which runs on the NSD thread) non-blocking.
        Task.Run(() => ResolveAsync(serviceInfo));
    }

    public void OnServiceLost(NsdServiceInfo? serviceInfo)
    {
        if (serviceInfo?.ServiceName == null) return;
        lock (_found)
        {
            if (!_found.TryGetValue(serviceInfo.ServiceName, out var server)) return;
            _found.Remove(serviceInfo.ServiceName);
            RaiseOnMainThread(ServerLost, server);
        }
    }

    // ── Resolution ────────────────────────────────────────────────────────────

    private async Task ResolveAsync(NsdServiceInfo serviceInfo)
    {
        // Serialize — only one resolution active at a time
        await _resolveSemaphore.WaitAsync();
        try
        {
            var tcs = new TaskCompletionSource<NsdServiceInfo?>();
            var listener = new ResolveListener(tcs);
            _nsd.ResolveService(serviceInfo, listener);

            // Give the OS up to 5 s to resolve the service
            var resolved = await Task.WhenAny(tcs.Task, Task.Delay(5_000)) == tcs.Task
                ? await tcs.Task
                : null;

            if (resolved?.Host == null) return;

            var host   = resolved.Host.HostAddress ?? resolved.Host.ToString();
            var port   = resolved.Port;
            var name   = resolved.ServiceName ?? "Bapala Server";
            var server = new DiscoveredServer(name, host!, port);

            lock (_found)
            {
                _found[name] = server;
            }

            RaiseOnMainThread(ServerFound, server);
        }
        catch
        {
            // Best-effort — if resolution fails the user still has manual entry
        }
        finally
        {
            _resolveSemaphore.Release();
        }
    }

    private static void RaiseOnMainThread(EventHandler<DiscoveredServer>? handler, DiscoveredServer server)
    {
        if (handler == null) return;
        MainThread.BeginInvokeOnMainThread(() => handler.Invoke(null, server));
    }

    // ── Inner resolve listener ────────────────────────────────────────────────

    private sealed class ResolveListener(TaskCompletionSource<NsdServiceInfo?> tcs)
        : Java.Lang.Object, NsdManager.IResolveListener
    {
        public void OnServiceResolved(NsdServiceInfo? serviceInfo)
            => tcs.TrySetResult(serviceInfo);

        public void OnResolveFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode)
            => tcs.TrySetResult(null);
    }
}
