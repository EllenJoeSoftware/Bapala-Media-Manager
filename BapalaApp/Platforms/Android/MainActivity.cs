using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace BapalaApp;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges =
        ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Always show focus highlights — critical for TV remote navigation.
        // On touch-only devices Android hides focus rings after first touch;
        // TV remotes never trigger touch events so the ring never appears unless we force it.
        Window?.DecorView?.SetFocusable(ViewFocusability.Focusable);

#pragma warning disable CA1422
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            Window?.DecorView?.SetDefaultFocusHighlightEnabled(true);
#pragma warning restore CA1422
    }

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        // Forward D-pad / media keys so MAUI can handle navigation and playback
        return base.OnKeyDown(keyCode, e);
    }
}
