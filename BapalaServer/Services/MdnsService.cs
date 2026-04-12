using Makaretu.Dns;

namespace BapalaServer.Services;

/// <summary>
/// Broadcasts the Bapala server on the local network using mDNS/DNS-SD.
/// Android clients discover it via NsdManager without manual IP entry.
/// Service type: _bapala._tcp
/// </summary>
public class MdnsService(IConfiguration config, ILogger<MdnsService> logger)
    : IMdnsService, IHostedService
{
    private ServiceDiscovery? _sd;

    public Task StartAsync(CancellationToken ct)
    {
        try
        {
            var serverName = config["Bapala:ServerName"] ?? "Bapala Server";
            var port = config.GetValue<int>("Bapala:Port", 8484);

            _sd = new ServiceDiscovery();
            var profile = new ServiceProfile(
                instanceName: serverName,
                serviceName: "_bapala._tcp",
                port: (ushort)port);

            profile.AddProperty("version", "1.0");
            profile.AddProperty("api", "/api");
            _sd.Advertise(profile);

            logger.LogInformation("mDNS: broadcasting '{Name}' on port {Port}", serverName, port);
        }
        catch (Exception ex)
        {
            // mDNS is best-effort — don't crash the server if the network stack rejects it
            logger.LogWarning(ex, "mDNS broadcast failed. Manual IP entry will still work.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        try { _sd?.Dispose(); } catch { /* best effort */ }
        return Task.CompletedTask;
    }
}
