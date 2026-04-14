using CommunityToolkit.Maui.Core.Primitives;
using CommunityToolkit.Maui.Views;
using BapalaApp.ViewModels;

namespace BapalaApp.Views;

public partial class PlayerPage : ContentPage
{
    private readonly PlayerViewModel _vm;

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

        // When the StreamUrl arrives (set after async metadata load), ensure the
        // MediaElement is actually playing.  On some Android devices / MIUI, the
        // auto-play binding fires before the ExoPlayer surface is ready, so the
        // player ends up paused at 0:00 with a black/white frame.
        if (e.PropertyName == nameof(PlayerViewModel.StreamUrl) &&
            !string.IsNullOrEmpty(_vm.StreamUrl))
        {
            // Small delay so ExoPlayer can attach its SurfaceTexture to the view
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), () =>
            {
                if (mediaElement.CurrentState is not
                    (MediaElementState.Playing or MediaElementState.Buffering))
                {
                    mediaElement.Play();
                }
            });
        }
    }

    // ── MediaElement event handlers ───────────────────────────────────────────

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
