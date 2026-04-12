using Microsoft.Extensions.Configuration;
using BapalaServer.Services;
using Xunit;

namespace BapalaServer.Tests.Controllers;

public class AuthControllerTests
{
    private static IJwtService CreateJwtService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test_secret_minimum_32_characters_long!!",
                ["Jwt:ExpiryHours"] = "24",
                ["Jwt:Issuer"] = "BapalaServer",
                ["Jwt:Audience"] = "BapalaClients"
            })
            .Build();
        return new JwtService(config);
    }

    [Fact]
    public void GenerateToken_ReturnsNonEmptyString()
    {
        var svc = CreateJwtService();
        var token = svc.GenerateToken("admin");
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public void ValidateToken_ReturnsTrueForValidToken()
    {
        var svc = CreateJwtService();
        var token = svc.GenerateToken("admin");
        Assert.True(svc.ValidateToken(token, out var username));
        Assert.Equal("admin", username);
    }

    [Fact]
    public void ValidateToken_ReturnsFalseForGarbage()
    {
        var svc = CreateJwtService();
        Assert.False(svc.ValidateToken("notavalidtoken", out _));
    }
}
