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

    /// <summary>Authenticate and receive a JWT token.</summary>
    /// <remarks>Use the returned token as a Bearer header on all other endpoints.</remarks>
    /// <response code="200">Login successful — returns JWT + server name</response>
    /// <response code="401">Invalid credentials</response>
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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

    /// <summary>Get server name and version info (no auth required).</summary>
    /// <response code="200">Server information</response>
    [HttpGet("info")]
    [ProducesResponseType<ServerInfo>(StatusCodes.Status200OK)]
    public IActionResult Info() =>
        Ok(new ServerInfo(
            config["Bapala:ServerName"] ?? "Bapala Server",
            Version: "1.0.0",
            ApiVersion: "v1"));
}
