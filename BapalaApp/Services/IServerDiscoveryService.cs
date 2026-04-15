using BapalaApp.Models;

namespace BapalaApp.Services;

/// <summary>
/// Platform-agnostic contract for discovering Bapala servers on the local
/// network via mDNS/DNS-SD (service type: <c>_bapala._tcp</c>).
/// </summary>
public interface IServerDiscoveryService
{
    /// <summary>
    /// Fired on the UI thread each time a new server is found or removed.
    /// </summary>
    event EventHandler<DiscoveredServer> ServerFound;
    event EventHandler<DiscoveredServer> ServerLost;

    /// <summary>Whether a discovery scan is currently in progress.</summary>
    bool IsScanning { get; }

    /// <summary>Start listening for <c>_bapala._tcp</c> service announcements.</summary>
    Task StartAsync();

    /// <summary>Stop listening and release OS resources.</summary>
    void Stop();
}
