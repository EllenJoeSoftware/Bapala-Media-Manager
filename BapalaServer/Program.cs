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
        // Allow JWT in query string for HTML <video> elements (can't set headers)
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

// Services (stubs for later tasks — will be fully implemented in Tasks 5-10)
builder.Services.AddScoped<IMediaRepository, SqliteMediaRepository>();
builder.Services.AddScoped<IJwtService, JwtService>();

// ASP.NET Core
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddHttpClient();

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

app.Run();

public partial class Program { }  // Required for WebApplicationFactory in integration tests
