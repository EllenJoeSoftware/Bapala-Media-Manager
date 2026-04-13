using CommunityToolkit.Maui;
using BapalaApp.Services;
using BapalaApp.ViewModels;
using BapalaApp.Views;

namespace BapalaApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitMediaElement()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // ── Services ──────────────────────────────────────────────────────────
        // Singleton: one HttpClient, one token store for the whole app lifetime
        builder.Services.AddSingleton<BapalaApiService>();

        // ── ViewModels ────────────────────────────────────────────────────────
        // Transient so each navigation to a page gets fresh VM state
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LibraryViewModel>();
        builder.Services.AddTransient<PlayerViewModel>();

        // ── Views ─────────────────────────────────────────────────────────────
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<LibraryPage>();
        builder.Services.AddTransient<PlayerPage>();

        return builder.Build();
    }
}
