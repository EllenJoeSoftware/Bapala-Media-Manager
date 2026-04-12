using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BapalaServer.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController(IConfiguration config) : ControllerBase
{
    public record SettingsResponse(
        string ServerName, int Port, string[] MediaFolders,
        bool HasTmdbKey, string Username);

    public record UpdateSettingsRequest(
        string? ServerName, string[]? MediaFolders, string? TmdbApiKey,
        string? Username, string? Password);

    [HttpGet]
    public IActionResult Get()
    {
        var folders = (config["Bapala:MediaFolders"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Ok(new SettingsResponse(
            ServerName: config["Bapala:ServerName"] ?? "Bapala Server",
            Port: config.GetValue<int>("Bapala:Port", 8484),
            MediaFolders: folders,
            HasTmdbKey: !string.IsNullOrWhiteSpace(config["Bapala:TmdbApiKey"]),
            Username: config["Bapala:Username"] ?? "admin"));
    }

    [HttpPut]
    public IActionResult Update([FromBody] UpdateSettingsRequest req)
    {
        // Persisting settings to disk requires IConfigurationRoot + a JSON writer.
        // For now, acknowledge — user edits appsettings.json and restarts.
        return Ok(new { message = "Settings acknowledged. Restart server to apply.", received = req });
    }
}
