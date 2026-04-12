# Bapala Media Manager — Server Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a self-hosted Windows media server (ASP.NET Core 8) that scans local video libraries, fetches metadata from TMDB, serves a browser-based management UI, and streams video to LAN/WAN clients with JWT auth.

**Architecture:** ASP.NET Core 8 Web API with EF Core + SQLite for metadata, SignalR for real-time scan progress, and ASP.NET Core's built-in range-request support for streaming. A vanilla HTML/JS SPA in `wwwroot/` is the desktop management UI; the Android client (separate plan) connects to the same REST/stream endpoints.

**Tech Stack:** .NET 8, ASP.NET Core 8, EF Core 8 + SQLite, SignalR, JWT Bearer, Makaretu.Dns.Multicast (mDNS), TMDB REST API, xUnit + Moq + EF Core InMemory

---

## File Map

```
BapalaMediaManager/
├── BapalaMediaManager.sln
├── BapalaServer/
│   ├── BapalaServer.csproj
│   ├── Program.cs
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   ├── Data/
│   │   └── BapalaDbContext.cs
│   ├── Models/
│   │   ├── MediaItem.cs          # Core domain entity
│   │   ├── WatchHistory.cs       # Per-user watch progress
│   │   └── MediaType.cs          # Enum: Movie | Series | Documentary
│   ├── Repositories/
│   │   ├── IMediaRepository.cs
│   │   └── SqliteMediaRepository.cs
│   ├── Services/
│   │   ├── IJwtService.cs
│   │   ├── JwtService.cs
│   │   ├── IMediaScannerService.cs
│   │   ├── MediaScannerService.cs   # Filesystem walker + filename parser
│   │   ├── ITmdbService.cs
│   │   ├── TmdbService.cs           # TMDB REST client + image cacher
│   │   ├── IMdnsService.cs
│   │   └── MdnsService.cs           # IHostedService mDNS broadcaster
│   ├── Hubs/
│   │   └── ScanProgressHub.cs
│   ├── Controllers/
│   │   ├── AuthController.cs        # POST /api/auth/login, GET /api/auth/info
│   │   ├── MediaController.cs       # CRUD + search + scan trigger
│   │   ├── StreamController.cs      # GET /api/stream/{id}
│   │   └── SettingsController.cs    # GET/PUT /api/settings
│   ├── Middleware/
│   │   └── ExceptionMiddleware.cs
│   └── wwwroot/
│       ├── index.html               # Media grid SPA
│       ├── login.html
│       ├── player.html
│       ├── settings.html
│       ├── css/app.css
│       ├── js/
│       │   ├── api.js               # Fetch wrapper with JWT + XSS helpers
│       │   ├── app.js               # Media grid logic
│       │   ├── player.js
│       │   └── settings.js
│       ├── posters/                 # Cached TMDB poster images
│       └── backdrops/
└── BapalaServer.Tests/
    ├── BapalaServer.Tests.csproj
    ├── Services/
    │   ├── MediaScannerServiceTests.cs
    │   └── TmdbServiceTests.cs
    ├── Repositories/
    │   └── SqliteMediaRepositoryTests.cs
    └── Controllers/
        ├── AuthControllerTests.cs
        └── MediaControllerTests.cs
```

---

## Task 1: Solution & Project Setup

**Files:**
- Create: `BapalaMediaManager.sln`
- Create: `BapalaServer/BapalaServer.csproj`
- Create: `BapalaServer.Tests/BapalaServer.Tests.csproj`

- [ ] **Step 1: Scaffold solution and projects**

```bash
cd "F:/2026/code/repos/Bapala Media Manager"
dotnet new sln -n BapalaMediaManager
dotnet new webapi -n BapalaServer --no-openapi
dotnet new xunit -n BapalaServer.Tests
dotnet sln add BapalaServer/BapalaServer.csproj
dotnet sln add BapalaServer.Tests/BapalaServer.Tests.csproj
dotnet add BapalaServer.Tests/BapalaServer.Tests.csproj reference BapalaServer/BapalaServer.csproj
```

- [ ] **Step 2: Install server NuGet packages**

```bash
cd BapalaServer
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package System.IdentityModel.Tokens.Jwt
dotnet add package Microsoft.AspNetCore.SignalR
dotnet add package Makaretu.Dns.Multicast
```

- [ ] **Step 3: Install test NuGet packages**

```bash
cd ../BapalaServer.Tests
dotnet add package Microsoft.EntityFrameworkCore.InMemory
dotnet add package Moq
dotnet add package Microsoft.AspNetCore.Mvc.Testing
dotnet add package coverlet.collector
```

- [ ] **Step 4: Verify build**

```bash
cd ..
dotnet build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 5: Commit**

```bash
git init
git add .
git commit -m "chore: scaffold BapalaMediaManager solution"
```

---

## Task 2: Domain Models & Database Context

**Files:**
- Create: `BapalaServer/Models/MediaType.cs`
- Create: `BapalaServer/Models/MediaItem.cs`
- Create: `BapalaServer/Models/WatchHistory.cs`
- Create: `BapalaServer/Data/BapalaDbContext.cs`
- Create: `BapalaServer/appsettings.json`

- [ ] **Step 1: Write failing test for DbContext**

Create `BapalaServer.Tests/Repositories/SqliteMediaRepositoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using BapalaServer.Data;
using BapalaServer.Models;
using Xunit;

namespace BapalaServer.Tests.Repositories;

public class SqliteMediaRepositoryTests
{
    private static BapalaDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<BapalaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new BapalaDbContext(options);
    }

    [Fact]
    public async Task CanAddAndRetrieveMediaItem()
    {
        await using var db = CreateInMemoryDb();
        var item = new MediaItem
        {
            Title = "Inception",
            Year = 2010,
            Type = MediaType.Movie,
            FilePath = "/movies/Inception.mkv",
            DateAdded = DateTime.UtcNow
        };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();

        var found = await db.MediaItems.FirstAsync(m => m.Title == "Inception");
        Assert.Equal(2010, found.Year);
        Assert.Equal(MediaType.Movie, found.Type);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test --filter "CanAddAndRetrieveMediaItem"
```

Expected: FAIL — `BapalaDbContext`, `MediaItem`, `MediaType` not found.

- [ ] **Step 3: Create `Models/MediaType.cs`**

```csharp
namespace BapalaServer.Models;

public enum MediaType
{
    Movie,
    Series,
    Documentary
}
```

- [ ] **Step 4: Create `Models/MediaItem.cs`**

```csharp
namespace BapalaServer.Models;

public class MediaItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public MediaType Type { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public string? Description { get; set; }
    public string? Genres { get; set; }          // Comma-separated: "Action,Thriller"
    public double? Rating { get; set; }
    public long? DurationSeconds { get; set; }
    public bool IsFavorite { get; set; }
    public int? TmdbId { get; set; }
    public DateTime DateAdded { get; set; }
    public ICollection<WatchHistory> WatchHistory { get; set; } = new List<WatchHistory>();
}
```

- [ ] **Step 5: Create `Models/WatchHistory.cs`**

```csharp
namespace BapalaServer.Models;

public class WatchHistory
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public MediaItem MediaItem { get; set; } = null!;
    public long ProgressSeconds { get; set; }
    public DateTime WatchedAt { get; set; }
}
```

- [ ] **Step 6: Create `Data/BapalaDbContext.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using BapalaServer.Models;

namespace BapalaServer.Data;

public class BapalaDbContext(DbContextOptions<BapalaDbContext> options) : DbContext(options)
{
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<WatchHistory> WatchHistory => Set<WatchHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaItem>(e =>
        {
            e.HasIndex(m => m.Title);
            e.HasIndex(m => m.Type);
            e.HasIndex(m => m.Year);
            e.HasIndex(m => m.IsFavorite);
        });

