using Android.App;
using Android.Runtime;

namespace BapalaApp;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
        // Catch ALL unhandled exceptions — even those that occur before MAUI is
        // fully initialised (e.g. font loading, resource parsing).  We write to a
        // plain-text file in the app's private directory; App.xaml.cs reads it on
        // the next launch and shows a visible error screen.
        AppDomain.CurrentDomain.UnhandledException   += OnDomainException;
        TaskScheduler.UnobservedTaskException        += OnUnobservedTaskException;
        AndroidEnvironment.UnhandledExceptionRaiser  += OnAndroidException;
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // ── Exception sinks ───────────────────────────────────────────────────────

    private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        => WriteCrash("AppDomain", e.ExceptionObject?.ToString());

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();   // prevent process termination so we can write the log
        WriteCrash("UnobservedTask", e.Exception?.ToString());
    }

    private static void OnAndroidException(object? sender, RaiseThrowableEventArgs e)
    {
        e.Handled = true;  // prevent default Android crash dialog; we show our own
        WriteCrash("AndroidRuntime", e.Exception?.ToString());
    }

    // ── Crash file helpers ────────────────────────────────────────────────────

    private static string CrashFilePath =>
        Path.Combine(
            Android.App.Application.Context.FilesDir!.AbsolutePath,
            "bapala_crash.txt");

    internal static string? ReadAndClearCrash()
    {
        try
        {
            var path = CrashFilePath;
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path);
            File.Delete(path);
            return text;
        }
        catch { return null; }
    }

    private static void WriteCrash(string source, string? message)
    {
        try
        {
            File.WriteAllText(
                CrashFilePath,
                $"[{DateTime.Now:u}] {source}\n\n{message ?? "null"}");
        }
        catch { /* last-ditch; if the filesystem is gone we can't help */ }
    }
}
