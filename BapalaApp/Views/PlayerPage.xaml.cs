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

        // Seek to saved position once the media has loaded its metadata
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
    }

    // ── MediaElement event handlers ───────────────────────────────────────────

    private void OnPositionChanged(object? sender, MediaPositionChangedEventArgs e)
    {
        _vm.OnPositionChanged(
            e.Position.TotalSeconds,
            mediaElement.Duration.TotalSeconds);
    }

    private void OnMediaEnded(object? sender, EventArgs e) => _vm.OnPlaybackStopped();
}
