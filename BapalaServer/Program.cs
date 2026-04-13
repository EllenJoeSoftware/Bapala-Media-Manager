using Scalar.AspNetCore;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Models;
using BapalaServer.Data;
using BapalaServer.Repositories;
using BapalaServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Database
var dbRelative = builder.Configuration.GetConnectionString("Default") ?? "Data Source=bapala.db";
var dbFileName = dbRelative.Replace("Data Source=", "").Trim();
if (!Path.IsPathRooted(dbFileName))
    dbFileName = Path.Combine(builder.Environment.ContentRootPath, dbFileName);
builder.Services.AddDbContext<BapalaDbContext>(opt =>
    opt.UseSqlite($"Data Source={dbFileName}"));

// Auth
var jwtSecret = builder.Configuration["Jwt:Secret"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer   = true,
            ValidIssuer      = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience    = builder.Configuration["Jwt:Audience"],
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

// Services
builder.Services.AddScoped<IMediaRepository, SqliteMediaRepository>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IMediaScannerService, MediaScannerService>();
builder.Services.AddScoped<ITmdbService, TmdbService>();
builder.Services.AddSingleton<MdnsService>();
builder.Services.AddSingleton<IMdnsService>(p => p.GetRequiredService<MdnsService>());
builder.Services.AddHostedService(p => p.GetRequiredService<MdnsService>());

// ASP.NET Core
builder.Services.AddControllers()
    .AddJsonOptions(opt =>
        opt.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSignalR();
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddHttpClient();

// OpenAPI / Scalar
builder.Services.AddOpenApi(opt =>
{
    // Document info
    opt.AddDocumentTransformer((doc, ctx, ct) =>
    {
        doc.Info = new OpenApiInfo
        {
            Title       = "Bapala Media Server API",
            Version     = "v1",
            Description = "REST API for the Bapala home media server. " +
                          "All /api/media and /api/stream endpoints require a Bearer JWT. " +
                          "Obtain a token via POST /api/auth/login."
        };
        return Task.CompletedTask;
    });

    // Bearer JWT security scheme
    opt.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});

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
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var ext = Path.GetExtension(ctx.File.Name).ToLowerInvariant();
        if (ext is ".js" or ".css" or ".html")
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache";
    }
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<BapalaServer.Hubs.ScanProgressHub>("/hubs/scan");

// OpenAPI JSON  →  /openapi/v1.json
app.MapOpenApi();

// Scalar UI  →  /scalar/v1
app.MapScalarApiReference(opt =>
{
    opt.Title = "Bapala API";
    opt.Theme = ScalarTheme.DeepSpace;
    opt.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
    opt.Authentication = new ScalarAuthenticationOptions
    {
        PreferredSecurityScheme = "Bearer"
    };
});

app.Run();

public partial class Program { }  // Required for WebApplicationFactory in integration tests

// ── Bearer security scheme transformer ───────────────────────────────────────
// Follows the pattern from Microsoft docs: uses IAuthenticationSchemeProvider
// to detect that JwtBearer is registered, then adds the scheme to the document.
internal sealed class BearerSecuritySchemeTransformer(
    Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider authSchemeProvider)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var schemes = await authSchemeProvider.GetAllSchemesAsync();
        if (schemes.Any(s => s.Name == JwtBearerDefaults.AuthenticationScheme))
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type         = SecuritySchemeType.Http,
                Scheme       = "bearer",
                BearerFormat = "JWT",
                Description  = "Enter your JWT (without the 'Bearer ' prefix). Get one from POST /api/auth/login."
            };
        }
    }
}
