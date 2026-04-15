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

        // Set source now if the URL is already loaded (fast devices / cached data)
        if (!string.IsNullOrEmpty(_vm.StreamUrl))
            ApplySource(_vm.StreamUrl);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _vm.StopSaveTimer();
        _vm.OnPlaybackStopped();
        _vm.PropertyChanged -= OnViewModelPropertyChanged;

        mediaElement.Stop();
        mediaElement.Handler?.DisconnectHandler();
    }

    // ── ViewModel property changes ────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Resume position — seek after a short delay so the player has had time to load
        if (e.PropertyName == nameof(PlayerViewModel.ResumePosition) && _vm.ResumePosition > 30)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
                mediaElement.SeekTo(TimeSpan.FromSeconds(_vm.ResumePosition)));
        }

        // URL arrived from async API call — apply it
        if (e.PropertyName == nameof(PlayerViewModel.StreamUrl) &&
            !string.IsNullOrEmpty(_vm.StreamUrl))
        {
            ApplySource(_vm.StreamUrl);
        }
    }

    // ── Source loading ────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the Source on the MediaElement and starts playback.
    /// We do NOT bind Source in XAML because ExoPlayer needs the view to be
    /// fully laid out before it can attach its SurfaceTexture.  Setting Source
    /// from code-behind after OnAppearing gives the layout pass time to complete.
    /// </summary>
    private void ApplySource(string url)
    {
        // 200 ms delay lets ExoPlayer finish attaching its SurfaceTexture
        // on slower/older Android devices before we hand it the stream URL.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), () =>
        {
            try
            {
                mediaElement.Source = MediaSource.FromUri(url);
                mediaElement.Play();
            }
            catch (Exception ex)
            {
                _vm.SetStreamError($"Player init error: {ex.Message}");
            }
        });
    }

    // ── MediaElement events ───────────────────────────────────────────────────

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        // Media opened successfully — seek to resume position if applicable
        if (_vm.ResumePosition > 30)
        {
            var dur = mediaElement.Duration.TotalSeconds;
            if (dur <= 0 || _vm.ResumePosition < dur - 30)
                mediaElement.SeekTo(TimeSpan.FromSeconds(_vm.ResumePosition));
        }
    }

    private void OnPositionChanged(object? sender, MediaPositionChangedEventArgs e)
    {
        _vm.OnPositionChanged(
            e.Position.TotalSeconds,
            mediaElement.Duration.TotalSeconds);
    }

    private void OnMediaEnded(object? sender, EventArgs e) => _vm.OnPlaybackStopped();

    private void OnMediaFailed(object? sender, MediaFailedEventArgs e)
    {
        _vm.SetStreamError($"Stream error: {e.ErrorMessage}");
    }

    private void OnMediaStateChanged(object? sender, MediaStateChangedEventArgs e)
    {
        // On some Android ROMs ExoPlayer briefly enters Stopped right after
        // source is set — nudge it back into playing.
        if (e.NewState == MediaElementState.Stopped &&
            !string.IsNullOrEmpty(_vm.StreamUrl) &&
            _vm.StreamError == null)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), () =>
                mediaElement.Play());
        }
    }
}
