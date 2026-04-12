using Microsoft.AspNetCore.Mvc;
using BapalaServer.Services;

namespace BapalaServer.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IJwtService jwtService, IConfiguration config) : ControllerBase
{
    public record LoginRequest(string Username, string Password);
    public record LoginResponse(string Token, string ServerName);
    public record ServerInfo(string ServerName, string Version, string ApiVersion);

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest req)
    {
        var expectedUsername = config["Bapala:Username"] ?? "admin";
        var expectedPassword = config["Bapala:Password"] ?? "changeme";

        static bool SafeEquals(string a, string b) =>
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(a),
                System.Text.Encoding.UTF8.GetBytes(b));

        if (!SafeEquals(req.Username, expectedUsername) || !SafeEquals(req.Password, expectedPassword))
            return Unauthorized(new { error = "Invalid credentials" });

        var token = jwtService.GenerateToken(req.Username);
        var serverName = config["Bapala:ServerName"] ?? "Bapala Server";
        return Ok(new LoginResponse(token, serverName));
    }

    [HttpGet("info")]
    public IActionResult Info() =>
        Ok(new ServerInfo(
            config["Bapala:ServerName"] ?? "Bapala Server",
            Version: "1.0.0",
            ApiVersion: "v1"));
}
