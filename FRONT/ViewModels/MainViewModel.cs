using CommunityToolkit.Mvvm.ComponentModel;
using DigitalBrain.UI.Services.Navigation;

namespace DigitalBrain.UI.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private BaseViewModel? _currentView;

    public MainViewModel(INavigationService navigationService)
    {
        Title = "DigitalBrain";
        _navigationService = navigationService;

        _navigationService.CurrentViewModelChanged += () =>
        {
            CurrentView = _navigationService.CurrentViewModel;
        };
    }
}