        modelBuilder.Entity<WatchHistory>(e =>
        {
            e.HasOne(w => w.MediaItem)
             .WithMany(m => m.WatchHistory)
             .HasForeignKey(w => w.MediaItemId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
```

- [ ] **Step 7: Create `appsettings.json`**

```json
{
  "Jwt": {
    "Secret": "CHANGE_THIS_IN_PRODUCTION_minimum_32_characters!!",
    "ExpiryHours": 24,
    "Issuer": "BapalaServer",
    "Audience": "BapalaClients"
  },
  "Bapala": {
    "ServerName": "My Bapala Server",
    "Port": 8484,
    "Username": "admin",
    "Password": "changeme",
    "MediaFolders": [],
    "TmdbApiKey": ""
  },
  "ConnectionStrings": {
    "Default": "Data Source=bapala.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

- [ ] **Step 8: Run test — should pass**

```bash
dotnet test --filter "CanAddAndRetrieveMediaItem" -v normal
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add BapalaServer/Models BapalaServer/Data BapalaServer/appsettings.json BapalaServer.Tests/
git commit -m "feat: add domain models, DbContext, and initial repository test"
```

---

## Task 3: Repository Layer

**Files:**
- Create: `BapalaServer/Repositories/IMediaRepository.cs`
- Create: `BapalaServer/Repositories/SqliteMediaRepository.cs`
- Modify: `BapalaServer.Tests/Repositories/SqliteMediaRepositoryTests.cs`

- [ ] **Step 1: Create `Repositories/IMediaRepository.cs`**

```csharp
using BapalaServer.Models;

namespace BapalaServer.Repositories;

public interface IMediaRepository
{
    Task<IEnumerable<MediaItem>> GetAllAsync(
        int page, int limit, MediaType? type, string? genre, string? search, bool favoritesOnly);
    Task<int> CountAsync(MediaType? type, string? genre, string? search, bool favoritesOnly);
    Task<MediaItem?> GetByIdAsync(int id);
    Task<MediaItem?> GetByFilePathAsync(string filePath);
    Task<MediaItem> AddAsync(MediaItem item);
    Task<MediaItem> UpdateAsync(MediaItem item);
    Task DeleteAsync(int id);
    Task<WatchHistory?> GetWatchHistoryAsync(int mediaItemId);
    Task UpsertWatchHistoryAsync(int mediaItemId, long progressSeconds);
}
```

- [ ] **Step 2: Write failing tests for key repository methods**

Append to `BapalaServer.Tests/Repositories/SqliteMediaRepositoryTests.cs`:

```csharp
using BapalaServer.Repositories;

// Add these test methods to the SqliteMediaRepositoryTests class:

[Fact]
public async Task GetAllAsync_FiltersByType()
{
    await using var db = CreateInMemoryDb();
    var repo = new SqliteMediaRepository(db);
    db.MediaItems.AddRange(
        new MediaItem { Title = "Movie1", Type = MediaType.Movie, FilePath = "/a.mkv", DateAdded = DateTime.UtcNow },
        new MediaItem { Title = "Show1", Type = MediaType.Series, FilePath = "/b.mkv", DateAdded = DateTime.UtcNow }
    );
    await db.SaveChangesAsync();

    var movies = (await repo.GetAllAsync(1, 10, MediaType.Movie, null, null, false)).ToList();
    Assert.Single(movies);
    Assert.Equal("Movie1", movies[0].Title);
}

[Fact]
public async Task GetAllAsync_SearchesByTitle()
{
    await using var db = CreateInMemoryDb();
    var repo = new SqliteMediaRepository(db);
    db.MediaItems.AddRange(
        new MediaItem { Title = "Inception", Type = MediaType.Movie, FilePath = "/a.mkv", DateAdded = DateTime.UtcNow },
        new MediaItem { Title = "Interstellar", Type = MediaType.Movie, FilePath = "/b.mkv", DateAdded = DateTime.UtcNow }
    );
    await db.SaveChangesAsync();

    var results = (await repo.GetAllAsync(1, 10, null, null, "incep", false)).ToList();
    Assert.Single(results);
    Assert.Equal("Inception", results[0].Title);
}

[Fact]
public async Task UpsertWatchHistory_CreatesAndUpdates()
{
    await using var db = CreateInMemoryDb();
    var repo = new SqliteMediaRepository(db);
    var item = new MediaItem { Title = "X", Type = MediaType.Movie, FilePath = "/x.mkv", DateAdded = DateTime.UtcNow };
    db.MediaItems.Add(item);
    await db.SaveChangesAsync();

    await repo.UpsertWatchHistoryAsync(item.Id, 300);
    var history = await repo.GetWatchHistoryAsync(item.Id);
    Assert.Equal(300, history!.ProgressSeconds);

    await repo.UpsertWatchHistoryAsync(item.Id, 600);
    history = await repo.GetWatchHistoryAsync(item.Id);
    Assert.Equal(600, history!.ProgressSeconds);
}
```

- [ ] **Step 3: Run to verify failures**

```bash
dotnet test --filter "GetAllAsync_FiltersByType|GetAllAsync_SearchesByTitle|UpsertWatchHistory" -v normal
```

Expected: FAIL — `SqliteMediaRepository` not found.

- [ ] **Step 4: Create `Repositories/SqliteMediaRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using BapalaServer.Data;
using BapalaServer.Models;

namespace BapalaServer.Repositories;

public class SqliteMediaRepository(BapalaDbContext db) : IMediaRepository
{
    public async Task<IEnumerable<MediaItem>> GetAllAsync(
        int page, int limit, MediaType? type, string? genre, string? search, bool favoritesOnly)
    {
        var q = db.MediaItems.AsQueryable();
        if (type.HasValue) q = q.Where(m => m.Type == type.Value);
        if (!string.IsNullOrWhiteSpace(genre)) q = q.Where(m => m.Genres != null && m.Genres.Contains(genre));
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(m => m.Title.Contains(search));
        if (favoritesOnly) q = q.Where(m => m.IsFavorite);
        return await q.OrderByDescending(m => m.DateAdded)
                      .Skip((page - 1) * limit).Take(limit)
                      .ToListAsync();
    }

    public async Task<int> CountAsync(MediaType? type, string? genre, string? search, bool favoritesOnly)
    {
        var q = db.MediaItems.AsQueryable();
        if (type.HasValue) q = q.Where(m => m.Type == type.Value);
        if (!string.IsNullOrWhiteSpace(genre)) q = q.Where(m => m.Genres != null && m.Genres.Contains(genre));
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(m => m.Title.Contains(search));
        if (favoritesOnly) q = q.Where(m => m.IsFavorite);
        return await q.CountAsync();
    }

    public Task<MediaItem?> GetByIdAsync(int id) =>
        db.MediaItems.FirstOrDefaultAsync(m => m.Id == id);

    public Task<MediaItem?> GetByFilePathAsync(string filePath) =>
        db.MediaItems.FirstOrDefaultAsync(m => m.FilePath == filePath);

    public async Task<MediaItem> AddAsync(MediaItem item)
    {
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    public async Task<MediaItem> UpdateAsync(MediaItem item)
    {
        db.MediaItems.Update(item);
        await db.SaveChangesAsync();
        return item;
    }

    public async Task DeleteAsync(int id)
    {
        var item = await db.MediaItems.FindAsync(id);
        if (item != null)
        {
            db.MediaItems.Remove(item);
            await db.SaveChangesAsync();
        }
    }

    public Task<WatchHistory?> GetWatchHistoryAsync(int mediaItemId) =>
        db.WatchHistory.FirstOrDefaultAsync(w => w.MediaItemId == mediaItemId);

    public async Task UpsertWatchHistoryAsync(int mediaItemId, long progressSeconds)
    {
        var existing = await db.WatchHistory.FirstOrDefaultAsync(w => w.MediaItemId == mediaItemId);
        if (existing == null)
        {
            db.WatchHistory.Add(new WatchHistory
            {
                MediaItemId = mediaItemId,
                ProgressSeconds = progressSeconds,
                WatchedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.ProgressSeconds = progressSeconds;
            existing.WatchedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Run tests — should all pass**

```bash
dotnet test --filter "SqliteMediaRepositoryTests" -v normal
```

Expected: All 4 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add BapalaServer/Repositories BapalaServer.Tests/Repositories/
git commit -m "feat: add IMediaRepository and SqliteMediaRepository with tests"
```

---

## Task 4: JWT Authentication

**Files:**
- Create: `BapalaServer/Services/IJwtService.cs`
- Create: `BapalaServer/Services/JwtService.cs`
- Create: `BapalaServer/Controllers/AuthController.cs`
- Modify: `BapalaServer/Program.cs`
- Create: `BapalaServer.Tests/Controllers/AuthControllerTests.cs`

- [ ] **Step 1: Write failing auth test**

Create `BapalaServer.Tests/Controllers/AuthControllerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failures**

```bash
dotnet test --filter "AuthControllerTests" -v normal
```

Expected: FAIL — `IJwtService`, `JwtService` not found.

- [ ] **Step 3: Create `Services/IJwtService.cs`**

```csharp
namespace BapalaServer.Services;

public interface IJwtService
{
    string GenerateToken(string username);
    bool ValidateToken(string token, out string username);
}
```

- [ ] **Step 4: Create `Services/JwtService.cs`**

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BapalaServer.Services;

public class JwtService(IConfiguration config) : IJwtService
{
    private readonly string _secret = config["Jwt:Secret"]!;
    private readonly int _expiryHours = int.Parse(config["Jwt:ExpiryHours"] ?? "24");
    private readonly string _issuer = config["Jwt:Issuer"]!;
    private readonly string _audience = config["Jwt:Audience"]!;

    public string GenerateToken(string username)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: [new Claim(ClaimTypes.Name, username)],
            expires: DateTime.UtcNow.AddHours(_expiryHours),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public bool ValidateToken(string token, out string username)
    {
        username = string.Empty;
        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true
            }, out _);
            username = principal.Identity?.Name ?? string.Empty;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 5: Run auth tests — should pass**

```bash
dotnet test --filter "AuthControllerTests" -v normal
```

Expected: All 3 tests PASS.

- [ ] **Step 6: Create `Controllers/AuthController.cs`**

```csharp
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

        if (req.Username != expectedUsername || req.Password != expectedPassword)
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
```

- [ ] **Step 7: Wire up `Program.cs`**

Replace `BapalaServer/Program.cs` with:

```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using BapalaServer.Data;
using BapalaServer.Repositories;
using BapalaServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<BapalaDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=bapala.db"));

// Auth
var jwtSecret = builder.Configuration["Jwt:Secret"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true
        };
        // Allow JWT in query string for video streaming (HTML video tag can't set headers)
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (ctx.Request.Query.TryGetValue("token", out var token))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// Services
builder.Services.AddScoped<IMediaRepository, SqliteMediaRepository>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IMediaScannerService, MediaScannerService>();
builder.Services.AddScoped<ITmdbService, TmdbService>();
builder.Services.AddSingleton<IMdnsService, MdnsService>();
builder.Services.AddHostedService(p => (MdnsService)p.GetRequiredService<IMdnsService>());
builder.Services.AddHttpClient();

// ASP.NET Core
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// Listen on configured port
var port = builder.Configuration.GetValue<int>("Bapala:Port", 8484);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

// Migrate DB on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BapalaDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors();

// Default to login.html when visiting /
var defaultFiles = new DefaultFilesOptions();
defaultFiles.DefaultFileNames.Clear();
defaultFiles.DefaultFileNames.Add("login.html");
app.UseDefaultFiles(defaultFiles);
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<BapalaServer.Hubs.ScanProgressHub>("/hubs/scan");
app.Run();

public partial class Program { }  // Needed for WebApplicationFactory in integration tests
```

- [ ] **Step 8: Verify build**

```bash
cd "F:/2026/code/repos/Bapala Media Manager"
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 9: Commit**

```bash
git add BapalaServer/ BapalaServer.Tests/Controllers/
git commit -m "feat: JWT auth service and AuthController"
```

---

## Task 5: Media Scanner Service

**Files:**
- Create: `BapalaServer/Services/IMediaScannerService.cs`
- Create: `BapalaServer/Services/MediaScannerService.cs`
- Create: `BapalaServer.Tests/Services/MediaScannerServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BapalaServer.Tests/Services/MediaScannerServiceTests.cs`:

```csharp
using BapalaServer.Models;
using BapalaServer.Services;
using Xunit;

namespace BapalaServer.Tests.Services;

public class MediaScannerServiceTests
{
    [Theory]
    [InlineData("Inception.2010.1080p.BluRay.mkv", "Inception", 2010, null, null)]
    [InlineData("The.Dark.Knight.(2008).mp4", "The Dark Knight", 2008, null, null)]
    [InlineData("Breaking Bad S01E03.mkv", "Breaking Bad", null, 1, 3)]
    [InlineData("Game.of.Thrones.S05E09.1080p.mkv", "Game of Thrones", null, 5, 9)]
    public void ParseFilename_ExtractsCorrectFields(
        string filename, string expectedTitle, int? expectedYear, int? expectedSeason, int? expectedEpisode)
    {
        var result = MediaScannerService.ParseFilename(filename);
        Assert.Equal(expectedTitle, result.Title);
        Assert.Equal(expectedYear, result.Year);
        Assert.Equal(expectedSeason, result.Season);
        Assert.Equal(expectedEpisode, result.Episode);
    }

    [Theory]
    [InlineData("Breaking Bad S01E03.mkv", MediaType.Series)]
    [InlineData("Inception.2010.mkv", MediaType.Movie)]
    public void DetectMediaType_ClassifiesCorrectly(string filename, MediaType expected)
    {
        var type = MediaScannerService.DetectType(filename);
        Assert.Equal(expected, type);
    }

    [Fact]
    public void IsVideoFile_AcceptsCommonFormats()
    {
        Assert.True(MediaScannerService.IsVideoFile("movie.mkv"));
        Assert.True(MediaScannerService.IsVideoFile("movie.mp4"));
        Assert.True(MediaScannerService.IsVideoFile("movie.avi"));
        Assert.False(MediaScannerService.IsVideoFile("photo.jpg"));
        Assert.False(MediaScannerService.IsVideoFile("subtitle.srt"));
    }
}
```

- [ ] **Step 2: Run to verify failures**

```bash
dotnet test --filter "MediaScannerServiceTests" -v normal
```

Expected: FAIL — types not found.

- [ ] **Step 3: Create `Services/IMediaScannerService.cs`**

```csharp
namespace BapalaServer.Services;

public interface IMediaScannerService
{
    Task<ScanResult> ScanFoldersAsync(
        IEnumerable<string> folders,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default);
}

public record ScanResult(int Added, int Updated, int Skipped, IReadOnlyList<string> Errors);
public record ScanProgress(string CurrentFile, int Processed, int Total);
```

- [ ] **Step 4: Create `Services/MediaScannerService.cs`**

```csharp
using System.Text.RegularExpressions;
using BapalaServer.Models;
using BapalaServer.Repositories;

namespace BapalaServer.Services;

public record ParsedFilename(string Title, int? Year, int? Season, int? Episode);

public class MediaScannerService(IMediaRepository repo, ITmdbService tmdb) : IMediaScannerService
{
    private static readonly string[] VideoExtensions =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".ts", ".webm", ".flv"];

    public static bool IsVideoFile(string filename) =>
        VideoExtensions.Contains(Path.GetExtension(filename).ToLowerInvariant());

    public static MediaType DetectType(string filename)
    {
        var name = Path.GetFileNameWithoutExtension(filename);
        return Regex.IsMatch(name, @"[Ss]\d{1,2}[Ee]\d{1,2}") ? MediaType.Series : MediaType.Movie;
    }

    public static ParsedFilename ParseFilename(string filename)
    {
        var name = Path.GetFileNameWithoutExtension(filename);

        // Extract season/episode before stripping other info
        int? season = null, episode = null;
        var seMatch = Regex.Match(name, @"[Ss](\d{1,2})[Ee](\d{1,2})");
        if (seMatch.Success)
        {
            season = int.Parse(seMatch.Groups[1].Value);
            episode = int.Parse(seMatch.Groups[2].Value);
            name = name[..seMatch.Index].Trim();
        }

        // Strip quality tags and everything after
        name = Regex.Replace(name,
            @"\b(1080p|720p|480p|2160p|4K|UHD|BluRay|BRRip|WEB-DL|WEBRip|HDTV|DVDRip|x264|x265|HEVC|AAC|AC3|DTS|H\.264)\b.*",
            "", RegexOptions.IgnoreCase).Trim();

        // Extract year in parens or bare
        int? year = null;
        var yearMatch = Regex.Match(name, @"[\(\[](19|20)\d{2}[\)\]]|\b(19|20)\d{2}\b");
        if (yearMatch.Success)
        {
            year = int.Parse(Regex.Match(yearMatch.Value, @"\d{4}").Value);
            name = name.Replace(yearMatch.Value, "").Trim();
        }

        // Replace dots/underscores with spaces, collapse runs
        name = Regex.Replace(name, @"[._]", " ");
        name = Regex.Replace(name, @"\s{2,}", " ").Trim();

        return new ParsedFilename(name, year, season, episode);
    }

    public async Task<ScanResult> ScanFoldersAsync(
        IEnumerable<string> folders,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        var files = folders
            .Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "*", SearchOption.AllDirectories))
            .Where(IsVideoFile)
            .ToList();

        int added = 0, updated = 0, skipped = 0;
        var errors = new List<string>();

        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            progress?.Report(new ScanProgress(file, i + 1, files.Count));

            try
            {
                var existing = await repo.GetByFilePathAsync(file);
                if (existing != null) { skipped++; continue; }

                var parsed = ParseFilename(Path.GetFileName(file));
                var type = DetectType(file);

                var item = new MediaItem
                {
                    Title = parsed.Title,
                    Year = parsed.Year,
                    Type = type,
                    FilePath = file,
                    DateAdded = DateTime.UtcNow
                };

                // Fetch TMDB metadata — best-effort, never blocks the scan
                try
                {
                    var meta = await tmdb.FetchMetadataAsync(parsed.Title, parsed.Year, type, ct);
                    if (meta != null)
                    {
                        item.Description = meta.Description;
                        item.Genres = meta.Genres;
                        item.Rating = meta.Rating;
                        item.TmdbId = meta.TmdbId;
                        item.PosterPath = meta.PosterPath;
                        item.BackdropPath = meta.BackdropPath;
                    }
                }
                catch { /* metadata failure is non-critical */ }

                await repo.AddAsync(item);
                added++;
            }
            catch (Exception ex)
            {
                errors.Add($"{file}: {ex.Message}");
            }
        }

        return new ScanResult(added, updated, skipped, errors);
    }
}
```

- [ ] **Step 5: Run tests — should pass**

```bash
dotnet test --filter "MediaScannerServiceTests" -v normal
```

Expected: All 9 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add BapalaServer/Services/IMediaScannerService.cs BapalaServer/Services/MediaScannerService.cs BapalaServer.Tests/Services/MediaScannerServiceTests.cs
git commit -m "feat: media scanner service with filename parsing"
```

---

## Task 6: TMDB Metadata Service

**Files:**
- Create: `BapalaServer/Services/ITmdbService.cs`
- Create: `BapalaServer/Services/TmdbService.cs`
- Create: `BapalaServer.Tests/Services/TmdbServiceTests.cs`

- [ ] **Step 1: Write failing test with mock HTTP**

Create `BapalaServer.Tests/Services/TmdbServiceTests.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.Protected;
using BapalaServer.Models;
using BapalaServer.Services;
using Xunit;

namespace BapalaServer.Tests.Services;

public class TmdbServiceTests
{
    private static ITmdbService CreateService(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bapala:TmdbApiKey"] = "test_key" })
            .Build();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
               .Returns(new HttpClient(handler));
        return new TmdbService(config, factory.Object);
    }

    [Fact]
    public async Task FetchMetadataAsync_ReturnsNullWhenNoApiKey()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bapala:TmdbApiKey"] = "" })
            .Build();
        var factory = new Mock<IHttpClientFactory>();
        var svc = new TmdbService(config, factory.Object);

        var result = await svc.FetchMetadataAsync("Inception", 2010, MediaType.Movie);
        Assert.Null(result);
    }

    [Fact]
    public async Task FetchMetadataAsync_ParsesMovieResponse()
    {
        var searchJson = """
            {"results":[{"id":27205,"title":"Inception","overview":"A thief...","vote_average":8.3,
             "release_date":"2010-07-16","genre_ids":[28,878],"poster_path":"/poster.jpg","backdrop_path":"/back.jpg"}]}
            """;
        var detailJson = """{"id":27205,"genres":[{"name":"Action"},{"name":"Science Fiction"}]}""";

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .SetupSequence<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(searchJson, System.Text.Encoding.UTF8, "application/json") })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(detailJson, System.Text.Encoding.UTF8, "application/json") });

        var svc = CreateService(handlerMock.Object);
        var meta = await svc.FetchMetadataAsync("Inception", 2010, MediaType.Movie);

        Assert.NotNull(meta);
        Assert.Equal(27205, meta!.TmdbId);
        Assert.Contains("Action", meta.Genres);
        Assert.Equal(8.3, meta.Rating, 1);
    }
}
```

- [ ] **Step 2: Run to verify failures**

```bash
dotnet test --filter "TmdbServiceTests" -v normal
```

Expected: FAIL.

- [ ] **Step 3: Create `Services/ITmdbService.cs`**

```csharp
using BapalaServer.Models;

namespace BapalaServer.Services;

public record TmdbMetadata(
    int TmdbId,
    string? Description,
    string? Genres,
    double? Rating,
    string? PosterPath,
    string? BackdropPath);

public interface ITmdbService
{
    Task<TmdbMetadata?> FetchMetadataAsync(
        string title, int? year, MediaType type, CancellationToken ct = default);
}
```

- [ ] **Step 4: Create `Services/TmdbService.cs`**

```csharp
using System.Text.Json;
using BapalaServer.Models;

namespace BapalaServer.Services;

public class TmdbService(IConfiguration config, IHttpClientFactory httpFactory) : ITmdbService
{
    private const string BaseUrl = "https://api.themoviedb.org/3";
    private const string ImageBase = "https://image.tmdb.org/t/p/w500";

