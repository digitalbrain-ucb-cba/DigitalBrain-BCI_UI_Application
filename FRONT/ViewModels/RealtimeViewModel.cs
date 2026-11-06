using System.ComponentModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalBrain.UI.Services.Dialogs;
using DigitalBrain.UI.Services.Navigation;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.ViewModels;

/// <summary>
/// Pantalla «Tiempo real» (paso 3): lanza el capturador con el modelo elegido y muestra la clase de cada ventana.
/// El envío de comandos al robot UGOT todavía no está conectado.
/// </summary>
public partial class RealtimeViewModel : BaseViewModel
{
    private const string NoPrediction = "—";
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private readonly INavigationService _navigationService;
    private readonly IRealtimeGateway _gateway;
    private readonly IBackendEnvironment _environment;
    private readonly ISettingsStore _settings;
    private readonly IDataCatalog _catalog;
    private readonly IDialogService _dialogs;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private CancellationTokenSource? _predictionCts;
    private Dictionary<string, string> _displayNames = [];
    private int _predictionCount;

    public RealtimeViewModel(
        INavigationService navigationService,
        IRealtimeGateway gateway,
        IBackendEnvironment environment,
        ISettingsStore settings,
        IDataCatalog catalog,
        IDialogService dialogs,
        SystemStatusViewModel status)
    {
        Title = "Tiempo real";
        _navigationService = navigationService;
        _gateway = gateway;
        _environment = environment;
        _settings = settings;
        _catalog = catalog;
        _dialogs = dialogs;
        Status = status;
        Status.PropertyChanged += OnStatusChanged;
        _modelPath = environment.ModelPath;
    }

    public SystemStatusViewModel Status { get; }

    [ObservableProperty] private string _executablePath = "";
    [ObservableProperty] private string _modelPath;
    [ObservableProperty] private bool _isPredicting;
    [ObservableProperty] private string _currentPrediction = NoPrediction;
    [ObservableProperty] private string _predictionCountText = "0";
    [ObservableProperty] private string _deviceStatus = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isError;

    public string ToggleText => IsPredicting ? "Detener predicción" : "Iniciar predicción";

    public override void OnNavigatedTo()
    {
        // La ruta del capturador se elige en la pantalla Capturar.
        ExecutablePath = _settings.Load().ExecutablePath ?? "";
    }

    partial void OnIsPredictingChanged(bool value)
    {
        Status.IsPredicting = value;
        OnPropertyChanged(nameof(ToggleText));
    }

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemStatusViewModel.IsCapturing) or nameof(SystemStatusViewModel.IsTraining))
            TogglePredictionCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void BrowseModel()
    {
        if (_dialogs.PickModel(ModelPath) is { } path)
            ModelPath = path;
    }

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanTogglePrediction))]
    private async Task TogglePredictionAsync()
    {
        if (IsPredicting)
        {
            Stop();
            return;
        }

        ExecutablePath = _settings.Load().ExecutablePath ?? "";
        if (!File.Exists(ExecutablePath))
        {
            ShowMessage("Elige la ruta de ConDumpAuto.exe en la pantalla Capturar.", isError: true);
            return;
        }
        if (!File.Exists(ModelPath))
        {
            ShowMessage("No se encontró el modelo: entrénalo en Entrenar o elige un archivo .pkl.", isError: true);
            return;
        }

        _displayNames = _catalog.GetMovements().ToDictionary(m => m.Label, m => m.DisplayName);
        _predictionCount = 0;
        PredictionCountText = "0";
        CurrentPrediction = NoPrediction;
        DeviceStatus = "";
        ShowMessage($"Iniciando {Path.GetFileName(ExecutablePath)}; la primera decisión llega tras 1 s de señal válida.");
        _predictionCts = new CancellationTokenSource();
        IsPredicting = true;

        try
        {
            var outcome = await _gateway.RunAsync(
                new RealtimeRequest(ExecutablePath, ModelPath), OnPrediction, OnDeviceLog, _predictionCts.Token);
            if (outcome.Cancelled)
                ShowMessage("Predicción detenida.");
            else
                ShowMessage(outcome.Error ?? "La predicción terminó.", isError: true);
        }
        catch (Exception ex)
        {
            ShowMessage($"Error durante la predicción: {ex.Message}", isError: true);
        }
        finally
        {
            IsPredicting = false;
            Status.IsHeadsetStreaming = false;
            _predictionCts.Dispose();
            _predictionCts = null;
        }
    }

    private bool CanTogglePrediction() => !Status.IsCapturing && !Status.IsTraining;

    /// <summary>Detiene la predicción en curso, si hay una; también se llama al cerrar la aplicación.</summary>
    public void Stop() => _predictionCts?.Cancel();

    private void OnPrediction(string label) => _dispatcher.InvokeAsync(() =>
    {
        Status.IsHeadsetStreaming = IsPredicting;
        _predictionCount++;
        PredictionCountText = _predictionCount.ToString("N0", Es);
        CurrentPrediction = _displayNames.GetValueOrDefault(label, label);
    });

    private void OnDeviceLog(string line) => _dispatcher.InvokeAsync(() => DeviceStatus = line);

    private void ShowMessage(string text, bool isError = false)
    {
        Message = text;
        IsError = isError;
    }

    [RelayCommand]
    private void NavigateCapture() => _navigationService.NavigateTo<CaptureViewModel>();
}
