using BapalaApp.Services;

namespace BapalaApp;

public partial class App : Application
{
    private readonly BapalaApiService _api;

    public App(BapalaApiService api)
    {
        _api = api;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Show a temporary splash while InitAsync runs, then swap to the real root.
        var loading = new ContentPage
        {
            BackgroundColor = Color.FromArgb("#0f0f0f"),
            Content = new ActivityIndicator
            {
                IsRunning = true,
                Color = Color.FromArgb("#e50914"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
            }
        };

        var window = new Window(loading);

        // InitAsync loads the JWT from SecureStorage asynchronously — safe on all platforms.
        // Wrap everything in try-catch: unhandled exceptions in Task.Run are silently
        // swallowed, causing the app to hang until the OS kills it. We surface them instead.
        Task.Run(async () =>
        {
            try
            {
                await _api.InitAsync();

                Application.Current!.Dispatcher.Dispatch(() =>
                {
                    try
                    {
                        window.Page = _api.IsAuthenticated
                            ? new AppShell()
                            : new NavigationPage(new Views.LoginPage(
                                  new ViewModels.LoginViewModel(_api)));
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

    /// <summary>
    /// Shows the full exception on a dark page so startup crashes are always visible
    /// on the device — far better than a silent hang.
    /// </summary>
    private static ContentPage BuildErrorPage(Exception ex, string context) => new()
    {
        BackgroundColor = Color.FromArgb("#0f0f0f"),
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing     = 12,
                Padding     = new Thickness(24),
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text             = $"⚠ {context}",
                        FontSize         = 18,
                        FontAttributes   = FontAttributes.Bold,
                        TextColor        = Color.FromArgb("#e50914"),
                        HorizontalOptions = LayoutOptions.Center,
                    },
                    new Label
                    {
                        Text          = ex.ToString(),
                        FontSize      = 11,
                        TextColor     = Colors.White,
                        LineBreakMode = LineBreakMode.WordWrap,
                    }
                }
            }
        }
    };
}
