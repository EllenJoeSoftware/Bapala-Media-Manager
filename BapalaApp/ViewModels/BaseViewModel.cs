using CommunityToolkit.Mvvm.ComponentModel;

namespace BapalaApp.ViewModels;

/// <summary>
/// Base class providing IsBusy / Title plumbing shared by all view models.
/// CommunityToolkit.Mvvm source-generates the property + INotifyPropertyChanged.
/// </summary>
public partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    [ObservableProperty]
    private string _title = string.Empty;

    public bool IsNotBusy => !IsBusy;
}
