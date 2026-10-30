using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalBrain.UI.Services.Navigation;

namespace DigitalBrain.UI.ViewModels;

/// <summary>
/// Pantalla «Base de datos» (en desarrollo): visualización, auditoría y exportación de grabaciones.
/// </summary>
public partial class DatabaseViewModel : BaseViewModel
{
    private readonly INavigationService _navigationService;

    public DatabaseViewModel(INavigationService navigationService, SystemStatusViewModel status)
    {
        Title = "Base de datos";
        _navigationService = navigationService;
        Status = status;
    }

    public SystemStatusViewModel Status { get; }

    [RelayCommand]
    private void NavigateHome() => _navigationService.NavigateTo<HomeViewModel>();

    [RelayCommand]
    private void NavigateCapture() => _navigationService.NavigateTo<CaptureViewModel>();
}
