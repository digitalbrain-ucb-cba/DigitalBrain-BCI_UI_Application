using DigitalBrain.UI.ViewModels;

namespace DigitalBrain.UI.Services.Navigation;

public interface INavigationService
{
    BaseViewModel? CurrentViewModel { get; }
    event Action? CurrentViewModelChanged;

    void NavigateTo<TViewModel>() where TViewModel : BaseViewModel;
}

public class NavigationService : INavigationService
{
    private readonly Func<Type, BaseViewModel> _viewModelFactory;
    private BaseViewModel? _currentViewModel;

    public BaseViewModel? CurrentViewModel => _currentViewModel;
    public event Action? CurrentViewModelChanged;

    public NavigationService(Func<Type, BaseViewModel> viewModelFactory)
    {
        _viewModelFactory = viewModelFactory;
    }

    public void NavigateTo<TViewModel>() where TViewModel : BaseViewModel
    {
        _currentViewModel = _viewModelFactory(typeof(TViewModel));
        _currentViewModel.OnNavigatedTo();
        CurrentViewModelChanged?.Invoke();
    }
}
