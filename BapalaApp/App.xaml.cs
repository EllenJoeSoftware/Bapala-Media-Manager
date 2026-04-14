using BapalaApp.Services;
using BapalaApp.Views;

namespace BapalaApp;

public partial class App : Application
{
    private readonly BapalaApiService _api;
    private readonly IServiceProvider _services;

    public App(BapalaApiService api, IServiceProvider services)
    {
        _api      = api;
        _services = services;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var loading = new ContentPage
        {
            BackgroundColor = Color.FromArgb("#0f0f0f"),
            Content = new ActivityIndicator
            {
                IsRunning = true,
                Color     = Color.FromArgb("#e50914"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
            }
        };

        var window = new Window(loading);

        Task.Run(async () =>
        {
            try
            {
                // ── 1. Check for a crash log written by the previous session ──────
                //    MainApplication.cs catches all unhandled exceptions and writes
                //    them to a file.  We display them on first launch after a crash.
#if ANDROID
                var crashLog = MainApplication.ReadAndClearCrash();
#else
                string? crashLog = null;
#endif

                // ── 2. Async init (SecureStorage is async-only on Android) ────────
                await _api.InitAsync();

                // ── 3. Choose the first page ──────────────────────────────────────
                Application.Current!.Dispatcher.Dispatch(() =>
                {
                    try
                    {
                        // If a crash was recorded, show it first regardless of auth state.
                        if (crashLog != null)
                        {
                            window.Page = BuildCrashPage(crashLog);
                            return;
                        }

                        // Resolve pages through DI so Shell/MAUI never calls
                        // Activator.CreateInstance on types that need constructor injection.
                        window.Page = _api.IsAuthenticated
                            ? _services.GetRequiredService<AppShell>()
                            : new NavigationPage(_services.GetRequiredService<LoginPage>());
                    }
                    catch (Exception dispatchEx)
                    {
                        window.Page = BuildErrorPage(dispatchEx, "Page creation failed");
                    }
                });
            }
            catch (Exception startupEx)
            {
                Application.Current!.Dispatcher.Dispatch(() =>
                    window.Page = BuildErrorPage(startupEx, "Startup failed"));
            }
        });

        return window;
    }

    // ── Error / crash display pages ───────────────────────────────────────────
    // These are plain C# so they cannot fail due to XAML parsing issues.

    private static ContentPage BuildCrashPage(string crashLog) =>
        BuildColoredPage(
            "💥 Previous crash log",
            $"The app crashed last time. Crash details:\n\n{crashLog}",
            Color.FromArgb("#5a0000"));

    private static ContentPage BuildErrorPage(Exception ex, string context) =>
        BuildColoredPage($"⚠ {context}", ex.ToString(), Color.FromArgb("#0f0f0f"));

    private static ContentPage BuildColoredPage(string heading, string body, Color bg) => new()
    {
        BackgroundColor = bg,
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing  = 12,
                Padding  = new Thickness(24),
                VerticalOptions = LayoutOptions.Start,
                Children =
                {
                    new Label
                    {
                        Text           = heading,
                        FontSize       = 18,
                        FontAttributes = FontAttributes.Bold,
                        TextColor      = Color.FromArgb("#e50914"),
                        HorizontalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0, 48, 0, 0),
                    },
                    new Label
                    {
                        Text          = body,
                        FontSize      = 11,
                        TextColor     = Colors.White,
                        LineBreakMode = LineBreakMode.WordWrap,
                    }
                }
            }
        }
    };
}
