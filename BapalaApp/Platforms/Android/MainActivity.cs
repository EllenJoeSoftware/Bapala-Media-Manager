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

        // Force focus to always be visible — essential for TV D-pad navigation.
        // On touch devices Android suppresses focus rings after first touch event,
        // but TV remotes never fire touch events so without this the ring never shows.
        // SetDefaultFocusHighlightEnabled is not in the MAUI bindings so we call it
        // via JNI on the underlying Java View object.
        var decorView = Window?.DecorView;
        if (decorView != null)
        {
            decorView.SetFocusable(ViewFocusability.Focusable);

            // Call the Java method directly via JNI — available from API 26+
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                try
                {
                    var method = decorView.Class?.GetDeclaredMethod(
                        "setDefaultFocusHighlightEnabled", Java.Lang.Boolean.Type!);
                    method?.Invoke(decorView, Java.Lang.Boolean.True);
                }
                catch
                {
                    // Non-fatal — XAML VisualStates still provide focus highlight
                }
            }
        }
    }

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        return base.OnKeyDown(keyCode, e);
    }
}
