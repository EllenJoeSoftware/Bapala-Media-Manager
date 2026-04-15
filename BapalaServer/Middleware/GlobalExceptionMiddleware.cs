using System.Net;
using System.Text.Json;

namespace BapalaServer.Middleware;

/// <summary>
/// Catches all unhandled exceptions and returns a consistent JSON error
/// response instead of an HTML stack trace.
///
/// Registration (Program.cs):
///   app.UseMiddleware&lt;GlobalExceptionMiddleware&gt;();
///   // Place BEFORE app.UseAuthentication() and app.MapControllers()
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    // Reusable JSON serialiser options (camelCase, no cycles)
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment env)
    {
        _next   = next;
        _logger = logger;
        _env    = env;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected — not an error, suppress logging
            ctx.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(ctx, ex);
        }
    }

    // ── Exception → HTTP response mapper ─────────────────────────────────────

    private async Task HandleExceptionAsync(HttpContext ctx, Exception ex)
    {
        var (statusCode, errorCode, userMessage) = ex switch
        {
            ArgumentNullException      => (400, "BAD_REQUEST",         "A required value was missing."),
            ArgumentException          => (400, "BAD_REQUEST",         ex.Message),
            UnauthorizedAccessException=> (401, "UNAUTHORIZED",        "Authentication required."),
            KeyNotFoundException       => (404, "NOT_FOUND",           "The requested resource was not found."),
            NotSupportedException      => (422, "UNPROCESSABLE",       ex.Message),
            TimeoutException           => (504, "GATEWAY_TIMEOUT",     "The operation timed out."),
            HttpRequestException       => (502, "UPSTREAM_ERROR",      "An upstream service error occurred."),
            _                          => (500, "INTERNAL_ERROR",      "An unexpected error occurred. Please try again.")
        };

        // Always log server errors; only log client errors in debug
        if (statusCode >= 500)
            _logger.LogError(ex, "Unhandled exception [{ErrorCode}] on {Method} {Path}",
                errorCode, ctx.Request.Method, ctx.Request.Path);
        else
            _logger.LogWarning("Client error [{Status}] [{ErrorCode}] on {Method} {Path}: {Message}",
                statusCode, errorCode, ctx.Request.Method, ctx.Request.Path, ex.Message);

        ctx.Response.StatusCode  = statusCode;
        ctx.Response.ContentType = "application/json";

        var payload = new ErrorResponse(
            StatusCode: statusCode,
            ErrorCode:  errorCode,
            Message:    userMessage,
            // Only expose the full stack trace in Development
            Detail: _env.IsDevelopment() ? ex.ToString() : null,
            TraceId: ctx.TraceIdentifier
        );

        var json = JsonSerializer.Serialize(payload, _jsonOpts);
        await ctx.Response.WriteAsync(json);
    }

    // ── Response shape ────────────────────────────────────────────────────────

    private record ErrorResponse(
        int    StatusCode,
        string ErrorCode,
        string Message,
        string? Detail,
        string? TraceId
    );
}
