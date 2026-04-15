using CommunityToolkit.Maui.Core.Primitives;
using CommunityToolkit.Maui.Views;
using BapalaApp.ViewModels;

namespace BapalaApp.Views;

public partial class PlayerPage : ContentPage
{
    private readonly PlayerViewModel _vm;
    private bool _surfaceReady;   // true once ExoPlayer's Android surface is attached
    private bool _urlPending;     // true if a URL arrived before the surface was ready

    public PlayerPage(PlayerViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.StartSaveTimer();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _vm.StopSaveTimer();
        _vm.OnPlaybackStopped();
        _vm.PropertyChanged -= OnViewModelPropertyChanged;

        // Stop and release the MediaElement to free the decoder hardware
        mediaElement.Stop();
        mediaElement.Handler?.DisconnectHandler();
    }

    // ── Resume at saved position ──────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.ResumePosition) && _vm.ResumePosition > 30)
        {
            // Seek after a short delay so the media element has had time to load
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
            {
                mediaElement.SeekTo(TimeSpan.FromSeconds(_vm.ResumePosition));
            });
        }

        // When the StreamUrl arrives from the async API call, either load it
        // immediately (surface already ready) or set a pending flag so
        // OnMediaHandlerChanged picks it up when the surface attaches.
        if (e.PropertyName == nameof(PlayerViewModel.StreamUrl) &&
            !string.IsNullOrEmpty(_vm.StreamUrl))
        {
            if (_surfaceReady)
                LoadAndPlay(_vm.StreamUrl);
            else
                _urlPending = true;
        }
    }

    // ── MediaElement event handlers ───────────────────────────────────────────

    /// <summary>
    /// Fires when ExoPlayer's native Android handler (SurfaceTexture) is attached
    /// to the view. This is the earliest safe moment to set a Source on Android —
    /// setting it before this point gives ExoPlayer a null surface which results
    /// in audio-only playback with a white video frame.
    /// </summary>
    private void OnMediaHandlerChanged(object? sender, EventArgs e)
    {
        if (mediaElement.Handler == null) return;   // handler detaching — ignore

        _surfaceReady = true;

        if (_urlPending && !string.IsNullOrEmpty(_vm.StreamUrl))
        {
            _urlPending = false;
            LoadAndPlay(_vm.StreamUrl);
        }
    }

    /// <summary>
    /// Sets the MediaElement Source and starts playback.
    /// Always called after the surface is confirmed ready.
    /// </summary>
    private void LoadAndPlay(string url)
    {
        // Small delay gives ExoPlayer one more layout pass to fully bind
        // the SurfaceTexture on slower/older devices before we hand it the URL.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () =>
        {
            mediaElement.Source = MediaSource.FromUri(url);
            mediaElement.Play();
        });
    }

    private void OnPositionChanged(object? sender, MediaPositionChangedEventArgs e)
    {
        _vm.OnPositionChanged(
            e.Position.TotalSeconds,
            mediaElement.Duration.TotalSeconds);
    }

    private void OnMediaEnded(object? sender, EventArgs e) => _vm.OnPlaybackStopped();

    /// <summary>
    /// Called by ExoPlayer when the stream cannot be opened or decoded.
    /// Surfaces the error as visible text so the user knows why the screen is blank.
    /// Common causes: 401 Unauthorized (token expired), unreachable server URL,
    /// unsupported video codec, or a network drop mid-stream.
    /// </summary>
    private void OnMediaFailed(object? sender, MediaFailedEventArgs e)
    {
        _vm.SetStreamError($"Stream error: {e.ErrorMessage}");
    }

    /// <summary>
    /// Tracks ExoPlayer state transitions for debugging.
    /// On MIUI and some custom ROMs, the state can briefly enter
    /// 'Stopped' right after source is set — we nudge Play() in that case.
    /// </summary>
    private void OnMediaStateChanged(object? sender, MediaStateChangedEventArgs e)
    {
        // If ExoPlayer enters Stopped state immediately after receiving a source
        // (happens on some MIUI builds), kick it into playing mode.
        if (e.NewState == MediaElementState.Stopped &&
            !string.IsNullOrEmpty(_vm.StreamUrl) &&
            _vm.StreamError == null)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), () =>
                mediaElement.Play());
        }
    }
}
