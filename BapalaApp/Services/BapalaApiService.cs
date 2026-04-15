using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BapalaApp.Models;

namespace BapalaApp.Services;

/// <summary>
/// Single entry-point for all communication with the Bapala server REST API.
/// Persists the server URL in Preferences (plain text) and the JWT token in
/// SecureStorage (Android Keystore / iOS Keychain).
///
/// IMPORTANT: SecureStorage is async-only on Android — never call
/// .GetAwaiter().GetResult() on the main thread; it deadlocks because Android's
/// Looper needs to be free to handle the Keystore callback.
/// We solve this with a two-phase init: the constructor sets up the HttpClient
/// without touching SecureStorage; InitAsync() is called once from App.xaml.cs
/// before the first page is shown.
/// </summary>
public class BapalaApiService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;
    private string? _cachedToken;   // kept in memory after InitAsync so GetStreamUrl never blocks

    public string? ServerUrl { get; private set; }
    public bool IsAuthenticated { get; private set; }

    /// <summary>
    /// Exposes the in-memory JWT so callers like SignalR can authenticate
    /// without touching SecureStorage from a background thread.
    /// </summary>
    public string? CachedToken => _cachedToken;

    public BapalaApiService()
    {
        _http = new HttpClient(new HttpClientHandler
        {
            // Allow self-signed / HTTP on the local network
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });
        _http.Timeout = TimeSpan.FromSeconds(15);

        // Preferences is synchronous and safe to call here
        ServerUrl = Preferences.Get("bapala_server_url", string.Empty);
    }

    /// <summary>
    /// Must be awaited once during app startup (in App.xaml.cs CreateWindow) before
    /// IsAuthenticated is read. Loads the JWT from SecureStorage asynchronously.
    /// </summary>
    public async Task InitAsync()
    {
        try
        {
            _cachedToken = await SecureStorage.GetAsync("bapala_jwt");
        }
        catch
        {
            // SecureStorage can throw on Android if the Keystore is unavailable
            // (e.g. after a device PIN change). Treat as logged-out.
            _cachedToken = null;
        }

        if (!string.IsNullOrEmpty(_cachedToken))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _cachedToken);
            IsAuthenticated = true;
        }
    }

    // ── Authentication ────────────────────────────────────────────────────────

    public async Task<(bool Success, string? Error, string? ServerName)> LoginAsync(
        string serverUrl, string username, string password)
    {
        var url = serverUrl.TrimEnd('/');

        try
        {
            _http.DefaultRequestHeaders.Authorization = null;

            var resp = await _http.PostAsJsonAsync(
                $"{url}/api/auth/login",
                new { username, password },
                JsonOpts);

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return (false, "Invalid username or password.", null);

            resp.EnsureSuccessStatusCode();

            var data = await resp.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);
            if (data == null) return (false, "Server returned an empty response.", null);

            ServerUrl    = url;
            _cachedToken = data.Token;
            Preferences.Set("bapala_server_url", url);
            // SecureStorage can throw on Android if the KeyStore is unavailable
            // (e.g. no device lock screen configured). Catch so a storage failure
            // does not abort a login that otherwise succeeded — the token is still
            // cached in memory for this session; the user will need to log in again
            // next launch.
            try { await SecureStorage.SetAsync("bapala_jwt", data.Token); }
            catch { /* best-effort persistence — session token still works */ }

            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", data.Token);
            IsAuthenticated = true;

            return (true, null, data.ServerName);
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Cannot reach server: {ex.Message}", null);
        }
        catch (TaskCanceledException)
        {
            return (false, "Connection timed out. Check the server URL.", null);
        }
    }

    public void Logout()
    {
        _http.DefaultRequestHeaders.Authorization = null;
        _cachedToken  = null;
        IsAuthenticated = false;
        SecureStorage.Remove("bapala_jwt");
    }

    // ── Media library ─────────────────────────────────────────────────────────

    public async Task<MediaListResult> GetMediaAsync(
        int page = 1, int limit = 20,
        string? type = null, string? search = null,
        bool favorites = false,
        string sortBy = "dateAdded", bool sortDesc = true)
    {
        var qs = $"page={page}&limit={limit}&sortBy={sortBy}&sortDesc={sortDesc}";
        if (type != null)                       qs += $"&type={type}";
        if (!string.IsNullOrWhiteSpace(search)) qs += $"&search={Uri.EscapeDataString(search)}";
        if (favorites)                          qs += "&favorites=true";

        var resp = await _http.GetAsync($"{ServerUrl}/api/media?{qs}");
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<MediaListResult>(JsonOpts))!;
    }

    public async Task<MediaItem?> GetMediaByIdAsync(int id)
    {
        var resp = await _http.GetAsync($"{ServerUrl}/api/media/{id}");
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<MediaItem>(JsonOpts);
    }

    public async Task<MediaItem> UpdateMediaAsync(int id, UpdateMediaRequest req)
    {
        var resp = await _http.PutAsJsonAsync($"{ServerUrl}/api/media/{id}", req, JsonOpts);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<MediaItem>(JsonOpts))!;
    }

    public async Task DeleteMediaAsync(int id)
    {
        var resp = await _http.DeleteAsync($"{ServerUrl}/api/media/{id}");
        await EnsureSuccessAsync(resp);
    }

    public async Task<bool> ToggleFavoriteAsync(int id)
    {
        var resp = await _http.PostAsync($"{ServerUrl}/api/media/{id}/favorite",
            new StringContent(""));
        await EnsureSuccessAsync(resp);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        return json.GetProperty("isFavorite").GetBoolean();
    }

    // ── Watch progress ────────────────────────────────────────────────────────

    public async Task<long> GetProgressAsync(int id)
    {
        try
        {
            var resp = await _http.GetAsync($"{ServerUrl}/api/media/{id}/progress");
            if (!resp.IsSuccessStatusCode) return 0;
            var data = await resp.Content.ReadFromJsonAsync<WatchProgressResponse>(JsonOpts);
            return data?.ProgressSeconds ?? 0;
        }
        catch { return 0; }
    }

    public async Task SaveProgressAsync(int id, long progressSeconds)
    {
        try
        {
            await _http.PostAsJsonAsync(
                $"{ServerUrl}/api/media/{id}/progress",
                new { progressSeconds },
                JsonOpts);
        }
        catch { /* best-effort — never crash the player */ }
    }

    // ── TMDB metadata ────────────────────────────────────────────────────────

    /// <summary>Refresh TMDB metadata for a single item.</summary>
    public async Task<(bool Success, string Message)> RefreshTmdbAsync(int id)
    {
        var resp = await _http.PostAsync($"{ServerUrl}/api/media/{id}/refresh-tmdb",
            new StringContent(""));
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var success = json.TryGetProperty("success", out var s) && s.GetBoolean();
        var message = json.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        return (success, message);
    }

    /// <summary>
    /// Kick off a background bulk TMDB refresh on the server.
    /// Progress is delivered via SignalR (handled by LibraryViewModel).
    /// </summary>
    /// <param name="force">When true, re-fetches even items that already have full metadata.</param>
    public async Task<bool> RefreshTmdbAllAsync(bool force = false)
    {
        try
        {
            var resp = await _http.PostAsync(
                $"{ServerUrl}/api/media/refresh-tmdb-all?force={force}",
                new StringContent(""));
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── Continue watching ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns items the user has started but not finished, ordered by most-recently-watched.
    /// Returns an empty list on any error so the UI degrades gracefully.
    /// </summary>
    public async Task<List<ContinueWatchingItem>> GetContinueWatchingAsync(int limit = 20)
    {
        try
        {
            var resp = await _http.GetAsync($"{ServerUrl}/api/media/continue-watching?limit={limit}");
            if (!resp.IsSuccessStatusCode) return [];
            return (await resp.Content.ReadFromJsonAsync<List<ContinueWatchingItem>>(JsonOpts)) ?? [];
        }
        catch { return []; }
    }

    /// <summary>Returns aggregate library statistics.</summary>
    public async Task<LibraryStats?> GetStatsAsync()
    {
        try
        {
            var resp = await _http.GetAsync($"{ServerUrl}/api/media/stats");
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<LibraryStats>(JsonOpts);
        }
        catch { return null; }
    }

    // ── Streaming URL ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a streaming URL with the JWT embedded as ?token=.
    /// Uses the in-memory cached token — never touches SecureStorage here,
    /// so this is safe to call on any thread at any time.
    /// </summary>
    public string GetStreamUrl(int id) =>
        $"{ServerUrl}/api/stream/{id}?token={Uri.EscapeDataString(_cachedToken ?? "")}";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync();
        throw new HttpRequestException($"HTTP {(int)resp.StatusCode}: {body}");
    }
}
