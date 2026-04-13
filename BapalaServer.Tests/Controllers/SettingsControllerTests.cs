using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using BapalaServer.Controllers;
using Xunit;

namespace BapalaServer.Tests.Controllers;

public class SettingsControllerTests : IDisposable
{
    // Write a real appsettings.json into a temp directory so the controller can persist to it
    private readonly string _tempDir;
    private readonly string _appSettingsPath;

    private static readonly string InitialJson = """
        {
          "Jwt": { "Secret": "test_secret_minimum_32_characters_long!!", "ExpiryHours": 24 },
          "Bapala": {
            "ServerName": "Test Server",
            "Port": 8484,
            "Username": "admin",
            "Password": "changeme",
            "MediaFolders": [],
            "TmdbApiKey": ""
          },
          "ConnectionStrings": { "Default": "Data Source=bapala.db" }
        }
        """;

    public SettingsControllerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _appSettingsPath = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(_appSettingsPath, InitialJson);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private SettingsController CreateController(IConfiguration config)
    {
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(_tempDir);
        return new SettingsController(config, envMock.Object);
    }

    private IConfiguration BuildConfig(Dictionary<string, string?> overrides = null!)
    {
        var values = new Dictionary<string, string?>
        {
            ["Bapala:ServerName"] = "Test Server",
            ["Bapala:Port"] = "8484",
            ["Bapala:Username"] = "admin",
            ["Bapala:Password"] = "changeme",
            ["Bapala:MediaFolders"] = "",
            ["Bapala:TmdbApiKey"] = ""
        };
        if (overrides is not null)
            foreach (var kv in overrides) values[kv.Key] = kv.Value;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    // ── GET ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Get_ReturnsSettingsFromConfig()
    {
        var config = BuildConfig(new() { ["Bapala:ServerName"] = "My Server" });
        var result = CreateController(config).Get() as OkObjectResult;
        Assert.NotNull(result);
        var json = JsonSerializer.Serialize(result!.Value);
        Assert.Contains("My Server", json);
    }

    [Fact]
    public void Get_HasTmdbKey_FalseWhenEmpty()
    {
        var result = CreateController(BuildConfig()).Get() as OkObjectResult;
        var json = JsonSerializer.Serialize(result!.Value);
        Assert.Contains("\"HasTmdbKey\":false", json);
    }

    [Fact]
    public void Get_HasTmdbKey_TrueWhenPopulated()
    {
        var config = BuildConfig(new() { ["Bapala:TmdbApiKey"] = "abc123" });
        var result = CreateController(config).Get() as OkObjectResult;
        var json = JsonSerializer.Serialize(result!.Value);
        Assert.Contains("\"HasTmdbKey\":true", json);
    }

    // ── PUT ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_WritesServerNameToFile()
    {
        var ctrl = CreateController(BuildConfig());
        ctrl.Update(new SettingsController.UpdateSettingsRequest(
            "New Name", null, null, null, null));

        var node = JsonNode.Parse(File.ReadAllText(_appSettingsPath))!;
        Assert.Equal("New Name", node["Bapala"]!["ServerName"]!.GetValue<string>());
    }

    [Fact]
    public void Update_WritesMediaFoldersAsJsonArray()
    {
        var ctrl = CreateController(BuildConfig());
        ctrl.Update(new SettingsController.UpdateSettingsRequest(
            null, ["C:\\Movies", "D:\\TV"], null, null, null));

        var node = JsonNode.Parse(File.ReadAllText(_appSettingsPath))!;
        var arr = node["Bapala"]!["MediaFolders"]!.AsArray();
        Assert.Equal(2, arr.Count);
        Assert.Equal("C:\\Movies", arr[0]!.GetValue<string>());
        Assert.Equal("D:\\TV", arr[1]!.GetValue<string>());
    }

    [Fact]
    public void Update_PreservesJwtSectionUntouched()
    {
        var ctrl = CreateController(BuildConfig());
        ctrl.Update(new SettingsController.UpdateSettingsRequest(
            "Changed", null, null, null, null));

        var node = JsonNode.Parse(File.ReadAllText(_appSettingsPath))!;
        // Jwt section must survive the write
        Assert.NotNull(node["Jwt"]);
        Assert.Equal("test_secret_minimum_32_characters_long!!",
            node["Jwt"]!["Secret"]!.GetValue<string>());
    }

    [Fact]
    public void Update_NullFieldsAreNotOverwritten()
    {
        // Only send Password — everything else must remain as-is
        var ctrl = CreateController(BuildConfig());
        ctrl.Update(new SettingsController.UpdateSettingsRequest(
            null, null, null, null, "newpass"));

        var node = JsonNode.Parse(File.ReadAllText(_appSettingsPath))!;
        Assert.Equal("Test Server", node["Bapala"]!["ServerName"]!.GetValue<string>());
        Assert.Equal("newpass", node["Bapala"]!["Password"]!.GetValue<string>());
    }

    [Fact]
    public void Update_ReturnsOkWithSavedMessage()
    {
        var ctrl = CreateController(BuildConfig());
        var result = ctrl.Update(new SettingsController.UpdateSettingsRequest(
            "X", null, null, null, null)) as OkObjectResult;
        Assert.NotNull(result);
        var json = JsonSerializer.Serialize(result!.Value);
        Assert.Contains("saved", json);
    }

    // ── Round-trip: the exact scenario that was silently failing ─────────────
    // PUT writes a JSON array → file is updated → GET reads via GetSection<string[]>
    // → same folders come back. Previously config["Bapala:MediaFolders"] returned
    // null for array-typed keys, so GET always returned [].
    [Fact]
    public void Get_ReturnsPersistedFolders_AfterUpdate()
    {
        // Write folders to the temp appsettings.json via Update
        var ctrl = CreateController(BuildConfig());
        ctrl.Update(new SettingsController.UpdateSettingsRequest(
            null, ["C:\\Movies", "D:\\TV Shows"], null, null, null));

        // Build a new config that reads from the updated file
        var configAfterSave = new ConfigurationBuilder()
            .AddJsonFile(_appSettingsPath, optional: false)
            .Build();

        var ctrl2 = CreateController(configAfterSave);
        var result = ctrl2.Get() as OkObjectResult;
        Assert.NotNull(result);

        // Deserialise the anonymous SettingsResponse
        var json = JsonSerializer.Serialize(result!.Value);
        using var doc = JsonDocument.Parse(json);
        // JsonSerializer without web options uses PascalCase (matches the record property name)
        var folders = doc.RootElement.GetProperty("MediaFolders");
        Assert.Equal(2, folders.GetArrayLength());
        Assert.Equal("C:\\Movies", folders[0].GetString());
        Assert.Equal("D:\\TV Shows", folders[1].GetString());
    }
}
