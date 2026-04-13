using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BapalaServer.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController(
    IConfiguration config,
    IWebHostEnvironment env) : ControllerBase
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
        var folders = config.GetSection("Bapala:MediaFolders").Get<string[]>() ?? [];
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
        var appSettingsPath = Path.Combine(env.ContentRootPath, "appsettings.json");

        // Read existing JSON preserving all sections (Jwt, ConnectionStrings, Logging, …)
        var raw = System.IO.File.ReadAllText(appSettingsPath);
        var root = JsonNode.Parse(raw) as JsonObject
            ?? throw new InvalidOperationException("appsettings.json is not a JSON object.");

        // Get or create the Bapala section
        if (root["Bapala"] is not JsonObject bapala)
        {
            bapala = new JsonObject();
            root["Bapala"] = bapala;
        }

        // Merge only the fields that were supplied in the request
        if (req.ServerName is not null)
            bapala["ServerName"] = req.ServerName;

        if (req.MediaFolders is not null)
        {
            var arr = new JsonArray();
            foreach (var folder in req.MediaFolders)
                arr.Add(folder);
            bapala["MediaFolders"] = arr;
        }

        if (req.TmdbApiKey is not null)
            bapala["TmdbApiKey"] = req.TmdbApiKey;

        if (req.Username is not null)
            bapala["Username"] = req.Username;

        if (req.Password is not null)
            bapala["Password"] = req.Password;

        // Write back with human-readable indentation
        var options = new JsonSerializerOptions { WriteIndented = true };
        System.IO.File.WriteAllText(appSettingsPath, root.ToJsonString(options));

        // Reload IConfiguration in-memory so changes take effect without a restart
        if (config is IConfigurationRoot configRoot)
            configRoot.Reload();

        return Ok(new { message = "Settings saved." });
    }
}
