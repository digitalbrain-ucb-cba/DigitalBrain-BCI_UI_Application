using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalBrain.UI.Services.Navigation;
using DigitalBrain.UI.Services.Theme;

namespace DigitalBrain.UI.ViewModels;

/// <summary>
/// Contenedor de la ventana: barra lateral, selector de tema, panel de estado y vista activa.
/// </summary>
public partial class MainViewModel : BaseViewModel
{
    private readonly INavigationService _navigationService;
    private readonly IThemeService _themeService;

    [ObservableProperty]
    private BaseViewModel? _currentView;

    [ObservableProperty]
    private bool _isDarkTheme;

    public MainViewModel(INavigationService navigationService, IThemeService themeService, SystemStatusViewModel status)
    {
        Title = "DigitalBrain";
        _navigationService = navigationService;
        _themeService = themeService;
        Status = status;
        _isDarkTheme = themeService.Current == AppTheme.Dark;

        _navigationService.CurrentViewModelChanged += () =>
        {
            CurrentView = _navigationService.CurrentViewModel;
        };
    }

    public SystemStatusViewModel Status { get; }

    public bool IsHomeActive => CurrentView is HomeViewModel;
    public bool IsCaptureActive => CurrentView is CaptureViewModel;
    public bool IsTrainingActive => CurrentView is TrainingViewModel;
    public bool IsRealtimeActive => CurrentView is RealtimeViewModel;
    public bool IsDatabaseActive => CurrentView is DatabaseViewModel;

    partial void OnCurrentViewChanged(BaseViewModel? value)
    {
        OnPropertyChanged(nameof(IsHomeActive));
        OnPropertyChanged(nameof(IsCaptureActive));
        OnPropertyChanged(nameof(IsTrainingActive));
        OnPropertyChanged(nameof(IsRealtimeActive));
        OnPropertyChanged(nameof(IsDatabaseActive));
    }

    partial void OnIsDarkThemeChanged(bool value) => _themeService.Apply(value ? AppTheme.Dark : AppTheme.Light);

    [RelayCommand]
    private void NavigateHome() => _navigationService.NavigateTo<HomeViewModel>();

    [RelayCommand]
    private void NavigateCapture() => _navigationService.NavigateTo<CaptureViewModel>();

    [RelayCommand]
    private void NavigateTraining() => _navigationService.NavigateTo<TrainingViewModel>();

    [RelayCommand]
    private void NavigateRealtime() => _navigationService.NavigateTo<RealtimeViewModel>();

    [RelayCommand]
    private void NavigateDatabase() => _navigationService.NavigateTo<DatabaseViewModel>();

    [RelayCommand]
    private void UseLightTheme() => IsDarkTheme = false;

    [RelayCommand]
    private void UseDarkTheme() => IsDarkTheme = true;
}