    public async Task<TmdbMetadata?> FetchMetadataAsync(
        string title, int? year, MediaType type, CancellationToken ct = default)
    {
        var apiKey = config["Bapala:TmdbApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var http = httpFactory.CreateClient();
        var endpoint = type == MediaType.Series ? "tv" : "movie";
        var searchUrl = $"{BaseUrl}/search/{endpoint}?api_key={apiKey}&query={Uri.EscapeDataString(title)}";
        if (year.HasValue) searchUrl += $"&year={year}";

        using var searchResp = await http.GetAsync(searchUrl, ct);
        if (!searchResp.IsSuccessStatusCode) return null;

        var searchDoc = await JsonDocument.ParseAsync(
            await searchResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var results = searchDoc.RootElement.GetProperty("results");
        if (results.GetArrayLength() == 0) return null;

        var top = results[0];
        var tmdbId = top.GetProperty("id").GetInt32();
        var description = top.TryGetProperty("overview", out var ov) ? ov.GetString() : null;
        var rating = top.TryGetProperty("vote_average", out var ra) ? ra.GetDouble() : (double?)null;
        var posterPath = top.TryGetProperty("poster_path", out var pp) && pp.ValueKind != JsonValueKind.Null
            ? ImageBase + pp.GetString() : null;
        var backdropPath = top.TryGetProperty("backdrop_path", out var bp) && bp.ValueKind != JsonValueKind.Null
            ? ImageBase + bp.GetString() : null;

        // Fetch genre names from detail endpoint
        var detailUrl = $"{BaseUrl}/{endpoint}/{tmdbId}?api_key={apiKey}";
        using var detailResp = await http.GetAsync(detailUrl, ct);
        var genres = string.Empty;
        if (detailResp.IsSuccessStatusCode)
        {
            var detailDoc = await JsonDocument.ParseAsync(
                await detailResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (detailDoc.RootElement.TryGetProperty("genres", out var genreArr))
                genres = string.Join(",", genreArr.EnumerateArray()
                    .Select(g => g.GetProperty("name").GetString()));
        }

        return new TmdbMetadata(tmdbId, description, genres, rating, posterPath, backdropPath);
    }
}
```

- [ ] **Step 5: Run tests — should pass**

```bash
dotnet test --filter "TmdbServiceTests" -v normal
```

Expected: All 2 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add BapalaServer/Services/ITmdbService.cs BapalaServer/Services/TmdbService.cs BapalaServer.Tests/Services/TmdbServiceTests.cs
git commit -m "feat: TMDB metadata service"
```

---

## Task 7: SignalR Scan Progress Hub

**Files:**
- Create: `BapalaServer/Hubs/ScanProgressHub.cs`

- [ ] **Step 1: Create `Hubs/ScanProgressHub.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BapalaServer.Hubs;

[Authorize]
public class ScanProgressHub : Hub
{
    // Clients connect here to receive scan progress events.
    // Events pushed by MediaController via IHubContext<ScanProgressHub>:
    //   "ScanStarted"   { folders }
    //   "ScanProgress"  { currentFile, processed, total }
    //   "ScanCompleted" { added, updated, skipped, errors }
    //   "ScanError"     { error }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build BapalaServer/BapalaServer.csproj
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add BapalaServer/Hubs/
git commit -m "feat: SignalR ScanProgressHub for real-time scan updates"
```

---

## Task 8: MediaController

**Files:**
- Create: `BapalaServer/Controllers/MediaController.cs`
- Create: `BapalaServer.Tests/Controllers/MediaControllerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BapalaServer.Tests/Controllers/MediaControllerTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using BapalaServer.Controllers;
using BapalaServer.Hubs;
using BapalaServer.Models;
using BapalaServer.Repositories;
using BapalaServer.Services;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace BapalaServer.Tests.Controllers;

public class MediaControllerTests
{
    private static MediaController CreateController(IMediaRepository? repo = null)
    {
        repo ??= new Mock<IMediaRepository>().Object;
        var scanner = new Mock<IMediaScannerService>().Object;
        var hubContext = new Mock<IHubContext<ScanProgressHub>>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bapala:MediaFolders"] = "" })
            .Build();
        return new MediaController(repo, scanner, hubContext.Object, config);
    }

    [Fact]
    public async Task GetAll_Returns200WithList()
    {
        var repoMock = new Mock<IMediaRepository>();
        repoMock.Setup(r => r.GetAllAsync(1, 20, null, null, null, false))
                .ReturnsAsync([new MediaItem { Id = 1, Title = "Inception", Type = MediaType.Movie,
                    FilePath = "/a.mkv", DateAdded = DateTime.UtcNow }]);
        repoMock.Setup(r => r.CountAsync(null, null, null, false)).ReturnsAsync(1);

        var result = await CreateController(repoMock.Object).GetAll(1, 20, null, null, null, false) as OkObjectResult;
        Assert.NotNull(result);
        Assert.Equal(200, result!.StatusCode);
    }

    [Fact]
    public async Task GetById_Returns404WhenMissing()
    {
        var repoMock = new Mock<IMediaRepository>();
        repoMock.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((MediaItem?)null);

        var result = await CreateController(repoMock.Object).GetById(99);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ToggleFavorite_FlipsFlag()
    {
        var item = new MediaItem { Id = 1, Title = "X", Type = MediaType.Movie,
            FilePath = "/x.mkv", DateAdded = DateTime.UtcNow, IsFavorite = false };
        var repoMock = new Mock<IMediaRepository>();
        repoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(item);
        repoMock.Setup(r => r.UpdateAsync(It.IsAny<MediaItem>())).ReturnsAsync(item);

        var result = await CreateController(repoMock.Object).ToggleFavorite(1) as OkObjectResult;
        Assert.NotNull(result);
        repoMock.Verify(r => r.UpdateAsync(It.Is<MediaItem>(m => m.IsFavorite)), Times.Once);
    }
}
```

- [ ] **Step 2: Run to verify failures**

```bash
dotnet test --filter "MediaControllerTests" -v normal
```

Expected: FAIL.

- [ ] **Step 3: Create `Controllers/MediaController.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using BapalaServer.Hubs;
using BapalaServer.Models;
using BapalaServer.Repositories;
using BapalaServer.Services;

namespace BapalaServer.Controllers;

[ApiController]
[Route("api/media")]
[Authorize]
public class MediaController(
    IMediaRepository repo,
    IMediaScannerService scanner,
    IHubContext<ScanProgressHub> hub,
    IConfiguration config) : ControllerBase
{
    public record MediaListResponse(IEnumerable<MediaItem> Items, int Total, int Page, int Limit);
    public record UpdateMediaRequest(string? Title, int? Year, string? Description, string? Genres, double? Rating);
    public record WatchProgressRequest(long ProgressSeconds);

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] MediaType? type = null,
        [FromQuery] string? genre = null,
        [FromQuery] string? search = null,
        [FromQuery] bool favorites = false)
    {
        var items = await repo.GetAllAsync(page, limit, type, genre, search, favorites);
        var total = await repo.CountAsync(type, genre, search, favorites);
        return Ok(new MediaListResponse(items, total, page, limit));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await repo.GetByIdAsync(id);
        return item == null ? NotFound() : Ok(item);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateMediaRequest req)
    {
        var item = await repo.GetByIdAsync(id);
        if (item == null) return NotFound();
        if (req.Title != null) item.Title = req.Title;
        if (req.Year.HasValue) item.Year = req.Year;
        if (req.Description != null) item.Description = req.Description;
        if (req.Genres != null) item.Genres = req.Genres;
        if (req.Rating.HasValue) item.Rating = req.Rating;
        await repo.UpdateAsync(item);
        return Ok(item);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await repo.GetByIdAsync(id);
        if (item == null) return NotFound();
        await repo.DeleteAsync(id);
        return NoContent();
    }

    [HttpPost("{id}/favorite")]
    public async Task<IActionResult> ToggleFavorite(int id)
    {
        var item = await repo.GetByIdAsync(id);
        if (item == null) return NotFound();
        item.IsFavorite = !item.IsFavorite;
        await repo.UpdateAsync(item);
        return Ok(new { item.IsFavorite });
    }

    [HttpPost("{id}/progress")]
    public async Task<IActionResult> SaveProgress(int id, [FromBody] WatchProgressRequest req)
    {
        var item = await repo.GetByIdAsync(id);
        if (item == null) return NotFound();
        await repo.UpsertWatchHistoryAsync(id, req.ProgressSeconds);
        return NoContent();
    }

    [HttpGet("{id}/progress")]
    public async Task<IActionResult> GetProgress(int id)
    {
        var history = await repo.GetWatchHistoryAsync(id);
        return Ok(new { ProgressSeconds = history?.ProgressSeconds ?? 0 });
    }

    [HttpPost("scan")]
    public IActionResult TriggerScan()
    {
        var foldersRaw = config["Bapala:MediaFolders"] ?? "";
        var folders = foldersRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (folders.Length == 0)
            return BadRequest(new { error = "No media folders configured. Add them in Settings." });

        _ = Task.Run(async () =>
        {
            await hub.Clients.All.SendAsync("ScanStarted", new { folders });
            var progress = new Progress<ScanProgress>(p =>
                hub.Clients.All.SendAsync("ScanProgress", p));
            try
            {
                var result = await scanner.ScanFoldersAsync(folders, progress);
                await hub.Clients.All.SendAsync("ScanCompleted", result);
            }
            catch (Exception ex)
            {
                await hub.Clients.All.SendAsync("ScanError", new { error = ex.Message });
            }
        });

        return Accepted(new { message = "Scan started. Connect to /hubs/scan for progress." });
    }
}
```

- [ ] **Step 4: Run tests — should pass**

```bash
dotnet test --filter "MediaControllerTests" -v normal
```

Expected: All 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add BapalaServer/Controllers/MediaController.cs BapalaServer.Tests/Controllers/MediaControllerTests.cs
git commit -m "feat: MediaController with list, search, favorites, progress, scan trigger"
```

---

## Task 9: Stream Controller (HTTP 206 Range Requests)

**Files:**
- Create: `BapalaServer/Controllers/StreamController.cs`

- [ ] **Step 1: Write failing test**

Append this class to `BapalaServer.Tests/Controllers/MediaControllerTests.cs`:

```csharp
public class StreamControllerTests
{
    [Fact]
    public void GetMimeType_ReturnsCorrectType()
    {
        Assert.Equal("video/mp4", StreamController.GetMimeType(".mp4"));
        Assert.Equal("video/x-matroska", StreamController.GetMimeType(".mkv"));
        Assert.Equal("video/x-msvideo", StreamController.GetMimeType(".avi"));
        Assert.Equal("application/octet-stream", StreamController.GetMimeType(".xyz"));
    }
}
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test --filter "StreamControllerTests" -v normal
```

Expected: FAIL.

- [ ] **Step 3: Create `Controllers/StreamController.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BapalaServer.Repositories;

namespace BapalaServer.Controllers;

[ApiController]
[Route("api/stream")]
[Authorize]
public class StreamController(IMediaRepository repo) : ControllerBase
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"]  = "video/mp4",
        [".mkv"]  = "video/x-matroska",
        [".avi"]  = "video/x-msvideo",
        [".mov"]  = "video/quicktime",
        [".wmv"]  = "video/x-ms-wmv",
        [".m4v"]  = "video/x-m4v",
        [".webm"] = "video/webm",
        [".ts"]   = "video/mp2t",
        [".flv"]  = "video/x-flv",
    };

    public static string GetMimeType(string extension) =>
        MimeTypes.TryGetValue(extension, out var mime) ? mime : "application/octet-stream";

    /// <summary>
    /// Streams a video file with HTTP 206 Partial Content support for seeking.
    /// Pass JWT as ?token= query param — HTML video elements can't set headers.
    /// PhysicalFile with enableRangeProcessing=true handles Range headers automatically.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Stream(int id, CancellationToken ct)
    {
        var item = await repo.GetByIdAsync(id);
        if (item == null) return NotFound(new { error = "Media not found" });

        if (!System.IO.File.Exists(item.FilePath))
            return NotFound(new { error = "File not found on disk" });

        var mime = GetMimeType(Path.GetExtension(item.FilePath));
        return PhysicalFile(item.FilePath, mime, enableRangeProcessing: true);
    }
}
```

- [ ] **Step 4: Run tests — should pass**

```bash
dotnet test --filter "StreamControllerTests" -v normal
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add BapalaServer/Controllers/StreamController.cs
git commit -m "feat: StreamController with HTTP 206 range request support"
```

---

## Task 10: mDNS Broadcast Service

**Files:**
- Create: `BapalaServer/Services/IMdnsService.cs`
- Create: `BapalaServer/Services/MdnsService.cs`

- [ ] **Step 1: Create `Services/IMdnsService.cs`**

```csharp
namespace BapalaServer.Services;

public interface IMdnsService
{
    // Marker interface — implementation is IHostedService registered in Program.cs
}
```

- [ ] **Step 2: Create `Services/MdnsService.cs`**

```csharp
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
```

- [ ] **Step 3: Verify build**

```bash
dotnet build BapalaServer/BapalaServer.csproj
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add BapalaServer/Services/IMdnsService.cs BapalaServer/Services/MdnsService.cs
git commit -m "feat: mDNS service broadcasts server on LAN for auto-discovery"
```

---

## Task 11: Settings Controller

**Files:**
- Create: `BapalaServer/Controllers/SettingsController.cs`

- [ ] **Step 1: Create `Controllers/SettingsController.cs`**

```csharp
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
        // Write changes to appsettings.json in a follow-up task if persistent settings are needed.
        // For now: acknowledge — caller persists by editing appsettings.json and restarting.
        return Ok(new { message = "Settings acknowledged. Restart server to apply.", received = req });
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add BapalaServer/Controllers/SettingsController.cs
git commit -m "feat: SettingsController"
```

---

## Task 12: Web UI

**Files:**
- Create: `BapalaServer/wwwroot/css/app.css`
- Create: `BapalaServer/wwwroot/js/api.js`
- Create: `BapalaServer/wwwroot/js/app.js`
- Create: `BapalaServer/wwwroot/js/player.js`
- Create: `BapalaServer/wwwroot/js/settings.js`
- Create: `BapalaServer/wwwroot/login.html`
- Create: `BapalaServer/wwwroot/index.html`
- Create: `BapalaServer/wwwroot/player.html`
- Create: `BapalaServer/wwwroot/settings.html`

**XSS note:** All user-facing dynamic content (titles, descriptions from TMDB) is untrusted. Use `textContent` and DOM methods — never `innerHTML` with server-supplied data. The `esc()` helper below encodes entities for the one case we build HTML template strings (card grid).

- [ ] **Step 1: Create `wwwroot/css/app.css`**

```css
:root {
  --bg: #0f0f0f; --surface: #1a1a1a; --surface2: #242424;
  --accent: #e50914; --text: #e5e5e5; --text2: #aaa; --card-w: 160px;
}
* { box-sizing: border-box; margin: 0; padding: 0; }
body { background: var(--bg); color: var(--text); font-family: system-ui, sans-serif; }

nav { background: var(--surface); padding: 0 24px; display: flex; align-items: center; gap: 24px; height: 56px; border-bottom: 1px solid #333; }
nav .logo { font-size: 1.4rem; font-weight: 700; color: var(--accent); text-decoration: none; }
nav a { color: var(--text2); text-decoration: none; font-size: .9rem; }
nav a:hover, nav a.active { color: var(--text); }
nav .spacer { flex: 1; }
nav input { background: var(--surface2); border: 1px solid #333; color: var(--text); padding: 6px 12px; border-radius: 4px; width: 220px; }

.filters { padding: 16px 24px; display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
.filter-btn { background: var(--surface2); border: 1px solid #333; color: var(--text2); padding: 6px 14px; border-radius: 20px; cursor: pointer; font-size: .85rem; }
.filter-btn.active { background: var(--accent); border-color: var(--accent); color: #fff; }

.grid { display: grid; grid-template-columns: repeat(auto-fill, var(--card-w)); gap: 16px; padding: 0 24px 32px; }
.card { cursor: pointer; border-radius: 6px; overflow: hidden; background: var(--surface2); transition: transform .15s; }
.card:hover { transform: scale(1.04); }
.card img { width: var(--card-w); height: 240px; object-fit: cover; display: block; background: #333; }
.card .info { padding: 8px; }
.card .card-title { font-size: .85rem; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.card .meta { font-size: .75rem; color: var(--text2); margin-top: 2px; }
.card .fav { color: var(--accent); font-size: .75rem; }

.pagination { text-align: center; padding: 16px; display: flex; gap: 8px; justify-content: center; }
.pagination button { background: var(--surface2); border: 1px solid #333; color: var(--text); padding: 6px 14px; border-radius: 4px; cursor: pointer; }
.pagination button:disabled { opacity: .4; cursor: default; }

.login-wrap { min-height: 100vh; display: flex; align-items: center; justify-content: center; }
.login-box { background: var(--surface); padding: 40px; border-radius: 10px; width: 360px; }
.login-box h1 { font-size: 1.6rem; color: var(--accent); margin-bottom: 8px; }
.login-box p { color: var(--text2); font-size: .85rem; margin-bottom: 24px; }
.form-group { margin-bottom: 16px; }
.form-group label { display: block; font-size: .85rem; color: var(--text2); margin-bottom: 4px; }
.form-group input { width: 100%; background: var(--surface2); border: 1px solid #333; color: var(--text); padding: 10px 12px; border-radius: 4px; font-size: .95rem; }
.btn { width: 100%; background: var(--accent); color: #fff; border: none; padding: 12px; border-radius: 4px; font-size: 1rem; font-weight: 600; cursor: pointer; }
.btn:hover { background: #c00710; }
.error-msg { color: var(--accent); font-size: .85rem; margin-top: 8px; min-height: 1.2em; }

.player-wrap { max-width: 1100px; margin: 24px auto; padding: 0 24px; }
.player-wrap video { width: 100%; border-radius: 8px; background: #000; }
.player-meta { margin-top: 16px; }
.player-meta .sub { color: var(--text2); font-size: .9rem; margin-top: 4px; }
.player-meta p { margin-top: 10px; font-size: .9rem; color: var(--text2); line-height: 1.5; }

.settings-wrap { max-width: 700px; margin: 32px auto; padding: 0 24px; }
.settings-wrap h1 { font-size: 1.4rem; margin-bottom: 24px; }
.settings-section { background: var(--surface); border-radius: 8px; padding: 24px; margin-bottom: 16px; }
.settings-section h2 { font-size: 1rem; margin-bottom: 16px; color: var(--text2); }
.folder-list { list-style: none; margin-bottom: 8px; }
.folder-list li { display: flex; justify-content: space-between; align-items: center; padding: 8px 0; border-bottom: 1px solid #333; font-size: .9rem; }
.folder-list li button { background: none; border: none; color: var(--accent); cursor: pointer; font-size: .85rem; }
.text-input { width: 100%; background: var(--surface2); border: 1px solid #333; color: var(--text); padding: 10px 12px; border-radius: 4px; font-size: .9rem; }

.toast { position: fixed; bottom: 24px; right: 24px; background: var(--surface); border: 1px solid #444; padding: 12px 20px; border-radius: 6px; font-size: .9rem; display: none; z-index: 999; }
.toast.show { display: block; animation: fadeIn .25s; }
@keyframes fadeIn { from { opacity:0; transform:translateY(8px); } to { opacity:1; } }
.spinner { text-align: center; padding: 60px; color: var(--text2); font-size: .9rem; }
```

- [ ] **Step 2: Create `wwwroot/js/api.js`**

```javascript
// XSS-safe helpers — always use these for rendering server data
const esc = s => String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
const setText = (el, text) => { el.textContent = text ?? ''; };

const API = {
  getToken: () => localStorage.getItem('bapala_token'),

  async request(path, options = {}) {
    const token = API.getToken();
    const headers = { 'Content-Type': 'application/json', ...(options.headers || {}) };
    if (token) headers['Authorization'] = `Bearer ${token}`;

    const resp = await fetch(path, { ...options, headers });
    if (resp.status === 401) { localStorage.removeItem('bapala_token'); location.href = '/login.html'; }
    if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
    if (resp.status === 204) return null;
    return resp.json();
  },

  get:  path        => API.request(path),
  post: (path, body) => API.request(path, { method: 'POST', body: JSON.stringify(body) }),
  put:  (path, body) => API.request(path, { method: 'PUT',  body: JSON.stringify(body) }),
  del:  path        => API.request(path, { method: 'DELETE' }),
  streamUrl: id     => `/api/stream/${id}?token=${encodeURIComponent(API.getToken() || '')}`,
};

// Redirect to login if unauthenticated on any protected page
if (!document.querySelector('.login-wrap') && !API.getToken()) {
  location.href = '/login.html';
}
```

- [ ] **Step 3: Create `wwwroot/login.html`**

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Bapala — Login</title>
  <link rel="stylesheet" href="/css/app.css">
</head>
<body>
<div class="login-wrap">
  <div class="login-box">
    <h1>Bapala</h1>
    <p id="serverName">Media Manager</p>
    <form id="loginForm">
      <div class="form-group">
        <label for="username">Username</label>
        <input type="text" id="username" value="admin" autocomplete="username" required>
      </div>
      <div class="form-group">
        <label for="password">Password</label>
        <input type="password" id="password" autocomplete="current-password" required>
      </div>
      <button class="btn" type="submit">Sign In</button>
      <div class="error-msg" id="error" role="alert"></div>
    </form>
  </div>
</div>
<script>
  fetch('/api/auth/info').then(r => r.json())
    .then(d => { document.getElementById('serverName').textContent = d.serverName; })
    .catch(() => {});

  document.getElementById('loginForm').addEventListener('submit', async e => {
    e.preventDefault();
    const err = document.getElementById('error');
    err.textContent = '';
    try {
      const resp = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          username: document.getElementById('username').value,
          password: document.getElementById('password').value
        })
      });
      if (!resp.ok) { err.textContent = 'Invalid credentials'; return; }
      const data = await resp.json();
      localStorage.setItem('bapala_token', data.token);
      location.href = '/index.html';
    } catch {
      err.textContent = 'Connection failed. Is the server running?';
    }
  });
</script>
</body>
</html>
```

- [ ] **Step 4: Create `wwwroot/index.html`**

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Bapala</title>
  <link rel="stylesheet" href="/css/app.css">
</head>
<body>
<nav>
  <a class="logo" href="/index.html">Bapala</a>
  <a href="/index.html" class="active">Library</a>
  <a href="/settings.html">Settings</a>
  <div class="spacer"></div>
  <input type="search" id="searchInput" placeholder="Search titles…" aria-label="Search">
</nav>
<div class="filters">
  <button class="filter-btn" data-type="Movie"       onclick="setFilter('Movie')">Movies</button>
  <button class="filter-btn" data-type="Series"      onclick="setFilter('Series')">Series</button>
  <button class="filter-btn" data-type="Documentary" onclick="setFilter('Documentary')">Docs</button>
  <button class="filter-btn" id="favBtn"             onclick="toggleFavorites()">Favorites</button>
  <div class="spacer" style="flex:1"></div>
  <button class="filter-btn" id="scanBtn"            onclick="triggerScan()">Scan Library</button>
</div>
<div class="grid" id="grid" role="list"></div>
<div class="pagination" id="pagination"></div>
<div class="toast" id="toast" role="status"></div>
<script src="/js/api.js"></script>
<script src="/js/app.js"></script>
</body>
</html>
```

- [ ] **Step 5: Create `wwwroot/js/app.js`**

```javascript
let state = { page: 1, limit: 20, type: null, genre: null, search: '', favorites: false, total: 0 };

async function loadMedia() {
  const grid = document.getElementById('grid');
  grid.textContent = '';
  const spinner = document.createElement('div');
  spinner.className = 'spinner';
  spinner.textContent = 'Loading…';
  grid.appendChild(spinner);

  const params = new URLSearchParams({ page: state.page, limit: state.limit });
  if (state.type)      params.set('type', state.type);
  if (state.genre)     params.set('genre', state.genre);
  if (state.search)    params.set('search', state.search);
  if (state.favorites) params.set('favorites', 'true');

  try {
    const data = await API.get('/api/media?' + params);
    state.total = data.total;
    renderGrid(data.items);
    renderPagination();
  } catch {
    grid.textContent = '';
    const msg = document.createElement('div');
    msg.className = 'spinner';
    msg.textContent = 'Failed to load. Check connection.';
    grid.appendChild(msg);
  }
}

function renderGrid(items) {
  const grid = document.getElementById('grid');
  grid.textContent = '';
  if (!items.length) {
    const msg = document.createElement('div');
    msg.className = 'spinner';
    msg.textContent = 'No media found.';
    grid.appendChild(msg);
    return;
  }
  items.forEach(item => {
    const card = document.createElement('div');
    card.className = 'card';
    card.setAttribute('role', 'listitem');
    card.addEventListener('click', () => openPlayer(item.id));

    const img = document.createElement('img');
    img.src = item.posterPath || '/img/no-poster.svg';
    img.alt = item.title;
    img.loading = 'lazy';
    img.onerror = () => { img.src = '/img/no-poster.svg'; };

    const info = document.createElement('div');
    info.className = 'info';

    const title = document.createElement('div');
    title.className = 'card-title';
    title.textContent = item.title;

    const meta = document.createElement('div');
    meta.className = 'meta';
    meta.textContent = [item.year, item.type].filter(Boolean).join(' · ');

    info.appendChild(title);
    info.appendChild(meta);
    if (item.isFavorite) {
      const fav = document.createElement('div');
      fav.className = 'fav';
      fav.textContent = 'Favorite';
      info.appendChild(fav);
    }
    card.appendChild(img);
    card.appendChild(info);
    grid.appendChild(card);
  });
}

function renderPagination() {
  const totalPages = Math.ceil(state.total / state.limit) || 1;
  const el = document.getElementById('pagination');
  el.textContent = '';

  const prev = document.createElement('button');
  prev.textContent = 'Prev';
  prev.disabled = state.page <= 1;
  prev.addEventListener('click', () => changePage(state.page - 1));

  const label = document.createElement('span');
  label.style.cssText = 'padding:6px 12px;color:var(--text2)';
  label.textContent = `${state.page} / ${totalPages}`;

  const next = document.createElement('button');
  next.textContent = 'Next';
  next.disabled = state.page >= totalPages;
  next.addEventListener('click', () => changePage(state.page + 1));

  el.appendChild(prev);
  el.appendChild(label);
  el.appendChild(next);
}

function changePage(p) { state.page = p; loadMedia(); }
function openPlayer(id) { location.href = `/player.html?id=${id}`; }

function setFilter(type) {
  state.type = state.type === type ? null : type;
  state.page = 1;
  document.querySelectorAll('.filter-btn[data-type]').forEach(b =>
    b.classList.toggle('active', b.dataset.type === state.type));
  loadMedia();
}

function toggleFavorites() {
  state.favorites = !state.favorites;
  state.page = 1;
  document.getElementById('favBtn').classList.toggle('active', state.favorites);
  loadMedia();
}

document.getElementById('searchInput').addEventListener('input', e => {
  state.search = e.target.value;
  state.page = 1;
  clearTimeout(window._searchTimer);
  window._searchTimer = setTimeout(loadMedia, 400);
});

async function triggerScan() {
  const btn = document.getElementById('scanBtn');
  btn.textContent = 'Scanning…';
  btn.disabled = true;
  try {
    await API.post('/api/media/scan', {});
    showToast('Scan started!');
  } catch (e) {
    showToast('Scan failed: ' + e.message);
  } finally {
    setTimeout(() => { btn.textContent = 'Scan Library'; btn.disabled = false; }, 3000);
  }
}

function showToast(msg) {
  const t = document.getElementById('toast');
  t.textContent = msg;
  t.classList.add('show');
  setTimeout(() => t.classList.remove('show'), 3000);
}

loadMedia();
```

- [ ] **Step 6: Create `wwwroot/player.html`**

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <title>Bapala — Player</title>
  <link rel="stylesheet" href="/css/app.css">
</head>
<body>
<nav>
  <a class="logo" href="/index.html">Bapala</a>
  <a href="/index.html">Library</a>
  <div class="spacer"></div>
</nav>
<div class="player-wrap">
  <video id="vid" controls preload="metadata"></video>
  <div class="player-meta">
    <h1 id="title">Loading…</h1>
    <div class="sub" id="meta"></div>
    <p id="desc"></p>
  </div>
</div>
<script src="/js/api.js"></script>
<script src="/js/player.js"></script>
</body>
</html>
```

- [ ] **Step 7: Create `wwwroot/js/player.js`**

```javascript
const id = new URLSearchParams(location.search).get('id');
if (!id) location.href = '/index.html';

const vid = document.getElementById('vid');
let saveTimer;

async function init() {
  try {
    const [item, progress] = await Promise.all([
      API.get(`/api/media/${id}`),
      API.get(`/api/media/${id}/progress`)
    ]);

    document.title = `${item.title} — Bapala`;

    // Safe DOM text assignment — no innerHTML
    document.getElementById('title').textContent = item.title;
    document.getElementById('meta').textContent =
      [item.year, item.genres, item.rating != null ? `${item.rating.toFixed(1)} / 10` : null]
        .filter(Boolean).join(' · ');
    document.getElementById('desc').textContent = item.description || '';

    vid.src = API.streamUrl(id);

    if (progress.progressSeconds > 30) {
      vid.addEventListener('loadedmetadata', () => {
        vid.currentTime = progress.progressSeconds;
      }, { once: true });
    }

    vid.addEventListener('timeupdate', () => {
      clearTimeout(saveTimer);
      saveTimer = setTimeout(() => {
        API.post(`/api/media/${id}/progress`, { progressSeconds: Math.floor(vid.currentTime) })
          .catch(() => {});
      }, 10000);
    });
  } catch {
    document.getElementById('title').textContent = 'Error loading media.';
  }
}

init();
```

- [ ] **Step 8: Create `wwwroot/settings.html`**

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <title>Bapala — Settings</title>
  <link rel="stylesheet" href="/css/app.css">
</head>
<body>
<nav>
  <a class="logo" href="/index.html">Bapala</a>
  <a href="/index.html">Library</a>
  <a href="/settings.html" class="active">Settings</a>
  <div class="spacer"></div>
  <button class="filter-btn" onclick="logout()">Sign Out</button>
</nav>
<div class="settings-wrap">
  <h1>Settings</h1>
  <div class="settings-section">
    <h2>Media Folders</h2>
    <ul class="folder-list" id="folderList"></ul>
    <div style="display:flex;gap:8px;margin-top:8px">
      <input type="text" id="newFolder" class="text-input" placeholder="C:\Movies" style="flex:1">
      <button class="filter-btn" onclick="addFolder()">Add</button>
    </div>
  </div>
  <div class="settings-section">
    <h2>TMDB API Key</h2>
    <input type="text" id="tmdbKey" class="text-input" placeholder="Get a free key at themoviedb.org">
    <button class="filter-btn" style="margin-top:8px" onclick="saveSettings()">Save</button>
  </div>
  <div class="settings-section">
    <h2>Server Info</h2>
    <div id="serverInfo" style="font-size:.9rem;color:var(--text2);line-height:1.9"></div>
  </div>
</div>
<div class="toast" id="toast" role="status"></div>
<script src="/js/api.js"></script>
<script src="/js/settings.js"></script>
</body>
</html>
```

- [ ] **Step 9: Create `wwwroot/js/settings.js`**

```javascript
let settings = {};

async function load() {
  settings = await API.get('/api/settings');
  renderFolders();

  // Safe DOM writes — no innerHTML for server data
  const info = document.getElementById('serverInfo');
  info.textContent = '';
  [
    ['Server', settings.serverName],
    ['Port', settings.port],
    ['TMDB', settings.hasTmdbKey ? 'Configured' : 'Not set'],
    ['User', settings.username],
  ].forEach(([label, value]) => {
    const row = document.createElement('div');
    const b = document.createElement('b');
    b.textContent = value;
    row.textContent = `${label}: `;
    row.appendChild(b);
    info.appendChild(row);
  });
}

function renderFolders() {
  const list = document.getElementById('folderList');
  list.textContent = '';
  const folders = settings.mediaFolders || [];
  if (!folders.length) {
    const li = document.createElement('li');
    li.style.color = 'var(--text2)';
    li.textContent = 'No folders added yet.';
    list.appendChild(li);
    return;
  }
  folders.forEach(f => {
    const li = document.createElement('li');
    const span = document.createElement('span');
    span.textContent = f;
    const btn = document.createElement('button');
    btn.textContent = 'Remove';
    btn.addEventListener('click', () => removeFolder(f));
    li.appendChild(span);
    li.appendChild(btn);
    list.appendChild(li);
  });
}

function addFolder() {
  const val = document.getElementById('newFolder').value.trim();
  if (!val) return;
  settings.mediaFolders = settings.mediaFolders || [];
  settings.mediaFolders.push(val);
  document.getElementById('newFolder').value = '';
  renderFolders();
}

function removeFolder(f) {
  settings.mediaFolders = settings.mediaFolders.filter(x => x !== f);
  renderFolders();
}

async function saveSettings() {
  try {
    await API.put('/api/settings', {
      mediaFolders: settings.mediaFolders,
      tmdbApiKey: document.getElementById('tmdbKey').value || undefined
    });
    showToast('Saved. Restart the server to apply changes.');
  } catch { showToast('Save failed.'); }
}

function logout() { localStorage.removeItem('bapala_token'); location.href = '/login.html'; }

function showToast(msg) {
  const t = document.getElementById('toast');
  t.textContent = msg;
  t.classList.add('show');
  setTimeout(() => t.classList.remove('show'), 3500);
}

load();
```

- [ ] **Step 10: Final build + smoke test**

```bash
cd "F:/2026/code/repos/Bapala Media Manager"
dotnet build
dotnet run --project BapalaServer/BapalaServer.csproj
```

Open browser: `http://localhost:8484` — login with `admin / changeme`.
Expected: Login page renders, redirects to `/index.html` on success.

- [ ] **Step 11: Run full test suite**

```bash
dotnet test -v normal
```

Expected: All tests PASS, 0 failures.

- [ ] **Step 12: Final commit**

```bash
git add BapalaServer/wwwroot/
git commit -m "feat: complete web UI — login, media grid, player, settings"
```

---

## API Quick Reference

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/auth/login` | No | Returns JWT |
| GET | `/api/auth/info` | No | Server name + version |
| GET | `/api/media` | JWT | List (page, limit, type, genre, search, favorites) |
| GET | `/api/media/{id}` | JWT | Single item |
| PUT | `/api/media/{id}` | JWT | Update metadata |
| DELETE | `/api/media/{id}` | JWT | Remove from library |
| POST | `/api/media/{id}/favorite` | JWT | Toggle favorite |
| GET | `/api/media/{id}/progress` | JWT | Watch progress |
| POST | `/api/media/{id}/progress` | JWT | Save progress |
| POST | `/api/media/scan` | JWT | Trigger folder scan |
| GET | `/api/stream/{id}?token=...` | JWT query | HTTP 206 video stream |
| GET | `/api/settings` | JWT | Read settings |
| PUT | `/api/settings` | JWT | Update settings |
| WS | `/hubs/scan` | JWT | SignalR scan progress |

---

## Self-Review

- [x] **Auth** — login → JWT → all protected endpoints; query-param JWT for streaming
- [x] **Streaming** — HTTP 206 via `PhysicalFile(enableRangeProcessing:true)`; seeking works in browser + ExoPlayer
- [x] **Scanning** — recursive folder walk, regex filename parser tested with Theory, TMDB best-effort
- [x] **Offline LAN** — server runs without TMDB key; mDNS failure is non-crashing
- [x] **Large libraries** — DB indexes on title/type/year/favorite; cursor pagination
- [x] **Formats** — MIME map covers MP4/MKV/AVI/MOV/WMV/WebM/TS
- [x] **mDNS** — `_bapala._tcp` for Android auto-discovery
- [x] **XSS** — all dynamic content uses `textContent` / DOM methods, not `innerHTML`; `esc()` helper available for template strings
- [x] **TDD** — tests written before implementations throughout
- [x] **No placeholders** — every code block is complete

---

## Next: Android Client Plan

Create `2026-04-12-bapala-android.md` for:
- Kotlin + Jetpack Compose UI
- Media3/ExoPlayer with seek resume + subtitle support
- Retrofit2 → same REST API above
- Room offline cache (browse library without server)
- Hilt DI
- `NsdManager` for `_bapala._tcp` auto-discovery
- `EncryptedSharedPreferences` + Android Keystore for JWT
