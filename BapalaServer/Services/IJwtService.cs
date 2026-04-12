namespace BapalaServer.Services;

public interface IJwtService
{
    string GenerateToken(string username);
    bool ValidateToken(string token, out string username);
}
