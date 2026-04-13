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
/// </summary>
public class BapalaApiService
{
    // ── JSON options — must match the server's JsonStringEnumConverter setup ──
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;

    public string? ServerUrl { get; private set; }
    public bool IsAuthenticated { get; private set; }

    public BapalaApiService()
    {
        _http = new HttpClient(new HttpClientHandler
        {
            // Allow self-signed certs on the local network (common for home servers)
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });
        _http.Timeout = TimeSpan.FromSeconds(15);

        // Restore saved server URL from previous session
        ServerUrl = Preferences.Get("bapala_server_url", string.Empty);

        // Restore JWT token — SecureStorage is async but we need it synchronously here.
        // In production, prefer an async initialisation method; this is safe for startup.
        var token = SecureStorage.GetAsync("bapala_jwt").GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            IsAuthenticated = true;
        }
    }

    // ── Authentication ────────────────────────────────────────────────────────

    /// <summary>Attempts login; saves credentials on success.</summary>
    public async Task<(bool Success, string? Error, string? ServerName)> LoginAsync(
        string serverUrl, string username, string password)
    {
        var url = serverUrl.TrimEnd('/');

        try
        {
            // Temporarily clear auth header for the login request itself
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

            // Persist
            ServerUrl = url;
            Preferences.Set("bapala_server_url", url);
            await SecureStorage.SetAsync("bapala_jwt", data.Token);

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
        if (type != null)                         qs += $"&type={type}";
        if (!string.IsNullOrWhiteSpace(search))   qs += $"&search={Uri.EscapeDataString(search)}";
        if (favorites)                             qs += "&favorites=true";

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
        catch { /* progress save is best-effort — never crash the player */ }
    }

    // ── Streaming URL ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a URL the MediaElement can load directly.
    /// JWT is embedded as ?token= because HTML video / MAUI MediaElement cannot set headers.
    /// </summary>
    public string GetStreamUrl(int id)
    {
        var token = SecureStorage.GetAsync("bapala_jwt").GetAwaiter().GetResult() ?? "";
        return $"{ServerUrl}/api/stream/{id}?token={Uri.EscapeDataString(token)}";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync();
        throw new HttpRequestException($"HTTP {(int)resp.StatusCode}: {body}");
    }
}
