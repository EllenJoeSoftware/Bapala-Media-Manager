namespace BapalaApp.Models;

/// <summary>
/// A Bapala server discovered on the local network via mDNS/NSD.
/// </summary>
public record DiscoveredServer(
    /// <summary>Friendly name broadcast by the server (e.g. "Living Room PC").</summary>
    string Name,
    /// <summary>Resolved IP address of the host.</summary>
    string Host,
    /// <summary>TCP port the server is listening on (default 8484).</summary>
    int Port)
{
    /// <summary>Ready-to-use base URL for the API client.</summary>
    public string BaseUrl => $"http://{Host}:{Port}";

    /// <summary>Human-readable label shown in the discovery list.</summary>
    public string DisplayLabel => $"{Name}  ·  {Host}:{Port}";
}
