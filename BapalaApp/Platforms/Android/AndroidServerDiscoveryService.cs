using Android.Content;
using Android.Net.Nsd;
using Android.Net.Wifi;
using BapalaApp.Models;
using BapalaApp.Services;
using Application = Android.App.Application;

namespace BapalaApp.Platforms.Android;

/// <summary>
/// Android implementation of server discovery using <see cref="NsdManager"/>
/// (Network Service Discovery — Android's mDNS/DNS-SD wrapper).
///
/// Known Android quirks handled here:
///
///  1. ServiceType suffix — NsdManager requires the service type to end with
///     a trailing dot AND to have the full DNS-SD format "_type._tcp." but when
///     comparing in OnServiceFound the OS sometimes strips the dot.  We match
///     on Contains rather than Equals to be safe.
///
///  2. One-at-a-time resolution — NsdManager.ResolveService throws
///     FAILURE_ALREADY_ACTIVE if called concurrently.  Resolutions are
///     serialised through a semaphore.
///
///  3. Multicast lock — Android's Wi-Fi chip can silently drop multicast
///     packets.  A MulticastLock must be held for the entire scan duration.
///
///  4. API 34 deprecations — ResolveService and NsdServiceInfo.Host are
///     obsoleted on API 34+ but no back-ported replacement exists.  They are
///     suppressed with #pragma and still work on API 26-33.
///
///  5. ResolveService on API 34+ sometimes returns FAILURE_ALREADY_ACTIVE
///     even through the semaphore because the OS-level queue is separate.
///     We retry once after a short delay before giving up.
/// </summary>
internal sealed class AndroidServerDiscoveryService : Java.Lang.Object,
    NsdManager.IDiscoveryListener,
    IServerDiscoveryService
{
    // The trailing dot is required by NsdManager for the browse call.
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
        try { _nsd.StopServiceDiscovery(this); }
        catch { /* can throw if listener was never fully started */ }
        finally
        {
            _mcLock.Release();
            _discovering = false;
        }
    }

    // ── NsdManager.IDiscoveryListener ─────────────────────────────────────────

    public void OnDiscoveryStarted(string? serviceType) { }

    public void OnDiscoveryStopped(string? serviceType)
        => _discovering = false;

    public void OnStartDiscoveryFailed(string? serviceType, NsdFailure errorCode)
    {
        _discovering = false;
        // Release only if we actually acquired
        try { _mcLock.Release(); } catch { }
    }

    public void OnStopDiscoveryFailed(string? serviceType, NsdFailure errorCode) { }

    public void OnServiceFound(NsdServiceInfo? serviceInfo)
    {
        if (serviceInfo == null) return;

        // The OS sometimes delivers services from OTHER types on the same
        // mDNS bus — filter to our service type only.
        // Use Contains because the OS may or may not include the trailing dot.
        var type = serviceInfo.ServiceType ?? string.Empty;
        if (!type.Contains("_bapala._tcp", StringComparison.OrdinalIgnoreCase))
            return;

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

    private async Task ResolveAsync(NsdServiceInfo serviceInfo, int attempt = 0)
    {
        await _resolveSemaphore.WaitAsync();
        try
        {
            var tcs = new TaskCompletionSource<NsdServiceInfo?>();
            var listener = new ResolveListener(tcs);

#pragma warning disable CA1422
            _nsd.ResolveService(serviceInfo, listener);
#pragma warning restore CA1422

            // Give the OS up to 6 s to respond
            var resolved = await Task.WhenAny(tcs.Task, Task.Delay(6_000)) == tcs.Task
                ? await tcs.Task
                : null;

            if (resolved == null)
            {
                // Timed out — retry once after a short back-off
                if (attempt == 0)
                {
                    _resolveSemaphore.Release();
                    await Task.Delay(1_500);
                    await ResolveAsync(serviceInfo, attempt: 1);
                }
                return;
            }

#pragma warning disable CA1422
            var hostObj = resolved.Host;
#pragma warning restore CA1422

            if (hostObj == null) return;

            // Prefer the first non-loopback, non-link-local IPv4 address
            string? host = null;
            try
            {
                var allAddresses = Java.Net.InetAddress.GetAllByName(hostObj.HostName);
                host = allAddresses
                    .OfType<Java.Net.Inet4Address>()
                    .Select(a => a.HostAddress)
                    .FirstOrDefault(a => a != null && !a.StartsWith("127.") && !a.StartsWith("169.254."));
            }
            catch { }

            // Fall back to whatever the OS gave us
            host ??= hostObj.HostAddress ?? hostObj.HostName ?? hostObj.ToString();

            if (string.IsNullOrEmpty(host)) return;

            var port   = resolved.Port;
            var name   = resolved.ServiceName ?? "Bapala Server";
            var server = new DiscoveredServer(name, host, port);

            lock (_found)
            {
                // Deduplicate — same name = same server (IP might have changed)
                _found[name] = server;
            }

            RaiseOnMainThread(ServerFound, server);
        }
        catch (Exception ex) when (ex.Message?.Contains("FAILURE_ALREADY_ACTIVE") == true)
        {
            // OS-level resolve queue is busy — back off and retry once
            _resolveSemaphore.Release();
            if (attempt == 0)
            {
                await Task.Delay(2_000);
                await ResolveAsync(serviceInfo, attempt: 1);
            }
            return;
        }
        catch
        {
            // Best-effort — manual entry is always available
        }
        finally
        {
            // Only release if we still hold it (the retry paths release early)
            if (_resolveSemaphore.CurrentCount == 0)
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
