using CommunityToolkit.Mvvm.ComponentModel;

namespace DigitalBrain.UI.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Se llama cada vez que la pantalla pasa a ser la vista activa.</summary>
    public virtual void OnNavigatedTo() { }
}
