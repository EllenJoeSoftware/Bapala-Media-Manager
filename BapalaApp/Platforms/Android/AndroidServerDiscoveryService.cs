using Android.Content;
using Android.Net.Nsd;
using Android.Net.Wifi;
using Android.Util;
using BapalaApp.Models;
using BapalaApp.Services;
using Application = Android.App.Application;

namespace BapalaApp.Platforms.Android;

internal sealed class AndroidServerDiscoveryService : Java.Lang.Object,
    NsdManager.IDiscoveryListener,
    IServerDiscoveryService
{
    private const string Tag = "BapalaDisc";
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

        Log.Debug(Tag, "AndroidServerDiscoveryService created");
    }

    // ── IServerDiscoveryService ───────────────────────────────────────────────

    public Task StartAsync()
    {
        if (_discovering)
        {
            Log.Debug(Tag, "StartAsync called but already discovering — skipped");
            return Task.CompletedTask;
        }
        _found.Clear();
        _mcLock.Acquire();
        Log.Debug(Tag, $"Multicast lock acquired. Starting NSD browse for '{ServiceType}'");
        _nsd.DiscoverServices(ServiceType, NsdProtocol.DnsSd, this);
        _discovering = true;
        return Task.CompletedTask;
    }

    public void Stop()
    {
        if (!_discovering)
        {
            Log.Debug(Tag, "Stop called but not discovering — skipped");
            return;
        }
        Log.Debug(Tag, "Stopping NSD discovery");
        try { _nsd.StopServiceDiscovery(this); }
        catch (Exception ex) { Log.Warn(Tag, $"StopServiceDiscovery threw: {ex.Message}"); }
        finally
        {
            _mcLock.Release();
            _discovering = false;
            Log.Debug(Tag, "Multicast lock released");
        }
    }

    // ── NsdManager.IDiscoveryListener ─────────────────────────────────────────

    public void OnDiscoveryStarted(string? serviceType)
        => Log.Debug(Tag, $"✅ Discovery started for type: '{serviceType}'");

    public void OnDiscoveryStopped(string? serviceType)
    {
        Log.Debug(Tag, $"Discovery stopped for type: '{serviceType}'");
        _discovering = false;
    }

    public void OnStartDiscoveryFailed(string? serviceType, NsdFailure errorCode)
    {
        Log.Error(Tag, $"❌ OnStartDiscoveryFailed — type: '{serviceType}', error: {errorCode}");
        _discovering = false;
        try { _mcLock.Release(); } catch { }
    }

    public void OnStopDiscoveryFailed(string? serviceType, NsdFailure errorCode)
        => Log.Warn(Tag, $"OnStopDiscoveryFailed — type: '{serviceType}', error: {errorCode}");

    public void OnServiceFound(NsdServiceInfo? serviceInfo)
    {
        if (serviceInfo == null)
        {
            Log.Warn(Tag, "OnServiceFound called with null serviceInfo");
            return;
        }

        Log.Debug(Tag, $"OnServiceFound — name: '{serviceInfo.ServiceName}', type: '{serviceInfo.ServiceType}'");

        var type = serviceInfo.ServiceType ?? string.Empty;
        if (!type.Contains("_bapala._tcp", StringComparison.OrdinalIgnoreCase))
        {
            Log.Debug(Tag, $"  → Ignored (not _bapala._tcp)");
            return;
        }

        Log.Debug(Tag, $"  → Matched! Queuing resolve...");
        Task.Run(() => ResolveAsync(serviceInfo));
    }

    public void OnServiceLost(NsdServiceInfo? serviceInfo)
    {
        if (serviceInfo?.ServiceName == null) return;
        Log.Debug(Tag, $"OnServiceLost — name: '{serviceInfo.ServiceName}'");
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
        Log.Debug(Tag, $"ResolveAsync attempt {attempt} for '{serviceInfo.ServiceName}'");
        await _resolveSemaphore.WaitAsync();
        try
        {
            var tcs = new TaskCompletionSource<NsdServiceInfo?>();
            var listener = new ResolveListener(tcs);

#pragma warning disable CA1422
            _nsd.ResolveService(serviceInfo, listener);
#pragma warning restore CA1422

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(6_000));
            if (completed != tcs.Task)
            {
                Log.Warn(Tag, $"  → Resolve timed out for '{serviceInfo.ServiceName}' (attempt {attempt})");
                if (attempt == 0)
                {
                    _resolveSemaphore.Release();
                    await Task.Delay(1_500);
                    await ResolveAsync(serviceInfo, attempt: 1);
                }
                return;
            }

            var resolved = await tcs.Task;
            if (resolved == null)
            {
                Log.Warn(Tag, $"  → Resolve returned null for '{serviceInfo.ServiceName}'");
                return;
            }

#pragma warning disable CA1422
            var hostObj = resolved.Host;
#pragma warning restore CA1422

            Log.Debug(Tag, $"  → Resolved: host={hostObj?.HostName}, hostAddress={hostObj?.HostAddress}, port={resolved.Port}");

            if (hostObj == null)
            {
                Log.Warn(Tag, "  → Host is null after resolve");
                return;
            }

            string? host = null;
            try
            {
                var allAddresses = Java.Net.InetAddress.GetAllByName(hostObj.HostName)
                    ?? Array.Empty<Java.Net.InetAddress>();
                Log.Debug(Tag, $"  → All addresses for '{hostObj.HostName}': {string.Join(", ", allAddresses.Select(a => a.HostAddress))}");
                host = allAddresses
                    .OfType<Java.Net.Inet4Address>()
                    .Select(a => a.HostAddress)
                    .FirstOrDefault(a => a != null && !a.StartsWith("127.") && !a.StartsWith("169.254."));
            }
            catch (Exception ex)
            {
                Log.Warn(Tag, $"  → GetAllByName failed: {ex.Message}");
            }

            host ??= hostObj.HostAddress ?? hostObj.HostName ?? hostObj.ToString();
            Log.Debug(Tag, $"  → Using host: '{host}'");

            if (string.IsNullOrEmpty(host))
            {
                Log.Warn(Tag, "  → Final host is empty — aborting");
                return;
            }

            var port   = resolved.Port;
            var name   = resolved.ServiceName ?? "Bapala Server";
            var server = new DiscoveredServer(name, host, port);

            Log.Debug(Tag, $"  ✅ Server discovered: {server.DisplayLabel}");

            lock (_found) { _found[name] = server; }
            RaiseOnMainThread(ServerFound, server);
        }
        catch (Exception ex) when (ex.Message?.Contains("FAILURE_ALREADY_ACTIVE") == true)
        {
            Log.Warn(Tag, $"  → FAILURE_ALREADY_ACTIVE on attempt {attempt} — backing off");
            _resolveSemaphore.Release();
            if (attempt == 0)
            {
                await Task.Delay(2_000);
                await ResolveAsync(serviceInfo, attempt: 1);
            }
            return;
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"  → Unexpected error in ResolveAsync: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
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
        {
            Log.Debug("BapalaDisc", $"  → OnServiceResolved: '{serviceInfo?.ServiceName}'");
            tcs.TrySetResult(serviceInfo);
        }

        public void OnResolveFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode)
        {
            Log.Warn("BapalaDisc", $"  → OnResolveFailed: '{serviceInfo?.ServiceName}', error: {errorCode}");
            tcs.TrySetResult(null);
        }
    }
}
