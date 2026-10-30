using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalBrain.UI.Services.Navigation;

namespace DigitalBrain.UI.ViewModels;

/// <summary>
/// Pantalla «Tiempo real» (en desarrollo): inferencia continua de EEG y transmisión de comandos motores al robot UGOT.
/// </summary>
public partial class RealtimeViewModel : BaseViewModel
{
    private readonly INavigationService _navigationService;

    public RealtimeViewModel(INavigationService navigationService, SystemStatusViewModel status)
    {
        Title = "Tiempo real";
        _navigationService = navigationService;
        Status = status;
    }

    public SystemStatusViewModel Status { get; }

    [RelayCommand]
    private void NavigateHome() => _navigationService.NavigateTo<HomeViewModel>();

    [RelayCommand]
    private void NavigateCapture() => _navigationService.NavigateTo<CaptureViewModel>();
}
